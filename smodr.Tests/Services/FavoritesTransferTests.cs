using System.Text;
using System.Text.Json;
using smodr.Models;
using smodr.Services;

namespace smodr.Tests.Services;

[TestClass]
public sealed class FavoritesTransferTests
{
    private const string FirstUuid = "96062a7b-0601-11e8-ae97-52543be04c81";
    private const string SecondUuid = "9608b51d-0601-11e8-ae97-52543be04c81";
    private static readonly string[] _favoriteKeys = ["favorites", "schemaVersion"];
    private static readonly string[] _iosOrder = ["Jazz FM", "News Radio"];
    private static readonly string[] _mergedIds = ["existing", FirstUuid, SecondUuid, "later"];

    private static readonly string[] _iosKeys =
        ["artworkURL", "bitrate", "codec", "countryCode", "genre", "id", "name", "sortIndex", "streamURL", "tags"];

    private static readonly RadioStation _first = new()
    {
        Id = FirstUuid,
        Name = "Jazz FM",
        StreamUrl = "https://stream.example/jazz",
        ArtworkUrl = "https://art.example/jazz.png",
        Tags = "jazz,smooth jazz",
        CountryCode = "GB",
        Codec = "MP3",
        Bitrate = 128
    };

    private static readonly RadioStation _second = new()
    {
        Id = SecondUuid, Name = "News Radio", StreamUrl = "https://stream.example/news"
    };

    [TestMethod]
    public void ExportUsesIosDocumentShapeWithSortedKeysAndOrder()
    {
        using var json = JsonDocument.Parse(FavoritesTransfer.Export((RadioStation[])[_first, _second]));
        var root = json.RootElement;
        CollectionAssert.AreEqual(_favoriteKeys, root.EnumerateObject().Select(property => property.Name).ToArray());
        Assert.AreEqual(1, root.GetProperty("schemaVersion").GetInt32());
        var favorites = root.GetProperty("favorites");
        CollectionAssert.AreEqual(_iosKeys,
            favorites[0].EnumerateObject().Select(property => property.Name).ToArray());
        Assert.AreEqual("jazz", favorites[0].GetProperty("genre").GetString());
        Assert.AreEqual(FirstUuid, favorites[0].GetProperty("id").GetString());
        Assert.AreEqual(1, favorites[1].GetProperty("sortIndex").GetInt32());
        Assert.IsFalse(favorites[1].TryGetProperty("artworkURL", out _), "iOS omits absent optionals.");
        Assert.AreEqual("", favorites[1].GetProperty("genre").GetString(), "iOS requires genre.");
    }

    [TestMethod]
    public void ExportRoundTripsIntoAnEmptyLibrary()
    {
        var local = new RadioStation { Name = "Local", StreamUrl = "https://stream.example/local" };
        var preview = Preview(FavoritesTransfer.Export((RadioStation[])[_first, _second, local]), []);

        Assert.IsTrue(preview.CanImport);
        Assert.HasCount(3, preview.StationsToAdd);
        var first = preview.StationsToAdd[0];
        Assert.AreEqual(_first.Id, first.Id);
        Assert.AreEqual(_first.Name, first.Name);
        Assert.AreEqual(_first.StreamUrl, first.StreamUrl);
        Assert.AreEqual(_first.ArtworkUrl, first.ArtworkUrl);
        Assert.AreEqual(_first.Tags, first.Tags);
        Assert.AreEqual(_first.CountryCode, first.CountryCode);
        Assert.AreEqual(_first.Codec, first.Codec);
        Assert.AreEqual(_first.Bitrate, first.Bitrate);
        Assert.AreEqual(string.Empty, preview.StationsToAdd[2].Id,
            "A stream-URL id written for iOS is not invented as a directory id.");
    }

    [TestMethod]
    public void ImportsIosBackupHonouringSortIndex()
    {
        const string ios = $$"""
                             {
                               "favorites" : [
                                 { "genre" : "News", "id" : "{{SecondUuid}}", "name" : "News Radio",
                                   "sortIndex" : 1, "streamURL" : "https://stream.example/news" },
                                 { "artworkURL" : "https://art.example/jazz.png", "genre" : "Jazz",
                                   "id" : "{{FirstUuid}}", "name" : "Jazz FM", "sortIndex" : 0,
                                   "streamURL" : "https://stream.example/jazz" }
                               ],
                               "schemaVersion" : 1
                             }
                             """;
        var preview = Preview(ios, []);

        CollectionAssert.AreEqual(_iosOrder,
            preview.StationsToAdd.Select(station => station.Name).ToArray());
        Assert.AreEqual("Jazz", preview.StationsToAdd[0].Tags, "iOS genre becomes the Windows tag list.");
    }

