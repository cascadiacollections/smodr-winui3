using System.Xml.Linq;

namespace smodr.Tests.Ui;

[TestClass]
public sealed class MainWindowAccessibilityTests
{
    private static readonly XNamespace _xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
    private static readonly string[] _backdropLabels = ["Acrylic", "Mica", "Solid"];

    [TestMethod]
    public void InteractiveXamlElementsExposeReadableNames()
    {
        foreach (var file in new[] { "MainWindow.xaml", "StationRowControl.xaml", "SoftwareLicensesView.xaml" })
        {
            var document = XDocument.Load(Path.Combine(AppContext.BaseDirectory, file));
            foreach (var element in document.Descendants().Where(element => element.Name.LocalName is
                "Button" or "HyperlinkButton" or "ToggleSwitch" or "ComboBox" or "ListView" or "MenuFlyoutItem"))
            {
                var automationName = (string?)element.Attribute("AutomationProperties.Name");
                var content = (string?)element.Attribute("Content");
                var text = (string?)element.Attribute("Text");
                var descendantText = element.Descendants().Any(child => child.Name.LocalName == "TextBlock"
                    && !string.IsNullOrWhiteSpace((string?)child.Attribute("Text")));
                Assert.IsTrue(!string.IsNullOrWhiteSpace(automationName)
                    || (!string.IsNullOrWhiteSpace(content) && content[0] != '{')
                    || !string.IsNullOrWhiteSpace(text) || descendantText,
                    $"{file}: {element.Name.LocalName} needs a readable content label or AutomationProperties.Name.");
            }
        }
    }

