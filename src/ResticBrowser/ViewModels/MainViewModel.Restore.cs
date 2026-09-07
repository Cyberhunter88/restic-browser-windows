using ResticBrowser.Models;
using ResticBrowser.Services;

namespace ResticBrowser.ViewModels;

public sealed partial class MainViewModel
{
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
        return await _restore.RestoreAsync(ActiveProfile, _credentials, SelectedSnapshot.Id, nodes, target, overwrite, progress, token);
    }

    public async Task<TarExportResult> ExportTarAsync(
        BackupNode node, string targetFile, CancellationToken token)
    {
        if (ActiveProfile is null || _credentials is null || SelectedSnapshot is null)
            throw new ResticException("Es ist kein Snapshot ausgewählt.");
        return await _restore.ExportTarAsync(ActiveProfile, _credentials, SelectedSnapshot.Id, node, targetFile, token);
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
        return _restore.RestoreRemoteAsync(target, sshCredentials, _credentials, SelectedSnapshot.Id,
            nodes, targetPath, overwrite, progress, token);
    }

    public Task TrustRemoteHostAsync(RemoteHostKeyInfo hostKey) => _remoteRestore.TrustHostAsync(hostKey);
    public Task RemoveRemoteHostTrustAsync(string host, int port) => _remoteRestore.RemoveHostTrustAsync(host, port);
}
