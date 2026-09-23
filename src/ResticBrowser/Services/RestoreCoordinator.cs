using ResticBrowser.Models;

namespace ResticBrowser.Services;

// Builds local restore requests without owning credentials or presentation state.
internal sealed class RestoreCoordinator(IResticRepositoryService repository)
{
    public Task<RestoreResult> RestoreAsync(RepositoryProfile profile, SessionCredentials credentials,
        string snapshotId, IReadOnlyList<BackupNode> nodes, string target, OverwritePolicy overwrite,
        IProgress<RestoreProgress> progress, CancellationToken token) =>
        repository.RestoreAsync(profile, credentials, Request(snapshotId, nodes, target, overwrite), progress, token);

    public Task<RestorePreviewResult> PreviewAsync(RepositoryProfile profile, SessionCredentials credentials,
        string snapshotId, IReadOnlyList<BackupNode> nodes, string target, OverwritePolicy overwrite, CancellationToken token) =>
        repository.PreviewRestoreAsync(profile, credentials, Request(snapshotId, nodes, target, overwrite), token);

    private static RestoreRequest Request(string snapshotId, IReadOnlyList<BackupNode> nodes, string target, OverwritePolicy overwrite) =>
        new(snapshotId, target, nodes.Select(n => n.Path).Distinct(StringComparer.Ordinal).ToList(), overwrite);
}
