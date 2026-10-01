using smodr.Services;

namespace smodr.Tests.Services;

[TestClass]
public sealed class AudioOutputChangePolicyTests
{
    [TestMethod]
    public void RequestedPlaybackPausesWhenDefaultOutputChanges()
    {
        Assert.IsTrue(AudioOutputChangePolicy.ShouldPause("headphones", "speakers", true, true));
        Assert.IsTrue(AudioOutputChangePolicy.ShouldPause("headphones", string.Empty, true, true));
    }

    [TestMethod]
    public void OtherDeviceEventsAndExplicitPauseDoNotChangePlayback()
    {
        Assert.IsFalse(AudioOutputChangePolicy.ShouldPause("headphones", "headphones", true, true));
        Assert.IsFalse(AudioOutputChangePolicy.ShouldPause("headphones", "speakers", false, true));
        Assert.IsFalse(AudioOutputChangePolicy.ShouldPause("headphones", "speakers", true, false));
        Assert.IsFalse(AudioOutputChangePolicy.ShouldPause(null, "speakers", true, true));
    }
}
