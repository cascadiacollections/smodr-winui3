using smodr.Services;

namespace smodr.Tests.Services;

[TestClass]
[TestCategory("RuntimeRace")]
public sealed class PlaybackEnvironmentPolicyTests
{
    [TestMethod]
    public void NetworkLossHoldsOnceAndRestorationResumesOnce()
    {
        var policy = new PlaybackEnvironmentPolicy();
        Assert.AreEqual(PlaybackEnvironmentAction.Hold, policy.Update(false, false, true, false, true));
        Assert.IsTrue(policy.IsResumePending);
        Assert.AreEqual(PlaybackEnvironmentAction.None, policy.Update(false, false, false, false, true));
        Assert.AreEqual(PlaybackEnvironmentAction.Resume, policy.Update(false, true, false, false, true));
        Assert.AreEqual(PlaybackEnvironmentAction.None, policy.Update(false, true, true, false, true));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void SleepResumeRequiresExplicitSetting(bool enabled)
    {
        var policy = new PlaybackEnvironmentPolicy();
        policy.Update(true, true, true, enabled, true);
        Assert.AreEqual(enabled ? PlaybackEnvironmentAction.Resume : PlaybackEnvironmentAction.None,
            policy.Update(false, true, false, enabled, true));
        Assert.IsFalse(policy.IsResumePending);
    }

    [TestMethod]
    public void PauseOrStopDuringInterruptionCancelsResume()
    {
        var policy = new PlaybackEnvironmentPolicy();
        policy.Update(true, false, true, true, true);
        policy.UserIntent(false);
        policy.Update(false, false, false, true, true);
        Assert.AreEqual(PlaybackEnvironmentAction.None, policy.Update(false, true, false, true, true));
    }

    [TestMethod]
    public void PausedStreamNeverGetsNewIntentFromOs()
    {
        var policy = new PlaybackEnvironmentPolicy();
        policy.Update(true, false, false, true, true);
        Assert.AreEqual(PlaybackEnvironmentAction.None, policy.Update(false, true, false, true, true));
    }

    [TestMethod]
    public void OverlappingLossAndSleepNeedBothPermissionsAndBothConditionsRestored()
    {
        var policy = new PlaybackEnvironmentPolicy();
        policy.Update(false, false, true, false, true);
        policy.Update(true, false, false, false, true);
        Assert.AreEqual(PlaybackEnvironmentAction.None, policy.Update(true, true, false, false, true));
        Assert.AreEqual(PlaybackEnvironmentAction.None, policy.Update(false, true, false, false, true));
    }

    [TestMethod]
    public void NewStationWhileOfflineReplacesIntentWithoutOpeningAStream()
    {
        var policy = new PlaybackEnvironmentPolicy();
        policy.Update(false, false, false, false, true);
        policy.UserIntent(true);
        Assert.IsTrue(policy.IsBlocked);
        Assert.IsTrue(policy.IsResumePending);
        Assert.AreEqual(PlaybackEnvironmentAction.Resume, policy.Update(false, true, false, false, true));
    }

    [TestMethod]
    public void DisabledNetworkResumeRequiresManualPlay()
    {
        var policy = new PlaybackEnvironmentPolicy();
        policy.Update(false, false, true, false, false);
        Assert.AreEqual(PlaybackEnvironmentAction.None, policy.Update(false, true, false, false, false));
        Assert.IsFalse(policy.IsBlocked);
        Assert.IsFalse(policy.IsResumePending);
    }
}
