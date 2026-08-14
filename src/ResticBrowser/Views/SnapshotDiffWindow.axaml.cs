using Avalonia.Controls;
using Avalonia.Interactivity;

using ResticBrowser.Models;
using ResticBrowser.Services;
using ResticBrowser.ViewModels;

namespace ResticBrowser.Views;

public partial class SnapshotDiffWindow : Window
{
    private readonly IResticRepositoryService? _repositoryService;
    private readonly RepositoryProfile? _profile;
    private readonly SessionCredentials? _credentials;
    private readonly List<DiffEntry> _allDiffs = [];
    private readonly BatchObservableCollection<DiffEntry> _visibleDiffs = [];
    private CancellationTokenSource? _comparison;
    private long _comparisonVersion;

    public SnapshotDiffWindow()
    {
        InitializeComponent();
        Closed += (_, _) => CancelComparison();
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

        CancelComparison();
        _comparison = new CancellationTokenSource();
        var cancellation = _comparison;
        var version = ++_comparisonVersion;
        try
        {
            StatsSummaryText.Text = "Unterschiede werden geladen …";
            var diffs = await _repositoryService.GetDiffAsync(
                _profile, _credentials, baseSnap.Id, targetSnap.Id, cancellation.Token);
            if (version != _comparisonVersion || cancellation.IsCancellationRequested) return;

            _allDiffs.Clear();
            _allDiffs.AddRange(diffs);

            ApplyFilter();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (version != _comparisonVersion) return;
            await DialogService.ShowMessageAsync(this, "Fehler", $"Fehler beim Vergleichen: {ex.Message}");
            StatsSummaryText.Text = "Fehler beim Laden";
        }
        finally
        {
            if (ReferenceEquals(_comparison, cancellation))
            {
                _comparison.Dispose();
                _comparison = null;
            }
        }
    }

    private void Filter_Changed(object? sender, RoutedEventArgs e) => ApplyFilter();

    private void ApplyFilter()
    {
        var showAdded = FilterAddedBox?.IsChecked == true;
        var showModified = FilterModifiedBox?.IsChecked == true;
        var showRemoved = FilterRemovedBox?.IsChecked == true;

        var visible = new List<DiffEntry>(_allDiffs.Count);
        var addedCount = 0;
        var modifiedCount = 0;
        var removedCount = 0;
        foreach (var item in _allDiffs)
        {
            switch (item.ChangeType)
            {
                case DiffChangeType.Added: addedCount++; break;
                case DiffChangeType.Modified: modifiedCount++; break;
                case DiffChangeType.Removed: removedCount++; break;
            }
            if (item.ChangeType == DiffChangeType.Added && !showAdded) continue;
            if (item.ChangeType == DiffChangeType.Modified && !showModified) continue;
            if (item.ChangeType == DiffChangeType.Removed && !showRemoved) continue;
            visible.Add(item);
        }
        _visibleDiffs.ReplaceWith(visible);

        StatsSummaryText.Text = $"Gesamt: {_allDiffs.Count} (Neu: {addedCount}, Geändert: {modifiedCount}, Entfernt: {removedCount})";
    }

    private void Close_Click(object? sender, RoutedEventArgs e) => Close();

    private void CancelComparison()
    {
        _comparisonVersion++;
        _comparison?.Cancel();
        _comparison?.Dispose();
        _comparison = null;
    }
}
