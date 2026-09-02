using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using ResticBrowser.Remote;
using ResticBrowser.RemoteHost;

if (!OperatingSystem.IsLinux() || RuntimeInformation.ProcessArchitecture != Architecture.X64)
    return await FailAsync("Der Remote-Helfer unterstützt ausschließlich Linux x64.", 2);

await WriteAsync(new RemoteProtocolMessage
{
    MessageType = "hello",
    ProtocolVersion = RemoteProtocol.Version,
    HelperVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.3.4.0"
});

string? line;
try
{
    line = await ReadBoundedLineAsync(Console.In, RemoteProtocol.MaximumFrameLength);
}
catch (Exception ex)
{
    return await FailAsync(ex.Message, 2);
}

RemoteRestoreCommand? request;
try
{
    request = JsonSerializer.Deserialize(line ?? "", RemoteJsonContext.Default.RemoteRestoreCommand);
}
catch (JsonException)
{
    return await FailAsync("Der Remote-Auftrag enthält ungültiges JSON.", 2);
}

if (request is null || request.ProtocolVersion != RemoteProtocol.Version)
    return await FailAsync("Die Protokollversion von App und Remote-Helfer ist nicht kompatibel.", 2);
if (string.IsNullOrWhiteSpace(request.Repository))
    return await FailAsync("Die Repository-Adresse für den VPS fehlt.", 2);
if (string.IsNullOrWhiteSpace(request.ResticExecutable))
    return await FailAsync("Der Restic-Pfad auf dem VPS fehlt.", 2);

using var shutdown = new CancellationTokenSource();
Process? activeProcess = null;
using var sigTerm = PosixSignalRegistration.Create(PosixSignal.SIGTERM, context =>
{
    context.Cancel = true;
    shutdown.Cancel();
    Kill(activeProcess);
});
using var sigInt = PosixSignalRegistration.Create(PosixSignal.SIGINT, context =>
{
    context.Cancel = true;
    shutdown.Cancel();
    Kill(activeProcess);
});
using var sigHup = PosixSignalRegistration.Create(PosixSignal.SIGHUP, context =>
{
    context.Cancel = true;
    shutdown.Cancel();
    Kill(activeProcess);
});
var environment = new Dictionary<string, string>(request.Environment, StringComparer.OrdinalIgnoreCase)
{
    ["RESTIC_PASSWORD"] = request.RepositoryPassword
};

try
{
    var versionResult = await RunAsync(request.ResticExecutable, ["version", "--json"], environment, null, shutdown.Token,
        process => activeProcess = process);
    EnsureSuccess(versionResult, "Restic konnte auf dem VPS nicht geprüft werden.");
    using (var versionDocument = JsonDocument.Parse(versionResult.Output))
    {
        var versionText = versionDocument.RootElement.TryGetProperty("version", out var versionElement) ? versionElement.GetString() : null;
        if (!Version.TryParse(versionText, out var version) || version < new Version(0, 17, 1))
            throw new InvalidOperationException($"Restic {versionText ?? "unbekannt"} ist zu alt. Benötigt wird mindestens 0.17.1.");
    }

    RemotePathValidator.Validate(request.AllowedRoot,
        string.Equals(request.Operation, "restore", StringComparison.OrdinalIgnoreCase) ? request.Target : request.AllowedRoot);

    var repositoryResult = await RunAsync(request.ResticExecutable,
        ["--repo", request.Repository, "snapshots", "--json", "--latest", "1"], environment, null, shutdown.Token,
        process => activeProcess = process);
    EnsureSuccess(repositoryResult, "Das Repository konnte vom VPS nicht geöffnet werden.");

    if (string.Equals(request.Operation, "validate", StringComparison.OrdinalIgnoreCase))
    {
        await WriteAsync(new RemoteProtocolMessage { MessageType = "result", ExitCode = 0, Message = "VPS-Verbindung erfolgreich geprüft." });
        return 0;
    }
    if (!string.Equals(request.Operation, "restore", StringComparison.OrdinalIgnoreCase))
        return await FailAsync("Der angeforderte Remote-Vorgang ist unbekannt.", 2);
    if (string.IsNullOrWhiteSpace(request.SnapshotId) || request.Includes.Count == 0)
        return await FailAsync("Snapshot oder Wiederherstellungsauswahl fehlt.", 2);

    var arguments = new List<string>
    {
        "--repo", request.Repository, "restore", "--json", request.SnapshotId,
        "--target", request.Target, "--overwrite", NormalizeOverwrite(request.Overwrite)
    };
    foreach (var include in request.Includes.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.Ordinal))
    {
        arguments.Add("--include");
        var normalized = include.Replace('\\', '/');
        arguments.Add(normalized.StartsWith('/') ? normalized : "/" + normalized);
    }

    long restored = 0, skipped = 0;
    var restoreResult = await RunAsync(request.ResticExecutable, arguments, environment, async outputLine =>
    {
        try
        {
            using var document = JsonDocument.Parse(outputLine);
            var root = document.RootElement;
            if (!root.TryGetProperty("message_type", out var type) || type.GetString() != "status") return;
            var message = new RemoteProtocolMessage
            {
                MessageType = "progress",
                PercentDone = GetDouble(root, "percent_done"),
                TotalFiles = GetInt64(root, "total_files"),
                FilesRestored = GetInt64(root, "files_restored"),
                FilesSkipped = GetInt64(root, "files_skipped"),
                TotalBytes = GetInt64(root, "total_bytes"),
                BytesRestored = GetInt64(root, "bytes_restored")
            };
            restored = message.FilesRestored;
            skipped = message.FilesSkipped;
            await WriteAsync(message);
        }
        catch (JsonException) { }
    }, shutdown.Token, process => activeProcess = process);
    EnsureSuccess(restoreResult, "Die Wiederherstellung auf dem VPS ist fehlgeschlagen.");
    await WriteAsync(new RemoteProtocolMessage
    {
        MessageType = "result",
        ExitCode = 0,
        FilesRestored = restored,
        FilesSkipped = skipped,
        Message = "Wiederherstellung auf dem VPS erfolgreich abgeschlossen."
    });
    return 0;
}
catch (OperationCanceledException)
{
    return await FailAsync("Die Wiederherstellung auf dem VPS wurde abgebrochen.", 130);
}
catch (Exception ex)
{
    return await FailAsync(ex.Message, 1);
}
finally
{
    Kill(activeProcess);
    request.RepositoryPassword = string.Empty;
    foreach (var key in environment.Keys.ToList()) environment[key] = string.Empty;
    environment.Clear();
    foreach (var key in request.Environment.Keys.ToList()) request.Environment[key] = string.Empty;
    request.Environment.Clear();
    shutdown.Cancel();
}

