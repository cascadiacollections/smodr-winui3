using Windows.Networking.Connectivity;
using PowerManager = Microsoft.Windows.System.Power.PowerManager;
using SystemSuspendStatus = Microsoft.Windows.System.Power.SystemSuspendStatus;

namespace smodr.Services;

/// <summary>OS subscriptions only; the consumer marshals immutable observations to its owner thread.</summary>
internal sealed class WindowsPlaybackEnvironment(Action<bool?, bool?> changed) : IDisposable
{
    private bool _powerSubscribed;
    private bool _networkSubscribed;
    private int _disposed;

    public void Start()
    {
        try
        {
            PowerManager.SystemSuspendStatusChanged += SuspendChanged;
            _powerSubscribed = true;
            PublishPower();
        }
        catch (Exception exception) { AppDiagnostics.Record("playback.power-watch", exception); }
        try
        {
            NetworkInformation.NetworkStatusChanged += NetworkChanged;
            _networkSubscribed = true;
            PublishNetwork();
        }
        catch (Exception exception) { AppDiagnostics.Record("playback.network-watch", exception); }
    }

    private void SuspendChanged(object? sender, object args) => PublishPower();
    private void NetworkChanged(object sender) => PublishNetwork();

    private void PublishPower()
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        try
        {
            var status = PowerManager.SystemSuspendStatus;
            if (status == SystemSuspendStatus.Uninitialized) return;
            changed(status == SystemSuspendStatus.Entering, null);
        }
        catch (Exception exception) { AppDiagnostics.Record("playback.power-read", exception); }
    }

    private void PublishNetwork()
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        try
        {
            // NCSI InternetAccess is not required: LAN radio and captive-network
            // streams may work. Only a complete lack of connected profiles blocks playback.
            var connected = NetworkInformation.GetConnectionProfiles().Any(profile =>
                profile.GetNetworkConnectivityLevel() != NetworkConnectivityLevel.None);
            changed(null, connected);
        }
        catch (Exception exception) { AppDiagnostics.Record("playback.network-read", exception); }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        if (_powerSubscribed)
        {
            try { PowerManager.SystemSuspendStatusChanged -= SuspendChanged; }
            catch (Exception exception) { AppDiagnostics.Record("playback.power-unwatch", exception); }
        }
        if (_networkSubscribed)
        {
            try { NetworkInformation.NetworkStatusChanged -= NetworkChanged; }
            catch (Exception exception) { AppDiagnostics.Record("playback.network-unwatch", exception); }
        }
        GC.SuppressFinalize(this);
    }
}
