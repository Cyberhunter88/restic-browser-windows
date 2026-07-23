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
    private string _currentPath = "/";
    private string _status = "Noch mit keinem Repository verbunden";
    private string _snapshotFilter = "";
    private bool _isBusy;
    private CancellationTokenSource? _operation;
    private readonly Stack<string> _backHistory = new();
    private readonly Stack<string> _forwardHistory = new();
    private readonly Dictionary<string, IReadOnlyList<BackupNode>> _directoryCache = new();

    public ObservableCollection<RepositoryProfile> Profiles { get; } = [];
    public ObservableCollection<SnapshotInfo> Snapshots { get; } = [];
    public ObservableCollection<SnapshotInfo> VisibleSnapshots { get; } = [];
    public ObservableCollection<BackupNode> Nodes { get; } = [];

    public RepositoryProfile? ActiveProfile { get => _activeProfile; private set => Set(ref _activeProfile, value); }
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

    public MainViewModel(IResticRepositoryService repository, SettingsService settings)
    {
        _repository = repository;
        _settings = settings;
    }

    public async Task InitializeAsync()
    {
        foreach (var profile in await _settings.LoadAsync())
            Profiles.Add(profile);
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
            }
            await _settings.SaveAsync(Profiles);
            await RefreshSnapshotsAsync();
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
        Snapshots.Clear();
        VisibleSnapshots.Clear();
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
            foreach (var snapshot in snapshots.OrderByDescending(s => s.Time)) Snapshots.Add(snapshot);
            ApplySnapshotFilter();
            Status = $"{Snapshots.Count} Snapshot(s) geladen";
            if (SelectedSnapshot is null && VisibleSnapshots.Count > 0)
                SelectedSnapshot = VisibleSnapshots[0];
        }
        finally { IsBusy = false; }
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

    public async Task<RestoreResult> RestoreAsync(
        IReadOnlyList<BackupNode> nodes, string target, OverwritePolicy overwrite,
        IProgress<RestoreProgress> progress, CancellationToken token)
    {
        if (ActiveProfile is null || _credentials is null || SelectedSnapshot is null)
            throw new ResticException("Es ist kein Snapshot ausgewählt.");
        var request = new RestoreRequest(SelectedSnapshot.Id, target, nodes.Select(n => n.Path).Distinct().ToList(), overwrite);
        return await _repository.RestoreAsync(ActiveProfile, _credentials, request, progress, token);
    }

    public void Cancel() => _operation?.Cancel();

    private void ApplySnapshotFilter()
    {
        var filter = SnapshotFilter.Trim();
        VisibleSnapshots.Clear();
        foreach (var snapshot in Snapshots.Where(s =>
                     filter.Length == 0 ||
                     s.Hostname.Contains(filter, StringComparison.CurrentCultureIgnoreCase) ||
                     s.PathText.Contains(filter, StringComparison.CurrentCultureIgnoreCase) ||
                     s.TagText.Contains(filter, StringComparison.CurrentCultureIgnoreCase) ||
                     s.DisplayId.Contains(filter, StringComparison.OrdinalIgnoreCase)))
            VisibleSnapshots.Add(snapshot);
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
