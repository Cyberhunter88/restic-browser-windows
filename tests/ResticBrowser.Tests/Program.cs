using System.Text.Json;
using System.Reflection;
using System.IO.Pipes;
using ResticBrowser.Models;
using ResticBrowser.Remote;
using ResticBrowser.Services;
using ResticBrowser.ViewModels;

if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("RESTIC_BROWSER_ASKPASS_PIPE")))
{
    var pipeName = Environment.GetEnvironmentVariable("RESTIC_BROWSER_ASKPASS_PIPE")!;
    using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.In, PipeOptions.Asynchronous);
    await pipe.ConnectAsync(10000);
    using var reader = new StreamReader(pipe);
    Console.Write(await reader.ReadToEndAsync());
    return 0;
}

var tests = new (string Name, Func<Task> Run)[]
{
    ("Snapshot JSON toleriert Zusatzfelder", () => Sync(SnapshotJson)),
    ("JSONL ignoriert unbekannte Zeilen", () => Sync(JsonLines)),
    ("Restore-Argumente sind getrennt und vollständig", () => Sync(RestoreArguments)),
    ("TAR-Export-Argumente sind getrennt und vollständig", () => Sync(TarExportArguments)),
    ("TAR-Dateinamen sind sicher und eindeutig", () => Sync(TarExportNames)),
    ("Unvollständige TAR-Dateien werden entfernt", TarExportCleanup),
    ("Snapshot-Pfade werden normalisiert", () => Sync(Paths)),
    ("Überschreibmodi werden korrekt abgebildet", () => Sync(OverwriteModes)),
    ("Zugangsdaten werden beim Dispose geleert", () => Sync(Credentials)),
    ("Remote-Protokoll toleriert Zusatzfelder", () => Sync(RemoteProtocolJson)),
    ("Remote-Zugangsdaten werden beim Dispose geleert", () => Sync(RemoteCredentials)),
    ("SSH-Hostvertrauen bleibt ohne Geheimnisse gespeichert", TrustedHostSettings),
    ("Linux-Helfer ist eingebettet", () => Sync(EmbeddedRemoteHelper)),
    ("Remote-Zielpfade bleiben im Basisordner", () => Sync(RemotePaths)),
    ("SFTP Repository-String wird ordnungsgemäß gebaut", () => Sync(SftpRepoString)),
    ("Diff, Stats und Dump Befehle sind korrekt", () => Sync(CommandBuilders)),
    ("Restic-Suche unterscheidet Windows und Linux", () => Sync(LocatorCandidates)),
    ("Linux-Einstellungen respektieren XDG_DATA_HOME", () => Sync(XdgSettings)),
    ("Zugriffsfehler bleiben plattformneutral", PermissionError),
    ("Symlink-Fehler ergeben einen Teilerfolg", SymbolicLinkPermissionError),
    ("Zusätzliche Restore-Fehler bleiben Fehler", MixedRestoreErrors),
    ("Binärvorschau erhält Originalbytes", BinaryPreview),
    ("Binäre Prozessausgabe wird begrenzt", BinaryOutputLimit),
    ("JSONL-Verzeichnis wird zeilenweise verarbeitet", StreamingDirectory),
    ("Verzeichnis-Cache bleibt begrenzt", DirectoryCacheBounded),
    ("E2E: Restic Repository, Suche, Stats, Diff und Restore", ResticIntegration),
    ("E2E Linux: Remote-Helfer stellt ausgewählte Datei wieder her", RemoteHelperIntegration),
    ("E2E Linux: OpenSSH stellt über den VPS-Dienst wieder her", RemoteSshIntegration)
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

static void SftpRepoString()
{
    var profile = new RepositoryProfile
    {
        Name = "SFTP Server",
        Type = RepositoryType.SFTP,
        SftpHost = "backup.server.de",
        SftpPort = 2222,
        SftpUser = "resticuser",
        SftpPath = "/var/restic-repo"
    };

    Equal("sftp:resticuser@backup.server.de:2222:/var/restic-repo", profile.BuildRepositoryString());
}

static void CommandBuilders()
{
    var statsArgs = ResticCommandBuilder.Stats("sftp:user@host:/repo");
    True(statsArgs.Contains("stats"));
    True(statsArgs.Contains("--json"));

    var diffArgs = ResticCommandBuilder.Diff("myrepo", "snap1", "snap2");
    True(diffArgs.Contains("diff"));
    True(diffArgs.Contains("snap1"));
    True(diffArgs.Contains("snap2"));

    var dumpArgs = ResticCommandBuilder.Dump("myrepo", "snap1", @"folder\test.txt");
    True(dumpArgs.Contains("dump"));
    True(dumpArgs.Contains("/folder/test.txt"));

    var mountArgs = ResticCommandBuilder.Mount("myrepo", new MountRequest("snap1", "Z:"));
    True(mountArgs.Contains("mount"));
    True(mountArgs.Contains("--snapshot"));
    True(mountArgs.Contains("snap1"));
    True(mountArgs.Contains("Z:"));

    var quickCheck = ResticCommandBuilder.Check("myrepo", CheckMode.Quick);
    var fullCheck = ResticCommandBuilder.Check("myrepo", CheckMode.Full);
    True(quickCheck.Contains("check"));
    True(!quickCheck.Contains("--read-data"));
    True(fullCheck.Contains("--read-data"));

    var lsJsonArgs = ResticCommandBuilder.LsJson("myrepo", "snap1");
    True(lsJsonArgs.Contains("ls"));
    True(lsJsonArgs.Contains("--json"));
    True(lsJsonArgs.Contains("snap1"));
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

static async Task SymbolicLinkPermissionError()
{
    const string error = "{\"message_type\":\"error\",\"error\":{\"message\":\"symlink \\\\usr\\\\bin\\\\mail: A required privilege is not held by the client.\"},\"during\":\"restore\"}";
    var service = new ResticRepositoryService(new RestoreFailingRunner(error));
    using var credentials = new SessionCredentials("secret");
    var profile = new RepositoryProfile { Repository = "repo", ResticExecutable = Environment.ProcessPath! };
    var result = await service.RestoreAsync(profile, credentials,
        new RestoreRequest("snapshot", "target", ["/etc"], OverwritePolicy.Never), null);
    True(result.Success);
    True(result.Message.Contains("symbolische Verknüpfung", StringComparison.Ordinal));
    True(!result.Message.Contains("message_type", StringComparison.Ordinal));
}

static void RemoteProtocolJson()
{
    const string json = """{"message_type":"progress","files_restored":3,"future_field":{"value":42}}""";
    var message = JsonSerializer.Deserialize<RemoteProtocolMessage>(json)!;
    Equal("progress", message.MessageType);
    Equal(3L, message.FilesRestored);

    var request = new RemoteRestoreCommand
    {
        Operation = "restore",
        Repository = "/srv/repo",
        AllowedRoot = "/srv/restore",
        Target = "/srv/restore/test",
        Includes = ["/datei.txt"]
    };
    var serialized = JsonSerializer.Serialize(request);
    True(serialized.Length < RemoteProtocol.MaximumFrameLength);
}

static void RemoteCredentials()
{
    var credentials = new RemoteSshCredentials("password", "passphrase");
    credentials.Dispose();
    Equal("", credentials.Password);
    Equal("", credentials.PrivateKeyPassphrase);
}

static async Task TrustedHostSettings()
{
    var path = Path.Combine(Path.GetTempPath(), $"ResticBrowserSettings-{Guid.NewGuid():N}.json");
    try
    {
        var settings = new SettingsService(path);
        await settings.TrustSshHostAsync(new TrustedSshHost
        {
            Host = "vps.example.test",
            Port = 2222,
            Algorithm = "ssh-ed25519",
            PublicKey = "AAAA-test-public-key",
            Fingerprint = "SHA256:test"
        });
        var loaded = await settings.LoadSettingsAsync();
        Equal(1, loaded.TrustedSshHosts.Count);
        var json = await File.ReadAllTextAsync(path);
        True(!json.Contains("password", StringComparison.OrdinalIgnoreCase));
        True(!json.Contains("passphrase", StringComparison.OrdinalIgnoreCase));
    }
    finally { if (File.Exists(path)) File.Delete(path); }
}

static void EmbeddedRemoteHelper()
{
    var assembly = typeof(RemoteProtocol).Assembly;
    True(assembly.GetManifestResourceNames().Contains("ResticBrowser.Remote.linux-x64", StringComparer.Ordinal));
    using var stream = assembly.GetManifestResourceStream("ResticBrowser.Remote.linux-x64")!;
    True(stream.Length > 1024 * 1024);
}

static void RemotePaths()
{
    if (!OperatingSystem.IsLinux()) return;
    var container = Path.Combine(Path.GetTempPath(), $"ResticBrowserRemotePaths-{Guid.NewGuid():N}");
    var root = Path.Combine(container, "root");
    var outside = Path.Combine(container, "outside");
    Directory.CreateDirectory(root);
    Directory.CreateDirectory(outside);
    try
    {
        Equal(Path.Combine(root, "target"), RemotePathValidator.Validate(root, Path.Combine(root, "target"), requireWritableRoot: false));
        try
        {
            RemotePathValidator.Validate(root, outside, requireWritableRoot: false);
            throw new Exception("Ein Ziel außerhalb des Basisordners hätte abgewiesen werden müssen.");
        }
        catch (InvalidOperationException) { }

        try
        {
            RemotePathValidator.Validate(root, root + "/folder/../target", requireWritableRoot: false);
            throw new Exception("Ein Ziel mit '..'-Segment hätte abgewiesen werden müssen.");
        }
        catch (InvalidOperationException) { }

        var link = Path.Combine(root, "link");
        Directory.CreateSymbolicLink(link, outside);
        try
        {
            RemotePathValidator.Validate(root, Path.Combine(link, "target"), requireWritableRoot: false);
            throw new Exception("Ein Symlink-Ausbruch hätte abgewiesen werden müssen.");
        }
        catch (InvalidOperationException) { }
    }
    finally { Directory.Delete(container, recursive: true); }
}

static void TarExportArguments()
{
    var request = new TarExportRequest("abc", @"/Ordner mit Leerzeichen/prüfung.txt", @"C:\Ziel mit Leerzeichen\prüfung.tar");
    var args = ResticCommandBuilder.DumpTar("s3:https://server/bucket", request);
    Equal("dump", args[2]);
    Equal("tar", args[args.IndexOf("--archive") + 1]);
    Equal(request.TargetFile, args[args.IndexOf("--target") + 1]);
    True(args.Contains("/Ordner mit Leerzeichen/prüfung.txt"));
}

static void TarExportNames()
{
    var invalid = Path.GetInvalidFileNameChars()[0];
    var fileName = TarExportPathHelper.BuildFileName($"Da{invalid}tei", "1234567890abcdef");
    True(!fileName.Contains(invalid));
    True(fileName.EndsWith("_12345678.tar", StringComparison.Ordinal));

    var longName = string.Concat(Enumerable.Repeat("prüfung-😀", 80));
    var boundedName = TarExportPathHelper.BuildFileName(longName, "1234567890abcdef");
    True(System.Text.Encoding.UTF8.GetByteCount(boundedName) <= 220);
    True(boundedName.EndsWith("_12345678.tar", StringComparison.Ordinal));
    True(!boundedName.EnumerateRunes().Any(rune => rune == System.Text.Rune.ReplacementChar));

    var directory = Path.Combine(Path.GetTempPath(), "tar-export-tests");
    var reserved = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    var first = TarExportPathHelper.GetUniquePath(directory, "Datei.tar", reserved);
    reserved.Add(first);
    var second = TarExportPathHelper.GetUniquePath(directory, "Datei.tar", reserved);
    True(!string.Equals(first, second, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal));
    True(second.EndsWith("Datei_2.tar", StringComparison.Ordinal));
}

static async Task TarExportCleanup()
{
    var root = Path.Combine(Path.GetTempPath(), "ResticBrowserTarCleanup-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    var target = Path.Combine(root, "export.tar");
    var profile = new RepositoryProfile { Repository = "repo", ResticExecutable = Environment.ProcessPath! };
    using var credentials = new SessionCredentials("secret");
    try
    {
        var service = new ResticRepositoryService(new TarTargetRunner(exitCode: 1));
        try
        {
            await service.ExportTarAsync(profile, credentials, new TarExportRequest("snapshot", "/data", target));
            throw new Exception("Ein TAR-Exportfehler wurde erwartet.");
        }
        catch (ResticException) { }
        True(!File.Exists(target));

        await File.WriteAllTextAsync(target, "bestehend");
        try
        {
            await service.ExportTarAsync(profile, credentials, new TarExportRequest("snapshot", "/data", target));
            throw new Exception("Eine vorhandene TAR-Datei hätte abgewiesen werden müssen.");
        }
        catch (ResticException) { }
        Equal("bestehend", await File.ReadAllTextAsync(target));
    }
    finally
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
}

static async Task MixedRestoreErrors()
{
    const string errors = """
        {"message_type":"error","error":{"message":"symlink \\usr\\bin\\mail: A required privilege is not held by the client."},"during":"restore"}
        {"message_type":"error","error":{"message":"open \\etc\\secret: permission denied"},"during":"restore"}
        """;
    var service = new ResticRepositoryService(new RestoreFailingRunner(errors));
    using var credentials = new SessionCredentials("secret");
    var profile = new RepositoryProfile { Repository = "repo", ResticExecutable = Environment.ProcessPath! };
    try
    {
        await service.RestoreAsync(profile, credentials,
            new RestoreRequest("snapshot", "target", ["/etc"], OverwritePolicy.Never), null);
        throw new Exception("Ein Fehler wurde erwartet.");
    }
    catch (ResticException ex)
    {
        Equal("Der Zugriff wurde verweigert.", ex.Message);
    }
}

static async Task BinaryPreview()
{
    var expected = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x00, 0xFF, 0x80, 0x01 };
    var runner = new BinaryRunner(expected);
    var service = new ResticRepositoryService(runner);
    using var credentials = new SessionCredentials("secret");
    var profile = new RepositoryProfile { Repository = "repo", ResticExecutable = Environment.ProcessPath! };
    var preview = await service.GetFilePreviewAsync(profile, credentials,
        new BackupNode { Name = "bild.png", Path = "/bild.png", Type = "file", Size = expected.Length }, "snapshot");
    True(preview.IsImage);
    True(preview.ImageBytes!.SequenceEqual(expected));
    Equal(5 * 1024 * 1024, runner.MaximumOutputBytes);
}

static async Task BinaryOutputLimit()
{
    ResticCommand command = OperatingSystem.IsWindows()
        ? new ResticCommand(Path.Combine(Environment.SystemDirectory, "cmd.exe"), ["/c", "for /L %i in (1,1,200) do @echo 0123456789"])
        : new ResticCommand("/bin/sh", ["-c", "yes 0123456789 | head -c 2048"]);
    try
    {
        await new ResticProcessRunner().RunBinaryAsync(command, 1024);
        throw new Exception("Eine zu große Ausgabe hätte abgewiesen werden müssen.");
    }
    catch (ResticException ex) { True(ex.Message.Contains("überschreitet", StringComparison.Ordinal)); }
}

static async Task StreamingDirectory()
{
    var lines = Enumerable.Range(0, 1000).Select(i =>
        $"{{\"message_type\":\"node\",\"name\":\"{i:D4}.txt\",\"path\":\"/{i:D4}.txt\",\"type\":\"file\",\"size\":{i}}}");
    var runner = new LineRunner(lines);
    var service = new ResticRepositoryService(runner);
    using var credentials = new SessionCredentials("secret");
    var profile = new RepositoryProfile { Repository = "repo", ResticExecutable = Environment.ProcessPath! };
    var nodes = await service.GetDirectoryAsync(profile, credentials, "snapshot", "/");
    Equal(1000, nodes.Count);
    Equal(1000, runner.LinesDelivered);
}

static Task DirectoryCacheBounded()
{
    using var viewModel = new MainViewModel(new ResticRepositoryService(new FailingRunner()), new SettingsService(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json")));
    var cacheDirectory = typeof(MainViewModel).GetMethod("CacheDirectory", BindingFlags.NonPublic | BindingFlags.Instance)!;
    var cache = typeof(MainViewModel).GetField("_directoryCache", BindingFlags.NonPublic | BindingFlags.Instance)!;
    for (var i = 0; i < 30; i++) cacheDirectory.Invoke(viewModel, [$"snapshot\n/{i}", Array.Empty<BackupNode>()]);
    Equal(24, ((System.Collections.IDictionary)cache.GetValue(viewModel)!).Count);
    return Task.CompletedTask;
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

        var preview = await service.GetFilePreviewAsync(profile, credentials, matches[0], snapshots[0].Id);
        True(preview.IsText);
        Equal("restic-browser-e2e", preview.TextContent?.Trim());

        var stats = await service.GetStatsAsync(profile, credentials);
        True(stats.TotalFileCount >= 0);

        var analysis = await service.AnalyzeSnapshotStorageAsync(profile, credentials, snapshots[0].Id);
        Equal(1, analysis.TotalFileCount);
        True(analysis.Categories.Any(c => c.Name == "Dokumente"));

        var restore = await service.RestoreAsync(profile, credentials,
            new RestoreRequest(snapshots[0].Id, target, [matches[0].Path], OverwritePolicy.Never),
            progress: null);
        True(restore.Success);
        var restoredFile = Directory.GetFiles(target, "prüfung.txt", SearchOption.AllDirectories).Single();
        Equal("restic-browser-e2e", await File.ReadAllTextAsync(restoredFile));

        var tarTarget = Path.Combine(root, "prüfung.tar");
        var tarExport = await service.ExportTarAsync(profile, credentials,
            new TarExportRequest(snapshots[0].Id, matches[0].Path, tarTarget));
        True(tarExport.Success);
        True(File.Exists(tarTarget));
        True(new FileInfo(tarTarget).Length > 0);

        var directoryTarTarget = Path.Combine(root, "snapshot-root.tar");
        var directoryTarExport = await service.ExportTarAsync(profile, credentials,
            new TarExportRequest(snapshots[0].Id, "/", directoryTarTarget));
        True(directoryTarExport.Success);
        True(File.Exists(directoryTarTarget));
        True(new FileInfo(directoryTarTarget).Length > 0);
    }
    finally
    {
        if (Directory.Exists(root) &&
            Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase))
            Directory.Delete(root, recursive: true);
    }
}

static async Task RemoteHelperIntegration()
{
    if (!OperatingSystem.IsLinux()) return;
    var restic = ResticLocator.Find();
    if (restic is null) throw new Exception("Installiertes Restic-Programm wurde nicht gefunden.");

    var root = Path.Combine(Path.GetTempPath(), $"ResticBrowserRemoteHelper-{Guid.NewGuid():N}");
    var source = Path.Combine(root, "source");
    var repository = Path.Combine(root, "repository");
    var allowedRoot = Path.Combine(root, "restore");
    var target = Path.Combine(allowedRoot, "successful");
    var helperPath = Path.Combine(root, "ResticBrowser.Remote");
    Directory.CreateDirectory(source);
    Directory.CreateDirectory(allowedRoot);
    await File.WriteAllTextAsync(Path.Combine(source, "remote.txt"), "remote-helper-e2e");

    var environment = new Dictionary<string, string> { ["RESTIC_PASSWORD"] = "remote-test-password" };
    var runner = new ResticProcessRunner();
    try
    {
        Equal(0, (await runner.RunAsync(new ResticCommand(restic, ["--repo", repository, "init", "--json"], environment))).ExitCode);
        Equal(0, (await runner.RunAsync(new ResticCommand(restic, ["--repo", repository, "backup", "--json", "."], environment, source))).ExitCode);

        using var repositoryCredentials = new SessionCredentials("remote-test-password");
        var service = new ResticRepositoryService(runner);
        var profile = new RepositoryProfile { Repository = repository, ResticExecutable = restic };
        var snapshot = (await service.GetSnapshotsAsync(profile, repositoryCredentials)).Single();

        await using (var resource = typeof(RemoteProtocol).Assembly.GetManifestResourceStream("ResticBrowser.Remote.linux-x64")!)
        await using (var file = File.Create(helperPath))
            await resource.CopyToAsync(file);
        File.SetUnixFileMode(helperPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        var command = new RemoteRestoreCommand
        {
            Operation = "restore",
            ResticExecutable = restic,
            Repository = repository,
            RepositoryPassword = "remote-test-password",
            AllowedRoot = allowedRoot,
            Target = target,
            SnapshotId = snapshot.Id,
            Includes = ["/remote.txt"],
            Overwrite = "never"
        };
        var messages = await RunRemoteHelperAsync(helperPath, command);
        True(messages.Any(message => message.MessageType == "hello" && message.ProtocolVersion == RemoteProtocol.Version));
        True(messages.Any(message => message.MessageType == "result" && message.ExitCode == 0));
        Equal("remote-helper-e2e", await File.ReadAllTextAsync(Path.Combine(target, "remote.txt")));

        command.Target = Path.Combine(root, "outside");
        var rejected = await RunRemoteHelperAsync(helperPath, command);
        True(rejected.Any(message => message.MessageType == "error" && message.Message.Contains("außerhalb", StringComparison.OrdinalIgnoreCase)));
    }
    finally
    {
        if (Directory.Exists(root) && Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.Ordinal))
            Directory.Delete(root, recursive: true);
    }
}

static async Task RemoteSshIntegration()
{
    if (!OperatingSystem.IsLinux()) return;
    var host = Environment.GetEnvironmentVariable("RESTIC_BROWSER_SSH_E2E_HOST");
    var user = Environment.GetEnvironmentVariable("RESTIC_BROWSER_SSH_E2E_USER");
    var password = Environment.GetEnvironmentVariable("RESTIC_BROWSER_SSH_E2E_PASSWORD");
    var restic = Environment.GetEnvironmentVariable("RESTIC_BROWSER_SSH_E2E_RESTIC");
    if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(user) ||
        string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(restic)) return;
    var port = int.TryParse(Environment.GetEnvironmentVariable("RESTIC_BROWSER_SSH_E2E_PORT"), out var configuredPort)
        ? configuredPort : 22222;

    var root = Path.Combine(Path.GetTempPath(), $"ResticBrowserSshE2E-{Guid.NewGuid():N}");
    var source = Path.Combine(root, "source");
    var repository = Path.Combine(root, "repository");
    var allowedRoot = Path.Combine(root, "restore");
    var targetPath = Path.Combine(allowedRoot, "over-ssh");
    var settingsPath = Path.Combine(root, "settings.json");
    Directory.CreateDirectory(source);
    Directory.CreateDirectory(allowedRoot);
    await File.WriteAllTextAsync(Path.Combine(source, "ssh.txt"), "ssh-remote-e2e");

    var runner = new ResticProcessRunner();
    var environment = new Dictionary<string, string> { ["RESTIC_PASSWORD"] = "ssh-repository-password" };
    try
    {
        Equal(0, (await runner.RunAsync(new ResticCommand(restic, ["--repo", repository, "init", "--json"], environment))).ExitCode);
        Equal(0, (await runner.RunAsync(new ResticCommand(restic, ["--repo", repository, "backup", "--json", "."], environment, source))).ExitCode);
        using var repositoryCredentials = new SessionCredentials("ssh-repository-password");
        var repositoryService = new ResticRepositoryService(runner);
        var profile = new RepositoryProfile { Repository = repository, ResticExecutable = restic };
        var snapshot = (await repositoryService.GetSnapshotsAsync(profile, repositoryCredentials)).Single();

        var settings = new SettingsService(settingsPath);
        var remoteService = new RemoteRestoreService(settings);
        var remoteTarget = new RemoteRestoreTarget
        {
            Name = "CI localhost",
            Host = host,
            Port = port,
            User = user,
            AuthenticationType = RemoteAuthenticationType.Password,
            ResticExecutable = restic,
            Repository = repository,
            AllowedRoot = allowedRoot
        };
        using var sshCredentials = new RemoteSshCredentials(password);
        try
        {
            await remoteService.ValidateAsync(remoteTarget, sshCredentials, repositoryCredentials);
            throw new Exception("Ein unbekannter Hostschlüssel hätte bestätigt werden müssen.");
        }
        catch (RemoteHostKeyException ex)
        {
            True(!ex.HostKey.Changed);
            await remoteService.TrustHostAsync(ex.HostKey);
        }
        await remoteService.ValidateAsync(remoteTarget, sshCredentials, repositoryCredentials);
        var result = await remoteService.RestoreAsync(remoteTarget, sshCredentials, repositoryCredentials,
            new RestoreRequest(snapshot.Id, targetPath, ["/ssh.txt"], OverwritePolicy.Never));
        True(result.Success);
        Equal("ssh-remote-e2e", await File.ReadAllTextAsync(Path.Combine(targetPath, "ssh.txt")));
        var settingsJson = await File.ReadAllTextAsync(settingsPath);
        True(!settingsJson.Contains(password, StringComparison.Ordinal));
        True(!settingsJson.Contains("ssh-repository-password", StringComparison.Ordinal));
    }
    finally
    {
        if (Directory.Exists(root) && Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.Ordinal))
            Directory.Delete(root, recursive: true);
    }
}

