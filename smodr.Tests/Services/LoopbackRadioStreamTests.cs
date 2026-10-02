using System.Threading.Channels;
using Microsoft.Extensions.Time.Testing;
using smodr.Services;
using smodr.Tests.Fixtures;

namespace smodr.Tests.Services;

[TestClass]
[TestCategory("Loopback")]
public sealed class LoopbackRadioStreamTests
{
    [TestMethod]
    [DataRow("icy", 2)]
    [DataRow("redirect", 2)]
    [DataRow("truncated", 1)]
    public async Task RealHttpTransportRetainsSplitUnicodeAndDoesNotPublishTruncatedCues(string path, int expected)
    {
        await using var server = new SyntheticRadioServer();
        using var handler = new HttpClientHandler { AllowAutoRedirect = true, UseProxy = false };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(3) };
        var cues = new List<string>();
        var supported = await new IcyMetadataStreamReader(client).ListenAsync(server.UriFor(path), cues.Add)
            .WaitAsync(TimeSpan.FromSeconds(5));
        Assert.IsTrue(supported);
        Assert.HasCount(expected, cues);
        var track = IcyTrackParser.Parse(cues[0], "Synthetic radio");
        Assert.AreEqual("Hüsker Dü", track?.Artist);
        Assert.AreEqual("Ice Cold Ice", track?.Title);
        if (path != "redirect") Assert.IsTrue(await server.MetadataHeadersObserved.Task);
    }

    [TestMethod]
    public async Task StalledBodyCanCancelAndNextConnectionStillWorks()
    {
        await using var server = new SyntheticRadioServer();
        using var handler = new HttpClientHandler { UseProxy = false };
        using var client = new HttpClient(handler);
        using var cancellation = new CancellationTokenSource();
        var stalled = new IcyMetadataStreamReader(client).ListenAsync(server.UriFor("stall"), _ => { }, cancellation.Token);
        await server.StallStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await cancellation.CancelAsync();
        await Assert.ThrowsAsync<OperationCanceledException>(() => stalled.WaitAsync(TimeSpan.FromSeconds(3)));
        var result = await new IcyMetadataProbe(client).ProbeAsync(server.UriFor("icy")).WaitAsync(TimeSpan.FromSeconds(3));
        Assert.IsNotNull(result.RawMetadata);
        Assert.AreEqual(2, server.Requests);
    }

    private static readonly string[] _expectedTransitions = ["First", "clear", "Second"];

    [TestMethod]
    public async Task DamagedCueInvalidatesBeforeTheNextValidTitleOverRealSocket()
    {
        await using var server = new SyntheticRadioServer();
        using var handler = new HttpClientHandler { UseProxy = false };
        using var client = new HttpClient(handler);
        var states = new List<string>();
        await new IcyMetadataStreamReader(client).ListenAsync(server.UriFor("damaged"), raw =>
        {
            if (IcyTrackParser.IsDamagedSongCue(raw, "Synthetic radio")) states.Add("clear");
            else if (IcyTrackParser.Parse(raw, "Synthetic radio") is { } track) states.Add(track.Title);
        }).WaitAsync(TimeSpan.FromSeconds(3));
        CollectionAssert.AreEqual(_expectedTransitions, states);
    }

    [TestMethod]
    public async Task RecoveryRetriesRealHttpFailuresWithFakeTimeAndStopsAfterSuccess()
    {
        await using var server = new SyntheticRadioServer();
        using var handler = new HttpClientHandler { UseProxy = false };
        using var client = new HttpClient(handler);
        var clock = new FakeTimeProvider();
        var restarts = Channel.CreateUnbounded<long>();
        var exhausted = false;
        using var recovery = new LiveRadioRecovery(epoch => restarts.Writer.TryWrite(epoch), _ => exhausted = true,
            clock, stallTimeout: TimeSpan.FromSeconds(30), retryBaseDelay: TimeSpan.FromSeconds(1), maxRetries: 2);
        var probe = new IcyMetadataProbe(client);
        recovery.Begin();
        for (var attempt = 0; attempt < 2; attempt++)
        {
            await Assert.ThrowsAsync<HttpRequestException>(() => probe.ProbeAsync(server.UriFor("recover")));
            recovery.Fail();
            clock.Advance(TimeSpan.FromSeconds(1 << attempt));
            var epoch = await restarts.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3));
            Assert.IsTrue(recovery.IsCurrent(epoch));
        }
        Assert.IsNotNull((await probe.ProbeAsync(server.UriFor("recover"))).RawMetadata);
        recovery.Playing();
        clock.Advance(TimeSpan.FromHours(1));
        Assert.IsFalse(exhausted);
        Assert.IsTrue(recovery.IsRequested);
        Assert.AreEqual(3, server.Requests);
        Assert.IsFalse(restarts.Reader.TryRead(out _));
    }

    [TestMethod]
    public async Task FiniteFixtureIsValidPcmWaveWithoutRequiringAudioOutput()
    {
        await using var server = new SyntheticRadioServer();
        using var handler = new HttpClientHandler { UseProxy = false };
        using var client = new HttpClient(handler);
        var bytes = await client.GetByteArrayAsync(server.UriFor("wav"));
        Assert.AreEqual(16044, bytes.Length);
        Assert.IsTrue(bytes.AsSpan(0, 4).SequenceEqual("RIFF"u8));
        Assert.IsTrue(bytes.AsSpan(8, 4).SequenceEqual("WAVE"u8));
    }
}
