using ResticBrowser.Models;
using ResticBrowser.Services;

namespace ResticBrowser.ViewModels;

public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly IResticRepositoryService _repository;
    private readonly SettingsService _settings;
    private readonly IRemoteRestoreService _remoteRestore;
    private SessionCredentials? _credentials;
    private RepositoryProfile? _activeProfile;
    private SnapshotInfo? _selectedSnapshot;
    private RepositoryStats? _repoStats;
    private string _currentPath = "/";
    private string _status = "Noch mit keinem Repository verbunden";
    private string _snapshotFilter = "";
    private string _filterHost = "";
    private string _filterTag = "";
    private bool _filterOnlyLatest;
    private bool _isBusy;
    private CancellationTokenSource? _operation;
    private CancellationTokenSource? _statsOperation;
    private CancellationTokenSource? _filterOperation;
    private long _operationVersion;
    private long _connectionVersion;
    private readonly Stack<string> _backHistory = new();
    private readonly Stack<string> _forwardHistory = new();
    private readonly Dictionary<string, DirectoryCacheEntry> _directoryCache = new();
    private readonly LinkedList<string> _directoryCacheOrder = new();
    private readonly Dictionary<SnapshotInfo, SnapshotIndexEntry> _snapshotIndex = new();
    private int _directoryCacheNodeCount;
    private const int DirectoryCacheCapacity = 24;
    private const int DirectoryCacheNodeCapacity = 50_000;

    public BatchObservableCollection<RepositoryProfile> Profiles { get; } = [];
    public BatchObservableCollection<SnapshotInfo> Snapshots { get; } = [];
    public BatchObservableCollection<SnapshotInfo> VisibleSnapshots { get; } = [];
    public BatchObservableCollection<BackupNode> Nodes { get; } = [];
    public BatchObservableCollection<string> AvailableHosts { get; } = [];
    public BatchObservableCollection<string> AvailableTags { get; } = [];
    public BatchObservableCollection<RemoteRestoreTarget> RemoteTargets { get; } = [];

    public RepositoryProfile? ActiveProfile { get => _activeProfile; private set => Set(ref _activeProfile, value); }
    public SessionCredentials? Credentials => _credentials;
    public RepositoryStats? RepoStats { get => _repoStats; private set => Set(ref _repoStats, value); }

    public SnapshotInfo? SelectedSnapshot
    {
        get => _selectedSnapshot;
        set
        {
            if (Set(ref _selectedSnapshot, value) && value is not null)
            {
                ClearDirectoryCache();
                _backHistory.Clear();
                _forwardHistory.Clear();
                NotifyNavigation();
                _ = SelectSnapshotAsync();
            }
        }
    }

    public string CurrentPath { get => _currentPath; private set { if (Set(ref _currentPath, value)) OnPropertyChanged(nameof(CanGoUp)); } }
    public bool CanGoUp => CurrentPath != "/" && !CurrentPath.StartsWith("Suchergebnisse:", StringComparison.Ordinal);
    public bool CanGoBack => _backHistory.Count > 0;
    public bool CanGoForward => _forwardHistory.Count > 0;
    public string Status { get => _status; private set => Set(ref _status, value); }
    public bool IsBusy { get => _isBusy; private set => Set(ref _isBusy, value); }
    public bool IsConnected => ActiveProfile is not null && _credentials is not null;

    public string SnapshotFilter
    {
        get => _snapshotFilter;
        set { if (Set(ref _snapshotFilter, value)) ScheduleSnapshotFilter(); }
    }

    public string FilterHost
    {
        get => _filterHost;
        set { if (Set(ref _filterHost, value)) ScheduleSnapshotFilter(); }
    }

    public string FilterTag
    {
        get => _filterTag;
        set { if (Set(ref _filterTag, value)) ScheduleSnapshotFilter(); }
    }

    public bool FilterOnlyLatest
    {
        get => _filterOnlyLatest;
        set { if (Set(ref _filterOnlyLatest, value)) ScheduleSnapshotFilter(); }
    }

    public MainViewModel(IResticRepositoryService repository, SettingsService settings, IRemoteRestoreService? remoteRestore = null)
    {
        _repository = repository;
        _settings = settings;
        _remoteRestore = remoteRestore ?? new RemoteRestoreService(settings);
    }

    public async Task InitializeAsync()
    {
        var settings = await _settings.LoadSettingsAsync();
        Profiles.ReplaceWith(settings.Profiles);
    }

    public async Task ConnectAsync(RepositoryProfile profile, SessionCredentials credentials)
    {
        var operation = BeginOperation();
        var credentialsAdopted = false;
        try
        {
            Status = "Restic wird geprüft …";
            await _repository.ValidateAsync(profile, operation.Token);
            if (!IsCurrent(operation)) return;
            _credentials?.Dispose();
            _credentials = credentials;
            credentialsAdopted = true;
            ActiveProfile = profile;
            _connectionVersion++;

            var existing = Profiles.FirstOrDefault(p => p.Id == profile.Id);
            if (existing is null) Profiles.Add(profile);
            else
            {
                existing.Name = profile.Name;
                existing.Repository = profile.Repository;
                existing.ResticExecutable = profile.ResticExecutable;
                existing.Type = profile.Type;
                existing.SftpHost = profile.SftpHost;
                existing.SftpPort = profile.SftpPort;
                existing.SftpUser = profile.SftpUser;
                existing.SftpPath = profile.SftpPath;
                existing.SftpKeyFile = profile.SftpKeyFile;
            }
            await SaveSettingsStateAsync();
            if (!IsCurrent(operation)) return;
            var selectedSnapshot = await RefreshSnapshotsCoreAsync(operation);
            if (!IsCurrent(operation)) return;
            _ = LoadRepositoryStatsAsync();
            OnPropertyChanged(nameof(IsConnected));
            if (selectedSnapshot is not null && !ReferenceEquals(selectedSnapshot, SelectedSnapshot))
                SelectedSnapshot = selectedSnapshot;
        }
        catch
        {
            if (credentialsAdopted) Disconnect();
            else credentials.Dispose();
            throw;
        }
        finally
        {
            if (!credentialsAdopted) credentials.Dispose();
            CompleteOperation(operation);
        }
    }

    public void Disconnect()
    {
        Cancel();
        _operationVersion++;
        IsBusy = false;
        _statsOperation?.Cancel();
        _statsOperation?.Dispose();
        _statsOperation = null;
        _connectionVersion++;
        _credentials?.Dispose();
        _credentials = null;
        ActiveProfile = null;
        RepoStats = null;
        Snapshots.Clear();
        VisibleSnapshots.Clear();
        AvailableHosts.Clear();
        AvailableTags.Clear();
        Nodes.Clear();
        _snapshotIndex.Clear();
        ClearDirectoryCache();
        _backHistory.Clear();
        _forwardHistory.Clear();
        SelectedSnapshot = null;
        CurrentPath = "/";
        Status = "Verbindung getrennt";
        OnPropertyChanged(nameof(IsConnected));
        NotifyNavigation();
    }

    public async Task RefreshSnapshotsAsync()
    {
        if (ActiveProfile is null || _credentials is null) return;
        var operation = BeginOperation();
        try
        {
            var selectedSnapshot = await RefreshSnapshotsCoreAsync(operation);
            if (IsCurrent(operation) && selectedSnapshot is not null && !ReferenceEquals(selectedSnapshot, SelectedSnapshot))
                SelectedSnapshot = selectedSnapshot;
        }
        finally { CompleteOperation(operation); }
    }

    private async Task<SnapshotInfo?> RefreshSnapshotsCoreAsync(OperationState operation)
    {
        if (ActiveProfile is null || _credentials is null) return null;
        Status = "Snapshots werden geladen …";
        var snapshots = await _repository.GetSnapshotsAsync(ActiveProfile, _credentials, operation.Token);
        if (!IsCurrent(operation)) return null;
        ClearDirectoryCache();

        var hostSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var tagSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var orderedSnapshots = snapshots.OrderByDescending(s => s.Time).ToList();
        _snapshotIndex.Clear();
        foreach (var snapshot in orderedSnapshots)
        {
            if (!string.IsNullOrWhiteSpace(snapshot.Hostname)) hostSet.Add(snapshot.Hostname);
            foreach (var tag in snapshot.Tags) if (!string.IsNullOrWhiteSpace(tag)) tagSet.Add(tag);
            _snapshotIndex[snapshot] = new SnapshotIndexEntry(
                string.Join('\n', snapshot.Hostname, snapshot.PathText, snapshot.TagText, snapshot.DisplayId),
                string.Join('\n', snapshot.Hostname, snapshot.PathText));
        }
        Snapshots.ReplaceWith(orderedSnapshots);
        AvailableHosts.ReplaceWith(["Alle Hosts", .. hostSet.OrderBy(x => x)]);
        AvailableTags.ReplaceWith(["Alle Tags", .. tagSet.OrderBy(x => x)]);

        FilterHost = "Alle Hosts";
        FilterTag = "Alle Tags";

        ApplySnapshotFilterImmediately();
        Status = $"{Snapshots.Count} Snapshot(s) geladen";
        return orderedSnapshots.FirstOrDefault(snapshot => snapshot.Id == SelectedSnapshot?.Id)
            ?? VisibleSnapshots.FirstOrDefault();
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
                _backHistory.Push(CurrentPath);
                _forwardHistory.Clear();
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
            _backHistory.Clear();
            _forwardHistory.Clear();
            var target = node.IsDirectory ? node.Path : ResticCommandBuilder.ParentPath(node.Path);
            await LoadDirectoryCoreAsync(target, recordHistory: false);
            return;
        }
        if (node.IsDirectory) await LoadDirectoryAsync(node.Path);
    }

    public Task GoUpAsync() => LoadDirectoryAsync(ResticCommandBuilder.ParentPath(CurrentPath));

    public async Task GoBackAsync()
    {
        if (_backHistory.Count == 0) return;
        var target = _backHistory.Pop();
        if (CurrentPath.StartsWith("Suchergebnisse:", StringComparison.Ordinal))
            _forwardHistory.Clear();
        else
            _forwardHistory.Push(CurrentPath);
        await LoadDirectoryCoreAsync(target, recordHistory: false);
    }

    public async Task GoForwardAsync()
    {
        if (_forwardHistory.Count == 0) return;
        var target = _forwardHistory.Pop();
        _backHistory.Push(CurrentPath);
        await LoadDirectoryCoreAsync(target, recordHistory: false);
    }

    public async Task SearchAsync(string pattern)
    {
        if (ActiveProfile is null || _credentials is null || SelectedSnapshot is null || string.IsNullOrWhiteSpace(pattern)) return;
        var operation = BeginOperation();
        try
        {
            Status = "Backup wird durchsucht …";
            var searchResult = await _repository.FindAsync(ActiveProfile, _credentials, SelectedSnapshot.Id, pattern.Trim(), operation.Token);
            if (!IsCurrent(operation)) return;
            if (!CurrentPath.StartsWith("Suchergebnisse:", StringComparison.Ordinal))
                _backHistory.Push(CurrentPath);
            _forwardHistory.Clear();
            Nodes.ReplaceWith(searchResult.Matches);
            CurrentPath = $"Suchergebnisse: {pattern.Trim()}";
            Status = searchResult.IsTruncated
                ? $"{Nodes.Count:N0} Treffer angezeigt (weitere Treffer werden aus Leistungsgründen nicht angezeigt)"
                : $"{Nodes.Count:N0} Treffer";
            NotifyNavigation();
        }
        finally { CompleteOperation(operation); }
    }

    public async Task<IReadOnlyList<DiffEntry>> GetDiffAsync(string snap1, string snap2, CancellationToken token = default)
    {
        if (ActiveProfile is null || _credentials is null) return [];
        return await _repository.GetDiffAsync(ActiveProfile, _credentials, snap1, snap2, token);
    }

    public async Task<FilePreviewData> GetFilePreviewAsync(BackupNode node, CancellationToken token = default)
    {
        if (ActiveProfile is null || _credentials is null || SelectedSnapshot is null)
            return new FilePreviewData { ErrorMessage = "Kein Snapshot ausgewählt." };
        return await _repository.GetFilePreviewAsync(ActiveProfile, _credentials, node, SelectedSnapshot.Id, token);
    }

    public async Task<RestoreResult> RestoreAsync(
        IReadOnlyList<BackupNode> nodes, string target, OverwritePolicy overwrite,
        IProgress<RestoreProgress> progress, CancellationToken token)
    {
        if (ActiveProfile is null || _credentials is null || SelectedSnapshot is null)
            throw new ResticException("Es ist kein Snapshot ausgewählt.");
        var request = new RestoreRequest(SelectedSnapshot.Id, target, nodes.Select(n => n.Path).Distinct().ToList(), overwrite);
        return await _repository.RestoreAsync(ActiveProfile, _credentials, request, progress, token);
    }

    public async Task<TarExportResult> ExportTarAsync(
        BackupNode node, string targetFile, CancellationToken token)
    {
        if (ActiveProfile is null || _credentials is null || SelectedSnapshot is null)
            throw new ResticException("Es ist kein Snapshot ausgewählt.");
        var request = new TarExportRequest(SelectedSnapshot.Id, node.Path, targetFile);
        return await _repository.ExportTarAsync(ActiveProfile, _credentials, request, token);
    }

    public Task ValidateRemoteTargetAsync(RemoteRestoreTarget target, RemoteSshCredentials sshCredentials,
        CancellationToken token = default)
    {
        if (_credentials is null) throw new ResticException("Es besteht keine Repository-Verbindung.");
        return _remoteRestore.ValidateAsync(target, sshCredentials, _credentials, token);
    }

    public Task<RestoreResult> RestoreRemoteAsync(RemoteRestoreTarget target, RemoteSshCredentials sshCredentials,
        IReadOnlyList<BackupNode> nodes, string targetPath, OverwritePolicy overwrite,
        IProgress<RestoreProgress> progress, CancellationToken token)
    {
        if (_credentials is null || SelectedSnapshot is null)
            throw new ResticException("Es ist kein Snapshot ausgewählt.");
        var request = new RestoreRequest(SelectedSnapshot.Id, targetPath,
            nodes.Select(node => node.Path).Distinct().ToList(), overwrite);
        return _remoteRestore.RestoreAsync(target, sshCredentials, _credentials, request, progress, token);
    }

    public Task TrustRemoteHostAsync(RemoteHostKeyInfo hostKey) => _remoteRestore.TrustHostAsync(hostKey);
    public Task RemoveRemoteHostTrustAsync(string host, int port) => _remoteRestore.RemoveHostTrustAsync(host, port);

    public async Task<BackupNode?> FindNewestAsync(string pattern)
    {
        if (ActiveProfile is null || _credentials is null || string.IsNullOrWhiteSpace(pattern)) return null;
        var operation = BeginOperation();
        try
        {
            Status = "Neueste Version wird gesucht …";
            var result = await _repository.FindNewestAsync(ActiveProfile, _credentials, pattern.Trim(), operation.Token);
            if (!IsCurrent(operation)) return null;
            if (result is not null)
            {
                var snapshot = Snapshots.FirstOrDefault(item =>
                    string.Equals(item.Id, result.SnapshotId, StringComparison.OrdinalIgnoreCase) ||
                    item.Id.StartsWith(result.SnapshotId, StringComparison.OrdinalIgnoreCase) ||
                    result.SnapshotId.StartsWith(item.Id, StringComparison.OrdinalIgnoreCase));
                if (snapshot is null) return null;
                Status = $"Neueste Version aus {snapshot.Time:g} gefunden";
                SelectedSnapshot = snapshot;
                return result.Node;
            }
            Status = "Keine passende Datei in den Snapshots gefunden";
            return null;
        }
        finally { CompleteOperation(operation); }
    }

    public void Cancel() => _operation?.Cancel();

    private async Task SaveSettingsStateAsync()
    {
        var settings = await _settings.LoadSettingsAsync();
        settings.Profiles = Profiles.ToList();
        await _settings.SaveSettingsAsync(settings);
    }

    private async Task SelectSnapshotAsync()
    {
        try { await LoadDirectoryCoreAsync("/", recordHistory: false); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Status = ex is ResticException ? ex.Message : $"Snapshot konnte nicht geladen werden: {ex.Message}"; }
    }

    private void NotifyNavigation()
    {
        OnPropertyChanged(nameof(CanGoUp));
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(CanGoForward));
    }

    public void Dispose()
    {
        _operation?.Cancel();
        _operation?.Dispose();
        _statsOperation?.Cancel();
        _statsOperation?.Dispose();
        _filterOperation?.Cancel();
        _filterOperation?.Dispose();
        _credentials?.Dispose();
        RemoteTargets.Clear();
    }

}
