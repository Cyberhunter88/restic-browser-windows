using System.Security.Cryptography;
using System.Text.Json;
using ResticBrowser.Models;
using ResticBrowser.Services;
using ResticBrowser.ViewModels;

internal sealed class VersionProbeRunner : IResticProcessRunner
{
    internal Func<ResticCommand, CancellationToken, Task<ResticProcessResult>>? Handler { get; init; }
    internal List<string> Started { get; } = [];
    internal static ResticProcessResult Valid => new(0, "{\"version\":\"0.19.1\",\"unknown\":true}", "");
    public Task<ResticProcessResult> RunAsync(ResticCommand command, Func<string, Task>? onOutputLine = null,
        CancellationToken cancellationToken = default)
    {
        TestSuite.True(command.Arguments.SequenceEqual(new[] { "version", "--json" }));
        TestSuite.True(command.Environment is null);
        Started.Add(command.Executable);
        return Handler?.Invoke(command, cancellationToken) ?? Task.FromResult(Valid);
    }
    public Task<ResticProcessResult> RunLinesAsync(ResticCommand command, Func<string, Task> onOutputLine,
        CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ResticBinaryProcessResult> RunBinaryAsync(ResticCommand command, int maximumOutputBytes,
        CancellationToken cancellationToken = default) => throw new NotSupportedException();
}

static partial class TestSuite
{
    private static string WriteExecutable(string directory, string name = "restic", string content = "fixture")
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, OperatingSystem.IsWindows() ? name + ".exe" : name);
        File.WriteAllText(path, content);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return path;
    }

    internal static void ResticCandidateOrder()
    {
        var baseDirectory = Path.Combine(Path.GetTempPath(), "restic-candidates");
        var first = Path.Combine(baseDirectory, "first");
        var windows = ResticLocator.Candidates(true, baseDirectory, first + ";" + first.ToUpperInvariant() + ";" + first, baseDirectory).ToList();
        Equal(1, windows.Count(path => string.Equals(path, Path.Combine(first, "restic.exe"), StringComparison.OrdinalIgnoreCase)));
        if (OperatingSystem.IsWindows()) return;
        var linux = ResticLocator.Candidates(false, "/portable", "/first:/FIRST:/first:/usr/bin", "unused").ToList();
        Equal("/portable/restic", linux[0]);
        Equal("/portable/tools/restic", linux[1]);
        True(linux.IndexOf("/first/restic") < linux.IndexOf("/usr/bin/restic"));
        Equal(1, linux.Count(path => path == "/first/restic"));
        True(linux.Contains("/FIRST/restic"));
        True(linux.Contains("/usr/local/bin/restic"));
        True(ResticLocator.Candidates(false, "/portable", "", "unused").Contains("/usr/bin/restic"));
        Equal("PATH", ResticLocator.Describe("/portable-other/restic", "/portable"));
        if (!OperatingSystem.IsWindows())
            Equal("PATH", ResticLocator.Describe("/PORTABLE/restic", "/portable"));
    }

    internal static async Task ResticAutomaticFallback()
    {
        var root = Path.Combine(Path.GetTempPath(), "restic-resolve-" + Guid.NewGuid().ToString("N"));
        try
        {
            var bundled = WriteExecutable(Path.Combine(root, "tools"));
            var portable = WriteExecutable(root);
            var system = WriteExecutable(Path.Combine(root, "path with spaces"));
            var runner = new VersionProbeRunner
            {
                Handler = (command, _) => Task.FromResult((command.Executable == portable || command.Executable == bundled)
                    ? new ResticProcessResult(0, "not json", "") : VersionProbeRunner.Valid)
            };
            var search = Path.GetDirectoryName(system)!;
            var service = new ResticProvisioningService(root, runner, root, search + Path.PathSeparator + search);
            var result = await service.ResolveAsync(null);
            Equal(system, result.Path);
            Equal("PATH", result.Source);
            Equal("0.19.1", result.Version);
            if (!OperatingSystem.IsWindows()) True(!runner.Started.Contains(bundled));
            Equal(1, runner.Started.Count(path => path == portable));
            Equal(1, runner.Started.Count(path => path == system));
            // Explicit selection never falls through to an unrelated automatic executable.
            var error = await ResolutionError(() => service.ResolveAsync(portable));
            True(error.Contains("ausdrücklich ausgewählte"));
            File.Delete(portable);
            File.Delete(bundled);
            Equal(system, (await service.ResolveAsync(null)).Path);
            if (!OperatingSystem.IsWindows())
            {
                bundled = WriteExecutable(Path.Combine(root, "tools"), content: "corrupt bundle");
                File.CreateSymbolicLink(portable, Path.Combine("tools", "restic"));
                runner.Started.Clear();
                Equal(system, (await service.ResolveAsync(null)).Path);
                True(!runner.Started.Contains(portable) && !runner.Started.Contains(bundled));
                File.Delete(portable);
                bundled = WriteExecutable(Path.Combine(root, "tools"), content: "valid bundle");
                var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(bundled)));
                var bundleRunner = new VersionProbeRunner();
                var bundleService = new ResticProvisioningService(root, bundleRunner, root, search, linuxSha256: hash);
                Equal(bundled, (await bundleService.ResolveAsync(null)).Path);
                Equal(1, bundleRunner.Started.Count);
            }
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private static async Task<string> ResolutionError(Func<Task<ResticExecutableInfo>> action)
    {
        try { await action(); }
        catch (ResticException ex) { return ex.Message; }
        throw new Exception("Eine unbrauchbare Restic-Datei wurde akzeptiert.");
    }

    internal static async Task ResticProbeFailures()
    {
        var root = Path.Combine(Path.GetTempPath(), "restic-probe-" + Guid.NewGuid().ToString("N"));
        try
        {
            var file = WriteExecutable(root);
            foreach (var output in new[] { "null", "{}", "{", "{\"version\":\"0.16.0\"}", "{\"version\":null}" })
            {
                var runner = new VersionProbeRunner { Handler = (_, _) => Task.FromResult(new ResticProcessResult(0, output, "")) };
                var service = new ResticProvisioningService(root, runner, root, "");
                True((await ResolutionError(() => service.ResolveAsync(file))).Contains("Restic"));
                True((await ResolutionError(() => service.ResolveAsync(null))).Contains("automatisch"));
            }
            var failing = new VersionProbeRunner { Handler = (_, _) => Task.FromResult(new ResticProcessResult(1, "", "private raw output")) };
            True(!(await ResolutionError(() => new ResticProvisioningService(root, failing, root, "").ResolveAsync(file))).Contains("private raw output"));
            var waiting = new VersionProbeRunner
            {
                Handler = async (_, token) => { await Task.Delay(System.Threading.Timeout.Infinite, token); return VersionProbeRunner.Valid; }
            };
            var timeoutService = new ResticProvisioningService(root, waiting, root, "", TimeSpan.FromMilliseconds(50));
            True((await ResolutionError(() => timeoutService.ResolveAsync(file))).Contains("Zeitlimit"));
            using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
            var cancelService = new ResticProvisioningService(root, waiting, root, "", TimeSpan.FromSeconds(5));
            try { await cancelService.ResolveAsync(file, cancellation.Token); throw new Exception("Benutzerabbruch fehlt."); }
            catch (OperationCanceledException) { True(cancellation.IsCancellationRequested); }
        }
        finally { Directory.Delete(root, true); }
    }

    internal static async Task ResticLinuxFiles()
    {
        if (OperatingSystem.IsWindows()) throw new SkippedTestException("Linux-Dateirechte und Symlinks.");
        var root = Path.Combine(Path.GetTempPath(), "restic-linux-" + Guid.NewGuid().ToString("N"));
        try
        {
            var file = WriteExecutable(root);
            File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            var service = new ResticProvisioningService(root, new VersionProbeRunner(), root, "");
            True((await ResolutionError(() => service.ResolveAsync(file))).Contains("Ausführungsrechte"));
            if (Environment.UserName != "root")
            {
                File.SetUnixFileMode(file, UnixFileMode.None);
                True((await ResolutionError(() => service.ResolveAsync(file))).Contains("gelesen"));
            }
            File.SetUnixFileMode(file, UnixFileMode.UserExecute | UnixFileMode.UserRead | UnixFileMode.UserWrite);
            var link = Path.Combine(root, "relative link");
            File.CreateSymbolicLink(link, Path.GetFileName(file));
            Equal(link, (await service.ResolveAsync(link)).Path);
            File.WriteAllText(file, "");
            True((await ResolutionError(() => service.ResolveAsync(link))).Contains("leer"));
            File.Delete(file);
            True((await ResolutionError(() => service.ResolveAsync(link))).Contains("fehlt"));
            // Exercise the real process launcher, timeout cleanup and paths containing spaces.
            var script = WriteExecutable(root, "restic test", "#!/bin/sh\nprintf '%s' '{\"version\":\"0.19.1\",\"extra\":1}'\n");
            var real = new ResticProvisioningService(root, new ResticProcessRunner(), root, "");
            Equal("0.19.1", (await real.ResolveAsync(script)).Version);
            File.WriteAllText(script, "invalid executable");
            True((await ResolutionError(() => real.ResolveAsync(script))).Contains("gestartet"));
            File.WriteAllText(script, "#!/bin/sh\nsleep 30\n");
            real = new ResticProvisioningService(root, new ResticProcessRunner(), root, "", TimeSpan.FromMilliseconds(100));
            True((await ResolutionError(() => real.ResolveAsync(script))).Contains("Zeitlimit"));
        }
        finally { Directory.Delete(root, true); }
    }

    internal static async Task ResticSourceIsTransient()
    {
        var profile = new RepositoryProfile { ResolvedResticSource = "Systempfad", ResolvedResticExecutable = "/transient/restic" };
        var json = JsonSerializer.Serialize(profile);
        True(!json.Contains("Systempfad") && !json.Contains("/transient"));
        var settings = Path.Combine(Path.GetTempPath(), "restic-source-" + Guid.NewGuid() + ".json");
        try
        {
            using var model = new MainViewModel(new ControlledRepositoryService(), new SettingsService(settings));
            await model.ConnectAsync(profile, new SessionCredentials("test"));
            Equal("Systempfad", model.ValidatedResticSource!);
        }
        finally { File.Delete(settings); }
    }
}
