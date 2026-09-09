using System.Text.Json;
using System.Reflection;
using System.IO.Pipes;
using System.Security.Cryptography;
using ResticBrowser.Models;
using ResticBrowser.Remote;
using ResticBrowser.Services;
using ResticBrowser.ViewModels;


sealed class FailingRunner : IResticProcessRunner
{
    public Task<ResticProcessResult> RunJsonArrayAsync<T>(ResticCommand command, Func<T, Task> onItem,
        JsonSerializerOptions? options = null, CancellationToken cancellationToken = default) =>
        Task.FromResult(new ResticProcessResult(1, "", "permission denied"));
    public Task<ResticProcessResult> RunAsync(ResticCommand command, Func<string, Task>? onOutputLine = null, CancellationToken cancellationToken = default) =>
        Task.FromResult(new ResticProcessResult(1, "", "permission denied"));
    public Task<ResticProcessResult> RunLinesAsync(ResticCommand command, Func<string, Task> onOutputLine, CancellationToken cancellationToken = default) =>
        Task.FromResult(new ResticProcessResult(1, "", "permission denied"));
    public Task<ResticBinaryProcessResult> RunBinaryAsync(ResticCommand command, int maximumOutputBytes, CancellationToken cancellationToken = default) =>
        Task.FromResult(new ResticBinaryProcessResult(1, [], "permission denied"));
    public Task<ResticJsonProcessResult<T>> RunJsonAsync<T>(ResticCommand command, JsonSerializerOptions? options = null, CancellationToken cancellationToken = default) =>
        Task.FromResult(new ResticJsonProcessResult<T>(1, default, "permission denied"));
}

sealed class RestoreFailingRunner(string error) : IResticProcessRunner
{
    public Task<ResticProcessResult> RunAsync(ResticCommand command, Func<string, Task>? onOutputLine = null, CancellationToken cancellationToken = default) =>
        Task.FromResult(new ResticProcessResult(1, "", error));
    public async Task<ResticProcessResult> RunLinesAsync(ResticCommand command, Func<string, Task> onOutputLine, CancellationToken cancellationToken = default)
    {
        await onOutputLine(error);
        return new ResticProcessResult(1, "", "");
    }
    public Task<ResticBinaryProcessResult> RunBinaryAsync(ResticCommand command, int maximumOutputBytes, CancellationToken cancellationToken = default) =>
        Task.FromResult(new ResticBinaryProcessResult(1, [], error));
}

sealed class BinaryRunner(byte[] bytes) : IResticProcessRunner
{
    public int MaximumOutputBytes { get; private set; }
    public Task<ResticProcessResult> RunAsync(ResticCommand command, Func<string, Task>? onOutputLine = null, CancellationToken cancellationToken = default) =>
        Task.FromResult(new ResticProcessResult(0, "", ""));
    public Task<ResticProcessResult> RunLinesAsync(ResticCommand command, Func<string, Task> onOutputLine, CancellationToken cancellationToken = default) =>
        Task.FromResult(new ResticProcessResult(0, "", ""));
    public Task<ResticBinaryProcessResult> RunBinaryAsync(ResticCommand command, int maximumOutputBytes, CancellationToken cancellationToken = default)
    {
        MaximumOutputBytes = maximumOutputBytes;
        return Task.FromResult(new ResticBinaryProcessResult(0, bytes, ""));
    }
}

sealed class LineRunner(IEnumerable<string> lines) : IResticProcessRunner
{
    public int LinesDelivered { get; private set; }
    public Task<ResticProcessResult> RunAsync(ResticCommand command, Func<string, Task>? onOutputLine = null, CancellationToken cancellationToken = default) =>
        Task.FromResult(new ResticProcessResult(0, "", ""));
    public async Task<ResticProcessResult> RunLinesAsync(ResticCommand command, Func<string, Task> onOutputLine, CancellationToken cancellationToken = default)
    {
        foreach (var line in lines) { await onOutputLine(line); LinesDelivered++; }
        return new ResticProcessResult(0, "", "");
    }
    public Task<ResticBinaryProcessResult> RunBinaryAsync(ResticCommand command, int maximumOutputBytes, CancellationToken cancellationToken = default) =>
        Task.FromResult(new ResticBinaryProcessResult(0, [], ""));
}

