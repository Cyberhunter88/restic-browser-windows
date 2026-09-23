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

    public async Task<IReadOnlyList<FileVersion>> GetFileVersionsAsync(BackupNode node, bool allHosts, CancellationToken token = default)
    {
        if (ActiveProfile is null || _credentials is null || SelectedSnapshot is null)
            return [];
        return await _repository.GetFileVersionsAsync(ActiveProfile, _credentials, node.Path,
            allHosts ? null : SelectedSnapshot.Hostname, token);
    }

    public async Task<FilePreviewData> GetFilePreviewAsync(FileVersion version, CancellationToken token = default)
    {
        if (ActiveProfile is null || _credentials is null) return new FilePreviewData { ErrorMessage = "Es besteht keine Repository-Verbindung." };
        return await _repository.GetFilePreviewAsync(ActiveProfile, _credentials, version.Node, version.Snapshot.Id, token);
    }

    public async Task<RestoreResult> RestoreAsync(
        IReadOnlyList<BackupNode> nodes, string target, OverwritePolicy overwrite,
        IProgress<RestoreProgress> progress, CancellationToken token)
    {
        if (ActiveProfile is null || _credentials is null || SelectedSnapshot is null)
            throw new ResticException("Es ist kein Snapshot ausgewählt.");
        return await _restore.RestoreAsync(ActiveProfile, _credentials, SelectedSnapshot.Id, nodes, target, overwrite, progress, token);
    }

    public Task<RestorePreviewResult> PreviewRestoreAsync(IReadOnlyList<BackupNode> nodes, string target,
        OverwritePolicy overwrite, CancellationToken token)
    {
        if (ActiveProfile is null || _credentials is null || SelectedSnapshot is null)
            throw new ResticException("Es ist kein Snapshot ausgewählt.");
        return _restore.PreviewAsync(ActiveProfile, _credentials, SelectedSnapshot.Id, nodes, target, overwrite, token);
    }

}
