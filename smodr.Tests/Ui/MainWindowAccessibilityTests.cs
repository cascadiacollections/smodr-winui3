using System.Xml.Linq;

namespace smodr.Tests.Ui;

[TestClass]
public sealed class MainWindowAccessibilityTests
{
    private static readonly XNamespace _xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    [TestMethod]
    public void NowPlayingControlsHaveNamesAndScaledLayoutCanScroll()
    {
        var document = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "MainWindow.xaml"));
        foreach (var viewName in new[] { "NowPlayingView", "MiniPlayer" })
        {
            var view = document.Descendants().Single(element => (string?)element.Attribute(_xaml + "Name") == viewName);
            foreach (var button in view.Descendants().Where(element => element.Name.LocalName == "Button"
                && element.Attribute("Content") is null))
            {
                var accessibleName = (string?)button.Attribute("AutomationProperties.Name");
                Assert.IsFalse(string.IsNullOrWhiteSpace(accessibleName),
                    $"Icon-only button in {viewName} needs an accessible name.");
            }
        }

        var nowPlaying = document.Descendants().Single(element =>
            (string?)element.Attribute(_xaml + "Name") == "NowPlayingView");
        Assert.IsTrue(nowPlaying.Descendants().Any(element => element.Name.LocalName == "ScrollViewer"),
            "Expanded Now Playing must scroll at high text scaling.");
        Assert.IsTrue(nowPlaying.Descendants().Any(element =>
            (string?)element.Attribute(_xaml + "Name") == "CloseNowPlayingButton"),
            "Keyboard focus needs a close target when Now Playing opens.");
    }

    [TestMethod]
    public void NowPlayingNavigationWiresKeyboardDismissalAndFocusTargets()
    {
        var document = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "MainWindow.xaml"));
        var nowPlaying = document.Descendants().Single(element =>
            (string?)element.Attribute(_xaml + "Name") == "NowPlayingView");
        var open = document.Descendants().Single(element =>
            (string?)element.Attribute(_xaml + "Name") == "OpenNowPlayingButton");
        var close = nowPlaying.Descendants().Single(element =>
            (string?)element.Attribute(_xaml + "Name") == "CloseNowPlayingButton");
        Assert.AreEqual("NowPlayingView_KeyDown", (string?)nowPlaying.Attribute("KeyDown"));
        Assert.AreEqual("OpenNowPlaying_Click", (string?)open.Attribute("Click"));
        Assert.AreEqual("CloseNowPlaying_Click", (string?)close.Attribute("Click"));

        var code = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "MainWindow.xaml.cs"));
        StringAssert.Contains(code, "CloseNowPlayingButton.Focus(FocusState.Programmatic)", StringComparison.Ordinal);
        StringAssert.Contains(code, "OpenNowPlayingButton.Focus(FocusState.Programmatic)", StringComparison.Ordinal);
        StringAssert.Contains(code, "e.Key != VirtualKey.Escape", StringComparison.Ordinal);
    }

    [TestMethod]
    public void SettingsCardsKeepControlsNamedAndScrollable()
    {
        var document = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "MainWindow.xaml"));
        var settings = document.Descendants().Single(element =>
            (string?)element.Attribute(_xaml + "Name") == "SettingsView");
        Assert.IsTrue(settings.Descendants().Any(element => element.Name.LocalName == "ScrollViewer"));
        Assert.HasCount(3, settings.Descendants().Where(element => element.Name.LocalName == "SettingsCard"));
        foreach (var action in settings.Descendants().Where(element => element.Name.LocalName == "ToggleSwitch"
            || (element.Name.LocalName == "SettingsCard" && (string?)element.Attribute("IsClickEnabled") == "True")))
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace((string?)action.Attribute("AutomationProperties.Name")),
                $"Settings action {action.Name.LocalName} needs an accessible name.");
        }
    }
}
