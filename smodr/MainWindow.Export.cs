using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using smodr.Services;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace smodr;

#pragma warning disable CA1001 // MainWindow_Closed owns cancellation and teardown.
public sealed partial class MainWindow
#pragma warning restore CA1001
{
    private bool _exportOpen;

    private async void ExportHistory_Click(object sender, RoutedEventArgs e)
    {
        await ExportAsync("listening-history", ViewModel.ExportHistoryAsync);
    }

    private async void ExportDiagnostics_Click(object sender, RoutedEventArgs e)
    {
        await ExportAsync("runtime-diagnostics",
            () => Task.FromResult(LocalDataExport.Diagnostics(RuntimeDiagnostics.Counters)));
    }

    private async void ExportFavorites_Click(object sender, RoutedEventArgs e)
    {
        await ExportAsync("shoutkit-favorites", ViewModel.ExportFavoritesAsync);
    }

    private async void ImportFavorites_Click(object sender, RoutedEventArgs e)
    {
        await ImportFavoritesAsync();
    }

    /// <summary>Pick, validate, preview, then append only after explicit confirmation.</summary>
    private async Task ImportFavoritesAsync()
    {
        if (_closed || _exportOpen || _confirmOpen || _licensesOpen)
        {
            return;
        }

        _exportOpen = true;
        try
        {
            var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
            picker.FileTypeFilter.Add(".json");
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
            var file = await picker.PickSingleFileAsync();
            if (file is null || _closed)
            {
                return;
            }

            FavoritesImportPreview preview;
            await using (var stream = await file.OpenStreamForReadAsync())
            {
                preview = await ViewModel.PreviewFavoritesImportAsync(stream);
            }

            if (_closed)
            {
                return;
            }

            if (!preview.CanImport)
            {
                await ShowExportMessageAsync(preview.IsValid ? "Nothing to import" : "Favorites not imported",
                    preview.Summary);
                return;
            }

            if (!await ConfirmImportAsync(preview) || _closed)
            {
                return;
            }

            var added = await ViewModel.ImportFavoritesAsync(preview);
            if (!_closed && added >= 0)
            {
                FavoritesImportBar.Message = added == 1
                    ? "Added 1 station to Favorites."
                    : $"Added {added:N0} stations to Favorites.";
                FavoritesImportBar.IsOpen = true;
            }
        }
        catch (Exception exception)
        {
            AppDiagnostics.Record("favorites-import.read", exception);
            await ShowExportMessageAsync("Backup could not be read",
                "Choose a favorites backup you can open and try again. Your favorites were not changed.");
        }
        finally { _exportOpen = false; }
    }

    private async Task<bool> ConfirmImportAsync(FavoritesImportPreview preview)
    {
        var count = preview.StationsToAdd.Count;
        var content = new StackPanel { Spacing = 12, MinWidth = 320 };
        content.Children.Add(new TextBlock { Text = preview.Summary, TextWrapping = TextWrapping.Wrap });
        var list = new ListView
        {
            ItemsSource = preview.StationsToAdd,
            SelectionMode = ListViewSelectionMode.None,
            MaxHeight = 280
        };
        AutomationProperties.SetName(list, "Stations to import");
        content.Children.Add(list);
        var dialog = new ContentDialog
        {
            Title = "Import favorites?",
            Content = content,
            PrimaryButtonText = count == 1 ? "Add 1 station" : $"Add {count:N0} stations",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = Content.XamlRoot
        };
        try { return await dialog.ShowAsync() == ContentDialogResult.Primary; }
        catch (Exception exception)
        {
            AppDiagnostics.Record("favorites-import.dialog", exception);
            return false;
        }
    }

    private async Task ShowExportMessageAsync(string title, string message)
    {
        if (_closed || _confirmOpen || _licensesOpen)
        {
            return;
        }

        try
        {
            await new ContentDialog
            {
                Title = title,
                Content = message,
                CloseButtonText = "Close",
                XamlRoot = Content.XamlRoot
            }.ShowAsync();
        }
        catch (Exception dialogException) { AppDiagnostics.Record("local-export.dialog", dialogException); }
    }

    private async Task ExportAsync(string name, Func<Task<string>> createSnapshot)
    {
        if (_closed || _exportOpen || _confirmOpen || _licensesOpen)
        {
            return;
        }

        _exportOpen = true;
        try
        {
            var picker = new FileSavePicker { SuggestedFileName = name };
            picker.FileTypeChoices.Add("JSON", [".json"]);
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
            var file = await picker.PickSaveFileAsync();
            if (file is null || _closed)
            {
                return;
            }

            await FileIO.WriteTextAsync(file, await createSnapshot());
        }
        catch (Exception exception)
        {
            AppDiagnostics.Record("local-export.write", exception);
            await ShowExportMessageAsync("Export could not be saved", "Choose a writable location and try again.");
        }
        finally { _exportOpen = false; }
    }
}
