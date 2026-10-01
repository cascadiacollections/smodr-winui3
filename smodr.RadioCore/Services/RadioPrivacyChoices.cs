namespace smodr.Services;

/// <summary>A coherent snapshot of the device-local network privacy choices.</summary>
public sealed record RadioPrivacyChoices(bool PlayReportingEnabled, bool AlbumArtworkEnabled)
{
    public static RadioPrivacyChoices Default { get; } = new(true, true);
    public static RadioPrivacyChoices FailClosed { get; } = new(false, false);
}
