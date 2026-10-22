using System.Diagnostics;
using System.Text;
using System.Text.Json;
using ResticBrowser.Models;

namespace ResticBrowser.Services;

public sealed record ResticCommand(
    string Executable,
    IReadOnlyList<string> Arguments,
    IReadOnlyDictionary<string, string>? Environment = null,
    string? WorkingDirectory = null);

public sealed record ResticProcessResult(int ExitCode, string StandardOutput, string StandardError, bool StoppedEarly = false);
public sealed record ResticBinaryProcessResult(int ExitCode, byte[] StandardOutput, string StandardError);
public sealed record ResticJsonProcessResult<T>(int ExitCode, T? StandardOutput, string StandardError);

public interface IResticProcessRunner
{
    Task<ResticProcessResult> RunFindMatchesAsync(ResticCommand command, Func<BackupNode, Task> onMatch,
        JsonSerializerOptions? options = null, CancellationToken cancellationToken = default) =>
        RunFindMatchesUntilAsync(command, async match =>
        {
            await onMatch(match);
            return false;
        }, options, cancellationToken);
    Task<ResticProcessResult> RunFindMatchesUntilAsync(ResticCommand command, Func<BackupNode, Task<bool>> onMatch,
        JsonSerializerOptions? options = null, CancellationToken cancellationToken = default) =>
        RunJsonArrayUntilAsync<FindSnapshotGroup>(command, async group =>
        {
            foreach (var match in group.Matches)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (await onMatch(match)) return true;
            }
            return false;
        }, options, cancellationToken);
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
    async Task<ResticProcessResult> RunJsonArrayUntilAsync<T>(
        ResticCommand command,
        Func<T, Task<bool>> onItem,
        JsonSerializerOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var stoppedEarly = false;
        var result = await RunJsonArrayAsync<T>(
            command,
            async item =>
            {
                if (stoppedEarly) return;
                stoppedEarly = await onItem(item);
            }, options, cancellationToken);
        return result with { StoppedEarly = result.StoppedEarly || stoppedEarly };
    }
}

public sealed class ResticProcessRunner : IResticProcessRunner
{
    private const int MaxStandardErrorLength = 64 * 1024;

    public Task<ResticProcessResult> RunFindMatchesAsync(ResticCommand command, Func<BackupNode, Task> onMatch,
        JsonSerializerOptions? options = null, CancellationToken cancellationToken = default) =>
        RunFindMatchesUntilAsync(command, async match =>
        {
            await onMatch(match);
            return false;
        }, options, cancellationToken);