sealed class TarTargetRunner(int exitCode) : IResticProcessRunner
{
    public async Task<ResticProcessResult> RunAsync(ResticCommand command, Func<string, Task>? onOutputLine = null, CancellationToken cancellationToken = default)
    {
        var targetIndex = command.Arguments.ToList().IndexOf("--target");
        if (targetIndex >= 0) await File.WriteAllBytesAsync(command.Arguments[targetIndex + 1], [1, 2, 3], cancellationToken);
        return new ResticProcessResult(exitCode, "", exitCode == 0 ? "" : "TAR-Export fehlgeschlagen");
    }

    public Task<ResticProcessResult> RunLinesAsync(ResticCommand command, Func<string, Task> onOutputLine, CancellationToken cancellationToken = default) =>
        Task.FromResult(new ResticProcessResult(exitCode, "", ""));

    public Task<ResticBinaryProcessResult> RunBinaryAsync(ResticCommand command, int maximumOutputBytes, CancellationToken cancellationToken = default) =>
        Task.FromResult(new ResticBinaryProcessResult(exitCode, [], ""));
}

sealed class JsonRunner(string json) : IResticProcessRunner
{
    public int JsonCalls { get; private set; }
    public IReadOnlyList<string> LastArguments { get; private set; } = [];

    public Task<ResticJsonProcessResult<T>> RunJsonAsync<T>(ResticCommand command, JsonSerializerOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        JsonCalls++;
        LastArguments = command.Arguments;
        var value = JsonSerializer.Deserialize<T>(json, options);
        return Task.FromResult(new ResticJsonProcessResult<T>(0, value, ""));
    }

    public async Task<ResticProcessResult> RunJsonArrayAsync<T>(ResticCommand command, Func<T, Task> onItem,
        JsonSerializerOptions? options = null, CancellationToken cancellationToken = default)
    {
        JsonCalls++;
        LastArguments = command.Arguments;
        var values = JsonSerializer.Deserialize<List<T>>(json, options) ?? [];
        foreach (var value in values) await onItem(value);
        return new ResticProcessResult(0, "", "");
    }

    public Task<ResticProcessResult> RunAsync(ResticCommand command, Func<string, Task>? onOutputLine = null,
        CancellationToken cancellationToken = default) => Task.FromResult(new ResticProcessResult(0, "", ""));
    public Task<ResticProcessResult> RunLinesAsync(ResticCommand command, Func<string, Task> onOutputLine,
        CancellationToken cancellationToken = default) => Task.FromResult(new ResticProcessResult(0, "", ""));
    public Task<ResticBinaryProcessResult> RunBinaryAsync(ResticCommand command, int maximumOutputBytes,
        CancellationToken cancellationToken = default) => Task.FromResult(new ResticBinaryProcessResult(0, [], ""));
}

sealed class ManyRestoreErrorsRunner(int count) : IResticProcessRunner
{
    public Task<ResticProcessResult> RunAsync(ResticCommand command, Func<string, Task>? onOutputLine = null,
        CancellationToken cancellationToken = default) => Task.FromResult(new ResticProcessResult(1, "", ""));

    public async Task<ResticProcessResult> RunLinesAsync(ResticCommand command, Func<string, Task> onOutputLine,
        CancellationToken cancellationToken = default)
    {
        for (var index = 0; index < count; index++)
            await onOutputLine($"{{\"message_type\":\"error\",\"error\":{{\"message\":\"Fehler {index:D3}\"}}}}");
        return new ResticProcessResult(1, "", "");
    }

