using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using smodr.Services;
using Windows.Storage;

namespace smodr;

public static class Program
{
    [STAThread]
    public static async Task Main(string[] args)
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();

        // The disposable CI package exercises the same import path as App's
        // constructor without opening a WinUI window or joining the player instance.
        if (args is ["--ci-verify-packaged-import"]
            && Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true")
        {
            try
            {
                var directory = AppStorageResolver.ResolveDirectory();
                if (!string.Equals(directory, ApplicationData.Current.LocalFolder.Path,
                    StringComparison.OrdinalIgnoreCase)) Environment.ExitCode = 1;
            }
            catch (Exception exception)
            {
                AppDiagnostics.Record("ci.package-import", exception);
                Environment.ExitCode = 1;
            }
            return;
        }

        var current = AppInstance.GetCurrent();
        var instance = AppInstance.FindOrRegisterForKey("CascadiaCollections.Shoutkit.Windows");
        if (!instance.IsCurrent)
        {
            await instance.RedirectActivationToAsync(current.GetActivatedEventArgs());
            return;
        }

        instance.Activated += (_, _) => App.ActivateMainWindow();
        Application.Start(initialization =>
        {
            SynchronizationContext.SetSynchronizationContext(
                new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
            _ = new App();
        });
    }
}