    public async Task<ResticProcessResult> RunFindMatchesUntilAsync(ResticCommand command, Func<BackupNode, Task<bool>> onMatch,
        JsonSerializerOptions? options = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await RunCoreAsync(command, async process =>
            {
                var stoppedEarly = await ReadJsonOutputAsync(process,
                    stream => FindMatchReader.ReadAsync(stream, onMatch, options, cancellationToken), cancellationToken);
                if (stoppedEarly) await StopProcessAsync(process);
                return stoppedEarly;
            }, cancellationToken);
            return new ResticProcessResult(result.ExitCode, "", result.StandardError, result.Output);
        }
        catch (JsonException ex)
        {
            throw new ResticException("Die Suchausgabe von Restic ist unvollständig oder ungültig.", ex);
        }
    }

    public async Task<ResticProcessResult> RunAsync(ResticCommand command, Func<string, Task>? onOutputLine = null,
        CancellationToken cancellationToken = default)
    {
        var stdout = new StringBuilder();
        var result = await RunLinesAsync(command, line =>
        {
            stdout.AppendLine(line);
            return onOutputLine is null ? Task.CompletedTask : onOutputLine(line);
        }, cancellationToken);
        return result with { StandardOutput = stdout.ToString() };
    }

    public async Task<ResticProcessResult> RunLinesAsync(ResticCommand command, Func<string, Task> onOutputLine,
        CancellationToken cancellationToken = default)
    {
        var result = await RunCoreAsync(command, async process =>
        {
            while (await process.StandardOutput.ReadLineAsync(cancellationToken) is { } line)
                await onOutputLine(line);
            return true;
        }, cancellationToken);
        return new ResticProcessResult(result.ExitCode, "", result.StandardError);
    }

    public async Task<ResticBinaryProcessResult> RunBinaryAsync(ResticCommand command, int maximumOutputBytes,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maximumOutputBytes);
        var result = await RunCoreAsync(command, async process =>
        {
            await using var stdout = new MemoryStream(Math.Min(maximumOutputBytes, 64 * 1024));
            var output = process.StandardOutput.BaseStream;
            var buffer = new byte[64 * 1024];
            while (true)
            {
                var count = await output.ReadAsync(buffer, cancellationToken);
                if (count == 0) break;
                if (stdout.Length + count > maximumOutputBytes)
                    throw new ResticException($"Die Vorschau überschreitet die erlaubte Größe von {maximumOutputBytes / (1024 * 1024)} MB.");
                await stdout.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
            }
            return stdout.ToArray();
        }, cancellationToken);
        return new ResticBinaryProcessResult(result.ExitCode, result.Output, result.StandardError);
    }

    public async Task<ResticJsonProcessResult<T>> RunJsonAsync<T>(ResticCommand command,
        JsonSerializerOptions? options = null, CancellationToken cancellationToken = default)
    {
        var result = await RunCoreAsync(command, process => ReadJsonOutputAsync(process,
            async stream => await JsonSerializer.DeserializeAsync<T>(stream, options, cancellationToken), cancellationToken),
            cancellationToken);
        return new ResticJsonProcessResult<T>(result.ExitCode, result.Output, result.StandardError);
    }

    public Task<ResticProcessResult> RunJsonArrayAsync<T>(ResticCommand command, Func<T, Task> onItem,
        JsonSerializerOptions? options = null, CancellationToken cancellationToken = default) =>
        RunJsonArrayUntilAsync<T>(command, async item =>
        {
            await onItem(item);
            return false;
        }, options, cancellationToken);

    public async Task<ResticProcessResult> RunJsonArrayUntilAsync<T>(ResticCommand command, Func<T, Task<bool>> onItem,
        JsonSerializerOptions? options = null, CancellationToken cancellationToken = default)
    {
        var result = await RunCoreAsync(command, process => ReadJsonOutputAsync(process, async stream =>
        {
            await foreach (var item in JsonSerializer.DeserializeAsyncEnumerable<T>(stream, options, cancellationToken))
            {
                if (item is not null && await onItem(item))
                {
                    await StopProcessAsync(process);
                    return true;
                }
            }
            return false;
        }, cancellationToken), cancellationToken);
        return new ResticProcessResult(result.ExitCode, "", result.StandardError, result.Output);
    }

    private static async Task<T> ReadJsonOutputAsync<T>(Process process, Func<Stream, Task<T>> read, CancellationToken token)
    {
        using var stream = new EndTrackingReadStream(process.StandardOutput.BaseStream);
        try { return await read(stream); }
        // stdout kann vor dem Prozessende schließen. Erst nach echtem EOF auf den Exitcode warten;
        // ungültiges JSON in einem noch offenen Stream beendet weiterhin sofort den Prozess.
        catch (JsonException) when (stream.EndReached)
        {
            await process.WaitForExitAsync(token);
            if (process.ExitCode != 0) return default!;
            throw;
        }
    }

    private sealed class EndTrackingReadStream(Stream inner) : Stream
    {
        public bool EndReached { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = inner.Read(buffer, offset, count);
            if (read == 0 && count > 0) EndReached = true;
            return read;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var read = await inner.ReadAsync(buffer, cancellationToken);
            if (read == 0 && buffer.Length > 0) EndReached = true;
            return read;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private static async Task<(int ExitCode, T Output, string StandardError)> RunCoreAsync<T>(
        ResticCommand command, Func<Process, Task<T>> readOutput, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var process = Start(command);
        using var registration = token.Register(() => { try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { } });
        // Bei Abbruch wird der Prozess beendet; stderr wird trotzdem bis zum Ende geleert.
        var stderrTask = ReadStandardErrorAsync(process.StandardError);
        try
        {
            var output = await readOutput(process);
            await process.WaitForExitAsync(token);
            var standardError = await stderrTask;
            token.ThrowIfCancellationRequested();
            return (process.ExitCode, output, standardError);
        }
        catch
        {
            await StopProcessAsync(process);
            try { await stderrTask; } catch { /* Ursprüngliche Ausnahme erhalten. */ }
            token.ThrowIfCancellationRequested();
            throw;
        }
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
        catch (Exception ex)
        {
            process.Dispose();
            if (ex is ResticException) throw;
            throw new ResticException($"Restic konnte nicht gestartet werden: {ex.Message}", ex);
        }
    }

    private static async Task StopProcessAsync(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
        try { await process.WaitForExitAsync(CancellationToken.None); } catch { }
    }

    private static async Task<string> ReadStandardErrorAsync(StreamReader reader)
    {
        var buffer = new char[4096];
        var output = new StringBuilder();
        while (await reader.ReadAsync(buffer) is var count && count > 0)
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
