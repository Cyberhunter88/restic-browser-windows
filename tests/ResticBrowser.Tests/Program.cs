using System.Text.Json;
using ResticBrowser.Models;
using ResticBrowser.Services;

var tests = new (string Name, Func<Task> Run)[]
{
    ("Snapshot JSON toleriert Zusatzfelder", () => Sync(SnapshotJson)),
    ("JSONL ignoriert unbekannte Zeilen", () => Sync(JsonLines)),
    ("Restore-Argumente sind getrennt und vollständig", () => Sync(RestoreArguments)),
    ("Snapshot-Pfade werden normalisiert", () => Sync(Paths)),
    ("Überschreibmodi werden korrekt abgebildet", () => Sync(OverwriteModes)),
    ("Zugangsdaten werden beim Dispose geleert", () => Sync(Credentials)),
    ("Restic-Suche unterscheidet Windows und Linux", () => Sync(LocatorCandidates)),
    ("Linux-Einstellungen respektieren XDG_DATA_HOME", () => Sync(XdgSettings)),
    ("Zugriffsfehler bleiben plattformneutral", PermissionError),
    ("E2E: Restic Repository, Suche und Restore", ResticIntegration)
};

var failures = 0;
foreach (var test in tests)
{
    try { await test.Run(); Console.WriteLine($"PASS  {test.Name}"); }
    catch (Exception ex) { failures++; Console.WriteLine($"FAIL  {test.Name}: {ex.Message}"); }
}
Console.WriteLine($"\n{tests.Length - failures}/{tests.Length} Tests erfolgreich.");
return failures == 0 ? 0 : 1;

static void SnapshotJson()
{
    const string json = """{"time":"2026-01-01T12:00:00Z","id":"abcdef123456","hostname":"pc","future_field":42,"summary":{"total_bytes_processed":2048}}""";
    var value = JsonSerializer.Deserialize<SnapshotInfo>(json)!;
    Equal("abcdef12", value.DisplayId);
    Equal("2 KB", value.SizeText);
}

static void JsonLines()
{
    const string json = """
        {"message_type":"snapshot","id":"abc"}
        not-json
        {"message_type":"node","name":"Datei.txt","path":"/Datei.txt","type":"file","size":15,"extra":true}
        """;
    var nodes = ResticRepositoryService.ParseJsonLines<BackupNode>(json);
    Equal(2, nodes.Count);
    Equal("Datei.txt", nodes[1].Name);
}

static void RestoreArguments()
{
    var request = new RestoreRequest("abc", @"C:\Ziel mit Leerzeichen", ["/Dokumente/a.txt", @"Bilder\b.jpg"], OverwritePolicy.Never);
    var args = ResticCommandBuilder.Restore("s3:https://server/bucket", request);
    True(args.Contains(@"C:\Ziel mit Leerzeichen"));
    Equal(2, args.Count(a => a == "--include"));
    True(args.Contains("/Bilder/b.jpg"));
    Equal("never", args[args.IndexOf("--overwrite") + 1]);
}

static void Paths()
{
    Equal("/C:/Users/Test", ResticCommandBuilder.NormalizeSnapshotPath(@"C:\Users\Test"));
    Equal("/C:/Users", ResticCommandBuilder.ParentPath(@"/C:/Users/Test"));
    Equal("/", ResticCommandBuilder.ParentPath("/home"));
}

static void OverwriteModes()
{
    Equal("never", ResticCommandBuilder.OverwriteValue(OverwritePolicy.Never));
    Equal("if-newer", ResticCommandBuilder.OverwriteValue(OverwritePolicy.IfNewer));
    Equal("if-changed", ResticCommandBuilder.OverwriteValue(OverwritePolicy.IfChanged));
    Equal("always", ResticCommandBuilder.OverwriteValue(OverwritePolicy.Always));
}

static void Credentials()
{
    var credentials = new SessionCredentials("secret", new Dictionary<string, string> { ["TOKEN"] = "hidden" });
    credentials.Dispose();
    Equal("", credentials.Password);
    Equal(0, credentials.Environment.Count);
}

