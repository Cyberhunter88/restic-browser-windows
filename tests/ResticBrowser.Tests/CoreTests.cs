using System.Text.Json;
using System.Reflection;
using System.IO.Pipes;
using System.Security.Cryptography;
using ResticBrowser.Models;
using ResticBrowser.Remote;
using ResticBrowser.Services;
using ResticBrowser.ViewModels;


internal static partial class TestSuite
{
    internal static void SnapshotJson()
    {
        const string json = """{"time":"2026-01-01T12:00:00Z","id":"abcdef123456","hostname":"pc","future_field":42,"summary":{"total_bytes_processed":2048}}""";
        var value = JsonSerializer.Deserialize<SnapshotInfo>(json)!;
        Equal("abcdef12", value.DisplayId);
        Equal("2 KB", value.SizeText);
    }

    internal static void JsonLines()
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

    internal static void Paths()
    {
        Equal("/C:/Users/Test", ResticCommandBuilder.NormalizeSnapshotPath(@"C:\Users\Test"));
        Equal("/C:/Users", ResticCommandBuilder.ParentPath(@"/C:/Users/Test"));
        Equal("/", ResticCommandBuilder.ParentPath("/home"));
    }

    internal static void OverwriteModes()
    {
        Equal("never", ResticCommandBuilder.OverwriteValue(OverwritePolicy.Never));
        Equal("if-newer", ResticCommandBuilder.OverwriteValue(OverwritePolicy.IfNewer));
        Equal("if-changed", ResticCommandBuilder.OverwriteValue(OverwritePolicy.IfChanged));
        Equal("always", ResticCommandBuilder.OverwriteValue(OverwritePolicy.Always));
    }

    internal static void PreviewRestoreArguments()
    {
        var request = new RestoreRequest("snapshot id", "C:\\Ziel mit Leerzeichen", ["/Datei mit Leerzeichen.txt"], OverwritePolicy.Never);
        var args = ResticCommandBuilder.PreviewRestore("repo", request);
        True(args.Contains("--dry-run"));
        True(args.Contains("--verbose=2"));
        Equal("snapshot id", args[args.IndexOf("--verbose=2") + 1]);
        True(!args.Contains("--delete"));
    }

    internal static void BackendEnvironmentValidation()
    {
        var values = BackendEnvironmentValidator.Normalize([new EnvironmentEntry { Name = " MY_BACKEND_TOKEN ", Value = "key" }]);
        Equal("key", values["MY_BACKEND_TOKEN"]);
        try { BackendEnvironmentValidator.Normalize([new EnvironmentEntry { Name = "RESTIC_PASSWORD", Value = "secret" }]); throw new Exception("Verwaltete Variable wurde akzeptiert."); }
        catch (ResticException) { }
        try { BackendEnvironmentValidator.Normalize([new EnvironmentEntry { Name = "A", Value = "1" }, new EnvironmentEntry { Name = " a ", Value = "2" }]); throw new Exception("Doppelte Variable wurde akzeptiert."); }
        catch (ResticException) { }
    }

    internal static void CloudRepositoryStrings()
    {
        var s3 = new RepositoryProfile { Type = RepositoryType.S3, S3Endpoint = "https://minio.example", S3Bucket = "backups", S3Prefix = "restic/pc" };
        Equal("s3:https://minio.example/backups/restic/pc", s3.BuildRepositoryString());
        var rest = new RepositoryProfile { Type = RepositoryType.REST, RestServerUrl = "https://rest.example/", RestRepositoryPath = "/computer" };
        Equal("rest:https://rest.example/computer", rest.BuildRepositoryString());
    }

    internal static void Credentials()
    {
        var credentials = new SessionCredentials("secret", new Dictionary<string, string> { ["TOKEN"] = "hidden" });
        credentials.Dispose();
        Equal("", credentials.Password);
        Equal(0, credentials.Environment.Count);
    }

    internal static void SftpRepoString()
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

    internal static void CommandBuilders()
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

    internal static void LocatorCandidates()
    {
        var windows = ResticLocator.Candidates(true, "C:\\Programm", "C:\\Werkzeuge", "C:\\Programme").ToList();
        True(windows.Contains(Path.Combine("C:\\Programm", "restic.exe")));
        True(windows.Any(path => path.EndsWith(Path.Combine("WinGet", "Links", "restic.exe"), StringComparison.OrdinalIgnoreCase)));

        var linux = ResticLocator.Candidates(false, "portable", "/usr/local/bin:/usr/bin", "unused").ToList();
        True(linux.Contains(Path.Combine("portable", "restic")));
        True(linux.Contains(Path.Combine("portable", "tools", "restic")));
        True(linux.All(path => !path.Contains("WinGet", StringComparison.OrdinalIgnoreCase)));
    }

    internal static async Task ResticSelectionAndHash()
    {
        var root = Path.Combine(Path.GetTempPath(), "restic-browser-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var selected = Path.Combine(root, OperatingSystem.IsWindows() ? "restic.exe" : "restic");
        await File.WriteAllTextAsync(selected, "selected-restic");
        try
        {
            var service = new ResticProvisioningService(root);
            var resolved = await service.ResolveAsync(selected);
            Equal(Path.GetFullPath(selected), resolved.Path);
            Equal("Ausgewähltes Programm", resolved.Source);

            var expected = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(selected)));
            True(ResticProvisioningService.IsVerified(selected, expected));
            await File.AppendAllTextAsync(selected, "tampered");
            True(!ResticProvisioningService.IsVerified(selected, expected));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    internal static async Task JsonArrayStopsProcess()
    {
        ResticCommand command = OperatingSystem.IsWindows()
            ? new ResticCommand("powershell.exe", ["-NoProfile", "-Command", """Write-Output '[{"value":1}]'; Start-Sleep -Seconds 30"""])
            : new ResticCommand("/bin/sh", ["-c", "printf '%s\\n' '[{\"value\":1}]'; sleep 30"]);
        var count = 0;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var result = await new ResticProcessRunner().RunJsonArrayUntilAsync<JsonElement>(command, _ =>
        {
            count++;
            return Task.FromResult(true);
        });
        watch.Stop();
        Equal(1, count);
        True(result.StoppedEarly);
        True(watch.Elapsed < TimeSpan.FromSeconds(10));
    }

    internal static async Task CommandMetricsAreSafe()
    {
        var observer = new RecordingCommandObserver();
        ResticCommand command = OperatingSystem.IsWindows()
            ? new ResticCommand(Path.Combine(Environment.SystemDirectory, "cmd.exe"), ["/c", "echo metric"])
            : new ResticCommand("/bin/sh", ["-c", "printf metric"]);
        await new ResticProcessRunner(observer).RunAsync(command);
        True(observer.Last is not null);
        True(observer.Last!.OutputBytes > 0);
        True(observer.Last.TimeToFirstOutput is not null);
        Equal(0, observer.Last.ExitCode);
        True(!observer.Last.Operation.Contains("metric", StringComparison.OrdinalIgnoreCase));
    }

    internal static void XdgSettings()
    {
        var previous = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        try
        {
            Environment.SetEnvironmentVariable("XDG_DATA_HOME", "/tmp/restic-browser-xdg");
            if (!OperatingSystem.IsWindows()) Equal("/tmp/restic-browser-xdg", SettingsService.GetDataDirectory());
        }
        finally { Environment.SetEnvironmentVariable("XDG_DATA_HOME", previous); }
    }

    internal static async Task BinaryPreview()
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

    internal static async Task BinaryOutputLimit()
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
}