static async Task<IReadOnlyList<RemoteProtocolMessage>> RunRemoteHelperAsync(string helperPath, RemoteRestoreCommand command)
{
    var startInfo = new System.Diagnostics.ProcessStartInfo
    {
        FileName = helperPath,
        UseShellExecute = false,
        RedirectStandardInput = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        CreateNoWindow = true
    };
    using var process = new System.Diagnostics.Process { StartInfo = startInfo };
    if (!process.Start()) throw new Exception("Remote-Helfer konnte im Test nicht gestartet werden.");
    await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(command));
    await process.StandardInput.FlushAsync();
    var messages = new List<RemoteProtocolMessage>();
    while (await process.StandardOutput.ReadLineAsync() is { } line)
    {
        var message = JsonSerializer.Deserialize<RemoteProtocolMessage>(line);
        if (message is not null) messages.Add(message);
    }
    process.StandardInput.Close();
    await process.WaitForExitAsync();
    var error = await process.StandardError.ReadToEndAsync();
    if (!string.IsNullOrWhiteSpace(error)) throw new Exception(error);
    return messages;
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
    public Task<ResticProcessResult> RunLinesAsync(ResticCommand command, Func<string, Task> onOutputLine, CancellationToken cancellationToken = default) =>
        Task.FromResult(new ResticProcessResult(1, "", "permission denied"));
    public Task<ResticBinaryProcessResult> RunBinaryAsync(ResticCommand command, int maximumOutputBytes, CancellationToken cancellationToken = default) =>
        Task.FromResult(new ResticBinaryProcessResult(1, [], "permission denied"));
}

