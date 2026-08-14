using System.Diagnostics;
using System.IO.Pipes;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ResticBrowser.Models;
using ResticBrowser.Remote;

namespace ResticBrowser.Services;

public interface IRemoteRestoreService
{
    Task ValidateAsync(RemoteRestoreTarget target, RemoteSshCredentials sshCredentials,
        SessionCredentials repositoryCredentials, CancellationToken token = default);
    Task<RestoreResult> RestoreAsync(RemoteRestoreTarget target, RemoteSshCredentials sshCredentials,
        SessionCredentials repositoryCredentials, RestoreRequest request,
        IProgress<RestoreProgress>? progress = null, CancellationToken token = default);
    Task TrustHostAsync(RemoteHostKeyInfo hostKey);
    Task RemoveHostTrustAsync(string host, int port);
}

public sealed class RemoteHostKeyException(RemoteHostKeyInfo hostKey)
    : Exception(hostKey.Changed
        ? $"Der SSH-Hostschlüssel für {hostKey.Host}:{hostKey.Port} hat sich geändert. Die Verbindung wurde blockiert."
        : $"Der SSH-Hostschlüssel für {hostKey.Host}:{hostKey.Port} ist noch nicht bestätigt.")
{
    public RemoteHostKeyInfo HostKey { get; } = hostKey;
}

public sealed class RemoteRestoreService(SettingsService settings) : IRemoteRestoreService
{
    private const string HelperResourceName = "ResticBrowser.Remote.linux-x64";
    private const string RemoteHelperDirectory = ".local/share/restic-browser/remote/v1";
    private const string RemoteHelperPath = RemoteHelperDirectory + "/ResticBrowser.Remote";
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public async Task ValidateAsync(RemoteRestoreTarget target, RemoteSshCredentials sshCredentials,
        SessionCredentials repositoryCredentials, CancellationToken token = default)
    {
        ValidateTargetFields(target, sshCredentials);
        var context = await PrepareAsync(target, sshCredentials, token);
        try
        {
            var request = BuildCommand("validate", target, repositoryCredentials, null);
            await ExecuteHelperAsync(context, request, null, token);
        }
        finally { context.Dispose(); }
    }

    public async Task<RestoreResult> RestoreAsync(RemoteRestoreTarget target, RemoteSshCredentials sshCredentials,
        SessionCredentials repositoryCredentials, RestoreRequest request,
        IProgress<RestoreProgress>? progress = null, CancellationToken token = default)
    {
        ValidateTargetFields(target, sshCredentials);
        var context = await PrepareAsync(target, sshCredentials, token);
        try
        {
            return await ExecuteHelperAsync(context, BuildCommand("restore", target, repositoryCredentials, request), progress, token);
        }
        finally { context.Dispose(); }
    }

    public Task TrustHostAsync(RemoteHostKeyInfo hostKey) => settings.TrustSshHostAsync(new TrustedSshHost
    {
        Host = hostKey.Host,
        Port = hostKey.Port,
        Algorithm = hostKey.Algorithm,
        PublicKey = hostKey.PublicKey,
        Fingerprint = hostKey.Fingerprint
    });

    public Task RemoveHostTrustAsync(string host, int port) => settings.RemoveTrustedSshHostAsync(host, port);

    private async Task<RemoteExecutionContext> PrepareAsync(RemoteRestoreTarget target,
        RemoteSshCredentials credentials, CancellationToken token)
    {
        var tools = OpenSshLocator.FindAll();
        var hostKeys = await GetHostKeysAsync(tools.KeyScan, target.Host, target.Port, token);
        var trustedHosts = await settings.LoadTrustedSshHostsAsync();
        var trusted = trustedHosts.FirstOrDefault(item => item.Port == target.Port &&
            string.Equals(item.Host, target.Host, StringComparison.OrdinalIgnoreCase));
        if (trusted is null)
            throw new RemoteHostKeyException(hostKeys[0] with { Changed = false });
        var matchingHostKey = hostKeys.FirstOrDefault(item =>
            string.Equals(trusted.Algorithm, item.Algorithm, StringComparison.Ordinal) &&
            CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(trusted.PublicKey), Encoding.UTF8.GetBytes(item.PublicKey)));
        if (matchingHostKey is null)
            throw new RemoteHostKeyException(hostKeys[0] with { Changed = true });