    [TestMethod]
    public void PreviewDeduplicatesAgainstLibraryAndWithinFile()
    {
        var savedByUrl = new RadioStation { Name = "Saved", StreamUrl = "https://stream.example/legacy" };
        var json = Backup(
            Entry(FirstUuid, "Jazz FM", "https://stream.example/jazz"),
            Entry(FirstUuid.ToUpperInvariant(), "Jazz FM again", "https://stream.example/jazz-2"),
            Entry("", "Legacy", "HTTPS://STREAM.EXAMPLE/legacy"),
            Entry(SecondUuid, "News", "https://stream.example/news"));

        var preview = Preview(json, (RadioStation[])[savedByUrl, _second]);

        Assert.HasCount(1, preview.StationsToAdd);
        Assert.AreEqual("Jazz FM", preview.StationsToAdd[0].Name, "The first occurrence in file order wins.");
        Assert.AreEqual(2, preview.AlreadySavedCount);
        Assert.AreEqual(1, preview.DuplicateCount);
        Assert.AreEqual(0, preview.InvalidCount);
        StringAssert.Contains(preview.Summary, "1 station will be added", StringComparison.Ordinal);
        StringAssert.Contains(preview.Summary, "2 stations already saved", StringComparison.Ordinal);
    }

    [TestMethod]
    public void NothingNewIsValidButNotImportable()
    {
        var preview = Preview(FavoritesTransfer.Export((RadioStation[])[_first]), (RadioStation[])[_first]);
        Assert.IsTrue(preview.IsValid);
        Assert.IsFalse(preview.CanImport);
        StringAssert.StartsWith(preview.Summary, "There are no new stations", StringComparison.Ordinal);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   \r\n")]
    public void EmptyFileIsRejected(string content)
    {
        Assert.AreEqual(FavoritesImportError.Empty, Preview(content, []).Error);
    }

    [TestMethod]
    [DataRow("{\"schemaVersion\":1,\"favorites\":[")]
    [DataRow("{\"schemaVersion\":1,\"favorites\":[],}")]
    [DataRow("// comment\n{\"schemaVersion\":1,\"favorites\":[]}")]
    [DataRow("{\"schemaVersion\":1,\"schemaVersion\":2,\"favorites\":[]}")]
    [DataRow("{\"schemaVersion\":1,\"favorites\":[[[[[[[[[[1]]]]]]]]]]}")]
    public void MalformedJsonIsRejected(string content)
    {
        Assert.AreEqual(FavoritesImportError.NotJson, Preview(content, []).Error);
    }

    [TestMethod]
    [DataRow("[]")]
    [DataRow("{\"favorites\":[]}")]
    [DataRow("{\"schemaVersion\":\"1\",\"favorites\":[]}")]
    [DataRow("{\"schemaVersion\":1}")]
    [DataRow("{\"schemaVersion\":1,\"favorites\":{}}")]
    [DataRow("{\"schemaVersion\":1,\"entries\":[]}")]
    public void WrongShapeIsNotABackup(string content)
    {
        var preview = Preview(content, []);
        Assert.AreEqual(FavoritesImportError.NotABackup, preview.Error);
        Assert.IsFalse(preview.CanImport);
        Assert.AreEqual(preview.ErrorMessage, preview.Summary);
    }

    [TestMethod]
    [DataRow(2, FavoritesImportError.NewerSchemaVersion)]
    [DataRow(0, FavoritesImportError.UnsupportedSchemaVersion)]
    [DataRow(-1, FavoritesImportError.UnsupportedSchemaVersion)]
    public void SchemaVersionMustBeCurrent(int version, FavoritesImportError expected)
    {
        var preview = Preview($"{{\"schemaVersion\":{version},\"favorites\":[]}}", []);
        Assert.AreEqual(expected, preview.Error);
        Assert.IsEmpty(preview.StationsToAdd);
    }

    [TestMethod]
    public void SizeAndEntryCountAreBounded()
    {
        var padding = new string(' ', FavoritesTransfer.MaxFileBytes);
        Assert.AreEqual(FavoritesImportError.TooLarge,
            Preview($"{{\"schemaVersion\":1,\"favorites\":[]}}{padding}", []).Error);

        var many = string.Join(',', Enumerable.Repeat("{}", FavoritesTransfer.MaxEntries + 1));
        Assert.AreEqual(FavoritesImportError.TooManyEntries,
            Preview($"{{\"schemaVersion\":1,\"favorites\":[{many}]}}", []).Error);
    }

    [TestMethod]
    public async Task StreamReadStopsAtTheSizeBound()
    {
        await using var oversized = new NonSeekableStream(new byte[FavoritesTransfer.MaxFileBytes + 10]);
        var preview = await FavoritesTransfer.PreviewAsync(oversized, []);
        Assert.AreEqual(FavoritesImportError.TooLarge, preview.Error);
        Assert.IsLessThanOrEqualTo(FavoritesTransfer.MaxFileBytes + 1, oversized.BytesRead);

        await using var seekable = new MemoryStream(new byte[FavoritesTransfer.MaxFileBytes + 1]);
        Assert.AreEqual(FavoritesImportError.TooLarge, (await FavoritesTransfer.PreviewAsync(seekable, [])).Error);
        Assert.AreEqual(0, seekable.Position, "A seekable stream's length is checked before reading.");
    }

    [TestMethod]
    public async Task StreamPreviewAcceptsUtf8ByteOrderMark()
    {
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(FavoritesTransfer.Export((RadioStation[])[_first])))
            .ToArray();
        await using var stream = new MemoryStream(bytes);
        Assert.HasCount(1, (await FavoritesTransfer.PreviewAsync(stream, [])).StationsToAdd);
    }

