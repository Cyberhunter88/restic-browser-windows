using System.Text.Json;
using ResticBrowser.Models;

namespace ResticBrowser.Services;

public interface IResticRepositoryService
{
    Task<ResticVersion> ValidateAsync(RepositoryProfile profile, CancellationToken token = default);
    Task<IReadOnlyList<SnapshotInfo>> GetSnapshotsAsync(RepositoryProfile profile, SessionCredentials credentials, CancellationToken token = default);
    Task<IReadOnlyList<BackupNode>> GetDirectoryAsync(RepositoryProfile profile, SessionCredentials credentials, string snapshotId, string path, CancellationToken token = default);
    Task<IReadOnlyList<BackupNode>> FindAsync(RepositoryProfile profile, SessionCredentials credentials, string snapshotId, string pattern, CancellationToken token = default);
    Task<RestoreResult> RestoreAsync(RepositoryProfile profile, SessionCredentials credentials, RestoreRequest request, IProgress<RestoreProgress>? progress, CancellationToken token = default);
    Task<RepositoryStats> GetStatsAsync(RepositoryProfile profile, SessionCredentials credentials, CancellationToken token = default);
    Task<IReadOnlyList<DiffEntry>> GetDiffAsync(RepositoryProfile profile, SessionCredentials credentials, string snapshotId1, string snapshotId2, CancellationToken token = default);
    Task<FilePreviewData> GetFilePreviewAsync(RepositoryProfile profile, SessionCredentials credentials, BackupNode node, string snapshotId, CancellationToken token = default);
}

