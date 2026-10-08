using ResticBrowser.Models;
using ResticBrowser.Services;

internal static partial class TestSuite
{
    internal static async Task ResticIntegration()
    {
        var executable = await ResolveTestResticAsync();
        var root = Path.Combine(Path.GetTempPath(), "ResticBrowserTests-" + Guid.NewGuid().ToString("N"));
        var source = Path.Combine(root, "Quelle mit Umlaut");
        var repository = Path.Combine(root, "repository");
        var target = Path.Combine(root, "Wiederhergestellt");
        using var cacheDirectory = new EnvironmentVariableScope("RESTIC_CACHE_DIR", Path.Combine(root, "cache"));
        Directory.CreateDirectory(source);
        await File.WriteAllTextAsync(Path.Combine(source, "prüfung.txt"), "restic-browser-e2e");
        var environment = new Dictionary<string, string> { ["RESTIC_PASSWORD"] = "test-password-only" };
        var runner = new ResticProcessRunner();
        var repositoryModes = new Dictionary<string, UnixFileMode>();
        try
        {
            EnsureResticSuccess("Initialisierung", await runner.RunAsync(new ResticCommand(executable, ["--repo", repository, "init", "--json"], environment)));
            EnsureResticSuccess("Sicherung", await runner.RunAsync(new ResticCommand(executable, ["--repo", repository, "backup", "--json", "."], environment, source)));
            // Ein schreibgeschütztes Repository darf für Lesen und Restore keine Lock-Dateien benötigen.
            if (OperatingSystem.IsLinux())
            {
                foreach (var path in Directory.EnumerateFileSystemEntries(repository, "*", SearchOption.AllDirectories).Append(repository))
                {
                    var mode = File.GetUnixFileMode(path);
                    repositoryModes[path] = mode;
                    File.SetUnixFileMode(path, mode & ~(UnixFileMode.UserWrite | UnixFileMode.GroupWrite | UnixFileMode.OtherWrite));
                }
            }
            var service = new ResticRepositoryService(runner);
            var profile = new RepositoryProfile { Name = "Integrationstest", Repository = repository, ResticExecutable = executable };
            using var credentials = new SessionCredentials("test-password-only");
            var snapshots = await service.GetSnapshotsAsync(profile, credentials);
            Equal(1, snapshots.Count);
            var matches = await service.FindAsync(profile, credentials, snapshots[0].Id, "prüfung.txt");
            Equal(1, matches.Matches.Count);
            var preview = await service.GetFilePreviewAsync(profile, credentials, matches.Matches[0], snapshots[0].Id);
            Equal("restic-browser-e2e", preview.TextContent?.Trim());
            var restore = await service.RestoreAsync(profile, credentials, new RestoreRequest(snapshots[0].Id, target, [matches.Matches[0].Path], OverwritePolicy.Never), null);
            True(restore.Success);
            Equal("restic-browser-e2e", await File.ReadAllTextAsync(Directory.GetFiles(target, "prüfung.txt", SearchOption.AllDirectories).Single()));
        }
        finally
        {
            if (OperatingSystem.IsLinux())
                foreach (var (path, mode) in repositoryModes) File.SetUnixFileMode(path, mode);
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    internal static async Task MountOptionsSupported()
    {
        if (!OperatingSystem.IsLinux()) throw new SkippedTestException("Restic mount ist unter Windows nicht verfügbar.");
        var executable = await ResolveTestResticAsync();
        foreach (var snapshot in new string?[] { null, "0123456789abcdef" })
        {
            var args = ResticCommandBuilder.Mount("/tmp/test-repo", new MountRequest(snapshot, "/tmp/target with spaces"));
            args.Add("--help");
            var result = await new ResticProcessRunner().RunAsync(new ResticCommand(executable, args));
            Equal(0, result.ExitCode);
            True(!result.StandardError.Contains("unknown flag", StringComparison.OrdinalIgnoreCase));
        }
    }

    internal static async Task LinuxUnmountArguments()
    {
        if (!OperatingSystem.IsLinux()) throw new SkippedTestException("Linux-Unmount-Helfer.");
        var root = Path.Combine(Path.GetTempPath(), "ResticUnmount-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var helper = Path.Combine(root, "fusermount3");
            var recorded = Path.Combine(root, "args.txt");
            await File.WriteAllTextAsync(helper, "#!/bin/sh\nprintf '%s\\n' \"$@\" > \"$RESTIC_TEST_UNMOUNT_ARGS\"\n");
            File.SetUnixFileMode(helper, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            using var search = new EnvironmentVariableScope("PATH", root);
            using var output = new EnvironmentVariableScope("RESTIC_TEST_UNMOUNT_ARGS", recorded);
            var target = Path.Combine(root, "Ordner mit Leerzeichen");
            await LinuxMountUtilities.UnmountAsync(target);
            True((await File.ReadAllLinesAsync(recorded)).SequenceEqual(new[] { "-u", "--", target }));
            True(!LinuxMountUtilities.IsMounted(target));
            True(LinuxMountUtilities.IsMounted("/proc"));
            using var process = System.Diagnostics.Process.GetCurrentProcess();
            var handle = new ResticMountHandle(target, "full-snapshot-id", process);
            Equal(Path.Combine(target, "ids", "full-snapshot-id"), handle.BrowsePath);
            await File.AppendAllTextAsync(helper, "exit 1\n");
            try { await LinuxMountUtilities.UnmountAsync(target); throw new Exception("Fehlgeschlagenes Aushängen wurde akzeptiert."); }
            catch (ResticException) { }
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    internal static async Task<string> ResolveTestResticAsync()
    {
        var configured = Environment.GetEnvironmentVariable("RESTIC_BROWSER_TEST_RESTIC");
        var executable = string.IsNullOrWhiteSpace(configured) ? ResticLocator.Find() : Path.GetFullPath(configured);
        if (string.IsNullOrWhiteSpace(executable) || !File.Exists(executable) || new FileInfo(executable).Length == 0)
            throw new Exception("Das für den Integrationstest konfigurierte Restic-Programm wurde nicht gefunden.");
        var result = await new ResticProcessRunner().RunAsync(new ResticCommand(executable, ["version", "--json"]));
        if (result.ExitCode != 0) throw new Exception($"Das für den Integrationstest konfigurierte Restic-Programm ist nicht ausführbar: {result.StandardError}");
        return executable;
    }

    private static void EnsureResticSuccess(string operation, ResticProcessResult result)
    {
        if (result.ExitCode != 0) throw new Exception($"Restic-{operation} fehlgeschlagen (Exitcode {result.ExitCode}): {result.StandardError}");
    }

    private sealed class EnvironmentVariableScope : IDisposable
    {
        private readonly string _name;
        private readonly string? _previous;
        public EnvironmentVariableScope(string name, string value)
        {
            _name = name;
            _previous = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, value);
        }
        public void Dispose() => Environment.SetEnvironmentVariable(_name, _previous);
    }
}
