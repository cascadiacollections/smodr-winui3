using Microsoft.Extensions.Time.Testing;
using smodr.Models;
using smodr.Services;

namespace smodr.Tests.Services;

[TestClass]
public sealed class RadioWarmupTests
{
    [TestMethod]
    public void PreparedResourceTransfersExactlyOnceAndDoesNotDisposeTransferredOwner()
    {
        using var slot = new RadioStreamWarmupSlot<Resource>();
        using var resource = new Resource();
        slot.Put("https://stream.example/live", resource);
        Assert.AreSame(resource, slot.Take("https://stream.example/live", true));
        Assert.IsNull(slot.Take("https://stream.example/live", true));
        Assert.AreEqual(0, resource.Disposals);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void ExpiredOrDisallowedResourceIsRetired(bool expired)
    {
        var clock = new FakeTimeProvider();
        using var slot = new RadioStreamWarmupSlot<Resource>(clock);
        using var resource = new Resource();
        slot.Put("https://stream.example/live", resource);
        if (expired)
        {
            clock.Advance(TimeSpan.FromSeconds(30));
        }

        Assert.IsNull(slot.Take("https://stream.example/live", expired));
        Assert.AreEqual(1, resource.Disposals);
    }

    [TestMethod]
    public void MismatchedReplacedAndPostDisposalResourcesAreRetired()
    {
        using var slot = new RadioStreamWarmupSlot<Resource>();
        using var first = new Resource();
        using var second = new Resource();
        slot.Put("one", first);
        slot.Put("two", second);
        Assert.AreEqual(1, first.Disposals);
        Assert.IsNull(slot.Take("other", true));
        Assert.AreEqual(1, second.Disposals);
        slot.Dispose();
        using var late = new Resource();
        slot.Put("late", late);
        Assert.AreEqual(1, late.Disposals);
        Assert.IsNull(slot.Take("late", true));
    }

    [TestMethod]
    public async Task WarmupHonorsOptInCostAndDspPoliciesWithoutOpeningNativeAudio()
    {
        using var profile = new TemporaryRadioProfile();
        var preferences = new RadioPlaybackPreferences(profile.FilePath);
        var calls = 0;
        var unrestricted = true;
        using var warmer = new RadioStreamPrewarmer(preferences, () => unrestricted, (_, _) =>
        {
            calls++;
            return Task.FromResult<PreparedRadioSource?>(null);
        });
        RadioStation[] stations = [new() { Id = "one", StreamUrl = "https://stream.example/live" }];
        await warmer.WarmAsync(stations);
        Assert.AreEqual(0, calls);
        await preferences.UpdateAsync(value => value with { PrewarmStreams = true });
        unrestricted = false;
        await warmer.WarmAsync(stations);
        Assert.AreEqual(0, calls);
        unrestricted = true;
        await preferences.UpdateAsync(value => value with { Equalizer = RadioEqualizerPreset.Speech });
        await warmer.WarmAsync(stations);
        Assert.AreEqual(0, calls);
        await preferences.UpdateAsync(value => value with { Equalizer = RadioEqualizerPreset.Off });
        await warmer.WarmAsync(stations);
        Assert.AreEqual(1, calls);
    }

    [TestMethod]
    public async Task WarmupSkipsUnsafeAndPlaylistSourcesAndCancelsPendingWorkOnTake()
    {
        using var profile = new TemporaryRadioProfile();
        var preferences = new RadioPlaybackPreferences(profile.FilePath);
        await preferences.UpdateAsync(value => value with { PrewarmStreams = true });
        var started = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var warmer = new RadioStreamPrewarmer(preferences, () => true, async (station, token) =>
        {
            Assert.AreEqual("safe", station.Id);
            started.SetResult(token);
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return null;
        });
        RadioStation[] stations =
        [
            new() { Id = "shoutcast:1", StreamUrl = "https://yp.shoutcast.com/playlist" },
            new() { Id = "local", StreamUrl = "http://localhost/private" },
            new() { Id = "credentials", StreamUrl = "https://user:secret@stream.example/live" },
            new() { Id = "safe", StreamUrl = "https://stream.example/live" }
        ];
        var pending = warmer.WarmAsync(stations);
        var token = await started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await warmer.WarmAsync(stations); // Coalesced: no second factory invocation.
        Assert.IsNull(warmer.Take(stations[3]));
        await pending;
        Assert.IsTrue(token.IsCancellationRequested);
    }

    [TestMethod]
    public void DspProgressDeadlineResetsOnProgressAndResume()
    {
        var clock = new FakeTimeProvider();
        var watchdog = new RadioPlaybackProgressWatchdog(clock);
        watchdog.Reset(TimeSpan.Zero);
        clock.Advance(TimeSpan.FromSeconds(29));
        Assert.IsFalse(watchdog.IsStalled(TimeSpan.Zero));
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.IsTrue(watchdog.IsStalled(TimeSpan.Zero));
        Assert.IsFalse(watchdog.IsStalled(TimeSpan.FromSeconds(1)));
        clock.Advance(TimeSpan.FromMinutes(2));
        watchdog.Reset(TimeSpan.FromSeconds(1)); // Resuming after a pause starts a new deadline.
        Assert.IsFalse(watchdog.IsStalled(TimeSpan.FromSeconds(1)));
    }

    [TestMethod]
    public void PlaybackProgressDoesNotLetBackwardNativeSamplesHideAStall()
    {
        var clock = new FakeTimeProvider();
        var watchdog = new RadioPlaybackProgressWatchdog(clock, TimeSpan.FromSeconds(10));
        watchdog.Reset(TimeSpan.FromSeconds(20));
        clock.Advance(TimeSpan.FromSeconds(6));
        Assert.IsFalse(watchdog.IsStalled(TimeSpan.FromSeconds(19)));
        clock.Advance(TimeSpan.FromSeconds(4));
        Assert.IsTrue(watchdog.IsStalled(TimeSpan.FromSeconds(19)));
        Assert.IsFalse(watchdog.IsStalled(TimeSpan.FromSeconds(21)));
    }

    [TestMethod]
    public void PlaybackProgressTreatsLargeLiveTimelineDiscontinuityAsProgress()
    {
        var clock = new FakeTimeProvider();
        var watchdog = new RadioPlaybackProgressWatchdog(clock, TimeSpan.FromSeconds(10));
        watchdog.Reset(TimeSpan.FromSeconds(30));
        clock.Advance(TimeSpan.FromSeconds(9));
        Assert.IsFalse(watchdog.IsStalled(TimeSpan.FromSeconds(2)));
        clock.Advance(TimeSpan.FromSeconds(9));
        Assert.IsFalse(watchdog.IsStalled(TimeSpan.FromSeconds(3)));
        clock.Advance(TimeSpan.FromSeconds(10));
        Assert.IsTrue(watchdog.IsStalled(TimeSpan.FromSeconds(3)));
    }

    [TestMethod]
    public void PlaybackProgressDoesNotLetOldAndRebasedSamplesOscillateForever()
    {
        var clock = new FakeTimeProvider();
        var watchdog = new RadioPlaybackProgressWatchdog(clock, TimeSpan.FromSeconds(10));
        watchdog.Reset(TimeSpan.FromSeconds(30));
        clock.Advance(TimeSpan.FromSeconds(5));
        Assert.IsFalse(watchdog.IsStalled(TimeSpan.FromSeconds(2)));
        clock.Advance(TimeSpan.FromSeconds(5));
        Assert.IsTrue(watchdog.IsStalled(TimeSpan.FromSeconds(30)));
        Assert.IsTrue(watchdog.IsStalled(TimeSpan.FromSeconds(2)));
    }

    [TestMethod]
    public void PlaybackProgressRejectsInvalidDeadline()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            new RadioPlaybackProgressWatchdog(stallTimeout: TimeSpan.Zero));
    }

    [TestMethod]
    public void PlaybackProgressRejectsInvalidTimelineResetThreshold()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            new RadioPlaybackProgressWatchdog(timelineResetThreshold: TimeSpan.Zero));
    }

    private sealed class Resource : IDisposable
    {
        public int Disposals { get; private set; }

        public void Dispose()
        {
            Disposals++;
        }
    }
}
