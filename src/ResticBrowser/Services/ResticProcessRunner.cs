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
    private readonly IResticCommandObserver _observer;

    public ResticProcessRunner(IResticCommandObserver? observer = null) =>
        _observer = observer ?? NullResticCommandObserver.Instance;

    public async Task<ResticProcessResult> RunFindMatchesAsync(ResticCommand command, Func<BackupNode, Task> onMatch,
        JsonSerializerOptions? options = null, CancellationToken cancellationToken = default)
        => await RunFindMatchesUntilAsync(command, async match =>
        {
            await onMatch(match);
            return false;
        }, options, cancellationToken);

    public async Task<ResticProcessResult> RunFindMatchesUntilAsync(ResticCommand command, Func<BackupNode, Task<bool>> onMatch,
        JsonSerializerOptions? options = null, CancellationToken cancellationToken = default)
    {
        using var process = Start(command);
        using var registration = RegisterCancellation(process, cancellationToken);
        var stderrTask = ReadStandardErrorAsync(process.StandardError, cancellationToken);
        JsonException? parseError = null;
        var stoppedEarly = false;
        var measurement = new ResticCommandMeasurement();
        var output = new CountingReadStream(process.StandardOutput.BaseStream, measurement.RecordBytes);
        try
        {
            stoppedEarly = await FindMatchReader.ReadAsync(output, onMatch, options, cancellationToken);
        }
        catch (JsonException ex)
        {
            parseError = ex;
        }
        catch
        {
            await StopProcessAsync(process);
            try { await stderrTask; } catch (OperationCanceledException) { }
            throw;
        }
        if (stoppedEarly) await StopProcessAsync(process);
        else await process.WaitForExitAsync(cancellationToken);
        var standardError = await stderrTask;
        if (parseError is not null && process.ExitCode == 0 && !stoppedEarly)
            throw new ResticException("Die Suchausgabe von Restic ist unvollständig oder ungültig.", parseError);
        Report(command, measurement, process.ExitCode, stoppedEarly);
        return new ResticProcessResult(process.ExitCode, "", standardError, stoppedEarly);
    }

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
        var measurement = new ResticCommandMeasurement();
        await using var stdout = new MemoryStream(Math.Min(maximumOutputBytes, 64 * 1024));
        await using var output = new CountingReadStream(process.StandardOutput.BaseStream, measurement.RecordBytes);
        var buffer = new byte[64 * 1024];
        while (true)
        {
            var count = await output.ReadAsync(buffer, cancellationToken);
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
        var standardError = await stderrTask;
        Report(command, measurement, process.ExitCode, false);
        return new ResticBinaryProcessResult(process.ExitCode, stdout.ToArray(), standardError);
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
        var measurement = new ResticCommandMeasurement();
        var countedOutput = new CountingReadStream(process.StandardOutput.BaseStream, measurement.RecordBytes);
        try
        {
            output = await JsonSerializer.DeserializeAsync<T>(
                countedOutput, options, cancellationToken);
        }
        catch (JsonException ex)
        {
            output = default;
            parseError = ex;
        }

        await process.WaitForExitAsync(cancellationToken);
        var standardError = await stderrTask;
        if (parseError is not null && process.ExitCode == 0) throw parseError;
        Report(command, measurement, process.ExitCode, false);
        return new ResticJsonProcessResult<T>(process.ExitCode, output, standardError);
    }

    public async Task<ResticProcessResult> RunJsonArrayAsync<T>(
        ResticCommand command,
        Func<T, Task> onItem,
        JsonSerializerOptions? options = null,
        CancellationToken cancellationToken = default)
        => await RunJsonArrayUntilAsync<T>(command, async item =>
        {
            await onItem(item);
            return false;
        }, options, cancellationToken);

    public async Task<ResticProcessResult> RunJsonArrayUntilAsync<T>(
        ResticCommand command,
        Func<T, Task<bool>> onItem,
        JsonSerializerOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        using var process = Start(command);
        using var registration = RegisterCancellation(process, cancellationToken);
        var stderrTask = ReadStandardErrorAsync(process.StandardError, cancellationToken);
        JsonException? parseError = null;
        var stoppedEarly = false;
        var measurement = new ResticCommandMeasurement();
        var output = new CountingReadStream(process.StandardOutput.BaseStream, measurement.RecordBytes);
        try
        {
            await foreach (var item in JsonSerializer.DeserializeAsyncEnumerable<T>(
                output, options, cancellationToken))
            {
                if (item is not null && await onItem(item))
                {
                    stoppedEarly = true;
                    break;
                }
            }
        }
        catch (JsonException ex)
        {
            parseError = ex;
        }
        catch
        {
            await StopProcessAsync(process);
            try { await stderrTask; } catch (OperationCanceledException) { }
            throw;
        }

        if (stoppedEarly) await StopProcessAsync(process);
        else await process.WaitForExitAsync(cancellationToken);
        var standardError = await stderrTask;
        if (parseError is not null && process.ExitCode == 0 && !stoppedEarly) throw parseError;
        Report(command, measurement, process.ExitCode, stoppedEarly);
        return new ResticProcessResult(process.ExitCode, string.Empty, standardError, stoppedEarly);
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
        var measurement = new ResticCommandMeasurement();
        using var output = new StreamReader(new CountingReadStream(process.StandardOutput.BaseStream, measurement.RecordBytes), Encoding.UTF8);
        while (await output.ReadLineAsync(cancellationToken) is { } line)
        {
            measurement.RecordLine();
            await onOutputLine(line);
        }
        await process.WaitForExitAsync(cancellationToken);
        var standardError = await stderrTask;
        Report(command, measurement, process.ExitCode, false);
        return new ResticProcessResult(process.ExitCode, string.Empty, standardError);
    }

    private static CancellationTokenRegistration RegisterCancellation(Process process, CancellationToken token) =>
        token.Register(() => { try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { } });

    private static async Task StopProcessAsync(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
        try { await process.WaitForExitAsync(CancellationToken.None); } catch { }
    }

    private void Report(ResticCommand command, ResticCommandMeasurement measurement, int exitCode, bool stoppedEarly)
    {
        try { _observer.Completed(measurement.Complete(command, exitCode, stoppedEarly)); } catch { }
    }

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

internal sealed class CountingReadStream(Stream inner, Action<int>? onRead = null) : Stream
{
    public long BytesRead { get; private set; }

    public override bool CanRead => inner.CanRead;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => inner.Length;
    public override long Position { get => inner.Position; set => throw new NotSupportedException(); }
    public override void Flush() => throw new NotSupportedException();
    public override int Read(byte[] buffer, int offset, int count) => Count(inner.Read(buffer, offset, count));
    public override int Read(Span<byte> buffer) => Count(inner.Read(buffer));
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => CountAsync(inner.ReadAsync(buffer, cancellationToken));
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => CountAsync(inner.ReadAsync(buffer, offset, count, cancellationToken));
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    private int Count(int count)
    {
        if (count > 0)
        {
            BytesRead += count;
            onRead?.Invoke(count);
        }
        return count;
    }
    private async ValueTask<int> CountAsync(ValueTask<int> read) => Count(await read);
    private async Task<int> CountAsync(Task<int> read) => Count(await read);
}

public sealed class ResticException : Exception
{
    public int? ExitCode { get; }
    public ResticException(string message, Exception? inner = null, int? exitCode = null)
        : base(message, inner) => ExitCode = exitCode;
}