    public Task<ResticBinaryProcessResult> RunBinaryAsync(ResticCommand command, int maximumOutputBytes,
        CancellationToken cancellationToken = default) => Task.FromResult(new ResticBinaryProcessResult(1, [], ""));
}

sealed class ControlledRepositoryService : IResticRepositoryService
{
    public Func<Func<IReadOnlyList<SnapshotInfo>, Task>, CancellationToken, Task>? SnapshotBatches { get; set; }
    public Func<string, Func<IReadOnlyList<BackupNode>, Task>, CancellationToken, Task<bool>>? SearchBatches { get; set; }
    public async Task GetSnapshotsBatchedAsync(RepositoryProfile profile, SessionCredentials credentials,
        Func<IReadOnlyList<SnapshotInfo>, Task> onBatch, CancellationToken token = default)
    {
        if (SnapshotBatches is not null) await SnapshotBatches(onBatch, token);
        else await onBatch(Snapshots);
    }
    public async Task<bool> FindBatchedAsync(RepositoryProfile profile, SessionCredentials credentials,
        string snapshotId, string pattern, Func<IReadOnlyList<BackupNode>, Task> onBatch, CancellationToken token = default)
    {
        if (SearchBatches is not null) return await SearchBatches(pattern, onBatch, token);
        await onBatch([]);
        return false;
    }
    private readonly Dictionary<string, TaskCompletionSource<IReadOnlyList<BackupNode>>> _directories = [];
    private readonly TaskCompletionSource<RepositoryStats> _stats = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public IReadOnlyList<SnapshotInfo> Snapshots { get; init; } = [];

    public void CompleteDirectory(string path, IReadOnlyList<BackupNode> nodes) =>
        GetDirectorySource(path).TrySetResult(nodes);
    public void CompleteStats(RepositoryStats stats) => _stats.TrySetResult(stats);

    public Task<ResticVersion> ValidateAsync(RepositoryProfile profile, CancellationToken token = default) =>
        Task.FromResult(new ResticVersion { Version = "0.19.1" });
    public Task<IReadOnlyList<SnapshotInfo>> GetSnapshotsAsync(RepositoryProfile profile, SessionCredentials credentials,
        CancellationToken token = default) => Task.FromResult(Snapshots);
    public Task<IReadOnlyList<BackupNode>> GetDirectoryAsync(RepositoryProfile profile, SessionCredentials credentials,
        string snapshotId, string path, CancellationToken token = default) => GetDirectorySource(path).Task;
    public Task<FileSearchResult> FindAsync(RepositoryProfile profile, SessionCredentials credentials,
        string snapshotId, string pattern, CancellationToken token = default) => Task.FromResult(new FileSearchResult([], false));
    public Task<LatestFileMatch?> FindNewestAsync(RepositoryProfile profile, SessionCredentials credentials,
        string pattern, CancellationToken token = default) => Task.FromResult<LatestFileMatch?>(null);
    public Task<IReadOnlyList<FileVersion>> GetFileVersionsAsync(RepositoryProfile profile, SessionCredentials credentials,
        string exactPath, string? hostname, CancellationToken token = default) => Task.FromResult<IReadOnlyList<FileVersion>>([]);
    public Task<RestoreResult> RestoreAsync(RepositoryProfile profile, SessionCredentials credentials, RestoreRequest request,
        IProgress<RestoreProgress>? progress, CancellationToken token = default) => throw new NotSupportedException();
    public Task<RestorePreviewResult> PreviewRestoreAsync(RepositoryProfile profile, SessionCredentials credentials,
        RestoreRequest request, CancellationToken token = default) => Task.FromResult(new RestorePreviewResult());
    public Task<TarExportResult> ExportTarAsync(RepositoryProfile profile, SessionCredentials credentials, TarExportRequest request,
        CancellationToken token = default) => throw new NotSupportedException();
    public Task<RepositoryCheckResult> CheckAsync(RepositoryProfile profile, SessionCredentials credentials, CheckMode mode,
        CancellationToken token = default) => throw new NotSupportedException();
    public Task<RepositoryStats> GetStatsAsync(RepositoryProfile profile, SessionCredentials credentials,
        CancellationToken token = default) => _stats.Task;
    public Task<IReadOnlyList<DiffEntry>> GetDiffAsync(RepositoryProfile profile, SessionCredentials credentials,
        string snapshotId1, string snapshotId2, CancellationToken token = default) => Task.FromResult<IReadOnlyList<DiffEntry>>([]);
    public Task<FilePreviewData> GetFilePreviewAsync(RepositoryProfile profile, SessionCredentials credentials, BackupNode node,
        string snapshotId, CancellationToken token = default) => Task.FromResult(new FilePreviewData());
    public Task<ResticMountHandle> StartMountAsync(RepositoryProfile profile, SessionCredentials credentials, MountRequest request,
        CancellationToken token = default) => throw new NotSupportedException();
    public Task<StorageAnalysisResult> AnalyzeSnapshotStorageAsync(RepositoryProfile profile, SessionCredentials credentials,
        string snapshotId, IProgress<StorageAnalysisProgress>? progress = null, CancellationToken token = default) =>
        Task.FromResult(new StorageAnalysisResult());

