namespace smodr.Services;

public interface IStationPlayReporter
{
    Task ReportPlayAsync(string stationId, CancellationToken cancellationToken = default);
}
