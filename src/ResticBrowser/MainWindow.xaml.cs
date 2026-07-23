using System.Windows;
using System.Windows.Input;
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
        _viewModel = new MainViewModel(
            new ResticRepositoryService(new ResticProcessRunner()),
            new SettingsService());
        DataContext = _viewModel;
        Loaded += async (_, _) =>
        {
            WindowTheme.Apply(this, App.IsDark);
            await RunSafeAsync(_viewModel.InitializeAsync);
        };
        Closed += (_, _) => _viewModel.Dispose();
    }

    private async void Connect_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ConnectionWindow(_viewModel.Profiles) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Profile is null || dialog.Credentials is null) return;
        await RunSafeAsync(() => _viewModel.ConnectAsync(dialog.Profile, dialog.Credentials));
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) =>
        await RunSafeAsync(_viewModel.RefreshSnapshotsAsync);

    private void Disconnect_Click(object sender, RoutedEventArgs e) => _viewModel.Disconnect();
    private void Theme_Click(object sender, RoutedEventArgs e) => App.SetTheme(!App.IsDark);
    private void Cancel_Click(object sender, RoutedEventArgs e) => _viewModel.Cancel();

    private async void Up_Click(object sender, RoutedEventArgs e) =>
        await RunSafeAsync(_viewModel.GoUpAsync);

    private async void Back_Click(object sender, RoutedEventArgs e) =>
        await RunSafeAsync(_viewModel.GoBackAsync);

    private async void Forward_Click(object sender, RoutedEventArgs e) =>
        await RunSafeAsync(_viewModel.GoForwardAsync);

    private async void FileList_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (FileList.SelectedItem is BackupNode node)
            await RunSafeAsync(() => _viewModel.OpenNodeAsync(node));
    }

    private async void Search_Click(object sender, RoutedEventArgs e) =>
        await RunSafeAsync(() => _viewModel.SearchAsync(SearchBox.Text));

    private void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) Search_Click(sender, e);
    }

    private void Restore_Click(object sender, RoutedEventArgs e)
    {
        var selected = FileList.SelectedItems.Cast<BackupNode>().ToList();
        if (selected.Count == 0)
        {
            MessageBox.Show(this, "Bitte mindestens eine Datei oder einen Ordner auswählen.", "Keine Auswahl",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        new RestoreWindow(_viewModel, selected) { Owner = this }.ShowDialog();
    }

    private async Task RunSafeAsync(Func<Task> action)
    {
        try { await action(); }
        catch (OperationCanceledException) { }
        catch (ResticException ex)
        {
            MessageBox.Show(this, ex.Message, "Restic-Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Unerwarteter Fehler: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
