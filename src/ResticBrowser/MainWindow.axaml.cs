using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using ResticBrowser.Models;
using ResticBrowser.Services;
using ResticBrowser.ViewModels;
using ResticBrowser.Views;

namespace ResticBrowser;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainViewModel(new ResticRepositoryService(new ResticProcessRunner()), new SettingsService());
        DataContext = _viewModel;
        Opened += async (_, _) => await RunSafeAsync(_viewModel.InitializeAsync);
        Closed += (_, _) => _viewModel.Dispose();
    }
    private async void Connect_Click(object? sender, RoutedEventArgs e)
    {
        var dialog = new ConnectionWindow(_viewModel.Profiles);
        if (await dialog.ShowDialog<bool>(this) != true || dialog.Profile is null || dialog.Credentials is null) return;
        await RunSafeAsync(() => _viewModel.ConnectAsync(dialog.Profile, dialog.Credentials));
    }
    private async void Refresh_Click(object? sender, RoutedEventArgs e) => await RunSafeAsync(_viewModel.RefreshSnapshotsAsync);
    private void Disconnect_Click(object? sender, RoutedEventArgs e) => _viewModel.Disconnect();
    private void Theme_Click(object? sender, RoutedEventArgs e) => App.SetTheme(!App.IsDark);
    private void Cancel_Click(object? sender, RoutedEventArgs e) => _viewModel.Cancel();
    private async void Up_Click(object? sender, RoutedEventArgs e) => await RunSafeAsync(_viewModel.GoUpAsync);
    private async void Back_Click(object? sender, RoutedEventArgs e) => await RunSafeAsync(_viewModel.GoBackAsync);
    private async void Forward_Click(object? sender, RoutedEventArgs e) => await RunSafeAsync(_viewModel.GoForwardAsync);
    private async void FileList_DoubleTapped(object? sender, TappedEventArgs e) { if (FileList.SelectedItem is BackupNode node) await RunSafeAsync(() => _viewModel.OpenNodeAsync(node)); }
    private async void Search_Click(object? sender, RoutedEventArgs e) => await RunSafeAsync(() => _viewModel.SearchAsync(SearchBox.Text ?? ""));
    private void SearchBox_KeyDown(object? sender, KeyEventArgs e) { if (e.Key == Key.Enter) Search_Click(sender, e); }

    private async void Restore_Click(object? sender, RoutedEventArgs e)
    {
        var selected = FileList.SelectedItems?.OfType<BackupNode>().ToList() ?? [];
        if (selected.Count == 0 && FileList.SelectedItem is BackupNode singleNode) selected.Add(singleNode);
        if (selected.Count == 0) { await DialogService.ShowMessageAsync(this, "Keine Auswahl", "Bitte mindestens eine Datei oder einen Ordner auswählen."); return; }
        await new RestoreWindow(_viewModel, selected).ShowDialog(this);
    }

    private async void Preview_Click(object? sender, RoutedEventArgs e)
    {
        if (FileList.SelectedItem is not BackupNode node)
        {
            await DialogService.ShowMessageAsync(this, "Keine Auswahl", "Bitte eine Datei für die Vorschau auswählen.");
            return;
        }

        if (node.IsDirectory)
        {
            await DialogService.ShowMessageAsync(this, "Hinweis", "Ordner können nicht in der Vorschau angezeigt werden.");
            return;
        }

        await RunSafeAsync(async () =>
        {
            var previewData = await _viewModel.GetFilePreviewAsync(node);
            await new FilePreviewWindow(previewData).ShowDialog(this);
        });
    }

    private async void Diff_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.ActiveProfile is null || _viewModel.Credentials is null || _viewModel.Snapshots.Count == 0)
        {
            await DialogService.ShowMessageAsync(this, "Hinweis", "Bitte verbinde erst ein Repository mit Snapshots.");
            return;
        }
        var service = new ResticRepositoryService(new ResticProcessRunner());
        await new SnapshotDiffWindow(service, _viewModel.ActiveProfile, _viewModel.Credentials, _viewModel.Snapshots).ShowDialog(this);
    }

    private async void CopyPath_Click(object? sender, RoutedEventArgs e)
    {
        if (FileList.SelectedItem is BackupNode node)
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard is not null)
                await clipboard.SetTextAsync(node.Path);
            await DialogService.ShowMessageAsync(this, "Kopiert", $"Pfad in die Zwischenablage kopiert:\n{node.Path}");
        }
    }

    private async void AddBookmark_Click(object? sender, RoutedEventArgs e)
    {
        await RunSafeAsync(_viewModel.AddBookmarkCurrentPathAsync);
        await DialogService.ShowMessageAsync(this, "Lesezeichen", "Lesezeichen für den aktuellen Pfad gespeichert.");
    }

    private async void BookmarkMenu_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.Bookmarks.Count == 0)
        {
            await DialogService.ShowMessageAsync(this, "Lesezeichen", "Noch keine Lesezeichen gespeichert.");
            return;
        }

        var menu = new ContextMenu();
        var items = new List<MenuItem>();
        foreach (var bookmark in _viewModel.Bookmarks)
        {
            var item = new MenuItem { Header = bookmark.Name, Tag = bookmark };
            item.Click += async (s, ev) =>
            {
                if (s is MenuItem mi && mi.Tag is Bookmark bm)
                    await RunSafeAsync(() => _viewModel.OpenBookmarkAsync(bm));
            };
            items.Add(item);
        }
        menu.ItemsSource = items;
        menu.Open(this);
    }

    private async Task RunSafeAsync(Func<Task> action)
    {
        try { await action(); }
        catch (OperationCanceledException) { }
        catch (ResticException ex) { await DialogService.ShowMessageAsync(this, "Restic-Fehler", ex.Message); }
        catch (Exception ex) { await DialogService.ShowMessageAsync(this, "Fehler", $"Unerwarteter Fehler: {ex.Message}"); }
    }
}