public sealed class ResticRepositoryService(IResticProcessRunner runner) : IResticRepositoryService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<ResticVersion> ValidateAsync(RepositoryProfile profile, CancellationToken token = default)
    {
        var executable = RequireExecutable(profile);
        var result = await runner.RunAsync(new ResticCommand(executable, ["version", "--json"]), cancellationToken: token);
        EnsureSuccess(result);
        var version = JsonSerializer.Deserialize<ResticVersion>(result.StandardOutput, JsonOptions)
                      ?? throw new ResticException("Die Restic-Versionsausgabe ist ungültig.");
        if (!System.Version.TryParse(version.Version.TrimStart('v'), out var parsed) || parsed < new Version(0, 17, 1))
            throw new ResticException($"Restic {version.Version} ist zu alt. Benötigt wird mindestens 0.17.1.");
        return version;
    }

    public async Task<IReadOnlyList<SnapshotInfo>> GetSnapshotsAsync(
        RepositoryProfile profile, SessionCredentials credentials, CancellationToken token = default)
    {
        var repo = profile.BuildRepositoryString();
        var result = await RunRepositoryAsync(profile, credentials,
            ResticCommandBuilder.WithRepository(repo, "snapshots", "--json"), token);
        return JsonSerializer.Deserialize<List<SnapshotInfo>>(result.StandardOutput, JsonOptions) ?? [];
    }

    public async Task<IReadOnlyList<BackupNode>> GetDirectoryAsync(
        RepositoryProfile profile, SessionCredentials credentials, string snapshotId, string path, CancellationToken token = default)
    {
        var repo = profile.BuildRepositoryString();
        var result = await RunRepositoryAsync(profile, credentials,
            ResticCommandBuilder.WithRepository(repo, "ls", "--json", snapshotId, ResticCommandBuilder.NormalizeSnapshotPath(path)), token);
        return ParseJsonLines<BackupNode>(result.StandardOutput)
            .Where(n => (n.MessageType == "node" || n.StructType == "node") &&
                        !PathsEqual(n.Path, path))
            .OrderByDescending(n => n.IsDirectory)
            .ThenBy(n => n.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public async Task<IReadOnlyList<BackupNode>> FindAsync(
        RepositoryProfile profile, SessionCredentials credentials, string snapshotId, string pattern, CancellationToken token = default)
    {
        var repo = profile.BuildRepositoryString();
        var result = await RunRepositoryAsync(profile, credentials,
            ResticCommandBuilder.WithRepository(repo, "find", "--json", "--snapshot", snapshotId, pattern), token);
        using var document = JsonDocument.Parse(result.StandardOutput);
        var nodes = new List<BackupNode>();
        foreach (var group in document.RootElement.EnumerateArray())
        {
            if (!group.TryGetProperty("matches", out var matches)) continue;
            foreach (var match in matches.EnumerateArray())
            {
                var node = match.Deserialize<BackupNode>(JsonOptions);
                if (node is not null) nodes.Add(node);
            }
        }
        return nodes;
    }

    public async Task<RestoreResult> RestoreAsync(
        RepositoryProfile profile, SessionCredentials credentials, RestoreRequest request,
        IProgress<RestoreProgress>? progress, CancellationToken token = default)
    {
        long restored = 0, skipped = 0;
        var repo = profile.BuildRepositoryString();
        var environment = BuildEnvironment(credentials);
        var command = new ResticCommand(RequireExecutable(profile),
            ResticCommandBuilder.Restore(repo, request), environment);
        var result = await runner.RunAsync(command, line =>
        {
            try
            {
                var status = JsonSerializer.Deserialize<RestoreProgress>(line, JsonOptions);
                if (status is not null && (status.MessageType is "status" or "summary"))
                {
                    restored = status.FilesRestored;
                    skipped = status.FilesSkipped;
                    progress?.Report(status);
                }
            }
            catch (JsonException) { /* stderr carries actionable details */ }
            return Task.CompletedTask;
        }, token);

        if (result.ExitCode != 0)
            throw CreateExitException(result);
        return new RestoreResult(true, 0, restored, skipped, "Wiederherstellung erfolgreich abgeschlossen.");
    }

    public async Task<RepositoryStats> GetStatsAsync(
        RepositoryProfile profile, SessionCredentials credentials, CancellationToken token = default)
    {
        var repo = profile.BuildRepositoryString();
        var result = await RunRepositoryAsync(profile, credentials,
            ResticCommandBuilder.Stats(repo), token);
        return JsonSerializer.Deserialize<RepositoryStats>(result.StandardOutput, JsonOptions)
               ?? new RepositoryStats();
    }

    public async Task<IReadOnlyList<DiffEntry>> GetDiffAsync(
        RepositoryProfile profile, SessionCredentials credentials, string snapshotId1, string snapshotId2, CancellationToken token = default)
    {
        var repo = profile.BuildRepositoryString();
        var result = await RunRepositoryAsync(profile, credentials,
            ResticCommandBuilder.Diff(repo, snapshotId1, snapshotId2), token);
        return ParseJsonLines<DiffEntry>(result.StandardOutput)
            .Where(d => d.MessageType == "change" || !string.IsNullOrWhiteSpace(d.Change))
            .ToList();
    }

    public async Task<FilePreviewData> GetFilePreviewAsync(
        RepositoryProfile profile, SessionCredentials credentials, BackupNode node, string snapshotId, CancellationToken token = default)
    {
        var preview = new FilePreviewData { Node = node, Path = node.Path };
        if (node.IsDirectory)
        {
            preview.ErrorMessage = "Ordner können nicht in der Vorschau angezeigt werden.";
            return preview;
        }

        const long maxPreviewSize = 5 * 1024 * 1024; // 5 MB Max
        if (node.Size > maxPreviewSize)
        {
            preview.ErrorMessage = $"Die Datei ist mit {SnapshotInfo.FormatBytes(node.Size)} zu groß für die Direktvorschau (Max 5 MB).";
            return preview;
        }

        try
        {
            var repo = profile.BuildRepositoryString();
            var result = await RunRepositoryAsync(profile, credentials,
                ResticCommandBuilder.Dump(repo, snapshotId, node.Path), token);

            var ext = Path.GetExtension(node.Name).ToLowerInvariant();
            var imageExts = new[] { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".ico", ".webp" };

            if (imageExts.Contains(ext))
            {
                preview.IsImage = true;
                preview.ImageBytes = System.Text.Encoding.Default.GetBytes(result.StandardOutput);
            }
            else
            {
                preview.IsText = true;
                preview.TextContent = result.StandardOutput;
            }
        }
        catch (Exception ex)
        {
            preview.ErrorMessage = $"Vorschau konnte nicht geladen werden: {ex.Message}";
        }

        return preview;
    }

    public static IReadOnlyList<T> ParseJsonLines<T>(string jsonLines)
    {
        var items = new List<T>();
        foreach (var line in jsonLines.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var item = JsonSerializer.Deserialize<T>(line, JsonOptions);
                if (item is not null) items.Add(item);
            }
            catch (JsonException) { /* future message types may be ignored */ }
        }
        return items;
    }

    private async Task<ResticProcessResult> RunRepositoryAsync(
        RepositoryProfile profile, SessionCredentials credentials, IReadOnlyList<string> arguments, CancellationToken token)
    {
        var result = await runner.RunAsync(new ResticCommand(RequireExecutable(profile), arguments, BuildEnvironment(credentials)),
            cancellationToken: token);
        EnsureSuccess(result);
        return result;
    }

    private static Dictionary<string, string> BuildEnvironment(SessionCredentials credentials)
    {
        var environment = new Dictionary<string, string>(credentials.Environment, StringComparer.OrdinalIgnoreCase);
        environment["RESTIC_PASSWORD"] = credentials.Password;
        return environment;
    }

    private static string RequireExecutable(RepositoryProfile profile) =>
        !string.IsNullOrWhiteSpace(profile.ResticExecutable) && File.Exists(profile.ResticExecutable)
            ? profile.ResticExecutable
            : throw new ResticException("Das ausgewählte Restic-Programm wurde nicht gefunden.");

    private static void EnsureSuccess(ResticProcessResult result)
    {
        if (result.ExitCode != 0) throw CreateExitException(result);
    }

    private static ResticException CreateExitException(ResticProcessResult result)
    {
        var detail = result.StandardError.Trim();
        var message = result.ExitCode switch
        {
            10 => "Das Repository wurde nicht gefunden oder ist nicht initialisiert.",
            11 => "Das Repository ist momentan durch einen anderen Vorgang gesperrt.",
            12 => "Das Repository-Passwort ist falsch.",
            130 => "Der Vorgang wurde abgebrochen.",
            _ when detail.Contains("no space", StringComparison.OrdinalIgnoreCase) => "Auf dem Ziellaufwerk ist nicht genügend Speicherplatz.",
            _ when detail.Contains("access", StringComparison.OrdinalIgnoreCase) ||
                   detail.Contains("permission", StringComparison.OrdinalIgnoreCase) => "Der Zugriff wurde verweigert.",
            _ => string.IsNullOrWhiteSpace(detail) ? $"Restic wurde mit Fehlercode {result.ExitCode} beendet." : detail
        };
        return new ResticException(message, exitCode: result.ExitCode);
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(ResticCommandBuilder.NormalizeSnapshotPath(left).TrimEnd('/'),
            ResticCommandBuilder.NormalizeSnapshotPath(right).TrimEnd('/'), StringComparison.Ordinal);
}
