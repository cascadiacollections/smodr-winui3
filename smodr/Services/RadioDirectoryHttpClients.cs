using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Polly;

namespace smodr.Services;

/// <summary>Only idempotent directory reads receive an extra per-host retry.</summary>
internal static class RadioDirectoryHttpClients
{
    internal const string ReadClient = "radio-browser-read";
    internal const string ReportClient = "radio-browser-report";

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