sealed class RestoreFailingRunner(string error) : IResticProcessRunner
{
    public Task<ResticProcessResult> RunAsync(ResticCommand command, Func<string, Task>? onOutputLine = null, CancellationToken cancellationToken = default) =>
        Task.FromResult(new ResticProcessResult(1, "", error));
    public async Task<ResticProcessResult> RunLinesAsync(ResticCommand command, Func<string, Task> onOutputLine, CancellationToken cancellationToken = default)
    {
        await onOutputLine(error);
        return new ResticProcessResult(1, "", "");
    }
    public Task<ResticBinaryProcessResult> RunBinaryAsync(ResticCommand command, int maximumOutputBytes, CancellationToken cancellationToken = default) =>
        Task.FromResult(new ResticBinaryProcessResult(1, [], error));
}

sealed class BinaryRunner(byte[] bytes) : IResticProcessRunner
{
    public int MaximumOutputBytes { get; private set; }
    public Task<ResticProcessResult> RunAsync(ResticCommand command, Func<string, Task>? onOutputLine = null, CancellationToken cancellationToken = default) =>
        Task.FromResult(new ResticProcessResult(0, "", ""));
    public Task<ResticProcessResult> RunLinesAsync(ResticCommand command, Func<string, Task> onOutputLine, CancellationToken cancellationToken = default) =>
        Task.FromResult(new ResticProcessResult(0, "", ""));
    public Task<ResticBinaryProcessResult> RunBinaryAsync(ResticCommand command, int maximumOutputBytes, CancellationToken cancellationToken = default)
    {
        MaximumOutputBytes = maximumOutputBytes;
        return Task.FromResult(new ResticBinaryProcessResult(0, bytes, ""));
    }
}

