using System.Diagnostics;
using System.Text;

namespace ResticBrowser.Services;

public sealed record ResticCommand(
    string Executable,
    IReadOnlyList<string> Arguments,
    IReadOnlyDictionary<string, string>? Environment = null,
    string? WorkingDirectory = null);

public sealed record ResticProcessResult(int ExitCode, string StandardOutput, string StandardError);

public interface IResticProcessRunner
{
    Task<ResticProcessResult> RunAsync(
        ResticCommand command,
        Func<string, Task>? onOutputLine = null,
        CancellationToken cancellationToken = default);
}

public sealed class ResticProcessRunner : IResticProcessRunner
{
    public async Task<ResticProcessResult> RunAsync(
        ResticCommand command,
        Func<string, Task>? onOutputLine = null,
        CancellationToken cancellationToken = default)
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
        if (!string.IsNullOrWhiteSpace(command.WorkingDirectory))
            startInfo.WorkingDirectory = command.WorkingDirectory;
        foreach (var argument in command.Arguments)
            startInfo.ArgumentList.Add(argument);
        if (command.Environment is not null)
            foreach (var pair in command.Environment)
                startInfo.Environment[pair.Key] = pair.Value;

        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start())
                throw new ResticException("Restic konnte nicht gestartet werden.");
        }
        catch (Exception ex) when (ex is not ResticException)
        {
            throw new ResticException($"Restic konnte nicht gestartet werden: {ex.Message}", ex);
        }

        using var registration = cancellationToken.Register(() =>
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch { /* process has already ended */ }
        });

        var stdout = new StringBuilder();
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        while (await process.StandardOutput.ReadLineAsync(cancellationToken) is { } line)
        {
            stdout.AppendLine(line);
            if (onOutputLine is not null)
                await onOutputLine(line);
        }

        await process.WaitForExitAsync(cancellationToken);
        var stderr = await stderrTask;
        return new ResticProcessResult(process.ExitCode, stdout.ToString(), stderr);
    }
}

public sealed class ResticException : Exception
{
    public int? ExitCode { get; }
    public ResticException(string message, Exception? inner = null, int? exitCode = null)
        : base(message, inner) => ExitCode = exitCode;
}
