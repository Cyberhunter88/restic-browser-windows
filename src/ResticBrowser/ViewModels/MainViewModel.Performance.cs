using ResticBrowser.Models;

namespace ResticBrowser.ViewModels;

public sealed partial class MainViewModel
{
    private bool SnapshotMatchesFilter(SnapshotInfo snapshot) =>
        _snapshotFilterIndex.Matches(snapshot, SnapshotFilter, FilterHost, FilterTag);

    private void ApplySnapshotFilter() =>
        VisibleSnapshots.ReplaceWith(_snapshotFilterIndex.Apply(Snapshots, SnapshotFilter, FilterHost, FilterTag, FilterOnlyLatest));

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
    private readonly record struct OperationState(long Version, CancellationToken Token);
}
