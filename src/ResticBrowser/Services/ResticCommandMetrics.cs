using System.Diagnostics;

namespace ResticBrowser.Services;

public sealed record ResticCommandMetric(
    string Operation,
    string BackendType,
    TimeSpan? TimeToFirstOutput,
    TimeSpan Duration,
    long OutputBytes,
    long OutputLines,
    int ExitCode,
    bool StoppedEarly);

public interface IResticCommandObserver
{
    void Completed(ResticCommandMetric metric);
}

public sealed class NullResticCommandObserver : IResticCommandObserver
{
    public static NullResticCommandObserver Instance { get; } = new();
    private NullResticCommandObserver() { }
    public void Completed(ResticCommandMetric metric) { }
}

internal sealed class ResticCommandMeasurement
{
    private readonly Stopwatch _watch = Stopwatch.StartNew();
    private TimeSpan? _timeToFirstOutput;

    public long OutputBytes { get; private set; }
    public long OutputLines { get; private set; }

    public void RecordBytes(int count)
    {
        if (count <= 0) return;
        _timeToFirstOutput ??= _watch.Elapsed;
        OutputBytes += count;
    }

    public void RecordLine() => OutputLines++;

    public ResticCommandMetric Complete(ResticCommand command, int exitCode, bool stoppedEarly) =>
        new(
            GetOperation(command.Arguments),
            GetBackendType(command.Arguments),
            _timeToFirstOutput,
            _watch.Elapsed,
            OutputBytes,
            OutputLines,
            exitCode,
            stoppedEarly);

    private static string GetOperation(IReadOnlyList<string> arguments)
    {
        for (var index = 0; index < arguments.Count; index++)
        {
            if (arguments[index] == "--repo") { index++; continue; }
            if (!arguments[index].StartsWith("--", StringComparison.Ordinal)) return arguments[index];
        }
        return "unbekannt";
    }

    private static string GetBackendType(IReadOnlyList<string> arguments)
    {
        var repositoryIndex = -1;
        for (var index = 0; index < arguments.Count; index++)
            if (arguments[index] == "--repo") { repositoryIndex = index; break; }
        if (repositoryIndex < 0 || repositoryIndex + 1 >= arguments.Count) return "unbekannt";
        var repository = arguments[repositoryIndex + 1];
        if (repository.StartsWith("sftp:", StringComparison.OrdinalIgnoreCase)) return "SFTP";
        if (repository.StartsWith("s3:", StringComparison.OrdinalIgnoreCase)) return "S3";
        if (repository.StartsWith("rest:", StringComparison.OrdinalIgnoreCase)) return "REST";
        return "Lokal";
    }
}
