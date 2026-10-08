namespace ResticBrowser.Services;

public static class ResticLocator
{
    public static string? Find() => Candidates().FirstOrDefault(candidate => GetFileError(candidate) is null);

    internal static IEnumerable<string> Candidates() => PortableCandidates().Concat(SystemCandidates())
        .Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    internal static IEnumerable<string> PortableCandidates()
    {
        var executable = OperatingSystem.IsWindows() ? "restic.exe" : "restic";
        yield return Path.Combine(AppContext.BaseDirectory, executable);
        yield return Path.Combine(AppContext.BaseDirectory, "tools", executable);
    }

    internal static IEnumerable<string> SystemCandidates() => Candidates(
        OperatingSystem.IsWindows(), AppContext.BaseDirectory,
        Environment.GetEnvironmentVariable("PATH") ?? "",
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)).Skip(2);

    internal static IEnumerable<string> Candidates(bool isWindows, string baseDirectory, string path, string programFiles)
    {
        var executable = isWindows ? "restic.exe" : "restic";
        var paths = new List<string>
        {
            Path.Combine(baseDirectory, executable),
            Path.Combine(baseDirectory, "tools", executable)
        };
        foreach (var directory in path.Split(isWindows ? ';' : ':', StringSplitOptions.RemoveEmptyEntries))
        {
            try { paths.Add(Path.GetFullPath(Path.Combine(directory, executable))); }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException) { }
        }
        if (!isWindows)
        {
            paths.Add("/usr/local/bin/restic");
            paths.Add("/usr/bin/restic");
        }
        else
        {
            paths.Add(Path.Combine(programFiles, "WinGet", "Links", executable));
            var packages = Path.Combine(programFiles, "WinGet", "Packages");
            try
            {
                if (Directory.Exists(packages))
                    paths.AddRange(Directory.EnumerateFiles(packages, "restic_*_windows_amd64.exe", SearchOption.AllDirectories));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        return paths.Distinct(isWindows ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    }

    internal static string GetIdentity(string path)
    {
        var file = new FileInfo(path);
        try { return file.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? file.FullName; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return file.FullName; }
    }

    internal static string? GetFileError(string path)
    {
        try
        {
            var file = new FileInfo(path);
            if (file.LinkTarget is not null)
                file = file.ResolveLinkTarget(returnFinalTarget: true) as FileInfo ?? file;
            if (!file.Exists) return "Die Restic-Datei fehlt oder ist nicht zugänglich.";
            if (file.Length == 0) return "Die Restic-Datei ist leer.";
            using var stream = File.OpenRead(file.FullName);
            if (!OperatingSystem.IsWindows() &&
                (File.GetUnixFileMode(file.FullName) & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) == 0)
                return "Die Restic-Datei ist nicht ausführbar. Prüfe die Ausführungsrechte.";
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return "Die Restic-Datei kann nicht gelesen werden. Prüfe Pfad und Zugriffsrechte.";
        }
    }

    internal static string Describe(string candidate) => Describe(candidate, AppContext.BaseDirectory);

    internal static string Describe(string candidate, string baseDirectory)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var root = Path.GetFullPath(baseDirectory);
        var fullPath = Path.GetFullPath(candidate);
        var executable = OperatingSystem.IsWindows() ? "restic.exe" : "restic";
        if (string.Equals(fullPath, Path.Combine(root, "tools", executable), comparison))
            return "Mit der Anwendung ausgeliefert";
        if (string.Equals(fullPath, Path.Combine(root, executable), comparison))
            return "Neben der Anwendung";
        if (OperatingSystem.IsWindows() && candidate.Contains("WinGet", comparison)) return "WinGet-Installation";
        return "PATH";
    }
}
