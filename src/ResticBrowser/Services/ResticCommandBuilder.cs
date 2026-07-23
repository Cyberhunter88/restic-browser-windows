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

    public static string OverwriteValue(OverwritePolicy policy) => policy switch
    {
        OverwritePolicy.Never => "never",
        OverwritePolicy.IfNewer => "if-newer",
        OverwritePolicy.IfChanged => "if-changed",
        _ => "always"
    };
}
