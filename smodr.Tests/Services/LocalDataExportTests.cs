using System.Text.Json;
using smodr.Models;
using smodr.Services;

namespace smodr.Tests.Services;

[TestClass]
public sealed class LocalDataExportTests
{
    private static readonly string[] _diagnosticFields = ["schemaVersion", "capturedAtUtc", "counters"];

    [TestMethod]
    public async Task HistoryExportWaitsForAcceptedWritesAndDoesNotChangeHistory()
    {
        var history = new GatedHistory();
        var export = LocalDataExport.HistoryAsync(history);
        Assert.IsFalse(export.IsCompleted);
        history.Release();
        using var json = JsonDocument.Parse(await export);
        var entry = json.RootElement.GetProperty("entries")[0];
        Assert.AreEqual("Heard song", entry.GetProperty("Title").GetString());
        Assert.AreEqual(history.Entries[0].HeardAt, entry.GetProperty("HeardAt").GetDateTimeOffset());
        Assert.HasCount(1, history.Entries);
    }

    [TestMethod]
    public void DiagnosticExportHasOnlyFixedCountersAndCaptureMetadata()
    {
        var counters = new RuntimeDiagnosticCounters();
        counters.Increment(RuntimeCounter.MetadataAccepted);
        using var json = JsonDocument.Parse(LocalDataExport.Diagnostics(counters));
        CollectionAssert.AreEquivalent(_diagnosticFields,
            json.RootElement.EnumerateObject().Select(property => property.Name).ToArray());
        var values = json.RootElement.GetProperty("counters");
        CollectionAssert.AreEquivalent(Enum.GetNames<RuntimeCounter>(),
            values.EnumerateObject().Select(property => property.Name).ToArray());
        Assert.AreEqual(1L, values.GetProperty("MetadataAccepted").GetInt64());
        foreach (var value in values.EnumerateObject())
        {
            Assert.AreEqual(JsonValueKind.Number, value.Value.ValueKind);
        }
    }

    private sealed class GatedHistory : ITrackHistoryService
    {
        private readonly TaskCompletionSource _flush = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IReadOnlyList<HeardTrack> Entries => _flush.Task.IsCompleted
            ? (HeardTrack[])[new HeardTrack { Title = "Heard song", HeardAt = DateTimeOffset.UnixEpoch }]
            : [];

        public Task FlushAsync()
        {
            return _flush.Task;
        }

        public Task<Guid> RecordAsync(RadioStation station, RadioTrackInfo track)
        {
            throw new NotSupportedException();
        }

        public Task UpdateArtworkAsync(Guid entryId, AlbumArtworkMatch artwork)
        {
            throw new NotSupportedException();
        }

        public Task ClearAsync()
        {
            throw new NotSupportedException();
        }

        public void Release()
        {
            _flush.SetResult();
        }
    }
}
