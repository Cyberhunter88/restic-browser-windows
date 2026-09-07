using ResticBrowser.Models;
using ResticBrowser.Services;

namespace ResticBrowser.ViewModels;

public sealed partial class MainViewModel
{
    public Task LoadDirectoryAsync(string path) => LoadDirectoryCoreAsync(path, recordHistory: true);

    private async Task LoadDirectoryCoreAsync(string path, bool recordHistory)
    {
        if (ActiveProfile is null || _credentials is null || SelectedSnapshot is null) return;
        var operation = BeginOperation();
        try
        {
            var normalized = ResticCommandBuilder.NormalizeSnapshotPath(path);
            Status = $"{normalized} wird geladen …";
            var cacheKey = $"{SelectedSnapshot.Id}\n{normalized}";
            if (!TryGetCachedDirectory(cacheKey, out var nodes))
            {
                nodes = await _repository.GetDirectoryAsync(ActiveProfile, _credentials, SelectedSnapshot.Id, normalized, operation.Token);
                if (!IsCurrent(operation)) return;
                CacheDirectory(cacheKey, nodes);
            }
            if (!IsCurrent(operation)) return;
            if (recordHistory && CurrentPath != normalized && !CurrentPath.StartsWith("Suchergebnisse:", StringComparison.Ordinal))
            {
                _history.PushBack(CurrentPath);
                _history.ClearForward();
            }
            Nodes.ReplaceWith(nodes);
            CurrentPath = normalized;
            Status = $"{Nodes.Count} Element(e)";
            NotifyNavigation();
        }
        finally { CompleteOperation(operation); }
    }

    public async Task OpenNodeAsync(BackupNode node)
    {
        if (CurrentPath.StartsWith("Suchergebnisse:", StringComparison.Ordinal))
        {
            _history.Clear();
            var target = node.IsDirectory ? node.Path : ResticCommandBuilder.ParentPath(node.Path);
            await LoadDirectoryCoreAsync(target, recordHistory: false);
            return;
        }
        if (node.IsDirectory) await LoadDirectoryAsync(node.Path);
    }

    public Task GoUpAsync() => LoadDirectoryAsync(ResticCommandBuilder.ParentPath(CurrentPath));

    public async Task GoBackAsync()
    {
        if (!_history.CanGoBack) return;
        var target = _history.PopBack();
        if (CurrentPath.StartsWith("Suchergebnisse:", StringComparison.Ordinal))
            _history.ClearForward();
        else
            _history.PushForward(CurrentPath);
        await LoadDirectoryCoreAsync(target, recordHistory: false);
    }

    public async Task GoForwardAsync()
    {
        if (!_history.CanGoForward) return;
        var target = _history.PopForward();
        _history.PushBack(CurrentPath);
        await LoadDirectoryCoreAsync(target, recordHistory: false);
    }

    public async Task SearchAsync(string pattern)
    {
        if (ActiveProfile is null || _credentials is null || SelectedSnapshot is null || string.IsNullOrWhiteSpace(pattern)) return;
        var operation = BeginOperation();
        var profile = ActiveProfile;
        var credentials = _credentials;
        var snapshotId = SelectedSnapshot.Id;
        if (!CurrentPath.StartsWith("Suchergebnisse:", StringComparison.Ordinal))
            _history.PushBack(CurrentPath);
        _history.ClearForward();
        Nodes.Clear();
        CurrentPath = $"Suchergebnisse: {pattern.Trim()}";
        NotifyNavigation();
        try
        {
            Status = "Backup wird durchsucht …";
            var truncated = await _repository.FindBatchedAsync(profile, credentials, snapshotId, pattern.Trim(), async batch =>
            {
                if (!IsCurrent(operation) || operation.Token.IsCancellationRequested) return;
                Nodes.AddRange(batch);
                Status = $"{Nodes.Count:N0} Treffer werden geladen …";
                await Task.Yield();
            }, operation.Token);
            if (!IsCurrent(operation)) return;
            operation.Token.ThrowIfCancellationRequested();
            Status = truncated
                ? $"{Nodes.Count:N0} Treffer angezeigt (weitere Treffer werden aus Leistungsgründen nicht angezeigt)"
                : $"{Nodes.Count:N0} Treffer";
        }
        catch
        {
            if (IsCurrent(operation))
            {
                Nodes.Clear();
                Status = operation.Token.IsCancellationRequested ? "Suche abgebrochen" : "Suche fehlgeschlagen";
            }
            throw;
        }
        finally { CompleteOperation(operation); }
    }
}
