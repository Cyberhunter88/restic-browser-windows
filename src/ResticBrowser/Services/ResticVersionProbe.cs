using System.Text.Json;
using ResticBrowser.Models;

namespace ResticBrowser.Services;

internal static class ResticVersionProbe
{
    internal static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    internal static async Task<ResticVersion> ValidateAsync(IResticProcessRunner runner, string executable,
        CancellationToken token = default, TimeSpan? timeout = null)
    {
        token.ThrowIfCancellationRequested();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(timeout ?? Timeout);
        try
        {
            var result = await runner.RunAsync(new ResticCommand(executable, ["version", "--json"]), cancellationToken: deadline.Token);
            deadline.Token.ThrowIfCancellationRequested();
            if (result.ExitCode != 0)
                throw new ResticException("Die Restic-Versionsprüfung ist fehlgeschlagen.");
            var version = JsonSerializer.Deserialize<ResticVersion>(result.StandardOutput)
                ?? throw new ResticException("Die Restic-Versionsausgabe ist ungültig.");
            if (!Version.TryParse(version.Version?.TrimStart('v'), out var parsed))
                throw new ResticException("Die Restic-Versionsausgabe enthält keine gültige Versionsnummer.");
            if (parsed < new Version(0, 17, 1))
                throw new ResticException("Die Restic-Version ist zu alt. Benötigt wird mindestens 0.17.1.");
            return version;
        }
        catch (JsonException ex)
        {
            throw new ResticException("Die Restic-Versionsausgabe ist kein gültiges JSON.", ex);
        }
        catch (OperationCanceledException ex) when (!token.IsCancellationRequested)
        {
            throw new ResticException("Die Restic-Versionsprüfung hat das Zeitlimit überschritten (5 Sekunden).", ex);
        }
    }
}
