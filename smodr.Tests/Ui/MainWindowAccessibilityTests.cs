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
}
