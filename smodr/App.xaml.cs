using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using smodr.Services;
using smodr.ViewModels;
using Windows.ApplicationModel;
using Windows.Storage;

namespace smodr;

public partial class App : Application
{
    private Window? _window;
    private readonly ServiceProvider _services;
    public static string StorageDirectory { get; private set; } = LegacyStorageDirectory;

    private static string LegacyStorageDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CascadiaCollections", "ShoutkitWindows");

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
        StorageDirectory = ResolveStorageDirectory();
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

    private static string ResolveStorageDirectory()
    {
        string packaged;
        try
        {
            _ = Package.Current.Id;
            packaged = ApplicationData.Current.LocalFolder.Path;
        }
        catch (Exception exception) when (exception is InvalidOperationException
            or System.Runtime.InteropServices.COMException)
        {
            return LegacyStorageDirectory; // Unpackaged preview has no package identity.
        }

        try { PackagedDataMigration.Import(LegacyStorageDirectory, packaged); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            AppDiagnostics.Record("package.data-import", exception);
        }
        return packaged;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow(_services.GetRequiredService<RadioMainViewModel>());
        MainWindow = _window;
        _window.Closed += (_, _) => _services.Dispose();
        _window.Activate();
    }
}