static void LocatorCandidates()
{
    var windows = ResticLocator.Candidates(true, "C:\\Programm", "C:\\Werkzeuge", "C:\\Programme").ToList();
    True(windows.Contains(Path.Combine("C:\\Programm", "restic.exe")));
    True(windows.Any(path => path.EndsWith(Path.Combine("WinGet", "Links", "restic.exe"), StringComparison.OrdinalIgnoreCase)));

    var linux = ResticLocator.Candidates(false, "portable", "/usr/local/bin:/usr/bin", "unused").ToList();
    True(linux.Contains(Path.Combine("portable", "restic")));
    True(linux.Contains(Path.Combine("portable", "tools", "restic")));
    True(linux.All(path => !path.Contains("WinGet", StringComparison.OrdinalIgnoreCase)));
}

static void XdgSettings()
{
    var previous = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
    try
    {
        Environment.SetEnvironmentVariable("XDG_DATA_HOME", "/tmp/restic-browser-xdg");
        if (!OperatingSystem.IsWindows()) Equal("/tmp/restic-browser-xdg", SettingsService.GetDataDirectory());
    }
    finally { Environment.SetEnvironmentVariable("XDG_DATA_HOME", previous); }
}

static async Task PermissionError()
{
    var service = new ResticRepositoryService(new FailingRunner());
    using var credentials = new SessionCredentials("secret");
    var profile = new RepositoryProfile { Repository = "repo", ResticExecutable = Environment.ProcessPath! };
    try
    {
        await service.GetSnapshotsAsync(profile, credentials);
        throw new Exception("Ein Fehler wurde erwartet.");
    }
    catch (ResticException ex) { Equal("Der Zugriff wurde verweigert.", ex.Message); }
}

static async Task ResticIntegration()
{
    var executable = ResticLocator.Find();
    if (executable is null)
        throw new Exception("Installiertes Restic-Programm wurde nicht gefunden.");

    var root = Path.Combine(Path.GetTempPath(), "ResticBrowserTests-" + Guid.NewGuid().ToString("N"));
    var source = Path.Combine(root, "Quelle mit Umlaut");
    var repository = Path.Combine(root, "repository");
    var target = Path.Combine(root, "Wiederhergestellt");
    Directory.CreateDirectory(source);
    await File.WriteAllTextAsync(Path.Combine(source, "prüfung.txt"), "restic-browser-e2e");

    var runner = new ResticProcessRunner();
    var environment = new Dictionary<string, string> { ["RESTIC_PASSWORD"] = "test-password-only" };
    try
    {
        var init = await runner.RunAsync(new ResticCommand(executable,
            ["--repo", repository, "init", "--json"], environment));
        Equal(0, init.ExitCode);

        var backup = await runner.RunAsync(new ResticCommand(executable,
            ["--repo", repository, "backup", "--json", "."], environment, source));
        Equal(0, backup.ExitCode);

        var service = new ResticRepositoryService(runner);
        var profile = new RepositoryProfile
        {
            Name = "Integrationstest",
            Repository = repository,
            ResticExecutable = executable
        };
        using var credentials = new SessionCredentials("test-password-only");
        var version = await service.ValidateAsync(profile);
        True(Version.Parse(version.Version) >= new Version(0, 17, 1));

        var snapshots = await service.GetSnapshotsAsync(profile, credentials);
        Equal(1, snapshots.Count);
        var matches = await service.FindAsync(profile, credentials, snapshots[0].Id, "prüfung.txt");
        Equal(1, matches.Count);

        var restore = await service.RestoreAsync(profile, credentials,
            new RestoreRequest(snapshots[0].Id, target, [matches[0].Path], OverwritePolicy.Never),
            progress: null);
        True(restore.Success);
        var restoredFile = Directory.GetFiles(target, "prüfung.txt", SearchOption.AllDirectories).Single();
        Equal("restic-browser-e2e", await File.ReadAllTextAsync(restoredFile));
    }
    finally
    {
        if (Directory.Exists(root) &&
            Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase))
            Directory.Delete(root, recursive: true);
    }
}

static Task Sync(Action action)
{
    action();
    return Task.CompletedTask;
}

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new Exception($"Erwartet: {expected}; erhalten: {actual}");
}

static void True(bool value)
{
    if (!value) throw new Exception("Bedingung ist nicht erfüllt.");
}

sealed class FailingRunner : IResticProcessRunner
{
    public Task<ResticProcessResult> RunAsync(ResticCommand command, Func<string, Task>? onOutputLine = null, CancellationToken cancellationToken = default) =>
        Task.FromResult(new ResticProcessResult(1, "", "permission denied"));
}