    [TestMethod]
    public void AppearanceUsesNativeBackdropAccessibleChoiceAndThemedSolidSurface()
    {
        var document = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "MainWindow.xaml"));
        Assert.IsTrue(document.Descendants().Any(element => element.Name.LocalName == "DesktopAcrylicBackdrop"));
        var choice = document.Descendants().Single(element => (string?)element.Attribute(_xaml + "Name") == "BackdropBox");
        Assert.AreEqual("Window background", (string?)choice.Attribute("AutomationProperties.Name"));
        CollectionAssert.AreEqual(_backdropLabels, choice.Elements().Select(element => (string?)element.Attribute("Content")).ToArray());
        var fallback = document.Descendants().Single(element => (string?)element.Attribute(_xaml + "Name") == "SolidWindowBackground");
        Assert.AreEqual("{ThemeResource ApplicationPageBackgroundThemeBrush}", (string?)fallback.Attribute("Background"));
        Assert.AreEqual("3", (string?)fallback.Attribute("Grid.RowSpan"));
    }

    [TestMethod]
    public void StationBrowsersUseRowsWithoutOuterScrollHosts()
    {
        var document = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "MainWindow.xaml"));
        foreach (var viewName in new[] { "ListenNowView", "FavoritesView", "SearchView" })
        {
            var view = document.Descendants().Single(element => (string?)element.Attribute(_xaml + "Name") == viewName);
            Assert.IsFalse(view.Descendants().Any(element => element.Name.LocalName is "ScrollViewer" or "GridView"));
            foreach (var list in view.Descendants().Where(element => element.Name.LocalName == "ListView"
                && element.Descendants().Any(child => child.Name.LocalName == "StationRowControl")))
            {
                Assert.AreEqual("StationList_ContainerContentChanging", (string?)list.Attribute("ContainerContentChanging"));
                Assert.IsFalse(string.IsNullOrWhiteSpace((string?)list.Attribute("AutomationProperties.Name")));
                Assert.AreEqual("StationList_ItemClick", (string?)list.Attribute("ItemClick"));
                Assert.IsTrue(int.Parse((string)list.Attribute("Grid.Row")!, System.Globalization.CultureInfo.InvariantCulture) > 0);
            }
        }
        var row = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "StationRowControl.xaml"));
        Assert.IsTrue(row.Descendants().Any(element => (string?)element.Attribute(_xaml + "Name") == "PlaybackLabel"));
        Assert.IsTrue(row.Descendants().Any(element => (string?)element.Attribute(_xaml + "Name") == "FavoriteButton"
            && element.Attribute("AutomationProperties.Name") is not null));
    }

    [TestMethod]
    public void SoftwareLicenseNoticesUseBoundedVirtualizedAccessibleList()
    {
        var document = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "SoftwareLicensesView.xaml"));
        var grid = document.Descendants().Single(element => element.Name.LocalName == "Grid");
        Assert.AreEqual("480", (string?)grid.Attribute("Height"));
        Assert.IsFalse(document.Descendants().Any(element => element.Name.LocalName == "ScrollViewer"),
            "An outer scroll host would give the list unbounded layout space.");
        var list = document.Descendants().Single(element => element.Name.LocalName == "ListView");
        Assert.IsTrue(list.Descendants().Any(element => element.Name.LocalName == "ItemsStackPanel"));
        Assert.AreEqual("Software license notices", (string?)list.Attribute("AutomationProperties.Name"));
        var text = list.Descendants().Single(element => element.Name.LocalName == "TextBlock");
        Assert.AreEqual("True", (string?)text.Attribute("IsTextSelectionEnabled"));
        var code = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "MainWindow.xaml.cs"));
        StringAssert.Contains(code, "if (_closed || _licensesOpen) return;", StringComparison.Ordinal);
        StringAssert.Contains(code, "WaitAsync(cancellationToken)", StringComparison.Ordinal);
    }

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
        Assert.HasCount(10, settings.Descendants().Where(element => element.Name.LocalName == "SettingsCard"));
        foreach (var action in settings.Descendants().Where(element => element.Name.LocalName is "ToggleSwitch" or "ComboBox"
            || (element.Name.LocalName == "SettingsCard" && (string?)element.Attribute("IsClickEnabled") == "True")))
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace((string?)action.Attribute("AutomationProperties.Name")),
                $"Settings action {action.Name.LocalName} needs an accessible name.");
        }
    }

    [TestMethod]
    public void SettingsBindDurableChoicesBusyStateAndLocalErrors()
    {
        var document = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "MainWindow.xaml"));
        var settings = document.Descendants().Single(element =>
            (string?)element.Attribute(_xaml + "Name") == "SettingsView");
        foreach (var toggle in settings.Descendants().Where(element => element.Name.LocalName == "ToggleSwitch"))
        {
            var name = (string?)toggle.Attribute(_xaml + "Name");
            var property = name switch
            {
                "PlayReportingSwitch" => "IsPlayReportingEnabled",
                "AlbumArtworkSwitch" => "IsAlbumArtworkEnabled",
                "PrewarmSwitch" => "IsStreamPrewarmingEnabled",
                "LoopBroadcastsSwitch" => "IsLoopFinishedBroadcastsEnabled",
                "ResumeSleepSwitch" => "IsResumeAfterSleepEnabled",
                "ResumeNetworkSwitch" => "IsResumeAfterNetworkLossEnabled",
                "JumpListSwitch" => "IsJumpListEnabled",
                _ => throw new InvalidOperationException("Unexpected settings toggle")
            };
            var busy = name == "JumpListSwitch" ? "CanEditJumpLists"
                : name is "PrewarmSwitch" or "LoopBroadcastsSwitch" or "ResumeSleepSwitch" or "ResumeNetworkSwitch" ? "CanEditPlayback" : "CanEdit";
            Assert.AreEqual($"{{x:Bind Settings.{busy}, Mode=OneWay}}", (string?)toggle.Attribute("IsEnabled"));
            Assert.AreEqual($"{{x:Bind Settings.{property}, Mode=OneWay}}", (string?)toggle.Attribute("IsOn"));
        }
        var equalizer = settings.Descendants().Single(element => (string?)element.Attribute(_xaml + "Name") == "EqualizerBox");
        Assert.AreEqual("{x:Bind Settings.CanEditPlayback, Mode=OneWay}", (string?)equalizer.Attribute("IsEnabled"));
        Assert.AreEqual("{x:Bind Settings.SelectedEqualizerPreset, Mode=OneWay}", (string?)equalizer.Attribute("SelectedIndex"));
        var error = settings.Descendants().Single(element => element.Name.LocalName == "InfoBar");
        Assert.AreEqual("{x:Bind Settings.HasError, Mode=OneWay}", (string?)error.Attribute("IsOpen"));
        Assert.AreEqual("{x:Bind Settings.ErrorMessage, Mode=OneWay}", (string?)error.Attribute("Message"));
    }
}
