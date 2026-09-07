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
    internal static async Task ResticIntegration()
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
            Equal(1, matches.Matches.Count);

            using (var wrongCredentials = new SessionCredentials("incorrect-password"))
            {
                try
                {
                    await service.FindAsync(profile, wrongCredentials, snapshots[0].Id, "*");
                    throw new Exception("Suche akzeptierte ein falsches Passwort.");
                }
                catch (ResticException ex) { Equal(12, ex.ExitCode); }
            }

            var preview = await service.GetFilePreviewAsync(profile, credentials, matches.Matches[0], snapshots[0].Id);
            True(preview.IsText);
            Equal("restic-browser-e2e", preview.TextContent?.Trim());

            var stats = await service.GetStatsAsync(profile, credentials);
            True(stats.TotalFileCount >= 0);

            var analysis = await service.AnalyzeSnapshotStorageAsync(profile, credentials, snapshots[0].Id);
            Equal(1, analysis.TotalFileCount);
            True(analysis.Categories.Any(c => c.Name == "Dokumente"));

            var restore = await service.RestoreAsync(profile, credentials,
                new RestoreRequest(snapshots[0].Id, target, [matches.Matches[0].Path], OverwritePolicy.Never),
                progress: null);
            True(restore.Success);
            var restoredFile = Directory.GetFiles(target, "prüfung.txt", SearchOption.AllDirectories).Single();
            Equal("restic-browser-e2e", await File.ReadAllTextAsync(restoredFile));

            var tarTarget = Path.Combine(root, "prüfung.tar");
            var tarExport = await service.ExportTarAsync(profile, credentials,
                new TarExportRequest(snapshots[0].Id, matches.Matches[0].Path, tarTarget));
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

    internal static async Task RemoteHelperIntegration()
    {
        if (!OperatingSystem.IsLinux()) throw new SkippedTestException("Benötigt Linux.");
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

    internal static async Task RemoteSshIntegration()
    {
        if (!OperatingSystem.IsLinux()) throw new SkippedTestException("Benötigt Linux.");
        var host = Environment.GetEnvironmentVariable("RESTIC_BROWSER_SSH_E2E_HOST");
        var user = Environment.GetEnvironmentVariable("RESTIC_BROWSER_SSH_E2E_USER");
        var password = Environment.GetEnvironmentVariable("RESTIC_BROWSER_SSH_E2E_PASSWORD");
        var restic = Environment.GetEnvironmentVariable("RESTIC_BROWSER_SSH_E2E_RESTIC");
        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(user) ||
            string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(restic))
            throw new SkippedTestException("OpenSSH-E2E-Ziel ist nicht konfiguriert.");
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

    internal static async Task<IReadOnlyList<RemoteProtocolMessage>> RunRemoteHelperAsync(string helperPath, RemoteRestoreCommand command)
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
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(command));
            await process.StandardInput.FlushAsync(timeout.Token);
            var messages = new List<RemoteProtocolMessage>();
            while (await process.StandardOutput.ReadLineAsync(timeout.Token) is { } line)
            {
                var message = JsonSerializer.Deserialize<RemoteProtocolMessage>(line);
                if (message is not null) messages.Add(message);
            }
            await process.WaitForExitAsync(timeout.Token);
            var error = await process.StandardError.ReadToEndAsync(timeout.Token);
            if (!string.IsNullOrWhiteSpace(error)) throw new Exception(error);
            return messages;
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
            throw new Exception("Der Remote-Helfer wurde im Test nicht innerhalb von 30 Sekunden beendet.");
        }
        finally { process.StandardInput.Close(); }
    }
}
