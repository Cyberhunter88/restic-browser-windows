namespace ResticBrowser.Services;

internal interface IRemoteProcessTransport
{
    Task<RemoteRestoreService.ProcessResult> RunAsync(
        string executable, IReadOnlyList<string> arguments, string? input,
        Func<string, Task>? onLine, string? askPassSecret, CancellationToken token);
}

internal sealed class OpenSshProcessTransport : IRemoteProcessTransport
{
    public Task<RemoteRestoreService.ProcessResult> RunAsync(
        string executable, IReadOnlyList<string> arguments, string? input,
        Func<string, Task>? onLine, string? askPassSecret, CancellationToken token) =>
        RemoteRestoreService.RunProcessAsync(executable, arguments, input, onLine, askPassSecret, token);
}
