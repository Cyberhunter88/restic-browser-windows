using Avalonia.Controls;
using Avalonia.Input;
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
        if (selected.Count == 0) { await DialogService.ShowMessageAsync(this, "Keine Auswahl", "Bitte mindestens eine Datei oder einen Ordner auswählen."); return; }
        await new RestoreWindow(_viewModel, selected).ShowDialog(this);
    }
    private async Task RunSafeAsync(Func<Task> action)
    {
        try { await action(); }
        catch (OperationCanceledException) { }
        catch (ResticException ex) { await DialogService.ShowMessageAsync(this, "Restic-Fehler", ex.Message); }
        catch (Exception ex) { await DialogService.ShowMessageAsync(this, "Fehler", $"Unerwarteter Fehler: {ex.Message}"); }
    }
}