    [TestMethod]
    [DataRow("{\"id\":\"a\",\"name\":\"\",\"streamURL\":\"https://stream.example/a\"}")]
    [DataRow("{\"id\":\"a\",\"name\":\"   \",\"streamURL\":\"https://stream.example/a\"}")]
    [DataRow("{\"id\":\"a\",\"name\":\"No stream\"}")]
    [DataRow("{\"id\":\"a\",\"name\":\"Local\",\"streamURL\":\"file:///C:/music.mp3\"}")]
    [DataRow("{\"id\":\"a\",\"name\":\"Script\",\"streamURL\":\"javascript:alert(1)\"}")]
    [DataRow("{\"id\":\"a\",\"name\":\"Creds\",\"streamURL\":\"https://user:pass@stream.example/a\"}")]
    [DataRow("{\"id\":\"a\",\"name\":\"Relative\",\"streamURL\":\"/live\"}")]
    [DataRow("{\"id\":7,\"name\":\"Typed\",\"streamURL\":\"https://stream.example/a\"}")]
    [DataRow("{\"id\":\"a\",\"name\":\"Bad index\",\"streamURL\":\"https://stream.example/a\",\"sortIndex\":\"1\"}")]
    [DataRow("{\"id\":\"a\",\"name\":\"Bad bitrate\",\"streamURL\":\"https://stream.example/a\",\"bitrate\":1.5}")]
    [DataRow("{\"id\":\"a\",\"name\":\"Line\\nbreak\",\"streamURL\":\"https://stream.example/a\"}")]
    [DataRow("\"not an object\"")]
    public void InvalidEntriesAreSkippedAndCounted(string entry)
    {
        var preview = Preview(Backup(entry, Entry(FirstUuid, "Jazz FM", "https://stream.example/jazz")), []);

        Assert.IsTrue(preview.IsValid);
        Assert.AreEqual(1, preview.InvalidCount);
        Assert.HasCount(1, preview.StationsToAdd);
        Assert.AreEqual("Jazz FM", preview.StationsToAdd[0].Name);
    }

    [TestMethod]
    public void OverlongFieldsInvalidateTheEntry()
    {
        var longName = new string('n', 201);
        var longUrl = "https://stream.example/" + new string('u', 2_048);
        var preview = Preview(Backup(
            Entry("a", longName, "https://stream.example/a"),
            Entry("b", "Long URL", longUrl)), []);
        Assert.AreEqual(2, preview.InvalidCount);
        Assert.IsFalse(preview.CanImport);
    }