static async Task<CommandResult> RunAsync(string executable, IReadOnlyList<string> arguments,
    IReadOnlyDictionary<string, string> environment, Func<string, Task>? onLine, CancellationToken token,
    Action<Process?> setProcess)
{
    var startInfo = new ProcessStartInfo
    {
        FileName = executable,
        UseShellExecute = false,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        CreateNoWindow = true,
        StandardOutputEncoding = Encoding.UTF8,
        StandardErrorEncoding = Encoding.UTF8
    };
    foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
    foreach (var pair in environment) startInfo.Environment[pair.Key] = pair.Value;

    using var process = new Process { StartInfo = startInfo };
    try
    {
        if (!process.Start()) throw new InvalidOperationException("Restic konnte auf dem VPS nicht gestartet werden.");
    }
    catch (Exception ex)
    {
        throw new InvalidOperationException($"Restic konnte auf dem VPS nicht gestartet werden: {ex.Message}", ex);
    }
    setProcess(process);
    using var registration = token.Register(() => Kill(process));
    var errorTask = ReadBoundedAsync(process.StandardError, RemoteProtocol.MaximumErrorLength, token);
    var output = new StringBuilder();
    while (await process.StandardOutput.ReadLineAsync(token) is { } outputLine)
    {
        if (output.Length + outputLine.Length + 1 <= RemoteProtocol.MaximumFrameLength) output.AppendLine(outputLine);
        if (onLine is not null) await onLine(outputLine);
    }
    await process.WaitForExitAsync(token);
    setProcess(null);
    return new CommandResult(process.ExitCode, output.ToString(), await errorTask);
}

static void EnsureSuccess(CommandResult result, string fallback)
{
    if (result.ExitCode == 0) return;
    var detail = result.Error.Trim();
    if (detail.Contains("permission denied", StringComparison.OrdinalIgnoreCase)) detail = "Der Zugriff wurde verweigert.";
    throw new InvalidOperationException(string.IsNullOrWhiteSpace(detail) ? fallback : detail);
}

static string NormalizeOverwrite(string value) => value switch
{
    "if-newer" => "if-newer",
    "if-changed" => "if-changed",
    "always" => "always",
    _ => "never"
};

static long GetInt64(JsonElement element, string name) =>
    element.TryGetProperty(name, out var value) && value.TryGetInt64(out var result) ? result : 0;

static double GetDouble(JsonElement element, string name) =>
    element.TryGetProperty(name, out var value) && value.TryGetDouble(out var result) ? result : 0;

static async Task<string?> ReadBoundedLineAsync(TextReader reader, int maximumLength)
{
    var builder = new StringBuilder();
    var buffer = new char[4096];
    while (true)
    {
        var count = await reader.ReadAsync(buffer);
        if (count == 0) return builder.Length == 0 ? null : builder.ToString();
        for (var index = 0; index < count; index++)
        {
            if (buffer[index] == '\n') return builder.ToString().TrimEnd('\r');
            if (builder.Length >= maximumLength) throw new InvalidOperationException("Der Remote-Auftrag überschreitet die erlaubte Größe.");
            builder.Append(buffer[index]);
        }
    }
}

static async Task<string> ReadBoundedAsync(StreamReader reader, int maximumLength, CancellationToken token)
{
    var result = new StringBuilder();
    var buffer = new char[4096];
    while (await reader.ReadAsync(buffer, token) is var count && count > 0)
    {
        var remaining = maximumLength - result.Length;
        if (remaining > 0) result.Append(buffer, 0, Math.Min(remaining, count));
    }
    return result.ToString();
}

static async Task WriteAsync(RemoteProtocolMessage message)
{
    await Console.Out.WriteLineAsync(JsonSerializer.Serialize(message, RemoteJsonContext.Default.RemoteProtocolMessage));
    await Console.Out.FlushAsync();
}

static async Task<int> FailAsync(string message, int exitCode)
{
    await WriteAsync(new RemoteProtocolMessage { MessageType = "error", ExitCode = exitCode, Message = message });
    return exitCode;
}

static void Kill(Process? process)
{
    try { if (process is { HasExited: false }) process.Kill(entireProcessTree: true); } catch { }
}

internal sealed record CommandResult(int ExitCode, string Output, string Error);
