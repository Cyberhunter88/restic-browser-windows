using ResticBrowser.Models;

namespace ResticBrowser.ViewModels;

public sealed partial class MainViewModel
{
    private void ApplySnapshotFilter()
    {
        var filter = SnapshotFilter.Trim();
        var hostFilter = FilterHost;
        var tagFilter = FilterTag;
        IEnumerable<SnapshotInfo> query = Snapshots;

        if (FilterOnlyLatest)
        {
            var seenGroups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            query = query.Where(snapshot => seenGroups.Add(GetSnapshotIndex(snapshot).GroupKey));
        }

        var visible = query.Where(snapshot =>
            (filter.Length == 0 || GetSnapshotIndex(snapshot).SearchText.Contains(filter, StringComparison.CurrentCultureIgnoreCase)) &&
            (string.IsNullOrWhiteSpace(hostFilter) || hostFilter == "Alle Hosts" || snapshot.Hostname.Equals(hostFilter, StringComparison.OrdinalIgnoreCase)) &&
            (string.IsNullOrWhiteSpace(tagFilter) || tagFilter == "Alle Tags" || snapshot.Tags.Contains(tagFilter, StringComparer.OrdinalIgnoreCase)) &&
            (!FilterStartDate.HasValue || snapshot.Time.Date >= FilterStartDate.Value.Date) &&
            (!FilterEndDate.HasValue || snapshot.Time.Date <= FilterEndDate.Value.Date)).ToList();
        VisibleSnapshots.ReplaceWith(visible);
    }

    private void ScheduleSnapshotFilter()
    {
        _filterOperation?.Cancel();
        _filterOperation?.Dispose();
        _filterOperation = new CancellationTokenSource();
        _ = ApplySnapshotFilterAfterDelayAsync(_filterOperation.Token);
    }

    private async Task ApplySnapshotFilterAfterDelayAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(200, token);
            ApplySnapshotFilter();
        }
        catch (OperationCanceledException) { }
    }

    private void ApplySnapshotFilterImmediately()
    {
        _filterOperation?.Cancel();
        _filterOperation?.Dispose();
        _filterOperation = null;
        ApplySnapshotFilter();
    }

    private SnapshotIndexEntry GetSnapshotIndex(SnapshotInfo snapshot)
    {
        if (_snapshotIndex.TryGetValue(snapshot, out var index)) return index;
        index = new SnapshotIndexEntry(
            string.Join('\n', snapshot.Hostname, snapshot.PathText, snapshot.TagText, snapshot.DisplayId),
            string.Join('\n', snapshot.Hostname, snapshot.PathText));
        _snapshotIndex[snapshot] = index;
        return index;
    }

    private void CacheDirectory(string key, IReadOnlyList<BackupNode> nodes)
    {
        if (nodes.Count > DirectoryCacheNodeCapacity) return;
        if (_directoryCache.TryGetValue(key, out var existing))
        {
            _directoryCacheOrder.Remove(existing.OrderNode);
            _directoryCacheNodeCount -= existing.Nodes.Count;
            _directoryCache.Remove(key);
        }
        while (_directoryCache.Count >= DirectoryCacheCapacity ||
               _directoryCacheNodeCount + nodes.Count > DirectoryCacheNodeCapacity)
        {
            var oldest = _directoryCacheOrder.First;
            if (oldest is null) break;
            _directoryCacheOrder.RemoveFirst();
            if (_directoryCache.Remove(oldest.Value, out var removed))
                _directoryCacheNodeCount -= removed.Nodes.Count;
        }
        var orderNode = _directoryCacheOrder.AddLast(key);
        _directoryCache[key] = new DirectoryCacheEntry(nodes, orderNode);
        _directoryCacheNodeCount += nodes.Count;
    }

    private bool TryGetCachedDirectory(string key, out IReadOnlyList<BackupNode> nodes)
    {
        if (!_directoryCache.TryGetValue(key, out var entry))
        {
            nodes = [];
            return false;
        }
        _directoryCacheOrder.Remove(entry.OrderNode);
        _directoryCacheOrder.AddLast(entry.OrderNode);
        nodes = entry.Nodes;
        return true;
    }

    private void ClearDirectoryCache()
    {
        _directoryCache.Clear();
        _directoryCacheOrder.Clear();
        _directoryCacheNodeCount = 0;
    }

    private OperationState BeginOperation()
    {
        _operation?.Cancel();
        _operation?.Dispose();
        _operation = new CancellationTokenSource();
        IsBusy = true;
        return new OperationState(++_operationVersion, _operation.Token);
    }

    private bool IsCurrent(OperationState operation) => operation.Version == _operationVersion;

    private void CompleteOperation(OperationState operation)
    {
        if (IsCurrent(operation)) IsBusy = false;
    }

    private sealed record DirectoryCacheEntry(IReadOnlyList<BackupNode> Nodes, LinkedListNode<string> OrderNode);
    private sealed record SnapshotIndexEntry(string SearchText, string GroupKey);
    private readonly record struct OperationState(long Version, CancellationToken Token);
}
