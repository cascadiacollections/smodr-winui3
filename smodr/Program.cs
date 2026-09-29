using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

namespace smodr;

public static class Program
{
    [STAThread]
    public static async Task Main(string[] args)
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();

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
