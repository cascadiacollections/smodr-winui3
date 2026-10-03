using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using smodr.Models;
using smodr.Services;
using smodr.ViewModels;

namespace smodr;

public partial class App : Application
{
    private Window? _window;
    private readonly ServiceProvider _services;
    private static StationLaunchLink? _pendingStationLink;
    private static string? _pendingStationId;
    public static string StorageDirectory { get; private set; } = AppStorageResolver.LegacyDirectory;

    public static Window? MainWindow { get; private set; }

    public static void HandleActivation(AppActivationArguments activation)
    {
        StationLaunchLink? link = null;
        if (activation.Kind == ExtendedActivationKind.Protocol
            && activation.Data is Windows.ApplicationModel.Activation.IProtocolActivatedEventArgs protocol
            && StationLaunchLink.TryParse(protocol.Uri, out var stationLink))
            link = stationLink;
        Interlocked.Exchange(ref _pendingStationLink, link);
        var arguments = activation.Data switch
        {
            Windows.ApplicationModel.Activation.ILaunchActivatedEventArgs launch => launch.Arguments,
            Windows.ApplicationModel.Activation.ICommandLineActivatedEventArgs command => command.Operation.Arguments,
            _ => string.Empty
        };
        Interlocked.Exchange(ref _pendingStationId,
            RadioQuickLaunch.TryParse(arguments, out var id, Environment.ProcessPath) ? id : null);
        ActivateMainWindow();
    }

    public static void ActivateMainWindow()
    {
        var window = MainWindow;
        if (window is null)
        {
            return;
        }

        window.DispatcherQueue.TryEnqueue(() =>
        {
            var handle = WinRT.Interop.WindowNative.GetWindowHandle(window);
            if (IsIconic(handle))
            {
                ShowWindow(handle, 9); // SW_RESTORE
            }

            window.Activate();
            if (window is MainWindow mainWindow
                && Interlocked.Exchange(ref _pendingStationLink, null) is { } link)
                _ = mainWindow.OpenStationLinkAsync(link);
            if (window is MainWindow quickWindow
                && Interlocked.Exchange(ref _pendingStationId, null) is { } id)
                _ = quickWindow.OpenQuickStationAsync(id);
        });
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool IsIconic(nint window);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool ShowWindow(nint window, int command);

    public App()
    {
        InitializeComponent();
        StorageDirectory = AppStorageResolver.ResolveDirectory();
        UnhandledException += (_, args) => AppDiagnostics.Record("winui.unhandled", args.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception exception)
            {
                AppDiagnostics.Record("process.unhandled", exception);
            }
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            AppDiagnostics.Record("task.unobserved", args.Exception);
            args.SetObserved();
        };
        var services = new ServiceCollection();
        services.AddSingleton(new RadioPlaybackPreferences(Path.Combine(StorageDirectory, "playback-settings.json")));
        services.AddSingleton(new RadioAppearancePreferences(Path.Combine(StorageDirectory, "appearance-settings.json")));
        services.AddSingleton<RadioStreamPrewarmer>();
        services.AddSingleton<RadioJumpList>();
        services.AddSingleton<IRadioPlayer, AudioService>();
        services.AddSingleton<ITrackHistoryService>(new TrackHistoryService(
            Path.Combine(StorageDirectory, "track-history.json")));
        services.AddHttpClient<IContinuousTrackMetadataReader, IcyMetadataStreamReader>(client =>
            client.Timeout = Timeout.InfiniteTimeSpan);
        services.AddHttpClient<ITrackMetadataProbe, IcyMetadataProbe>(client =>
            client.Timeout = TimeSpan.FromSeconds(35));
        services.AddSingleton(provider => new IcyTrackMonitor(
            provider.GetRequiredService<ITrackMetadataProbe>(),
            continuousReader: provider.GetRequiredService<IContinuousTrackMetadataReader>()));
        services.AddSingleton<IRadioLibraryService>(new RadioLibraryService(
            Path.Combine(StorageDirectory, "library.json")));
        services.AddSingleton<IRadioDirectorySnapshotCache>(new RadioDirectorySnapshotCache(
            Path.Combine(StorageDirectory, "directory-cache.json")));
        services.AddSingleton<IRadioPrivacySettings>(new RadioPrivacySettings(
            Path.Combine(StorageDirectory, "privacy-settings.json")));
        services.AddHttpClient<IAlbumArtworkLookup, AlbumArtworkLookup>(client =>
            client.Timeout = TimeSpan.FromSeconds(8))
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        services.AddSingleton<PlaybackSleepTimer>();
        services.AddRadioDirectoryHttpClients();
        var shoutcastKey = Environment.GetEnvironmentVariable("SHOUTKIT_SHOUTCAST_API_KEY");
        if (!string.IsNullOrWhiteSpace(shoutcastKey))
        {
            services.AddHttpClient("shoutcast", client => client.Timeout = TimeSpan.FromSeconds(8))
                .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
            services.AddSingleton(provider => new ShoutcastDirectoryService(
                provider.GetRequiredService<IHttpClientFactory>().CreateClient("shoutcast"), shoutcastKey));
            services.AddSingleton<IStationStreamResolver>(provider => provider.GetRequiredService<ShoutcastDirectoryService>());
            services.AddTransient<IRadioDirectoryService>(provider => new FallbackRadioDirectory(
                provider.GetRequiredService<RadioDirectoryService>(), provider.GetRequiredService<ShoutcastDirectoryService>()));
        }
        services.AddSingleton<RadioMainViewModel>();
        services.AddSingleton(provider => new RadioSettingsViewModel(
            provider.GetRequiredService<IRadioPrivacySettings>(),
            provider.GetRequiredService<RadioMainViewModel>().SetAlbumArtworkEnabledAsync,
            playback: provider.GetRequiredService<RadioPlaybackPreferences>(),
            clearWarmup: provider.GetRequiredService<RadioStreamPrewarmer>().Clear,
            jumpListSupported: RadioJumpList.IsSupported,
            refreshJumpList: () =>
            {
                var viewModel = provider.GetRequiredService<RadioMainViewModel>();
                _ = provider.GetRequiredService<RadioJumpList>().UpdateAsync(
                    (RadioStation[])[.. viewModel.Favorites], (RadioStation[])[.. viewModel.Recents]);
            }, appearance: provider.GetRequiredService<RadioAppearancePreferences>()));
        _services = services.BuildServiceProvider(validateScopes: true);
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow(_services.GetRequiredService<RadioMainViewModel>(),
            _services.GetRequiredService<RadioSettingsViewModel>(),
            _services.GetRequiredService<RadioStreamPrewarmer>(), _services.GetRequiredService<RadioJumpList>());
        MainWindow = _window;
        _window.Closed += (_, _) => _services.Dispose();
        _window.Activate();
        ActivateMainWindow();
    }
}
