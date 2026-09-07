using ResticBrowser.Models;
using ResticBrowser.Services;

namespace ResticBrowser.ViewModels;

public sealed partial class MainViewModel
{
    public async Task RefreshSnapshotsAsync()
    {
        if (ActiveProfile is null || _credentials is null) return;
        var operation = BeginOperation();
        try
        {
            var selectedSnapshot = await RefreshSnapshotsCoreAsync(operation);
            if (IsCurrent(operation) && selectedSnapshot is not null)
                ActivateSnapshot(selectedSnapshot);
        }
        finally { CompleteOperation(operation); }
    }

    private async Task<SnapshotInfo?> RefreshSnapshotsCoreAsync(OperationState operation)
    {
        if (ActiveProfile is null || _credentials is null) return null;
        var previousId = SelectedSnapshot?.Id;
        _snapshotLoadVersion = operation.Version;
        SelectedSnapshot = null;
        Nodes.Clear();
        CurrentPath = "/";
        Status = "Snapshots werden geladen …";
        Snapshots.Clear();
        VisibleSnapshots.Clear();
        _snapshotFilterIndex.Clear();
        ClearDirectoryCache();
        try
        {
            await _repository.GetSnapshotsBatchedAsync(ActiveProfile, _credentials, async batch =>
            {
                if (!IsCurrent(operation) || operation.Token.IsCancellationRequested) return;
                Snapshots.AddRange(batch);
                // Append without resetting the selection; sort once loading completes.
                if (FilterOnlyLatest) ApplySnapshotFilterImmediately();
                else VisibleSnapshots.AddRange(batch.Where(snapshot => SnapshotMatchesFilter(snapshot)));
                Status = $"{Snapshots.Count:N0} Snapshot(s) werden geladen …";
                await Task.Yield();
            }, operation.Token);
            if (!IsCurrent(operation)) return null;
            operation.Token.ThrowIfCancellationRequested();
            var selectedId = SelectedSnapshot?.Id ?? previousId;
            var ordered = Snapshots.OrderByDescending(s => s.Time).ToList();
            Snapshots.ReplaceWith(ordered);
            AvailableHosts.ReplaceWith(["Alle Hosts", .. ordered.Select(s => s.Hostname)
                .Where(h => !string.IsNullOrWhiteSpace(h)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(h => h)]);
            AvailableTags.ReplaceWith(["Alle Tags", .. ordered.SelectMany(s => s.Tags)
                .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(t => t)]);
            ApplySnapshotFilterImmediately();
            Status = $"{Snapshots.Count:N0} Snapshot(s) geladen";
            return ordered.FirstOrDefault(s => s.Id == selectedId) ?? VisibleSnapshots.FirstOrDefault();
        }
        catch
        {
            if (IsCurrent(operation))
            {
                Snapshots.Clear();
                VisibleSnapshots.Clear();
                _snapshotFilterIndex.Clear();
                Status = operation.Token.IsCancellationRequested ? "Laden der Snapshots abgebrochen" : "Snapshots konnten nicht vollständig geladen werden";
            }
            throw;
        }
        finally
        {
            if (_snapshotLoadVersion == operation.Version) _snapshotLoadVersion = null;
        }
    }

    public async Task LoadRepositoryStatsAsync()
    {
        if (ActiveProfile is null || _credentials is null) return;
        _statsOperation?.Cancel();
        _statsOperation?.Dispose();
        _statsOperation = new CancellationTokenSource();
        var cancellation = _statsOperation;
        var profile = ActiveProfile;
        var credentials = _credentials;
        var connectionVersion = _connectionVersion;
        try
        {
            var stats = await _repository.GetStatsAsync(profile, credentials, cancellation.Token);
            if (!cancellation.IsCancellationRequested && connectionVersion == _connectionVersion &&
                ReferenceEquals(profile, ActiveProfile) && ReferenceEquals(credentials, _credentials))
                RepoStats = stats;
        }
        catch (OperationCanceledException) { }
        catch { /* Stats fail quietly if not supported by backend */ }
        finally
        {
            if (ReferenceEquals(_statsOperation, cancellation))
            {
                cancellation.Dispose();
                _statsOperation = null;
            }
        }
    }
}
