using System.Net.Sockets;
using Microsoft.Extensions.Time.Testing;
using smodr.Services;

namespace smodr.Tests.Services;

[TestClass]
public sealed class RadioBrowserServersTests
{
    [TestMethod]
    public async Task DiscoveryIsCachedFilteredAndRefreshed()
    {
        var clock = new FakeTimeProvider();
        var calls = 0;
        using var servers = new RadioBrowserServers(_ =>
        {
            calls++;
            return Task.FromResult<IReadOnlyList<Uri>>((Uri[])[
                new Uri($"https://mirror{calls}.api.radio-browser.info/"),
                new Uri("http://insecure.api.radio-browser.info/"), new Uri("https://outside.example/")]);
        }, clock);
        var first = await servers.GetServersAsync();
        Assert.AreEqual("mirror1.api.radio-browser.info", first.Single().Host);
        Assert.AreEqual(first.Single(), (await servers.GetServersAsync()).Single());
        Assert.AreEqual(1, calls);
        clock.Advance(TimeSpan.FromHours(6));
        Assert.AreEqual("mirror2.api.radio-browser.info", (await servers.GetServersAsync()).Single().Host);
        Assert.AreEqual(2, calls);
    }

    [TestMethod]
    public async Task DnsFailureKeepsPreviousMirrorsAndBacksOffRefresh()
    {
        var clock = new FakeTimeProvider();
        var calls = 0;
        using var servers = new RadioBrowserServers(_ => ++calls == 1
            ? Task.FromResult<IReadOnlyList<Uri>>((Uri[])[new Uri("https://working.api.radio-browser.info/")])
            : Task.FromException<IReadOnlyList<Uri>>(new SocketException()), clock);
        await servers.GetServersAsync();
        clock.Advance(TimeSpan.FromHours(6));
        Assert.AreEqual("working.api.radio-browser.info", (await servers.GetServersAsync()).Single().Host);
        await servers.GetServersAsync();
        Assert.AreEqual(2, calls);
        clock.Advance(TimeSpan.FromMinutes(1));
        await servers.GetServersAsync();
        Assert.AreEqual(3, calls);
    }

    [TestMethod]
    public async Task FirstLaunchFailureUsesAggregateAndCallerCancellationDoesNotPoisonCache()
    {
        using var servers = new RadioBrowserServers(_ => Task.FromException<IReadOnlyList<Uri>>(new SocketException()));
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        await Assert.ThrowsAsync<OperationCanceledException>(() => servers.GetServersAsync(cancellation.Token));
        Assert.AreEqual("all.api.radio-browser.info", (await servers.GetServersAsync()).Single().Host);
    }
}
