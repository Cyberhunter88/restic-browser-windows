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
        => Candidates(
            OperatingSystem.IsWindows(),
            AppContext.BaseDirectory,
            Environment.GetEnvironmentVariable("PATH") ?? "",
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));

    internal static IEnumerable<string> Candidates(bool isWindows, string baseDirectory, string path, string programFiles)
    {
        var executable = isWindows ? "restic.exe" : "restic";
        // Portable layout: Restic Browser and restic can live together in one folder.
        yield return Path.Combine(baseDirectory, executable);
        yield return Path.Combine(baseDirectory, "tools", executable);

        foreach (var directory in path.Split(isWindows ? ';' : ':', StringSplitOptions.RemoveEmptyEntries))
            yield return Path.Combine(directory.Trim(), executable);

        if (!isWindows) yield break;

        yield return Path.Combine(programFiles, "WinGet", "Links", executable);
        var packages = Path.Combine(programFiles, "WinGet", "Packages");
        if (Directory.Exists(packages))
        {
            IEnumerable<string> files = [];
            try { files = Directory.EnumerateFiles(packages, "restic_*_windows_amd64.exe", SearchOption.AllDirectories); }
            catch { /* Program Files may be protected */ }
            foreach (var file in files) yield return file;
        }
    }
}
