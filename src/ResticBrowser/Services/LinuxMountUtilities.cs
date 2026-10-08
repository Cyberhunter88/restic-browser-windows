using System.Diagnostics;

namespace ResticBrowser.Services;

internal static class LinuxMountUtilities
{
    internal static bool IsMounted(string path)
    {
        var target = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
        return File.ReadLines("/proc/self/mountinfo").Any(line =>
        {
            var fields = line.Split(' ');
            if (fields.Length < 6) return false;
            var mounted = fields[4].Replace("\\040", " ").Replace("\\011", "\t")
                .Replace("\\012", "\n").Replace("\\134", "\\");
            return string.Equals(mounted.TrimEnd(Path.DirectorySeparatorChar), target, StringComparison.Ordinal);
        });
    }

    internal static async Task UnmountAsync(string path)
    {
        var search = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator);
        var executable = new[] { "fusermount3", "fusermount" }
            .SelectMany(name => search.Where(directory => !string.IsNullOrWhiteSpace(directory)).Select(directory => Path.Combine(directory, name)))
            .FirstOrDefault(File.Exists) ?? throw new ResticException("Zum Trennen wird fusermount3 oder fusermount benötigt.");
        var start = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        start.ArgumentList.Add("-u");
        start.ArgumentList.Add("--");
        start.ArgumentList.Add(path);
        using var process = Process.Start(start) ?? throw new ResticException("Das Laufwerk konnte nicht getrennt werden.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new ResticException("Das Trennen dauert zu lange. Schließe geöffnete Dateien und Dateimanager-Fenster im eingebundenen Ordner und versuche es erneut.");
        }
        await stdout;
        var error = await stderr;
        if (process.ExitCode != 0)
            throw new ResticException($"Das Laufwerk konnte nicht getrennt werden. Schließe geöffnete Dateien im Zielordner und versuche es erneut.\n{error.Trim()}");
    }
}