    private TaskCompletionSource<IReadOnlyList<BackupNode>> GetDirectorySource(string path)
    {
        if (!_directories.TryGetValue(path, out var source))
        {
            source = new TaskCompletionSource<IReadOnlyList<BackupNode>>(TaskCreationOptions.RunContinuationsAsynchronously);
            _directories[path] = source;
        }
        return source;
    }
}

sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
{
    public void Report(T value) => report(value);
}

sealed class RecordingRemoteTransport : IRemoteProcessTransport
{
    private readonly string _host;
    private readonly string _publicKey;
    private readonly string _helperHash;

    public RecordingRemoteTransport(string host, string publicKey)
    {
        _host = host;
        _publicKey = publicKey;
        using var helper = typeof(RemoteRestoreService).Assembly
            .GetManifestResourceStream("ResticBrowser.Remote.linux-x64")!;
        _helperHash = Convert.ToHexString(SHA256.HashData(helper)).ToLowerInvariant();
    }

    public int SshCalls { get; private set; }
    public int SftpCalls { get; private set; }
    public int KeyScanCalls { get; private set; }

    public async Task<RemoteRestoreService.ProcessResult> RunAsync(
        string executable, IReadOnlyList<string> arguments, string? input,
        Func<string, Task>? onLine, string? askPassSecret, CancellationToken token)
    {
        var executableName = Path.GetFileNameWithoutExtension(executable);
        if (executableName.Equals("ssh-keyscan", StringComparison.OrdinalIgnoreCase))
        {
            KeyScanCalls++;
            return new RemoteRestoreService.ProcessResult(0, $"{_host} ssh-ed25519 {_publicKey}\n", "");
        }
        if (executableName.Equals("sftp", StringComparison.OrdinalIgnoreCase))
        {
            SftpCalls++;
            return new RemoteRestoreService.ProcessResult(0, "", "");
        }

        SshCalls++;
        var command = arguments[^1];
        if (command.Contains("uname -s", StringComparison.Ordinal))
            return new RemoteRestoreService.ProcessResult(0, $"Linux\nx86_64\n{_helperHash}  helper\n", "");

        var messages = new[]
        {
            new RemoteProtocolMessage { MessageType = "hello", ProtocolVersion = RemoteProtocol.Version },
            new RemoteProtocolMessage { MessageType = "result", ExitCode = 0, Message = "VPS-Verbindung erfolgreich geprüft." }
        };
        foreach (var message in messages)
            if (onLine is not null) await onLine(JsonSerializer.Serialize(message));
        return new RemoteRestoreService.ProcessResult(0,
            string.Join(Environment.NewLine, messages.Select(message => JsonSerializer.Serialize(message))), "");
    }
}

sealed class SkippedTestException(string reason) : Exception(reason);
