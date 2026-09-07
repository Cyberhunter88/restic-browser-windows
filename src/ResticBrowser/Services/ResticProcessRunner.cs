using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace ResticBrowser.Services;

public sealed record ResticCommand(
    string Executable,
    IReadOnlyList<string> Arguments,
    IReadOnlyDictionary<string, string>? Environment = null,
    string? WorkingDirectory = null);

public sealed record ResticProcessResult(int ExitCode, string StandardOutput, string StandardError);
public sealed record ResticBinaryProcessResult(int ExitCode, byte[] StandardOutput, string StandardError);
public sealed record ResticJsonProcessResult<T>(int ExitCode, T? StandardOutput, string StandardError);

public interface IResticProcessRunner
{
    Task<ResticProcessResult> RunAsync(
        ResticCommand command,
        Func<string, Task>? onOutputLine = null,
        CancellationToken cancellationToken = default);
    Task<ResticProcessResult> RunLinesAsync(
        ResticCommand command,
        Func<string, Task> onOutputLine,
        CancellationToken cancellationToken = default);
    Task<ResticBinaryProcessResult> RunBinaryAsync(
        ResticCommand command,
        int maximumOutputBytes,
        CancellationToken cancellationToken = default);
    Task<ResticJsonProcessResult<T>> RunJsonAsync<T>(
        ResticCommand command,
        JsonSerializerOptions? options = null,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Dieser Prozess-Runner unterstützt kein JSON-Streaming.");
    Task<ResticProcessResult> RunJsonArrayAsync<T>(
        ResticCommand command,
        Func<T, Task> onItem,
        JsonSerializerOptions? options = null,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Dieser Prozess-Runner unterstützt kein JSON-Array-Streaming.");
}

public sealed class ResticProcessRunner : IResticProcessRunner
{
    private const int MaxStandardErrorLength = 64 * 1024;

    public async Task<ResticProcessResult> RunAsync(
        ResticCommand command,
        Func<string, Task>? onOutputLine = null,
        CancellationToken cancellationToken = default)
    {
        var stdout = new StringBuilder();
        var result = await RunLinesCoreAsync(command, line =>
        {
            stdout.AppendLine(line);
            return onOutputLine is null ? Task.CompletedTask : onOutputLine(line);
        }, cancellationToken);
        return new ResticProcessResult(result.ExitCode, stdout.ToString(), result.StandardError);
    }

    public Task<ResticProcessResult> RunLinesAsync(
        ResticCommand command, Func<string, Task> onOutputLine, CancellationToken cancellationToken = default) =>
        RunLinesCoreAsync(command, onOutputLine, cancellationToken);

    public async Task<ResticBinaryProcessResult> RunBinaryAsync(
        ResticCommand command, int maximumOutputBytes, CancellationToken cancellationToken = default)
    {
        using var process = Start(command);
        using var registration = RegisterCancellation(process, cancellationToken);
        var stderrTask = ReadStandardErrorAsync(process.StandardError, cancellationToken);
        await using var stdout = new MemoryStream(Math.Min(maximumOutputBytes, 64 * 1024));
        var buffer = new byte[64 * 1024];
        while (true)
        {
            var count = await process.StandardOutput.BaseStream.ReadAsync(buffer, cancellationToken);
            if (count == 0) break;
            if (stdout.Length + count > maximumOutputBytes)
            {
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
                await process.WaitForExitAsync(CancellationToken.None);
                await stderrTask;
                throw new ResticException($"Die Vorschau überschreitet die erlaubte Größe von {maximumOutputBytes / (1024 * 1024)} MB.");
            }
            await stdout.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
        }
        await process.WaitForExitAsync(cancellationToken);
        return new ResticBinaryProcessResult(process.ExitCode, stdout.ToArray(), await stderrTask);
    }

    public async Task<ResticJsonProcessResult<T>> RunJsonAsync<T>(
        ResticCommand command, JsonSerializerOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        using var process = Start(command);
        using var registration = RegisterCancellation(process, cancellationToken);
        var stderrTask = ReadStandardErrorAsync(process.StandardError, cancellationToken);
        T? output;
        JsonException? parseError = null;
        try
        {
            output = await JsonSerializer.DeserializeAsync<T>(
                process.StandardOutput.BaseStream, options, cancellationToken);
        }
        catch (JsonException ex)
        {
            output = default;
            parseError = ex;
        }

        await process.WaitForExitAsync(cancellationToken);
        var standardError = await stderrTask;
        if (parseError is not null && process.ExitCode == 0) throw parseError;
        return new ResticJsonProcessResult<T>(process.ExitCode, output, standardError);
    }

    public async Task<ResticProcessResult> RunJsonArrayAsync<T>(
        ResticCommand command,
        Func<T, Task> onItem,
        JsonSerializerOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        using var process = Start(command);
        using var registration = RegisterCancellation(process, cancellationToken);
        var stderrTask = ReadStandardErrorAsync(process.StandardError, cancellationToken);
        JsonException? parseError = null;
        try
        {
            await foreach (var item in JsonSerializer.DeserializeAsyncEnumerable<T>(
                process.StandardOutput.BaseStream, options, cancellationToken))
            {
                if (item is not null) await onItem(item);
            }
        }
        catch (JsonException ex)
        {
            parseError = ex;
        }

        await process.WaitForExitAsync(cancellationToken);
        var standardError = await stderrTask;
        if (parseError is not null && process.ExitCode == 0) throw parseError;
        return new ResticProcessResult(process.ExitCode, string.Empty, standardError);
    }

    private static Process Start(ResticCommand command)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = command.Executable,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        if (!string.IsNullOrWhiteSpace(command.WorkingDirectory)) startInfo.WorkingDirectory = command.WorkingDirectory;
        foreach (var argument in command.Arguments) startInfo.ArgumentList.Add(argument);
        if (command.Environment is not null)
            foreach (var pair in command.Environment) startInfo.Environment[pair.Key] = pair.Value;
        var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start()) throw new ResticException("Restic konnte nicht gestartet werden.");
            return process;
        }
        catch (Exception ex) when (ex is not ResticException)
        {
            process.Dispose();
            throw new ResticException($"Restic konnte nicht gestartet werden: {ex.Message}", ex);
        }
    }

    private async Task<ResticProcessResult> RunLinesCoreAsync(
        ResticCommand command, Func<string, Task> onOutputLine, CancellationToken cancellationToken)
    {
        using var process = Start(command);
        using var registration = RegisterCancellation(process, cancellationToken);
        var stderrTask = ReadStandardErrorAsync(process.StandardError, cancellationToken);
        while (await process.StandardOutput.ReadLineAsync(cancellationToken) is { } line)
            await onOutputLine(line);
        await process.WaitForExitAsync(cancellationToken);
        return new ResticProcessResult(process.ExitCode, string.Empty, await stderrTask);
    }

    private static CancellationTokenRegistration RegisterCancellation(Process process, CancellationToken token) =>
        token.Register(() => { try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { } });

    private static async Task<string> ReadStandardErrorAsync(StreamReader reader, CancellationToken token)
    {
        var buffer = new char[4096];
        var output = new StringBuilder();
        while (await reader.ReadAsync(buffer, token) is var count && count > 0)
        {
            var remaining = MaxStandardErrorLength - output.Length;
            if (remaining > 0) output.Append(buffer, 0, Math.Min(remaining, count));
        }
        return output.ToString();
    }
}

public sealed class ResticException : Exception
{
    public int? ExitCode { get; }
    public ResticException(string message, Exception? inner = null, int? exitCode = null)
        : base(message, inner) => ExitCode = exitCode;
}
