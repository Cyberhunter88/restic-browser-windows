using ResticBrowser.Models;
using ResticBrowser.Services;

namespace ResticBrowser.ViewModels;

public sealed partial class MainViewModel
{
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
                existing.ResolvedResticExecutable = profile.ResolvedResticExecutable;
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
            OnPropertyChanged(nameof(IsConnected));
            if (selectedSnapshot is not null)
                ActivateSnapshot(selectedSnapshot);
        }
        catch
        {
            if (credentialsAdopted && IsCurrent(operation)) Disconnect();
            else if (!credentialsAdopted) credentials.Dispose();
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
        _snapshotFilterIndex.Clear();
        ClearDirectoryCache();
        _history.Clear();
        SelectedSnapshot = null;
        CurrentPath = "/";
        Status = "Verbindung getrennt";
        OnPropertyChanged(nameof(IsConnected));
        NotifyNavigation();
    }
}
