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

    internal static IEnumerable<string> PortableCandidates()
    {
        var executable = OperatingSystem.IsWindows() ? "restic.exe" : "restic";
        yield return Path.Combine(AppContext.BaseDirectory, executable);
        yield return Path.Combine(AppContext.BaseDirectory, "tools", executable);
    }

    internal static IEnumerable<string> SystemCandidates()
    {
        var all = Candidates(
            OperatingSystem.IsWindows(),
            AppContext.BaseDirectory,
            Environment.GetEnvironmentVariable("PATH") ?? "",
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
        return all.Skip(2);
    }

    private static IEnumerable<string> Candidates()
        => PortableCandidates().Concat(SystemCandidates());

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

    internal static string Describe(string candidate)
    {
        var baseDirectory = AppContext.BaseDirectory;
        if (candidate.StartsWith(baseDirectory, StringComparison.OrdinalIgnoreCase))
            return candidate.Contains($"{Path.DirectorySeparatorChar}tools{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                ? "Mit der Anwendung ausgeliefert" : "Neben der Anwendung";
        if (candidate.Contains("WinGet", StringComparison.OrdinalIgnoreCase)) return "WinGet-Installation";
        return "PATH";
    }
}
