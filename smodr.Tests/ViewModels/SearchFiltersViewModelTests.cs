using System.Xml.Linq;
using smodr.Services;
using smodr.ViewModels;

namespace smodr.Tests.ViewModels;

[TestClass]
public sealed class SearchFiltersViewModelTests
{
    private static readonly XNamespace _xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
    private static readonly string[] _expectedCountries = ["US", "GB", "DE", "JP"];

    private static readonly string[] _derivedProperties =
        [nameof(SearchFiltersViewModel.Current), nameof(SearchFiltersViewModel.IsActive), nameof(SearchFiltersViewModel.Summary)];

    private static readonly string[] _filterControls =
        ["SearchFiltersExpander", "FilterTagBox", "FilterCountryBox", "FilterBitrateBox"];

    [TestMethod]
    public void DefaultsAreInactiveAndClearIsDisabled()
    {
        var filters = new SearchFiltersViewModel();
        Assert.AreEqual(RadioSearchFilters.None, filters.Current);
        Assert.IsFalse(filters.IsActive);
        Assert.AreEqual("No filters", filters.Summary);
        Assert.IsFalse(filters.ClearCommand.CanExecute(null));
    }

    [TestMethod]
    public void EditsProduceNormalizedFiltersSummaryAndNotifications()
    {
        var filters = new SearchFiltersViewModel();
        var changed = new List<string?>();
        filters.PropertyChanged += (_, args) => changed.Add(args.PropertyName);
        var canExecuteChanges = 0;
        filters.ClearCommand.CanExecuteChanged += (_, _) => canExecuteChanges++;

        filters.TagText = "  Smooth Jazz ";
        filters.SelectedBitrateIndex = IndexOf(SearchFiltersViewModel.BitrateOptions, option => option.Kbps == 192);
        filters.SelectedCountryIndex = IndexOf(SearchFiltersViewModel.CountryOptions, option => option.Code == "GB");

        Assert.AreEqual(RadioSearchFilters.Create(192, "smooth jazz", "GB"), filters.Current);
        Assert.IsTrue(filters.ClearCommand.CanExecute(null));
        StringAssert.StartsWith(filters.Summary, "192 kbps or higher · Tag: smooth jazz · ", StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(3, canExecuteChanges);
        foreach (var name in _derivedProperties)
        {
            Assert.Contains(name, changed);
        }

        filters.ClearCommand.Execute(null);
        Assert.AreEqual(string.Empty, filters.TagText);
        Assert.AreEqual(0, filters.SelectedBitrateIndex);
        Assert.AreEqual(0, filters.SelectedCountryIndex);
        Assert.IsFalse(filters.IsActive);
    }

    [TestMethod]
    public void OutOfRangeSelectionsMeanAny()
    {
        var filters = new SearchFiltersViewModel { SelectedBitrateIndex = -1, SelectedCountryIndex = 100_000 };
        Assert.IsFalse(filters.IsActive);
    }

    [TestMethod]
    public void CountryOptionsStartWithAnyAndHaveUniqueIsoCodes()
    {
        var options = SearchFiltersViewModel.CountryOptions;
        Assert.IsNull(options[0].Code);
        Assert.AreEqual("Any country", options[0].ToString());
        var codes = options.Skip(1).Select(option => option.Code!).ToArray();
        Assert.IsGreaterThan(100, codes.Length);
        Assert.HasCount(codes.Length, codes.Distinct(StringComparer.Ordinal));
        Assert.IsTrue(codes.All(code => code.Length == 2 && code.All(char.IsAsciiLetterUpper)));
        CollectionAssert.IsSubsetOf(_expectedCountries, codes);
        Assert.IsTrue(options.All(option => !string.IsNullOrWhiteSpace(option.ToString())),
            "Narrator reads ToString() for each item.");
    }

    [TestMethod]
    public void XamlBitrateChoicesMatchTheViewModelAndFilterControlsAreWired()
    {
        var document = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "MainWindow.xaml"));
        XElement Named(string name)
        {
            return document.Descendants().Single(element => (string?)element.Attribute(_xaml + "Name") == name);
        }

        CollectionAssert.AreEqual(SearchFiltersViewModel.BitrateOptions.Select(option => option.Label).ToArray(),
            Named("FilterBitrateBox").Elements().Select(element => (string?)element.Attribute("Content")).ToArray());
        Assert.AreEqual("FilterBitrateBox_SelectionChanged",
            (string?)Named("FilterBitrateBox").Attribute("SelectionChanged"));
        Assert.AreEqual("FilterCountryBox_SelectionChanged",
            (string?)Named("FilterCountryBox").Attribute("SelectionChanged"));
        StringAssert.Contains((string?)Named("FilterTagBox").Attribute("Text"), "Mode=TwoWay",
            StringComparison.Ordinal);
        Assert.AreEqual("FilterTagBox_KeyDown", (string?)Named("FilterTagBox").Attribute("KeyDown"));
        Assert.AreEqual("64", (string?)Named("FilterTagBox").Attribute("MaxLength"));
        foreach (var name in _filterControls)
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace((string?)Named(name).Attribute("AutomationProperties.Name")),
                $"{name} needs an accessible name.");
        }

        Assert.AreEqual("ApplyFilters_Click", (string?)Named("ApplyFiltersButton").Attribute("Click"));
        StringAssert.Contains((string?)Named("ClearFiltersButton").Attribute("IsEnabled"), "SearchFilters.IsActive",
            StringComparison.Ordinal);
        Assert.IsTrue(Named("SearchView").Descendants().Contains(Named("SearchFiltersExpander")));

        var code = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "MainWindow.xaml.cs"));
        StringAssert.Contains(code, "FilterCountryBox.ItemsSource = SearchFiltersViewModel.CountryOptions",
            StringComparison.Ordinal);
        StringAssert.Contains(code, "SearchFilters.PropertyChanged -= SearchFilters_PropertyChanged",
            StringComparison.Ordinal);
    }

    [TestMethod]
    public void FavoritesHeaderOffersNamedImportAndExport()
    {
        var document = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "MainWindow.xaml"));
        var section = document.Descendants()
            .Single(element => (string?)element.Attribute(_xaml + "Name") == "FavoritesSection");
        foreach (var (name, handler) in new[]
                 {
                     ("ImportFavoritesButton", "ImportFavorites_Click"),
                     ("ExportFavoritesButton", "ExportFavorites_Click")
                 })
        {
            var button = section.Descendants()
                .Single(element => (string?)element.Attribute(_xaml + "Name") == name);
            Assert.AreEqual(handler, (string?)button.Attribute("Click"));
            StringAssert.Contains((string?)button.Attribute("AutomationProperties.Name"), "favorites",
                StringComparison.Ordinal);
        }

        var bar = document.Descendants()
            .Single(element => (string?)element.Attribute(_xaml + "Name") == "FavoritesImportBar");
        Assert.AreEqual("Polite", (string?)bar.Attribute("AutomationProperties.LiveSetting"));
    }

    private static int IndexOf<T>(IReadOnlyList<T> options, Func<T, bool> match)
    {
        for (var index = 0; index < options.Count; index++)
        {
            if (match(options[index]))
            {
                return index;
            }
        }

        throw new AssertFailedException("Option not found.");
    }
}
