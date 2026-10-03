using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Polly;

namespace smodr.Services;

/// <summary>Only idempotent directory reads receive an extra per-host retry.</summary>
internal static class RadioDirectoryHttpClients
{
    /// <summary>
    /// The read client is used for idempotent directory reads. The report client is used for non-idempotent play count reports. The report client does not receive a retry policy because a retry could result in double-counting a single user action.
    /// </summary>
    internal const string ReadClient = "radio-browser-read";
    /// <summary>
    /// The report client is used for non-idempotent play count reports. The report client does not receive a retry policy because a retry could result in double-counting a single user action.
    /// </summary>
    internal const string ReportClient = "radio-browser-report";

    /// <summary>
    /// Adds the radio directory HTTP clients to the service collection.
    /// </summary>
    /// <param name="services">The service collection to add the clients to.</param>
    /// <returns>The updated service collection.</returns>
    public static IServiceCollection AddRadioDirectoryHttpClients(this IServiceCollection services)
    {
        services.AddSingleton<IRadioBrowserServerProvider, RadioBrowserServers>();
        services.AddHttpClient(ReadClient, client => client.Timeout = TimeSpan.FromSeconds(8))
            .AddResilienceHandler("directory-read-retry", builder => builder.AddRetry(new HttpRetryStrategyOptions
            {
                MaxRetryAttempts = 1,
                Delay = TimeSpan.FromMilliseconds(200),
                UseJitter = true,
                ShouldRetryAfterHeader = false,
                ShouldHandle = args => ValueTask.FromResult(
                    args.Outcome.Exception is HttpRequestException
                    || args.Outcome.Result?.StatusCode == HttpStatusCode.ServiceUnavailable),
            }));

        // /json/url/{uuid} contributes to the community play count. A retry
        // after a lost response could count one user action more than once.
        services.AddHttpClient(ReportClient, client => client.Timeout = TimeSpan.FromSeconds(8));
        services.AddTransient(provider =>
        {
            var factory = provider.GetRequiredService<IHttpClientFactory>();
            return new RadioDirectoryService(factory.CreateClient(ReadClient),
                reportClient: factory.CreateClient(ReportClient),
                serverProvider: provider.GetRequiredService<IRadioBrowserServerProvider>());
        });
        services.AddTransient<IRadioDirectoryService>(provider => provider.GetRequiredService<RadioDirectoryService>());
        services.AddTransient<IStationPlayReporter>(provider => provider.GetRequiredService<RadioDirectoryService>());
        return services;
    }
}
