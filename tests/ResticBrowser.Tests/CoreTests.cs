using System.Text.Json;
using System.Reflection;
using System.IO.Pipes;
using System.Security.Cryptography;
using ResticBrowser.Models;
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

    internal static void ReadOnlyRepositoryArguments()
    {
        const string repo = "/run/user/1000/kio-fuse-test/smb/server/Backup mit Leerzeichen";
        var request = new RestoreRequest("snapshot", "/tmp/restore", ["/file"], OverwritePolicy.Never);
        var commands = new[]
        {
            ResticCommandBuilder.WithRepository(repo, "snapshots", "--json"),
            ResticCommandBuilder.WithRepository(repo, "find", "--json", "file"),
            ResticCommandBuilder.LsJson(repo, "snapshot"),
            ResticCommandBuilder.Dump(repo, "snapshot", "/file"),
            ResticCommandBuilder.Stats(repo),
            ResticCommandBuilder.Check(repo, CheckMode.Quick),
            ResticCommandBuilder.Check(repo, CheckMode.Full),
            ResticCommandBuilder.Restore(repo, request),
            ResticCommandBuilder.PreviewRestore(repo, request),
            ResticCommandBuilder.Mount(repo, new MountRequest("snapshot", "/tmp/mount")),
            ResticCommandBuilder.Mount(repo, new MountRequest(null, "/tmp/mount"))
        };
        foreach (var args in commands)
        {
            Equal(repo, args[args.IndexOf("--repo") + 1]);
            Equal(1, args.Count(arg => arg == "--no-lock"));
        }
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

    internal static async Task LegacyBackendProfilesAreRemoved()
    {
        var root = Path.Combine(Path.GetTempPath(), "ResticBrowserMigration-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var profiles = new[]
            {
                new RepositoryProfile { Name = "Local", Type = RepositoryType.Local, Repository = "local-repo" },
                new RepositoryProfile { Name = "SFTP", Type = RepositoryType.SFTP, Repository = "sftp:user@host:/repo" },
                new RepositoryProfile { Name = "S3", Type = RepositoryType.S3, Repository = "s3:https://host/bucket" },
                new RepositoryProfile { Name = "REST", Type = RepositoryType.REST, Repository = "rest:https://rest.example/repo" },
                new RepositoryProfile { Name = "REST-Adresse", Repository = "REST:https://rest.example/legacy" }
            };
            foreach (var legacyArray in new[] { false, true })
            {
                var path = Path.Combine(root, legacyArray ? "array.json" : "object.json");
                var json = legacyArray ? JsonSerializer.Serialize(profiles)
                    : JsonSerializer.Serialize(new AppSettings { Profiles = profiles.ToList() });
                json = json.Replace("\"Name\":\"REST\"", "\"Name\":\"REST\",\"RestServerUrl\":\"https://rest.example\",\"RestRepositoryPath\":\"repo\"");
                json = json.Replace("\"Name\":\"SFTP\"", "\"Name\":\"SFTP\",\"SftpHost\":\"host\",\"SftpPort\":22");
                await File.WriteAllTextAsync(path, json);
                var service = new SettingsService(path);
                var settings = await service.LoadSettingsAsync();
                Equal(1, settings.Profiles.Count);
                Equal(profiles[0].Id, settings.Profiles[0].Id);
                Equal("local-repo", settings.Profiles[0].BuildRepositoryString());
                var saved = JsonSerializer.Deserialize<AppSettings>(await File.ReadAllTextAsync(path))!;
                Equal(1, saved.Profiles.Count);
                var reloaded = await service.LoadSettingsAsync();
                Equal(1, reloaded.Profiles.Count);
                Equal(profiles[0].Id, reloaded.Profiles[0].Id);
            }
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    internal static void RemovedBackendsAreRejected()
    {
        foreach (var type in new[] { RepositoryType.SFTP, RepositoryType.S3, RepositoryType.REST, RepositoryType.Other })
        {
            try { new RepositoryProfile { Type = type }.BuildRepositoryString(); throw new Exception("Entferntes Backend wurde akzeptiert."); }
            catch (ResticException) { }
        }
        foreach (var repository in new[] { "sftp:user@host:/repo", "S3:https://host/bucket", "REST:https://host/repo" })
        {
            try { new RepositoryProfile { Repository = repository }.BuildRepositoryString(); throw new Exception("Entfernte Backend-Adresse wurde akzeptiert."); }
            catch (ResticException) { }
        }
    }

    internal static void Credentials()
    {
        var credentials = new SessionCredentials("secret", new Dictionary<string, string> { ["TOKEN"] = "hidden" });
        credentials.Dispose();
        Equal("", credentials.Password);
        Equal(0, credentials.Environment.Count);
    }

    internal static void CommandBuilders()
    {
        var statsArgs = ResticCommandBuilder.Stats("rest:https://host/repo");
        True(statsArgs.Contains("stats"));
        True(statsArgs.Contains("--json"));

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
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(selected, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var service = new ResticProvisioningService(root, new VersionProbeRunner(), root, "");
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