        var knownHostsFile = Path.Combine(Path.GetTempPath(), $"restic-browser-known-hosts-{Guid.NewGuid():N}");
        var hostToken = target.Port == 22 ? target.Host : $"[{target.Host}]:{target.Port}";
        await File.WriteAllTextAsync(knownHostsFile, $"{hostToken} {trusted.Algorithm} {trusted.PublicKey}{Environment.NewLine}", token);
        var context = new RemoteExecutionContext(tools, target, credentials, trusted.Algorithm, knownHostsFile);
        try
        {
            var system = await RunSshAsync(context, "uname -s", null, null, token);
            if (system.ExitCode != 0 || !system.Output.Trim().Equals("Linux", StringComparison.OrdinalIgnoreCase))
                throw new ResticException("Der Zielserver ist kein unterstütztes Linux-System.");
            var architecture = await RunSshAsync(context, "uname -m", null, null, token);
            if (architecture.ExitCode != 0 || architecture.Output.Trim() is not ("x86_64" or "amd64"))
                throw new ResticException("Der Zielserver verwendet keine unterstützte x86_64-Architektur.");
            await EnsureHelperAsync(context, token);
            return context;
        }
        catch
        {
            context.Dispose();
            throw;
        }
    }

    private async Task EnsureHelperAsync(RemoteExecutionContext context, CancellationToken token)
    {
        await using var helper = Assembly.GetExecutingAssembly().GetManifestResourceStream(HelperResourceName)
            ?? throw new ResticException("Der eingebettete Linux-Remote-Helfer fehlt in dieser Anwendung.");
        using var memory = new MemoryStream();
        await helper.CopyToAsync(memory, token);
        var helperBytes = memory.ToArray();
        var expectedHash = Convert.ToHexString(SHA256.HashData(helperBytes)).ToLowerInvariant();

        var hashResult = await RunSshAsync(context, $"sha256sum -- {RemoteHelperPath}", null, null, token);
        if (hashResult.ExitCode == 0 && hashResult.Output.StartsWith(expectedHash, StringComparison.OrdinalIgnoreCase)) return;

        var localHelper = Path.Combine(Path.GetTempPath(), $"ResticBrowser.Remote-{Guid.NewGuid():N}");
        try
        {
            await File.WriteAllBytesAsync(localHelper, helperBytes, token);
            var batch = string.Join('\n',
                $"mkdir .local",
                $"mkdir .local/share",
                $"mkdir .local/share/restic-browser",
                $"mkdir .local/share/restic-browser/remote",
                $"mkdir {RemoteHelperDirectory}",
                $"rm {RemoteHelperPath}.tmp",
                $"put \"{localHelper.Replace('\\', '/').Replace("\"", "\"\"")}\" {RemoteHelperPath}.tmp",
                $"chmod 700 {RemoteHelperPath}.tmp",
                "quit") + "\n";
            var upload = await RunSftpAsync(context, batch, token);
            if (upload.ExitCode != 0)
                throw new ResticException(MapSshError(upload.Error, "Der Remote-Helfer konnte nicht per SFTP installiert werden."));

            var replace = await RunSshAsync(context, $"mv -f -- {RemoteHelperPath}.tmp {RemoteHelperPath}", null, null, token);
            if (replace.ExitCode != 0)
                throw new ResticException(MapSshError(replace.Error, "Der Remote-Helfer konnte nicht atomar aktiviert werden."));

            hashResult = await RunSshAsync(context, $"sha256sum -- {RemoteHelperPath}", null, null, token);
            if (hashResult.ExitCode != 0 || !hashResult.Output.StartsWith(expectedHash, StringComparison.OrdinalIgnoreCase))
                throw new ResticException("Die Prüfsumme des installierten Remote-Helfers stimmt nicht überein.");
        }
        finally
        {
            try { if (File.Exists(localHelper)) File.Delete(localHelper); } catch { }
        }
    }

    private async Task<RestoreResult> ExecuteHelperAsync(RemoteExecutionContext context,
        RemoteRestoreCommand request, IProgress<RestoreProgress>? progress, CancellationToken token)
    {
        var input = JsonSerializer.Serialize(request) + "\n";
        RemoteProtocolMessage? final = null;
        string? protocolError = null;
        var sawHello = false;
        ProcessResult result;
        try
        {
            result = await RunSshAsync(context, RemoteHelperPath, input, line =>
            {
                if (line.Length > RemoteProtocol.MaximumFrameLength)
                    throw new ResticException("Eine Antwort des Remote-Helfers überschreitet die erlaubte Größe.");
                RemoteProtocolMessage? message;
                try { message = JsonSerializer.Deserialize<RemoteProtocolMessage>(line, JsonOptions); }
                catch (JsonException) { return Task.CompletedTask; }
                if (message is null) return Task.CompletedTask;
                switch (message.MessageType)
                {
                    case "hello":
                        sawHello = message.ProtocolVersion == RemoteProtocol.Version;
                        break;
                    case "progress":
                        progress?.Report(new RestoreProgress
                        {
                            MessageType = "status",
                            PercentDone = message.PercentDone,
                            TotalFiles = message.TotalFiles,
                            FilesRestored = message.FilesRestored,
                            FilesSkipped = message.FilesSkipped,
                            TotalBytes = message.TotalBytes,
                            BytesRestored = message.BytesRestored
                        });
                        break;
                    case "result": final = message; break;
                    case "error": protocolError = message.Message; break;
                }
                return Task.CompletedTask;
            }, token);
        }
        finally
        {
            request.RepositoryPassword = string.Empty;
            foreach (var key in request.Environment.Keys.ToList()) request.Environment[key] = string.Empty;
            request.Environment.Clear();
        }

        if (!sawHello) throw new ResticException("Der Remote-Helfer hat keine kompatible Protokollmeldung geliefert.");
        if (!string.IsNullOrWhiteSpace(protocolError)) throw new ResticException(protocolError);
        if (result.ExitCode != 0) throw new ResticException(MapSshError(result.Error, "Die SSH-Verbindung oder der Remote-Helfer wurde unerwartet beendet."));
        if (final is null) throw new ResticException("Der Remote-Helfer hat kein Abschlussergebnis geliefert.");
        return new RestoreResult(true, final.ExitCode, final.FilesRestored, final.FilesSkipped, final.Message);
    }

    private static RemoteRestoreCommand BuildCommand(string operation, RemoteRestoreTarget target,
        SessionCredentials credentials, RestoreRequest? request) => new()
        {
            Operation = operation,
            ResticExecutable = target.ResticExecutable,
            Repository = target.Repository,
            RepositoryPassword = credentials.Password,
            Environment = new Dictionary<string, string>(credentials.Environment, StringComparer.OrdinalIgnoreCase),
            AllowedRoot = target.AllowedRoot,
            SnapshotId = request?.SnapshotId ?? "",
            Target = request?.Target ?? target.AllowedRoot,
            Includes = request?.Includes.ToList() ?? [],
            Overwrite = request is null ? "never" : ResticCommandBuilder.OverwriteValue(request.Overwrite)
        };

    private static void ValidateTargetFields(RemoteRestoreTarget target, RemoteSshCredentials credentials)
    {
        if (string.IsNullOrWhiteSpace(target.Host) || string.IsNullOrWhiteSpace(target.User))
            throw new ResticException("Bitte Host und SSH-Benutzer für den VPS angeben.");
        if (target.Host.StartsWith('-') || target.Host.Any(char.IsWhiteSpace) || target.Host.Any(char.IsControl))
            throw new ResticException("Der SSH-Hostname enthält ungültige Zeichen.");
        if (target.Port is < 1 or > 65535) throw new ResticException("Der SSH-Port ist ungültig.");
        if (string.IsNullOrWhiteSpace(target.Repository)) throw new ResticException("Bitte die Repository-Adresse aus Sicht des VPS angeben.");
        if (string.IsNullOrWhiteSpace(target.AllowedRoot) || !target.AllowedRoot.StartsWith('/'))
            throw new ResticException("Bitte einen absoluten erlaubten Basisordner auf dem VPS angeben.");
        if (target.AuthenticationType == RemoteAuthenticationType.PrivateKey &&
            (string.IsNullOrWhiteSpace(target.PrivateKeyFile) || !File.Exists(target.PrivateKeyFile)))
            throw new ResticException("Die ausgewählte SSH-Schlüsseldatei wurde nicht gefunden.");
        if (target.AuthenticationType == RemoteAuthenticationType.Password && string.IsNullOrEmpty(credentials.Password))
            throw new ResticException("Bitte das SSH-Passwort eingeben.");
    }

    private static async Task<IReadOnlyList<RemoteHostKeyInfo>> GetHostKeysAsync(string keyScan, string host, int port, CancellationToken token)
    {
        var result = await RunProcessAsync(keyScan, ["-p", port.ToString(), "-T", "10", host], null, null, null, token);
        if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.Output))
            throw new ResticException(MapSshError(result.Error, "Der SSH-Hostschlüssel konnte nicht abgerufen werden."));
        var candidates = result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Where(line => !line.StartsWith('#'))
            .Select(line => line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Where(parts => parts.Length >= 3)
            .OrderBy(parts => parts[1] == "ssh-ed25519" ? 0 : parts[1].StartsWith("ecdsa-", StringComparison.Ordinal) ? 1 : 2)
            .ToList();
        if (candidates.Count == 0) throw new ResticException("Der Server hat keinen unterstützten SSH-Hostschlüssel geliefert.");
        var hostKeys = new List<RemoteHostKeyInfo>();
        foreach (var candidate in candidates)
        {
            byte[] keyBytes;
            try { keyBytes = Convert.FromBase64String(candidate[2]); }
            catch (FormatException) { continue; }
            var fingerprint = "SHA256:" + Convert.ToBase64String(SHA256.HashData(keyBytes)).TrimEnd('=');
            hostKeys.Add(new RemoteHostKeyInfo(host, port, candidate[1], candidate[2], fingerprint, false));
        }
        if (hostKeys.Count == 0) throw new ResticException("Der SSH-Hostschlüssel des Servers ist ungültig.");
        return hostKeys;
    }

    private static Task<ProcessResult> RunSshAsync(RemoteExecutionContext context, string command,
        string? input, Func<string, Task>? onLine, CancellationToken token)
    {
        var arguments = BuildConnectionArguments(context, sftp: false);
        arguments.Add(context.Target.Host);
        arguments.Add("--");
        arguments.Add(command);
        return RunProcessAsync(context.Tools.Ssh, arguments, input, onLine, context.AskPassSecret, token);
    }

    private static Task<ProcessResult> RunSftpAsync(RemoteExecutionContext context, string batch, CancellationToken token)
    {
        var arguments = BuildConnectionArguments(context, sftp: true);
        arguments.Add(context.Target.Host);
        return RunProcessAsync(context.Tools.Sftp, arguments, batch, null, context.AskPassSecret, token);
    }

    private static List<string> BuildConnectionArguments(RemoteExecutionContext context, bool sftp)
    {
        var target = context.Target;
        var arguments = new List<string>
        {
            sftp ? "-P" : "-p", target.Port.ToString(), "-l", target.User,
            "-o", $"UserKnownHostsFile={context.KnownHostsFile}",
            "-o", "StrictHostKeyChecking=yes",
            "-o", $"HostKeyAlgorithms={context.HostKeyAlgorithm}",
            "-o", "ConnectTimeout=10",
            "-o", "LogLevel=ERROR"
        };
        switch (target.AuthenticationType)
        {
            case RemoteAuthenticationType.Agent:
                arguments.AddRange(["-o", "BatchMode=yes", "-o", "PreferredAuthentications=publickey"]);
                break;
            case RemoteAuthenticationType.PrivateKey:
                arguments.AddRange(["-i", target.PrivateKeyFile, "-o", "IdentitiesOnly=yes", "-o", "PreferredAuthentications=publickey"]);
                if (context.AskPassSecret is null) arguments.AddRange(["-o", "BatchMode=yes"]);
                break;
            case RemoteAuthenticationType.Password:
                arguments.AddRange(["-o", "PubkeyAuthentication=no", "-o", "PreferredAuthentications=password", "-o", "NumberOfPasswordPrompts=1"]);
                break;
        }
        return arguments;
    }

    private static async Task<ProcessResult> RunProcessAsync(string executable, IReadOnlyList<string> arguments,
        string? input, Func<string, Task>? onLine, string? askPassSecret, CancellationToken token)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);

        CancellationTokenSource? askPassCancellation = null;
        Task? askPassTask = null;
        if (askPassSecret is not null)
        {
            var pipeName = $"restic-browser-askpass-{Guid.NewGuid():N}";
            startInfo.Environment["SSH_ASKPASS"] = Environment.ProcessPath!;
            startInfo.Environment["SSH_ASKPASS_REQUIRE"] = "force";
            startInfo.Environment["DISPLAY"] = ":0";
            startInfo.Environment["RESTIC_BROWSER_ASKPASS_PIPE"] = pipeName;
            askPassCancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
            askPassTask = ServeAskPassAsync(pipeName, askPassSecret, askPassCancellation.Token);
        }

        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start()) throw new ResticException($"{Path.GetFileName(executable)} konnte nicht gestartet werden.");
        }
        catch (Exception ex)
        {
            askPassCancellation?.Cancel();
            if (askPassTask is not null)
            {
                try { await askPassTask; } catch (OperationCanceledException) { }
            }
            askPassCancellation?.Dispose();
            if (ex is ResticException) throw;
            throw new ResticException($"OpenSSH konnte nicht gestartet werden: {ex.Message}", ex);
        }
        try
        {
            using var registration = token.Register(() => { try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { } });
            var errorTask = ReadBoundedAsync(process.StandardError, RemoteProtocol.MaximumErrorLength, token);
            if (input is not null)
            {
                await process.StandardInput.WriteAsync(input.AsMemory(), token);
                await process.StandardInput.FlushAsync(token);
            }

            var output = new StringBuilder();
            while (await process.StandardOutput.ReadLineAsync(token) is { } line)
            {
                if (output.Length + line.Length + 1 <= RemoteProtocol.MaximumFrameLength) output.AppendLine(line);
                if (onLine is not null) await onLine(line);
            }
            await process.WaitForExitAsync(token);
            return new ProcessResult(process.ExitCode, output.ToString(), await errorTask);
        }
        catch
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
            throw;
        }
        finally
        {
            try { process.StandardInput.Close(); } catch { }
            askPassCancellation?.Cancel();
            if (askPassTask is not null)
            {
                try { await askPassTask; } catch (OperationCanceledException) { }
            }
            askPassCancellation?.Dispose();
        }
    }

    private static async Task ServeAskPassAsync(string pipeName, string secret, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            await using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.Out, 1,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            await pipe.WaitForConnectionAsync(token);
            await using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true);
            await writer.WriteAsync(secret.AsMemory(), token);
            await writer.FlushAsync(token);
        }
    }

    private static async Task<string> ReadBoundedAsync(StreamReader reader, int maximumLength, CancellationToken token)
    {
        var output = new StringBuilder();
        var buffer = new char[4096];
        while (await reader.ReadAsync(buffer, token) is var count && count > 0)
        {
            var remaining = maximumLength - output.Length;
            if (remaining > 0) output.Append(buffer, 0, Math.Min(remaining, count));
        }
        return output.ToString();
    }

    private static string MapSshError(string error, string fallback)
    {
        var detail = error.Trim();
        if (detail.Contains("Permission denied", StringComparison.OrdinalIgnoreCase)) return "Die SSH-Anmeldung wurde abgelehnt.";
        if (detail.Contains("Could not resolve hostname", StringComparison.OrdinalIgnoreCase)) return "Der VPS-Hostname konnte nicht aufgelöst werden.";
        if (detail.Contains("Connection refused", StringComparison.OrdinalIgnoreCase)) return "Der VPS hat die SSH-Verbindung abgelehnt.";
        if (detail.Contains("Connection timed out", StringComparison.OrdinalIgnoreCase)) return "Zeitüberschreitung beim Aufbau der SSH-Verbindung.";
        if (detail.Contains("Host key verification failed", StringComparison.OrdinalIgnoreCase)) return "Die SSH-Hostschlüsselprüfung ist fehlgeschlagen.";
        return string.IsNullOrWhiteSpace(detail) ? fallback : detail;
    }

    private sealed class RemoteExecutionContext(OpenSshTools tools, RemoteRestoreTarget target,
        RemoteSshCredentials credentials, string hostKeyAlgorithm, string knownHostsFile) : IDisposable
    {
        public OpenSshTools Tools { get; } = tools;
        public RemoteRestoreTarget Target { get; } = target;
        public string HostKeyAlgorithm { get; } = hostKeyAlgorithm;
        public string KnownHostsFile { get; } = knownHostsFile;
        public string? AskPassSecret { get; } = target.AuthenticationType switch
        {
            RemoteAuthenticationType.Password => credentials.Password,
            RemoteAuthenticationType.PrivateKey when !string.IsNullOrEmpty(credentials.PrivateKeyPassphrase) => credentials.PrivateKeyPassphrase,
            _ => null
        };

        public void Dispose()
        {
            try { if (File.Exists(KnownHostsFile)) File.Delete(KnownHostsFile); } catch { }
        }
    }

    private sealed record ProcessResult(int ExitCode, string Output, string Error);
}

public sealed record OpenSshTools(string Ssh, string Sftp, string KeyScan);

public static class OpenSshLocator
{
    public static OpenSshTools FindAll() => new(Find("ssh"), Find("sftp"), Find("ssh-keyscan"));

    private static string Find(string name)
    {
        var executable = OperatingSystem.IsWindows() ? name + ".exe" : name;
        var candidates = new List<string>
        {
            Path.Combine(AppContext.BaseDirectory, executable),
            Path.Combine(AppContext.BaseDirectory, "tools", executable)
        };
        if (OperatingSystem.IsWindows())
            candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "OpenSSH", executable));
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            candidates.Add(Path.Combine(directory.Trim('"'), executable));
        return candidates.FirstOrDefault(File.Exists)
            ?? throw new ResticException($"OpenSSH-Komponente '{executable}' wurde nicht gefunden.");
    }
}
