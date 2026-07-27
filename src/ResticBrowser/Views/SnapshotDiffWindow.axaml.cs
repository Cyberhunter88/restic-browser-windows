using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Interactivity;

using ResticBrowser.Models;
using ResticBrowser.Services;

namespace ResticBrowser.Views;

public partial class SnapshotDiffWindow : Window
{
    private readonly IResticRepositoryService? _repositoryService;
    private readonly RepositoryProfile? _profile;
    private readonly SessionCredentials? _credentials;
    private readonly List<DiffEntry> _allDiffs = [];
    private readonly ObservableCollection<DiffEntry> _visibleDiffs = [];

    public SnapshotDiffWindow()
    {
        InitializeComponent();
    }

    public SnapshotDiffWindow(
        IResticRepositoryService repositoryService,
        RepositoryProfile profile,
        SessionCredentials credentials,
        IEnumerable<SnapshotInfo> snapshots) : this()
    {
        _repositoryService = repositoryService;
        _profile = profile;
        _credentials = credentials;

        var list = snapshots.ToList();
        SnapshotBaseBox.ItemsSource = list;
        SnapshotTargetBox.ItemsSource = list;
        DiffGrid.ItemsSource = _visibleDiffs;

        if (list.Count >= 2)
        {
            SnapshotBaseBox.SelectedIndex = 1;
            SnapshotTargetBox.SelectedIndex = 0;
        }
        else if (list.Count == 1)
        {
            SnapshotBaseBox.SelectedIndex = 0;
            SnapshotTargetBox.SelectedIndex = 0;
        }
    }

    private async void Compare_Click(object? sender, RoutedEventArgs e)
    {
        if (_repositoryService is null || _profile is null || _credentials is null) return;
        if (SnapshotBaseBox.SelectedItem is not SnapshotInfo baseSnap ||
            SnapshotTargetBox.SelectedItem is not SnapshotInfo targetSnap)
        {
            await DialogService.ShowMessageAsync(this, "Auswahl fehlt", "Bitte wähle zwei Snapshots zum Vergleichen aus.");
            return;
        }

        try
        {
            StatsSummaryText.Text = "Unterschiede werden geladen …";
            var diffs = await _repositoryService.GetDiffAsync(_profile, _credentials, baseSnap.Id, targetSnap.Id);

            _allDiffs.Clear();
            _allDiffs.AddRange(diffs);

            ApplyFilter();
        }
        catch (Exception ex)
        {
            await DialogService.ShowMessageAsync(this, "Fehler", $"Fehler beim Vergleichen: {ex.Message}");
            StatsSummaryText.Text = "Fehler beim Laden";
        }
    }

    private void Filter_Changed(object? sender, RoutedEventArgs e) => ApplyFilter();

    private void ApplyFilter()
    {
        var showAdded = FilterAddedBox?.IsChecked == true;
        var showModified = FilterModifiedBox?.IsChecked == true;
        var showRemoved = FilterRemovedBox?.IsChecked == true;

        _visibleDiffs.Clear();
        foreach (var item in _allDiffs)
        {
            if (item.ChangeType == DiffChangeType.Added && !showAdded) continue;
            if (item.ChangeType == DiffChangeType.Modified && !showModified) continue;
            if (item.ChangeType == DiffChangeType.Removed && !showRemoved) continue;
            _visibleDiffs.Add(item);
        }

        var addedCount = _allDiffs.Count(d => d.ChangeType == DiffChangeType.Added);
        var modifiedCount = _allDiffs.Count(d => d.ChangeType == DiffChangeType.Modified);
        var removedCount = _allDiffs.Count(d => d.ChangeType == DiffChangeType.Removed);

        StatsSummaryText.Text = $"Gesamt: {_allDiffs.Count} (Neu: {addedCount}, Geändert: {modifiedCount}, Entfernt: {removedCount})";
    }

    private void Close_Click(object? sender, RoutedEventArgs e) => Close();
}