sealed class LineRunner(IEnumerable<string> lines) : IResticProcessRunner
{
    public int LinesDelivered { get; private set; }
    public Task<ResticProcessResult> RunAsync(ResticCommand command, Func<string, Task>? onOutputLine = null, CancellationToken cancellationToken = default) =>
        Task.FromResult(new ResticProcessResult(0, "", ""));
    public async Task<ResticProcessResult> RunLinesAsync(ResticCommand command, Func<string, Task> onOutputLine, CancellationToken cancellationToken = default)
    {
        foreach (var line in lines) { await onOutputLine(line); LinesDelivered++; }
        return new ResticProcessResult(0, "", "");
    }
    public Task<ResticBinaryProcessResult> RunBinaryAsync(ResticCommand command, int maximumOutputBytes, CancellationToken cancellationToken = default) =>
        Task.FromResult(new ResticBinaryProcessResult(0, [], ""));
}

sealed class TarTargetRunner(int exitCode) : IResticProcessRunner
{
    public async Task<ResticProcessResult> RunAsync(ResticCommand command, Func<string, Task>? onOutputLine = null, CancellationToken cancellationToken = default)
    {
        var targetIndex = command.Arguments.ToList().IndexOf("--target");
        if (targetIndex >= 0) await File.WriteAllBytesAsync(command.Arguments[targetIndex + 1], [1, 2, 3], cancellationToken);
        return new ResticProcessResult(exitCode, "", exitCode == 0 ? "" : "TAR-Export fehlgeschlagen");
    }

    public Task<ResticProcessResult> RunLinesAsync(ResticCommand command, Func<string, Task> onOutputLine, CancellationToken cancellationToken = default) =>
        Task.FromResult(new ResticProcessResult(exitCode, "", ""));

    public Task<ResticBinaryProcessResult> RunBinaryAsync(ResticCommand command, int maximumOutputBytes, CancellationToken cancellationToken = default) =>
        Task.FromResult(new ResticBinaryProcessResult(exitCode, [], ""));
}
