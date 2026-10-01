namespace smodr.Services;

public interface IRadioPrivacySettings
{
    RadioPrivacyChoices Current { get; }
    bool IsPlayReportingEnabled { get; }
    bool IsAlbumArtworkEnabled { get; }
    Task SetPlayReportingEnabledAsync(bool enabled);
    Task SetAlbumArtworkEnabledAsync(bool enabled);
    Task FlushAsync();
}
