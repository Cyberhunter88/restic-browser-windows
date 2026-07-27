using ResticBrowser.Models;

namespace ResticBrowser.Services;

public static class ResticCommandBuilder
{
    public static List<string> WithRepository(string repository, params string[] command)
    {
        var args = new List<string> { "--repo", repository };
        args.AddRange(command);
        return args;
    }

    public static List<string> Restore(string repository, RestoreRequest request)
    {
        var args = WithRepository(repository, "restore", "--json", request.SnapshotId,
            "--target", request.Target, "--overwrite", OverwriteValue(request.Overwrite));
        foreach (var include in request.Includes)
        {
            args.Add("--include");
            args.Add(NormalizeSnapshotPath(include));
        }
        return args;
    }

    public static string NormalizeSnapshotPath(string path)
    {
        var normalized = path.Replace('\\', '/');
        return normalized.StartsWith('/') ? normalized : "/" + normalized;
    }

    public static string ParentPath(string path)
    {
        var normalized = NormalizeSnapshotPath(path).TrimEnd('/');
        var index = normalized.LastIndexOf('/');
        return index <= 0 ? "/" : normalized[..index];
    }

    public static List<string> Stats(string repository) =>
        WithRepository(repository, "stats", "--json");

    public static List<string> Diff(string repository, string snapshot1, string snapshot2) =>
        WithRepository(repository, "diff", "--json", snapshot1, snapshot2);

    public static List<string> Dump(string repository, string snapshotId, string path) =>
        WithRepository(repository, "dump", snapshotId, NormalizeSnapshotPath(path));

    public static List<string> Mount(string repository, MountRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.SnapshotId))
            return WithRepository(repository, "mount", "--snapshot", request.SnapshotId, request.MountPoint);
        return WithRepository(repository, "mount", request.MountPoint);
    }

    public static List<string> LsJson(string repository, string snapshotId) =>
        WithRepository(repository, "ls", "--json", snapshotId);

    public static string OverwriteValue(OverwritePolicy policy) => policy switch
    {
        OverwritePolicy.Never => "never",
        OverwritePolicy.IfNewer => "if-newer",
        OverwritePolicy.IfChanged => "if-changed",
        _ => "always"
    };
}
