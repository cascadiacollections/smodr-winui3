using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using smodr.Models;
using smodr.Services;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;
using Windows.UI.Core;

namespace smodr;

/// <summary>Favorites ordering, undoable removal and history clearing.</summary>
#pragma warning disable CA1001 // Teardown is owned by MainWindow_Closed; see the primary declaration.
public sealed partial class MainWindow
#pragma warning restore CA1001
{
    private static readonly TimeSpan _undoWindow = TimeSpan.FromSeconds(10);
    private readonly DispatcherTimer _undoTimer = new() { Interval = _undoWindow };
    private bool _confirmOpen;

    private void UpdateFavoriteUndo()
    {
        _undoTimer.Stop();
        if (ViewModel.RemovedFavorite is not { } station)
        {
            FavoriteUndoBar.IsOpen = false;
            return;
        }

        FavoriteUndoBar.Title = "Removed from Favorites";
        FavoriteUndoBar.Message = station.Name;
        FavoriteUndoBar.IsOpen = true;
        _undoTimer.Start();
    }

    private void UndoTimer_Tick(object? sender, object e)
    {
        _undoTimer.Stop();
        ViewModel.DismissRemovedFavorite();
    }

    private async void UndoRemoveFavorite_Click(object sender, RoutedEventArgs e) =>
        await ViewModel.UndoRemoveFavoriteAsync();

    private void FavoriteUndoBar_Closed(InfoBar sender, InfoBarClosedEventArgs args)
    {
        if (args.Reason == InfoBarCloseReason.CloseButton) ViewModel.DismissRemovedFavorite();
    }

    private async void FavoritesList_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
    {
        if (args.DropResult == DataPackageOperation.Move) await ViewModel.SaveFavoriteOrderAsync();
    }

    private async void StationRow_MoveRequested(object? sender, int offset)
    {
        if (sender is StationRowControl { Station: { } station }) await MoveFavoriteAsync(station, offset);
    }

    private async void FavoritesList_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        // Alt+arrow arrives as a system key; KeyStatus carries its Alt state reliably.
        if (e.Key is not (VirtualKey.Up or VirtualKey.Down)
            || !(e.KeyStatus.IsMenuKeyDown
                || InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Menu).HasFlag(CoreVirtualKeyStates.Down))
            || FindContainer(e.OriginalSource as DependencyObject) is not { } container
            || FavoritesList.ItemFromContainer(container) is not RadioStation station) return;
        e.Handled = true;
        await MoveFavoriteAsync(station, e.Key == VirtualKey.Up ? -1 : 1);
    }

    private static ListViewItem? FindContainer(DependencyObject? element)
    {
        while (element is not null and not ListViewItem) element = VisualTreeHelper.GetParent(element);
        return element as ListViewItem;
    }

    private async Task MoveFavoriteAsync(RadioStation station, int offset)
    {
        var move = ViewModel.MoveFavoriteAsync(station, offset);
        // Keep focus on the moved row so repeated Alt+Up/Down keeps moving the same station.
        FocusFavorite(station);
        await move;
        if (!_closed) FocusFavorite(station);
    }

    private void FocusFavorite(RadioStation station)
    {
        var index = ViewModel.Favorites.ToList().FindIndex(item => RadioStationIdentity.Matches(item, station));
        if (index < 0) return;
        FavoritesList.ScrollIntoView(ViewModel.Favorites[index]);
        (FavoritesList.ContainerFromIndex(index) as Control)?.Focus(FocusState.Keyboard);
    }

    private async void ClearRecentsButton_Click(object sender, RoutedEventArgs e)
    {
        if (await ConfirmAsync("Clear recently played stations?",
            "Favorites and listening history are not affected.", "Clear"))
            await ViewModel.ClearRecentsAsync();
    }

    private async void ClearHeardTracksButton_Click(object sender, RoutedEventArgs e)
    {
        if (await ConfirmAsync("Clear listening history?",
            "Removes every Recently Heard track and resets Top Tracks on this device. This cannot be undone.", "Clear history"))
            await ViewModel.ClearHeardTracksAsync();
    }

    private async Task<bool> ConfirmAsync(string title, string content, string primary)
    {
        if (_closed || _confirmOpen) return false;
        _confirmOpen = true;
        try
        {
            var dialog = new ContentDialog
            {
                Title = title,
                Content = content,
                PrimaryButtonText = primary,
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = Content.XamlRoot,
            };
            return await dialog.ShowAsync() == ContentDialogResult.Primary && !_closed;
        }
        catch (Exception exception)
        {
            AppDiagnostics.Record("library.confirm-dialog", exception);
            return false;
        }
        finally { _confirmOpen = false; }
    }
}
