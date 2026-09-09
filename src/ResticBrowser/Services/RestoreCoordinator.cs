using ResticBrowser.Models;

namespace ResticBrowser.Services;

// Builds restore requests without owning credentials or presentation state.
internal sealed class RestoreCoordinator(IResticRepositoryService repository, IRemoteRestoreService remote)
{
    public Task<RestoreResult> RestoreAsync(RepositoryProfile profile, SessionCredentials credentials,
        string snapshotId, IReadOnlyList<BackupNode> nodes, string target, OverwritePolicy overwrite,
        IProgress<RestoreProgress> progress, CancellationToken token) =>
        repository.RestoreAsync(profile, credentials, Request(snapshotId, nodes, target, overwrite), progress, token);

    public Task<RestorePreviewResult> PreviewAsync(RepositoryProfile profile, SessionCredentials credentials,
        string snapshotId, IReadOnlyList<BackupNode> nodes, string target, OverwritePolicy overwrite, CancellationToken token) =>
        repository.PreviewRestoreAsync(profile, credentials, Request(snapshotId, nodes, target, overwrite), token);

    public Task<TarExportResult> ExportTarAsync(RepositoryProfile profile, SessionCredentials credentials,
        string snapshotId, BackupNode node, string targetFile, CancellationToken token) =>
        repository.ExportTarAsync(profile, credentials, new TarExportRequest(snapshotId, node.Path, targetFile), token);

    public Task<RestoreResult> RestoreRemoteAsync(RemoteRestoreTarget target, RemoteSshCredentials sshCredentials,
        SessionCredentials credentials, string snapshotId, IReadOnlyList<BackupNode> nodes, string targetPath,
        OverwritePolicy overwrite, IProgress<RestoreProgress> progress, CancellationToken token) =>
        remote.RestoreAsync(target, sshCredentials, credentials, Request(snapshotId, nodes, targetPath, overwrite), progress, token);

    private static RestoreRequest Request(string snapshotId, IReadOnlyList<BackupNode> nodes, string target, OverwritePolicy overwrite) =>
        new(snapshotId, target, nodes.Select(n => n.Path).Distinct(StringComparer.Ordinal).ToList(), overwrite);
}
