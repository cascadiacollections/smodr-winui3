using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using smodr.Services;
using smodr.ViewModels;

namespace smodr;

public partial class App : Application
{
    private Window? _window;
    private readonly ServiceProvider _services;
    public static string StorageDirectory { get; private set; } = AppStorageResolver.LegacyDirectory;

    public static Window? MainWindow { get; private set; }

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
        services.AddHttpClient<RadioDirectoryService>(client =>
            client.Timeout = TimeSpan.FromSeconds(8));
        services.AddTransient<IRadioDirectoryService>(provider => provider.GetRequiredService<RadioDirectoryService>());
        services.AddTransient<IStationPlayReporter>(provider => provider.GetRequiredService<RadioDirectoryService>());
        services.AddSingleton<RadioMainViewModel>();
        _services = services.BuildServiceProvider(validateScopes: true);
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow(_services.GetRequiredService<RadioMainViewModel>());
        MainWindow = _window;
        _window.Closed += (_, _) => _services.Dispose();
        _window.Activate();
    }
}
