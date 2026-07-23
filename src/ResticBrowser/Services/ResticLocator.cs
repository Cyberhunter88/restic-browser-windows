namespace ResticBrowser.Services;

public static class ResticLocator
{
    public static string? Find()
    {
        foreach (var candidate in Candidates())
        {
            try
            {
                var info = new FileInfo(candidate);
                if (info.Exists && info.Length > 0) return info.FullName;
                if (info.Exists && info.LinkTarget is { } target)
                {
                    var resolved = Path.IsPathRooted(target) ? target : Path.Combine(info.DirectoryName!, target);
                    if (File.Exists(resolved)) return Path.GetFullPath(resolved);
                }
            }
            catch { /* inaccessible candidate */ }
        }
        return null;
    }

    private static IEnumerable<string> Candidates()
    {
        // Portable layout: Restic Browser and restic.exe can live together on a USB stick.
        yield return Path.Combine(AppContext.BaseDirectory, "restic.exe");
        yield return Path.Combine(AppContext.BaseDirectory, "tools", "restic.exe");

        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            yield return Path.Combine(directory.Trim(), "restic.exe");

        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WinGet", "Links", "restic.exe");
        var packages = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WinGet", "Packages");
        if (Directory.Exists(packages))
        {
            IEnumerable<string> files = [];
            try { files = Directory.EnumerateFiles(packages, "restic_*_windows_amd64.exe", SearchOption.AllDirectories); }
            catch { /* Program Files may be protected */ }
            foreach (var file in files) yield return file;
        }
    }
}
