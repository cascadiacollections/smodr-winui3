using System.Net;
using Microsoft.Extensions.DependencyInjection;
using smodr.Services;

namespace smodr.Tests.Services;

[TestClass]
public sealed class RadioDirectoryHttpClientsTests
{
    [TestMethod]
    public async Task DirectoryReadRetriesOnceButPlayReportDoesNot()
    {
        var readCount = 0;
        var reportCount = 0;
        var services = new ServiceCollection();
        services.AddRadioDirectoryHttpClients();
        services.AddSingleton<IRadioBrowserServerProvider>(new StubServers());
        services.AddHttpClient(RadioDirectoryHttpClients.ReadClient)
            .ConfigurePrimaryHttpMessageHandler(() => new StubHandler(_ =>
            {
                readCount++;
                return readCount == 1
                    ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                    : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[]") };
            }));
        services.AddHttpClient(RadioDirectoryHttpClients.ReportClient)
            .ConfigurePrimaryHttpMessageHandler(() => new StubHandler(_ =>
            {
                reportCount++;
                return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            }));

        using var provider = services.BuildServiceProvider(validateScopes: true);
        var directory = provider.GetRequiredService<RadioDirectoryService>();
        Assert.IsEmpty(await directory.GetPopularStationsAsync());
        Assert.AreEqual(2, readCount);

        await directory.ReportPlayAsync("bdb9fa3b-5672-4e0e-9b75-dcb19295c483");
        Assert.AreEqual(3, reportCount, "Each fallback host may be tried once, but telemetry must not be retried per host.");
        Assert.AreEqual(2, readCount);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }

    private sealed class StubServers : IRadioBrowserServerProvider
    {
        public Task<IReadOnlyList<Uri>> GetServersAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Uri>>((Uri[])[new Uri("https://one.example/"), new Uri("https://two.example/"), new Uri("https://three.example/")]);
    }
}
