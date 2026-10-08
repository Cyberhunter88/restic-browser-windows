using Avalonia.Controls;
using Avalonia.Interactivity;
using ResticBrowser.Models;
using ResticBrowser.Services;
using ResticBrowser.ViewModels;

namespace ResticBrowser.Views;

public partial class FileVersionsWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly BackupNode _node;

    public FileVersionsWindow()
    {
        InitializeComponent();
        _viewModel = null!;
        _node = new BackupNode();
    }

    public FileVersionsWindow(MainViewModel viewModel, BackupNode node) : this()
    {
        _viewModel = viewModel;
        _node = node;
        PathText.Text = node.Path;
        Opened += async (_, _) => await LoadAsync();
    }

    private async Task LoadAsync()
    {
        VersionsGrid.IsEnabled = false;
        try
        {
            VersionsGrid.ItemsSource = await _viewModel.GetFileVersionsAsync(_node, AllHostsBox.IsChecked == true);
        }
        catch (ResticException ex) { await DialogService.ShowMessageAsync(this, "Dateiversionen", ex.Message); }
        finally { VersionsGrid.IsEnabled = true; }
    }

    private async void Reload_Click(object? sender, RoutedEventArgs e) => await LoadAsync();
    private async void Preview_Click(object? sender, RoutedEventArgs e)
    {
        if (SelectedVersions().FirstOrDefault() is not { } version) return;
        await new FilePreviewWindow(await _viewModel.GetFilePreviewAsync(version)).ShowDialog(this);
    }
    private async void Restore_Click(object? sender, RoutedEventArgs e)
    {
        if (SelectedVersions().FirstOrDefault() is not { } version) return;
        _viewModel.SelectedSnapshot = version.Snapshot;
        await new RestoreWindow(_viewModel, [version.Node]).ShowDialog(this);
    }
    private IEnumerable<FileVersion> SelectedVersions() => VersionsGrid.SelectedItems?.OfType<FileVersion>() ?? [];
    private void Close_Click(object? sender, RoutedEventArgs e) => Close();
}
