namespace smodr.Services;

public interface IRadioPrivacySettings
{
    bool IsPlayReportingEnabled { get; }
    bool IsAlbumArtworkEnabled { get; }
    Task SetPlayReportingEnabledAsync(bool enabled);
    Task SetAlbumArtworkEnabledAsync(bool enabled);
    Task FlushAsync();
}
