using System.Collections.ObjectModel;
using ResticBrowser.Models;
using ResticBrowser.Services;

namespace ResticBrowser.ViewModels;

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private readonly IResticRepositoryService _repository;
    private readonly SettingsService _settings;
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

    public ObservableCollection<RepositoryProfile> Profiles { get; } = [];
    public ObservableCollection<SnapshotInfo> Snapshots { get; } = [];
    public ObservableCollection<SnapshotInfo> VisibleSnapshots { get; } = [];
    public ObservableCollection<BackupNode> Nodes { get; } = [];
    public ObservableCollection<Bookmark> Bookmarks { get; } = [];
    public ObservableCollection<string> AvailableHosts { get; } = [];
    public ObservableCollection<string> AvailableTags { get; } = [];

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

    public MainViewModel(IResticRepositoryService repository, SettingsService settings)
    {
        _repository = repository;
        _settings = settings;
    }

    public async Task InitializeAsync()
    {
        var settings = await _settings.LoadSettingsAsync();
        Profiles.Clear();
        foreach (var profile in settings.Profiles) Profiles.Add(profile);

        Bookmarks.Clear();
        foreach (var bookmark in settings.Bookmarks) Bookmarks.Add(bookmark);
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
        _directoryCache.Clear();
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
            _directoryCache.Clear();
            Snapshots.Clear();

            AvailableHosts.Clear();
            AvailableTags.Clear();
            AvailableHosts.Add("Alle Hosts");
            AvailableTags.Add("Alle Tags");

            var hostSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var tagSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var snapshot in snapshots.OrderByDescending(s => s.Time))
            {
                Snapshots.Add(snapshot);
                if (!string.IsNullOrWhiteSpace(snapshot.Hostname)) hostSet.Add(snapshot.Hostname);
                foreach (var tag in snapshot.Tags) if (!string.IsNullOrWhiteSpace(tag)) tagSet.Add(tag);
            }

            foreach (var h in hostSet.OrderBy(x => x)) AvailableHosts.Add(h);
            foreach (var t in tagSet.OrderBy(x => x)) AvailableTags.Add(t);

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
                _directoryCache[cacheKey] = nodes;
            }
            if (recordHistory && CurrentPath != normalized && !CurrentPath.StartsWith("Suchergebnisse:", StringComparison.Ordinal))
            {
                _backHistory.Push(CurrentPath);
                _forwardHistory.Clear();
            }
            Nodes.Clear();
            foreach (var node in nodes) Nodes.Add(node);
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
            Nodes.Clear();
            foreach (var node in nodes) Nodes.Add(node);
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

    public async Task<RestorePreviewResult> PreviewRestoreAsync(
        IReadOnlyList<BackupNode> nodes, string target, OverwritePolicy overwrite, CancellationToken token)
    {
        if (ActiveProfile is null || _credentials is null || SelectedSnapshot is null)
            throw new ResticException("Es ist kein Snapshot ausgewählt.");
        var request = new RestoreRequest(SelectedSnapshot.Id, target, nodes.Select(n => n.Path).Distinct().ToList(), overwrite);
        return await _repository.PreviewRestoreAsync(ActiveProfile, _credentials, request, token);
    }

    public async Task AddBookmarkCurrentPathAsync()
    {
        if (ActiveProfile is null || SelectedSnapshot is null) return;
        var bookmark = new Bookmark
        {
            Name = $"{ActiveProfile.Name}: {SelectedSnapshot.DisplayId} ({CurrentPath})",
            RepositoryProfileId = ActiveProfile.Id,
            SnapshotId = SelectedSnapshot.Id,
            Path = CurrentPath
        };
        Bookmarks.Add(bookmark);
        await SaveSettingsStateAsync();
    }

    public async Task RemoveBookmarkAsync(Bookmark bookmark)
    {
        Bookmarks.Remove(bookmark);
        await SaveSettingsStateAsync();
    }

    public async Task OpenBookmarkAsync(Bookmark bookmark)
    {
        var targetSnap = Snapshots.FirstOrDefault(s => s.Id == bookmark.SnapshotId || s.ShortId == bookmark.SnapshotId);
        if (targetSnap is not null)
        {
            SelectedSnapshot = targetSnap;
            await LoadDirectoryAsync(bookmark.Path);
        }
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

        VisibleSnapshots.Clear();
        foreach (var snapshot in query.Where(s =>
                     (filter.Length == 0 ||
                      s.Hostname.Contains(filter, StringComparison.CurrentCultureIgnoreCase) ||
                      s.PathText.Contains(filter, StringComparison.CurrentCultureIgnoreCase) ||
                      s.TagText.Contains(filter, StringComparison.CurrentCultureIgnoreCase) ||
                      s.DisplayId.Contains(filter, StringComparison.OrdinalIgnoreCase)) &&
                     (string.IsNullOrWhiteSpace(hostFilter) || hostFilter == "Alle Hosts" || s.Hostname.Equals(hostFilter, StringComparison.OrdinalIgnoreCase)) &&
                     (string.IsNullOrWhiteSpace(tagFilter) || tagFilter == "Alle Tags" || s.Tags.Contains(tagFilter, StringComparer.OrdinalIgnoreCase)) &&
                     (!FilterStartDate.HasValue || s.Time.Date >= FilterStartDate.Value.Date) &&
                     (!FilterEndDate.HasValue || s.Time.Date <= FilterEndDate.Value.Date)))
        {
            VisibleSnapshots.Add(snapshot);
        }
    }

    private async Task SaveSettingsStateAsync()
    {
        var settings = new AppSettings
        {
            Profiles = Profiles.ToList(),
            Bookmarks = Bookmarks.ToList()
        };
        await _settings.SaveSettingsAsync(settings);
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
    }
}
