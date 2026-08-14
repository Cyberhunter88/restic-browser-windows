using ResticBrowser.Models;
using ResticBrowser.Services;

namespace ResticBrowser.ViewModels;

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private readonly IResticRepositoryService _repository;
    private readonly SettingsService _settings;
    private readonly IRemoteRestoreService _remoteRestore;
    private SessionCredentials? _credentials;
    private RepositoryProfile? _activeProfile;
    private SnapshotInfo? _selectedSnapshot;
    private BackupNode? _selectedNode;
    private RepositoryStats? _repoStats;
    private string _currentPath = "/";
    private string _status = "Noch mit keinem Repository verbunden";
    private string _snapshotFilter = "";
    private DateTime? _filterStartDate;
    private DateTime? _filterEndDate;
    private string _filterHost = "";
    private string _filterTag = "";
    private bool _filterOnlyLatest;
    private bool _isBusy;
    private CancellationTokenSource? _operation;
    private readonly Stack<string> _backHistory = new();
    private readonly Stack<string> _forwardHistory = new();
    private readonly Dictionary<string, IReadOnlyList<BackupNode>> _directoryCache = new();
    private readonly Queue<string> _directoryCacheOrder = new();
    private const int DirectoryCacheCapacity = 24;

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

    public BackupNode? SelectedNode { get => _selectedNode; set => Set(ref _selectedNode, value); }
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
        set { if (Set(ref _snapshotFilter, value)) ApplySnapshotFilter(); }
    }

    public DateTime? FilterStartDate
    {
        get => _filterStartDate;
        set { if (Set(ref _filterStartDate, value)) ApplySnapshotFilter(); }
    }

    public DateTime? FilterEndDate
    {
        get => _filterEndDate;
        set { if (Set(ref _filterEndDate, value)) ApplySnapshotFilter(); }
    }

    public string FilterHost
    {
        get => _filterHost;
        set { if (Set(ref _filterHost, value)) ApplySnapshotFilter(); }
    }

    public string FilterTag
    {
        get => _filterTag;
        set { if (Set(ref _filterTag, value)) ApplySnapshotFilter(); }
    }

    public bool FilterOnlyLatest
    {
        get => _filterOnlyLatest;
        set { if (Set(ref _filterOnlyLatest, value)) ApplySnapshotFilter(); }
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
        BeginOperation();
        try
        {
            IsBusy = true;
            Status = "Restic wird geprüft …";
            await _repository.ValidateAsync(profile, _operation!.Token);
            _credentials?.Dispose();
            _credentials = credentials;
            ActiveProfile = profile;

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
            await RefreshSnapshotsAsync();
            _ = LoadRepositoryStatsAsync();
            OnPropertyChanged(nameof(IsConnected));
        }
        catch
        {
            credentials.Dispose();
            throw;
        }
        finally { IsBusy = false; }
    }

    public void Disconnect()
    {
        Cancel();
        _credentials?.Dispose();
        _credentials = null;
        ActiveProfile = null;
        RepoStats = null;
        Snapshots.Clear();
        VisibleSnapshots.Clear();
        AvailableHosts.Clear();
        AvailableTags.Clear();
        Nodes.Clear();
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
        BeginOperation();
        IsBusy = true;
        try
        {
            Status = "Snapshots werden geladen …";
            var snapshots = await _repository.GetSnapshotsAsync(ActiveProfile, _credentials, _operation!.Token);
            ClearDirectoryCache();

            var hostSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var tagSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var orderedSnapshots = snapshots.OrderByDescending(s => s.Time).ToList();
            foreach (var snapshot in orderedSnapshots)
            {
                if (!string.IsNullOrWhiteSpace(snapshot.Hostname)) hostSet.Add(snapshot.Hostname);
                foreach (var tag in snapshot.Tags) if (!string.IsNullOrWhiteSpace(tag)) tagSet.Add(tag);
            }
            Snapshots.ReplaceWith(orderedSnapshots);
            AvailableHosts.ReplaceWith(["Alle Hosts", .. hostSet.OrderBy(x => x)]);
            AvailableTags.ReplaceWith(["Alle Tags", .. tagSet.OrderBy(x => x)]);

            FilterHost = "Alle Hosts";
            FilterTag = "Alle Tags";

            ApplySnapshotFilter();
            Status = $"{Snapshots.Count} Snapshot(s) geladen";
            if (SelectedSnapshot is null && VisibleSnapshots.Count > 0)
                SelectedSnapshot = VisibleSnapshots[0];
        }
        finally { IsBusy = false; }
    }

    public async Task LoadRepositoryStatsAsync()
    {
        if (ActiveProfile is null || _credentials is null) return;
        try
        {
            var stats = await _repository.GetStatsAsync(ActiveProfile, _credentials);
            RepoStats = stats;
        }
        catch { /* Stats fail quietly if not supported by backend */ }
    }

    public Task LoadDirectoryAsync(string path) => LoadDirectoryCoreAsync(path, recordHistory: true);

    private async Task LoadDirectoryCoreAsync(string path, bool recordHistory)
    {
        if (ActiveProfile is null || _credentials is null || SelectedSnapshot is null) return;
        BeginOperation();
        IsBusy = true;
        try
        {
            var normalized = ResticCommandBuilder.NormalizeSnapshotPath(path);
            Status = $"{normalized} wird geladen …";
            var cacheKey = $"{SelectedSnapshot.Id}\n{normalized}";
            if (!_directoryCache.TryGetValue(cacheKey, out var nodes))
            {
                nodes = await _repository.GetDirectoryAsync(ActiveProfile, _credentials, SelectedSnapshot.Id, normalized, _operation!.Token);
                CacheDirectory(cacheKey, nodes);
            }
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
        finally { IsBusy = false; }
    }

    public async Task OpenNodeAsync(BackupNode node)
    {
        if (CurrentPath.StartsWith("Suchergebnisse:", StringComparison.Ordinal))
        {
            _backHistory.Clear();
            _forwardHistory.Clear();
            var target = node.IsDirectory ? node.Path : ResticCommandBuilder.ParentPath(node.Path);
            await LoadDirectoryCoreAsync(target, recordHistory: false);
            if (!node.IsDirectory)
                SelectedNode = Nodes.FirstOrDefault(n => n.Path == node.Path);
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
        BeginOperation();
        IsBusy = true;
        try
        {
            Status = "Backup wird durchsucht …";
            var nodes = await _repository.FindAsync(ActiveProfile, _credentials, SelectedSnapshot.Id, pattern.Trim(), _operation!.Token);
            if (!CurrentPath.StartsWith("Suchergebnisse:", StringComparison.Ordinal))
                _backHistory.Push(CurrentPath);
            _forwardHistory.Clear();
            Nodes.ReplaceWith(nodes);
            CurrentPath = $"Suchergebnisse: {pattern.Trim()}";
            Status = $"{Nodes.Count} Treffer";
            NotifyNavigation();
        }
        finally { IsBusy = false; }
    }

    public async Task<IReadOnlyList<DiffEntry>> GetDiffAsync(string snap1, string snap2)
    {
        if (ActiveProfile is null || _credentials is null) return [];
        return await _repository.GetDiffAsync(ActiveProfile, _credentials, snap1, snap2);
    }

    public async Task<FilePreviewData> GetFilePreviewAsync(BackupNode node)
    {
        if (ActiveProfile is null || _credentials is null || SelectedSnapshot is null)
            return new FilePreviewData { ErrorMessage = "Kein Snapshot ausgewählt." };
        return await _repository.GetFilePreviewAsync(ActiveProfile, _credentials, node, SelectedSnapshot.Id);
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
        BeginOperation();
        IsBusy = true;
        try
        {
            Status = "Neueste Version wird gesucht …";
            foreach (var snapshot in Snapshots.OrderByDescending(s => s.Time))
            {
                var matches = await _repository.FindAsync(ActiveProfile, _credentials, snapshot.Id, pattern.Trim(), _operation!.Token);
                var match = matches.FirstOrDefault(n => !n.IsDirectory);
                if (match is null) continue;
                SelectedSnapshot = snapshot;
                Status = $"Neueste Version aus {snapshot.Time:g} gefunden";
                return match;
            }
            Status = "Keine passende Datei in den Snapshots gefunden";
            return null;
        }
        finally { IsBusy = false; }
    }

    public void Cancel() => _operation?.Cancel();

    private void ApplySnapshotFilter()
    {
        var filter = SnapshotFilter.Trim();
        var hostFilter = FilterHost;
        var tagFilter = FilterTag;

        IEnumerable<SnapshotInfo> query = Snapshots;

        if (FilterOnlyLatest)
        {
            query = query.GroupBy(s => $"{s.Hostname}\n{s.PathText}")
                         .Select(g => g.OrderByDescending(s => s.Time).First());
        }

        var visible = query.Where(s =>
                     (filter.Length == 0 ||
                      s.Hostname.Contains(filter, StringComparison.CurrentCultureIgnoreCase) ||
                      s.PathText.Contains(filter, StringComparison.CurrentCultureIgnoreCase) ||
                      s.TagText.Contains(filter, StringComparison.CurrentCultureIgnoreCase) ||
                      s.DisplayId.Contains(filter, StringComparison.OrdinalIgnoreCase)) &&
                     (string.IsNullOrWhiteSpace(hostFilter) || hostFilter == "Alle Hosts" || s.Hostname.Equals(hostFilter, StringComparison.OrdinalIgnoreCase)) &&
                     (string.IsNullOrWhiteSpace(tagFilter) || tagFilter == "Alle Tags" || s.Tags.Contains(tagFilter, StringComparer.OrdinalIgnoreCase)) &&
                     (!FilterStartDate.HasValue || s.Time.Date >= FilterStartDate.Value.Date) &&
                     (!FilterEndDate.HasValue || s.Time.Date <= FilterEndDate.Value.Date)).ToList();
        VisibleSnapshots.ReplaceWith(visible);
    }

    private async Task SaveSettingsStateAsync()
    {
        var settings = await _settings.LoadSettingsAsync();
        settings.Profiles = Profiles.ToList();
        await _settings.SaveSettingsAsync(settings);
    }

    private void CacheDirectory(string key, IReadOnlyList<BackupNode> nodes)
    {
        if (_directoryCache.ContainsKey(key)) return;
        while (_directoryCacheOrder.Count >= DirectoryCacheCapacity)
            _directoryCache.Remove(_directoryCacheOrder.Dequeue());
        _directoryCache[key] = nodes;
        _directoryCacheOrder.Enqueue(key);
    }

    private void ClearDirectoryCache()
    {
        _directoryCache.Clear();
        _directoryCacheOrder.Clear();
    }

    private void BeginOperation()
    {
        _operation?.Cancel();
        _operation?.Dispose();
        _operation = new CancellationTokenSource();
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
        _credentials?.Dispose();
        RemoteTargets.Clear();
    }
}
