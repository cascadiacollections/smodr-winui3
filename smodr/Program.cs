using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using smodr.Services;
using Windows.Storage;

namespace smodr;

public static class Program
{
    // Main must stay synchronous: the compiler does not copy [STAThread] onto the
    // entry point it synthesizes for an async Main, so XAML would start on an MTA
    // thread and the first out-of-process UI Automation query (Narrator, Voice
    // Access, test tools) crashes the app. ProgramEntryPointTests guards this.
    [STAThread]
    public static void Main(string[] args)
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
            // Run the redirect off the STA; the blocking wait still pumps COM, as Windows App SDK's sample requires.
            var activation = current.GetActivatedEventArgs();
            Task.Run(() => instance.RedirectActivationToAsync(activation).AsTask()).Wait();
            return;
        }

        instance.Activated += (_, activation) => App.HandleActivation(activation);
        App.HandleActivation(current.GetActivatedEventArgs());
        Application.Start(initialization =>
        {
            SynchronizationContext.SetSynchronizationContext(
                new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
            _ = new App();
        });
    }
}
