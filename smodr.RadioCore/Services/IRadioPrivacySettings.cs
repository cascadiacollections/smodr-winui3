namespace smodr.Services;

public interface IRadioPrivacySettings
{
    bool IsPlayReportingEnabled { get; }
    Task SetPlayReportingEnabledAsync(bool enabled);
    Task FlushAsync();
}
