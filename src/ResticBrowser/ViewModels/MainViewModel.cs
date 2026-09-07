using ResticBrowser.Models;
using ResticBrowser.Services;

namespace ResticBrowser.ViewModels;

public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly IResticRepositoryService _repository;
    private readonly SettingsService _settings;
    private readonly IRemoteRestoreService _remoteRestore;
    private readonly RestoreCoordinator _restore;
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
    private long? _snapshotLoadVersion;
    private readonly NavigationHistory _history = new();
    private readonly Dictionary<string, DirectoryCacheEntry> _directoryCache = new();
    private readonly LinkedList<string> _directoryCacheOrder = new();
    private readonly SnapshotFilter _snapshotFilterIndex = new();
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
            if (Set(ref _selectedSnapshot, value) && value is not null && _snapshotLoadVersion != _operationVersion)
            {
                ClearDirectoryCache();
                _history.Clear();
                NotifyNavigation();
                _ = SelectSnapshotAsync();
            }
        }
    }

    public string CurrentPath { get => _currentPath; private set { if (Set(ref _currentPath, value)) OnPropertyChanged(nameof(CanGoUp)); } }
    public bool CanGoUp => CurrentPath != "/" && !CurrentPath.StartsWith("Suchergebnisse:", StringComparison.Ordinal);
    public bool CanGoBack => _history.CanGoBack;
    public bool CanGoForward => _history.CanGoForward;
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
        _restore = new RestoreCoordinator(repository, _remoteRestore);
    }

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

    private void ActivateSnapshot(SnapshotInfo snapshot)
    {
        if (ReferenceEquals(snapshot, SelectedSnapshot)) _ = SelectSnapshotAsync();
        else SelectedSnapshot = snapshot;
    }

    private void NotifyNavigation()
    {
        OnPropertyChanged(nameof(CanGoUp));
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(CanGoForward));
    }

    public void Dispose()
    {
        _operationVersion++;
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
