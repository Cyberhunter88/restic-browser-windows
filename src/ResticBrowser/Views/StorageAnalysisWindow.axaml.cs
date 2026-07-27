using Avalonia.Controls;
using Avalonia.Interactivity;
using ResticBrowser.Models;
using ResticBrowser.Services;

namespace ResticBrowser.Views;

public partial class StorageAnalysisWindow : Window
{
    private readonly IResticRepositoryService _service;
    private readonly RepositoryProfile _profile;
    private readonly SessionCredentials _credentials;
    private readonly SnapshotInfo _snapshot;

    public StorageAnalysisWindow()
    {
        InitializeComponent();
        _service = null!;
        _profile = null!;
        _credentials = null!;
        _snapshot = null!;
    }

    public StorageAnalysisWindow(
        IResticRepositoryService service,
        RepositoryProfile profile,
        SessionCredentials credentials,
        SnapshotInfo snapshot)
    {
        InitializeComponent();
        _service = service;
        _profile = profile;
        _credentials = credentials;
        _snapshot = snapshot;

        SubtitleText.Text = $"Snapshot {_snapshot.DisplayId} ({_snapshot.Hostname}) vom {_snapshot.Time:g}";
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object? sender, RoutedEventArgs e)
    {
        try
        {
            var result = await _service.AnalyzeSnapshotStorageAsync(_profile, _credentials, _snapshot.Id);

            TotalSizeText.Text = result.TotalSizeText;
            TotalFilesText.Text = $"{result.TotalFileCount:N0}";

            CategoriesGrid.ItemsSource = result.Categories;
            FoldersGrid.ItemsSource = result.TopFolders;
            FilesGrid.ItemsSource = result.TopFiles;

            LoadingPanel.IsVisible = false;
            MainTabControl.IsVisible = true;
        }
        catch (Exception ex)
        {
            LoadingPanel.IsVisible = false;
            await DialogService.ShowMessageAsync(this, "Analyse-Fehler", $"Die Speicheranalyse konnte nicht geladen werden: {ex.Message}");
            Close();
        }
    }

    private void Close_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
