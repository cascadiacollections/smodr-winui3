using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using smodr.Services;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace smodr;

#pragma warning disable CA1001 // MainWindow_Closed owns cancellation and teardown.
public sealed partial class MainWindow
#pragma warning restore CA1001
{
    private bool _exportOpen;

    private async void ExportHistory_Click(object sender, RoutedEventArgs e) =>
        await ExportAsync("listening-history", ViewModel.ExportHistoryAsync);

    private async void ExportDiagnostics_Click(object sender, RoutedEventArgs e) =>
        await ExportAsync("runtime-diagnostics", () => Task.FromResult(LocalDataExport.Diagnostics(RuntimeDiagnostics.Counters)));

    private async Task ExportAsync(string name, Func<Task<string>> createSnapshot)
    {
        if (_closed || _exportOpen || _confirmOpen || _licensesOpen) return;
        _exportOpen = true;
        try
        {
            var picker = new FileSavePicker { SuggestedFileName = name };
            picker.FileTypeChoices.Add("JSON", [".json"]);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
            var file = await picker.PickSaveFileAsync();
            if (file is null || _closed) return;
            await FileIO.WriteTextAsync(file, await createSnapshot());
        }
        catch (Exception exception)
        {
            AppDiagnostics.Record("local-export.write", exception);
            if (!_closed && !_confirmOpen && !_licensesOpen)
            {
                try
                {
                    await new ContentDialog
                    {
                        Title = "Export could not be saved",
                        Content = "Choose a writable location and try again.",
                        CloseButtonText = "Close",
                        XamlRoot = Content.XamlRoot
                    }.ShowAsync();
                }
                catch (Exception dialogException) { AppDiagnostics.Record("local-export.dialog", dialogException); }
            }
        }
        finally { _exportOpen = false; }
    }
}