    [TestMethod]
    public void UnusableCosmeticFieldsAreDroppedNotFatal()
    {
        var preview = Preview(Backup("""
                                     { "id": "a", "name": "  Padded  ", "streamURL": "https://stream.example/a",
                                       "artworkURL": "file:///etc/passwd", "countryCode": "usa",
                                       "bitrate": 999999, "genre": null, "unknownFutureField": { "x": 1 } }
                                     """), []);

        var station = preview.StationsToAdd.Single();
        Assert.AreEqual("Padded", station.Name);
        Assert.AreEqual(string.Empty, station.ArtworkUrl);
        Assert.AreEqual(string.Empty, station.CountryCode);
        Assert.AreEqual(0, station.Bitrate);
        Assert.AreEqual(string.Empty, station.Tags);
    }

    [TestMethod]
    public void SelectNewKeepsOrderAndNeverReturnsSavedStations()
    {
        var third = new RadioStation { Id = "third", StreamUrl = "https://stream.example/third" };
        var result = FavoritesTransfer.SelectNew((RadioStation[])[_first], (RadioStation[])[_second, _first, third, _second]);
        CollectionAssert.AreEqual(new[] { _second, third }, result.ToArray());
    }

    [TestMethod]
    public async Task LibraryAppendsImportedStationsDurablyWithoutTouchingExistingOrder()
    {
        var path = Path.Combine(Path.GetTempPath(), $"shoutkit-import-{Guid.NewGuid():N}.json");
        try
        {
            var library = new RadioLibraryService(path);
            var existing = new RadioStation { Id = "existing", Name = "Existing", StreamUrl = "https://s.example/e" };
            await library.ToggleFavoriteAsync(existing);
            await library.ToggleFavoriteAsync(_first);
            var preview = Preview(FavoritesTransfer.Export((RadioStation[])[_second, _first]), library.Favorites);
            Assert.HasCount(1, preview.StationsToAdd);

            // The library changes between preview and confirmation.
            await library.ToggleFavoriteAsync(_second);
            Assert.AreEqual(0, await library.AddFavoritesAsync(preview.StationsToAdd));

            var later = new RadioStation { Id = "later", Name = "Later", StreamUrl = "https://s.example/l" };
            Assert.AreEqual(1, await library.AddFavoritesAsync((RadioStation[])[later, existing]));
            CollectionAssert.AreEqual(_mergedIds,
                new RadioLibraryService(path).Favorites.Select(station => station.Id).ToArray());
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public async Task NewerLibraryFormatRejectsImportAndKeepsTheFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"shoutkit-import-{Guid.NewGuid():N}.json");
        const string future = """{"SchemaVersion":99,"Favorites":[],"Recents":[]}""";
        await File.WriteAllTextAsync(path, future);
        try
        {
            var library = new RadioLibraryService(path);
            await Assert.ThrowsExactlyAsync<IOException>(() => library.AddFavoritesAsync((RadioStation[])[_first]));
            Assert.AreEqual(future, await File.ReadAllTextAsync(path));
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public async Task FavoritesExportWaitsForAcceptedWrites()
    {
        var path = Path.Combine(Path.GetTempPath(), $"shoutkit-import-{Guid.NewGuid():N}.json");
        try
        {
            var library = new RadioLibraryService(path);
            _ = library.ToggleFavoriteAsync(_first);
            using var json = JsonDocument.Parse(await LocalDataExport.FavoritesAsync(library));
            Assert.AreEqual(1, json.RootElement.GetProperty("favorites").GetArrayLength());
        }
        finally { File.Delete(path); }
    }

    private static FavoritesImportPreview Preview(string json, IReadOnlyList<RadioStation> saved)
    {
        return FavoritesTransfer.Preview(Encoding.UTF8.GetBytes(json), saved);
    }

    private static string Backup(params string[] entries)
    {
        return $"{{\"schemaVersion\":1,\"favorites\":[{string.Join(',', entries)}]}}";
    }

    private static string Entry(string id, string name, string stream)
    {
        return JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["id"] = id, ["name"] = name, ["streamURL"] = stream
        });
    }

    private sealed class NonSeekableStream(byte[] data) : Stream
    {
        private readonly MemoryStream _inner = new(data);
        public int BytesRead { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = _inner.Read(buffer, offset, Math.Min(count, 4096));
            BytesRead += read;
            return read;
        }

        public override void Flush() { }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
