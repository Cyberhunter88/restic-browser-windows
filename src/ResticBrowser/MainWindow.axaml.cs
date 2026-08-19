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
    private readonly IResticRepositoryService _repository;
    private readonly MainViewModel _viewModel;
    private ResticMountHandle? _activeMountHandle;

    public MainWindow()
    {
        InitializeComponent();
        _repository = new ResticRepositoryService(new ResticProcessRunner());
        _viewModel = new MainViewModel(_repository, new SettingsService());
        DataContext = _viewModel;
        MountButton.IsVisible = OperatingSystem.IsLinux();
        Opened += async (_, _) => await RunSafeAsync(_viewModel.InitializeAsync);
        Closed += async (_, _) =>
        {
            if (_activeMountHandle != null)
            {
                await _activeMountHandle.StopAsync();
                _activeMountHandle = null;
            }
            _viewModel.Dispose();
        };
    }

    private async void Mount_Click(object? sender, RoutedEventArgs e)
    {
        if (!OperatingSystem.IsLinux())
        {
            await DialogService.ShowMessageAsync(this, "Mount nicht verfügbar", "Restic unterstützt das Einbinden als Laufwerk in dieser Anwendung nur unter Linux.");
            return;
        }
        if (_viewModel.ActiveProfile is null || _viewModel.Credentials is null)
        {
            await DialogService.ShowMessageAsync(this, "Hinweis", "Bitte verbinde erst ein Repository.");
            return;
        }
        var mountWindow = new MountWindow(_repository, _viewModel.ActiveProfile, _viewModel.Credentials, _viewModel.SelectedSnapshot, _activeMountHandle);
        var resultHandle = await mountWindow.ShowDialog<ResticMountHandle?>(this);
        _activeMountHandle = resultHandle ?? _activeMountHandle;
    }

    private async void Check_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.ActiveProfile is null || _viewModel.Credentials is null)
        {
            await DialogService.ShowMessageAsync(this, "Hinweis", "Bitte verbinde erst ein Repository.");
            return;
        }
        await new RepositoryCheckWindow(_repository, _viewModel.ActiveProfile, _viewModel.Credentials).ShowDialog(this);
    }

    private async void Timeline_Click(object? sender, RoutedEventArgs e)
    {
        var snapshot = await new SnapshotTimelineWindow(_viewModel.Snapshots).ShowDialog<SnapshotInfo?>(this);
        if (snapshot is not null) _viewModel.SelectedSnapshot = snapshot;
    }

    private async void StorageAnalysis_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.ActiveProfile is null || _viewModel.Credentials is null || _viewModel.SelectedSnapshot is null)
        {
            await DialogService.ShowMessageAsync(this, "Hinweis", "Bitte wähle zuerst einen Snapshot in der linken Liste aus.");
            return;
        }
        await new StorageAnalysisWindow(_repository, _viewModel.ActiveProfile, _viewModel.Credentials, _viewModel.SelectedSnapshot).ShowDialog(this);
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
    private async void RestoreNewest_Click(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(SearchBox.Text))
        {
            await DialogService.ShowMessageAsync(this, "Suchbegriff fehlt", "Bitte gib einen Dateinamen oder ein Restic-Muster ein.");
            return;
        }
        BackupNode? node = null;
        await RunSafeAsync(async () => node = await _viewModel.FindNewestAsync(SearchBox.Text));
        if (node is not null) await new RestoreWindow(_viewModel, [node]).ShowDialog(this);
    }
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
        await new SnapshotDiffWindow(_repository, _viewModel.ActiveProfile, _viewModel.Credentials, _viewModel.Snapshots).ShowDialog(this);
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

    private async Task RunSafeAsync(Func<Task> action)
    {
        try { await action(); }
        catch (OperationCanceledException) { }
        catch (ResticException ex) { await DialogService.ShowMessageAsync(this, "Restic-Fehler", ex.Message); }
        catch (Exception ex) { await DialogService.ShowMessageAsync(this, "Fehler", $"Unerwarteter Fehler: {ex.Message}"); }
    }
}
