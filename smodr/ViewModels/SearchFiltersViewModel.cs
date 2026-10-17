using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using smodr.Services;

namespace smodr.ViewModels;

/// <summary>A minimum-bitrate choice; list containers announce ToString() to screen readers.</summary>
public sealed record BitrateFilterOption(int? Kbps, string Label)
{
    public override string ToString()
    {
        return Label;
    }
}

/// <summary>A country choice; list containers announce ToString() to screen readers.</summary>
public sealed record CountryFilterOption(string? Code, string Label)
{
    public override string ToString()
    {
        return Label;
    }
}

/// <summary>Editable bitrate/tag/country filters for directory search, applied on the next search.</summary>
public sealed partial class SearchFiltersViewModel : ObservableObject
{
    private static readonly Lazy<CountryFilterOption[]> _countries = new(CreateCountryOptions);

    public static IReadOnlyList<BitrateFilterOption> BitrateOptions { get; } =
        (BitrateFilterOption[])
        [
            new(null, "Any bitrate"),
            .. new[] { 64, 96, 128, 192, 256, 320 }.Select(kbps =>
                new BitrateFilterOption(kbps, $"{kbps} kbps or higher"))
        ];

    /// <summary>"Any country" first, then ISO regions known to Windows, sorted by display name.</summary>
    public static IReadOnlyList<CountryFilterOption> CountryOptions => _countries.Value;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Current), nameof(IsActive), nameof(Summary))]
    [NotifyCanExecuteChangedFor(nameof(ClearCommand))]
    public partial string TagText { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Current), nameof(IsActive), nameof(Summary))]
    [NotifyCanExecuteChangedFor(nameof(ClearCommand))]
    public partial int SelectedBitrateIndex { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Current), nameof(IsActive), nameof(Summary))]
    [NotifyCanExecuteChangedFor(nameof(ClearCommand))]
    public partial int SelectedCountryIndex { get; set; }

    public RadioSearchFilters Current => RadioSearchFilters.Create(
        Option(BitrateOptions, SelectedBitrateIndex)?.Kbps, TagText,
        Option(CountryOptions, SelectedCountryIndex)?.Code);

    public bool IsActive => Current.IsActive;

    public string Summary
    {
        get
        {
            var filters = Current;
            if (!filters.IsActive)
            {
                return "No filters";
            }

            var parts = new List<string>(3);
            if (filters.MinimumBitrate is { } kbps)
            {
                parts.Add($"{kbps} kbps or higher");
            }

            if (filters.Tag is { } tag)
            {
                parts.Add($"Tag: {tag}");
            }

            if (filters.CountryCode is { } code)
            {
                parts.Add(CountryOptions.FirstOrDefault(option => option.Code == code)?.Label ?? code);
            }

            return string.Join(" · ", parts);
        }
    }

    [RelayCommand(CanExecute = nameof(IsActive))]
    public void Clear()
    {
        TagText = string.Empty;
        SelectedBitrateIndex = 0;
        SelectedCountryIndex = 0;
    }

    private static T? Option<T>(IReadOnlyList<T> options, int index) where T : class
    {
        return index >= 0 && index < options.Count ? options[index] : null;
    }

    private static CountryFilterOption[] CreateCountryOptions()
    {
        var regions = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var culture in CultureInfo.GetCultures(CultureTypes.SpecificCultures))
        {
            try
            {
                var region = new RegionInfo(culture.Name);
                var code = region.TwoLetterISORegionName;
                if (code.Length == 2 && code.All(char.IsAsciiLetterUpper))
                {
                    regions.TryAdd(code, region.DisplayName);
                }
            }
            catch (ArgumentException)
            {
                // Some specific cultures (for example, script-only ones) have no region.
            }
        }

        var comparer = StringComparer.Create(CultureInfo.CurrentCulture, true);
        return
        [
            new(null, "Any country"),
            .. regions.OrderBy(pair => pair.Value, comparer)
                .Select(pair => new CountryFilterOption(pair.Key, pair.Value))
        ];
    }
}
