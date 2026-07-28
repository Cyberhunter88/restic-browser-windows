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
    Task<ResticMountHandle> StartMountAsync(RepositoryProfile profile, SessionCredentials credentials, MountRequest request, CancellationToken token = default);
    Task<StorageAnalysisResult> AnalyzeSnapshotStorageAsync(RepositoryProfile profile, SessionCredentials credentials, string snapshotId, CancellationToken token = default);
}

public sealed class ResticRepositoryService(IResticProcessRunner runner) : IResticRepositoryService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public async Task<ResticVersion> ValidateAsync(RepositoryProfile profile, CancellationToken token = default)
    {
        var result = await runner.RunAsync(new ResticCommand(RequireExecutable(profile), ["version", "--json"]), cancellationToken: token);
        EnsureSuccess(result);
        var version = JsonSerializer.Deserialize<ResticVersion>(result.StandardOutput, JsonOptions)
            ?? throw new ResticException("Die Restic-Versionsausgabe ist ungültig.");
        if (!Version.TryParse(version.Version.TrimStart('v'), out var parsed) || parsed < new Version(0, 17, 1))
            throw new ResticException($"Restic {version.Version} ist zu alt. Benötigt wird mindestens 0.17.1.");
        return version;
    }

    public async Task<IReadOnlyList<SnapshotInfo>> GetSnapshotsAsync(RepositoryProfile profile, SessionCredentials credentials, CancellationToken token = default)
    {
        var result = await RunRepositoryAsync(profile, credentials,
            ResticCommandBuilder.WithRepository(profile.BuildRepositoryString(), "snapshots", "--json"), token);
        return JsonSerializer.Deserialize<List<SnapshotInfo>>(result.StandardOutput, JsonOptions) ?? [];
    }

    public async Task<IReadOnlyList<BackupNode>> GetDirectoryAsync(RepositoryProfile profile, SessionCredentials credentials, string snapshotId, string path, CancellationToken token = default)
    {
        var nodes = new List<BackupNode>();
        await RunRepositoryLinesAsync(profile, credentials,
            ResticCommandBuilder.WithRepository(profile.BuildRepositoryString(), "ls", "--json", snapshotId, ResticCommandBuilder.NormalizeSnapshotPath(path)),
            line => AddNodeIfMatchingAsync(nodes, line, path), token);
        return nodes.OrderByDescending(n => n.IsDirectory).ThenBy(n => n.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public async Task<IReadOnlyList<BackupNode>> FindAsync(RepositoryProfile profile, SessionCredentials credentials, string snapshotId, string pattern, CancellationToken token = default)
    {
        var result = await RunRepositoryAsync(profile, credentials,
            ResticCommandBuilder.WithRepository(profile.BuildRepositoryString(), "find", "--json", "--snapshot", snapshotId, pattern), token);
        using var document = JsonDocument.Parse(result.StandardOutput);
        var nodes = new List<BackupNode>();
        foreach (var group in document.RootElement.EnumerateArray())
            if (group.TryGetProperty("matches", out var matches))
                foreach (var match in matches.EnumerateArray())
                    if (match.Deserialize<BackupNode>(JsonOptions) is { } node) nodes.Add(node);
        return nodes;
    }

    public async Task<RestoreResult> RestoreAsync(RepositoryProfile profile, SessionCredentials credentials, RestoreRequest request, IProgress<RestoreProgress>? progress, CancellationToken token = default)
    {
        long restored = 0, skipped = 0;
        var result = await runner.RunLinesAsync(new ResticCommand(RequireExecutable(profile),
            ResticCommandBuilder.Restore(profile.BuildRepositoryString(), request), BuildEnvironment(credentials)), line =>
        {
            if (TryDeserializeJsonLine(line, out RestoreProgress? status) &&
                status is not null && (status.MessageType is "status" or "summary"))
            {
                restored = status.FilesRestored;
                skipped = status.FilesSkipped;
                progress?.Report(status);
            }
            return Task.CompletedTask;
        }, token);
        EnsureSuccess(result);
        return new RestoreResult(true, 0, restored, skipped, "Wiederherstellung erfolgreich abgeschlossen.");
    }

    public async Task<RepositoryStats> GetStatsAsync(RepositoryProfile profile, SessionCredentials credentials, CancellationToken token = default)
    {
        var result = await RunRepositoryAsync(profile, credentials, ResticCommandBuilder.Stats(profile.BuildRepositoryString()), token);
        return JsonSerializer.Deserialize<RepositoryStats>(result.StandardOutput, JsonOptions) ?? new RepositoryStats();
    }

    public async Task<IReadOnlyList<DiffEntry>> GetDiffAsync(RepositoryProfile profile, SessionCredentials credentials, string snapshotId1, string snapshotId2, CancellationToken token = default)
    {
        var entries = new List<DiffEntry>();
        await RunRepositoryLinesAsync(profile, credentials, ResticCommandBuilder.Diff(profile.BuildRepositoryString(), snapshotId1, snapshotId2), line =>
        {
            if (TryDeserializeJsonLine(line, out DiffEntry? entry) && entry is not null &&
                (entry.MessageType == "change" || !string.IsNullOrWhiteSpace(entry.Change))) entries.Add(entry);
            return Task.CompletedTask;
        }, token);
        return entries;
    }

    public async Task<FilePreviewData> GetFilePreviewAsync(RepositoryProfile profile, SessionCredentials credentials, BackupNode node, string snapshotId, CancellationToken token = default)
    {
        var preview = new FilePreviewData { Node = node, Path = node.Path };
        if (node.IsDirectory) { preview.ErrorMessage = "Ordner können nicht in der Vorschau angezeigt werden."; return preview; }
        const long maxPreviewSize = 5 * 1024 * 1024;
        if (node.Size > maxPreviewSize) { preview.ErrorMessage = $"Die Datei ist mit {SnapshotInfo.FormatBytes(node.Size)} zu groß für die Direktvorschau (Max 5 MB)."; return preview; }
        try
        {
            var command = ResticCommandBuilder.Dump(profile.BuildRepositoryString(), snapshotId, node.Path);
            if (IsImageExtension(Path.GetExtension(node.Name).ToLowerInvariant()))
            {
                var result = await RunRepositoryBinaryAsync(profile, credentials, command, token);
                preview.IsImage = true;
                preview.ImageBytes = result.StandardOutput;
            }
            else
            {
                var result = await RunRepositoryAsync(profile, credentials, command, token);
                preview.IsText = true;
                preview.TextContent = result.StandardOutput;
            }
        }
        catch (Exception ex) { preview.ErrorMessage = $"Vorschau konnte nicht geladen werden: {ex.Message}"; }
        return preview;
    }

    public async Task<ResticMountHandle> StartMountAsync(RepositoryProfile profile, SessionCredentials credentials, MountRequest request, CancellationToken token = default)
    {
        var startInfo = new System.Diagnostics.ProcessStartInfo { FileName = RequireExecutable(profile), UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true, StandardOutputEncoding = System.Text.Encoding.UTF8, StandardErrorEncoding = System.Text.Encoding.UTF8 };
        foreach (var arg in ResticCommandBuilder.Mount(profile.BuildRepositoryString(), request)) startInfo.ArgumentList.Add(arg);
        foreach (var pair in BuildEnvironment(credentials)) startInfo.Environment[pair.Key] = pair.Value;
        var process = new System.Diagnostics.Process { StartInfo = startInfo };
        var stderrBuilder = new System.Text.StringBuilder();
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null && stderrBuilder.Length < 64 * 1024) stderrBuilder.AppendLine(e.Data); };
        if (!process.Start()) throw new ResticException("Der Restic-Mount-Prozess konnte nicht gestartet werden.");
        process.BeginErrorReadLine();
        var completed = await Task.WhenAny(Task.Delay(2500, token), process.WaitForExitAsync(token));
        token.ThrowIfCancellationRequested();
        if (completed.IsCompletedSuccessfully && process.HasExited)
        {
            var error = stderrBuilder.ToString().Trim();
            if (error.Contains("winfsp", StringComparison.OrdinalIgnoreCase) || error.Contains("fuse", StringComparison.OrdinalIgnoreCase))
                throw new ResticException("Für das Einbinden als virtuelles Laufwerk wird WinFsp unter Windows beziehungsweise FUSE unter Linux benötigt.", exitCode: process.ExitCode);
            throw new ResticException(string.IsNullOrWhiteSpace(error) ? $"Mount fehlgeschlagen mit Beendigungscode {process.ExitCode}." : error, exitCode: process.ExitCode);
        }
        return new ResticMountHandle(request.MountPoint, request.SnapshotId, process);
    }

    public async Task<StorageAnalysisResult> AnalyzeSnapshotStorageAsync(RepositoryProfile profile, SessionCredentials credentials, string snapshotId, CancellationToken token = default)
    {
        long totalSize = 0, totalFiles = 0, totalDirs = 0;
        var categories = CreateCategories();
        var dirSizes = new Dictionary<string, (long totalSize, long fileCount)>(StringComparer.OrdinalIgnoreCase);
        var topFiles = new List<BackupNode>(15);
        await RunRepositoryLinesAsync(profile, credentials, ResticCommandBuilder.LsJson(profile.BuildRepositoryString(), snapshotId), line =>
        {
            if (!TryDeserializeJsonLine(line, out BackupNode? node) || node is null || (node.MessageType != "node" && node.StructType != "node")) return Task.CompletedTask;
            if (node.IsDirectory) { totalDirs++; return Task.CompletedTask; }
            totalFiles++; totalSize += node.Size;
            var category = GetStorageCategory(Path.GetExtension(node.Name).ToLowerInvariant());
            var currentCategory = categories[category]; categories[category] = (currentCategory.icon, currentCategory.size + node.Size, currentCategory.count + 1);
            AddTopFile(topFiles, node);
            for (var parent = ResticCommandBuilder.ParentPath(node.Path); parent != "/"; parent = ResticCommandBuilder.ParentPath(parent))
            {
                var current = dirSizes.GetValueOrDefault(parent);
                dirSizes[parent] = (current.totalSize + node.Size, current.fileCount + 1);
            }
            return Task.CompletedTask;
        }, token);

        return new StorageAnalysisResult
        {
            SnapshotId = snapshotId,
            TotalSize = totalSize,
            TotalFileCount = totalFiles,
            TotalDirectoryCount = totalDirs,
            Categories = categories.Where(kv => kv.Value.count > 0).Select(kv => new StorageCategory { Name = kv.Key, Icon = kv.Value.icon, TotalSize = kv.Value.size, FileCount = kv.Value.count, Percentage = totalSize > 0 ? kv.Value.size * 100.0 / totalSize : 0 }).OrderByDescending(c => c.TotalSize).ToList(),
            TopFolders = dirSizes.Select(kv => new FolderSizeNode { Path = kv.Key, Name = Path.GetFileName(kv.Key.TrimEnd('/')), TotalSize = kv.Value.totalSize, FileCount = kv.Value.fileCount, IsDirectory = true, Percentage = totalSize > 0 ? kv.Value.totalSize * 100.0 / totalSize : 0 }).Where(f => !string.IsNullOrEmpty(f.Name)).OrderByDescending(f => f.TotalSize).Take(15).ToList(),
            TopFiles = topFiles.Select(f => new FolderSizeNode { Path = f.Path, Name = f.Name, TotalSize = f.Size, FileCount = 1, IsDirectory = false, Percentage = totalSize > 0 ? f.Size * 100.0 / totalSize : 0 }).ToList()
        };
    }

    public static IReadOnlyList<T> ParseJsonLines<T>(string jsonLines)
    {
        var items = new List<T>();
        foreach (var line in jsonLines.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)) if (TryDeserializeJsonLine(line, out T? item) && item is not null) items.Add(item);
        return items;
    }

    private async Task<ResticProcessResult> RunRepositoryAsync(RepositoryProfile profile, SessionCredentials credentials, IReadOnlyList<string> arguments, CancellationToken token)
    {
        var result = await runner.RunAsync(new ResticCommand(RequireExecutable(profile), arguments, BuildEnvironment(credentials)), cancellationToken: token);
        EnsureSuccess(result); return result;
    }
    private async Task RunRepositoryLinesAsync(RepositoryProfile profile, SessionCredentials credentials, IReadOnlyList<string> arguments, Func<string, Task> onOutputLine, CancellationToken token)
    {
        var result = await runner.RunLinesAsync(new ResticCommand(RequireExecutable(profile), arguments, BuildEnvironment(credentials)), onOutputLine, token);
        EnsureSuccess(result);
    }
    private async Task<ResticBinaryProcessResult> RunRepositoryBinaryAsync(RepositoryProfile profile, SessionCredentials credentials, IReadOnlyList<string> arguments, CancellationToken token)
    {
        var result = await runner.RunBinaryAsync(new ResticCommand(RequireExecutable(profile), arguments, BuildEnvironment(credentials)), token);
        if (result.ExitCode != 0) throw CreateExitException(new ResticProcessResult(result.ExitCode, string.Empty, result.StandardError));
        return result;
    }
    private static Task AddNodeIfMatchingAsync(List<BackupNode> nodes, string line, string path)
    {
        if (TryDeserializeJsonLine(line, out BackupNode? node) && node is not null && (node.MessageType == "node" || node.StructType == "node") && !PathsEqual(node.Path, path)) nodes.Add(node);
        return Task.CompletedTask;
    }
    private static bool TryDeserializeJsonLine<T>(string line, out T? item) { try { item = JsonSerializer.Deserialize<T>(line, JsonOptions); return item is not null; } catch (JsonException) { item = default; return false; } }
    private static Dictionary<string, (string icon, long size, long count)> CreateCategories() => new()
    {
        ["Dokumente"] = ("📕", 0, 0),
        ["Bilder"] = ("🖼️", 0, 0),
        ["Medien (Audio/Video)"] = ("🎬", 0, 0),
        ["Archive & ISOs"] = ("📦", 0, 0),
        ["Code & Skripte"] = ("📝", 0, 0),
        ["Ausführbar & System"] = ("⚙️", 0, 0),
        ["Sonstiges"] = ("📄", 0, 0)
    };
    private static string GetStorageCategory(string ext) => ext switch
    {
        ".pdf" or ".doc" or ".docx" or ".xls" or ".xlsx" or ".ppt" or ".pptx" or ".txt" or ".md" or ".odt" or ".ods" or ".rtf" or ".csv" or ".json" or ".xml" => "Dokumente",
        ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif" or ".ico" or ".webp" or ".svg" or ".tif" or ".tiff" or ".heic" => "Bilder",
        ".mp3" or ".wav" or ".flac" or ".ogg" or ".aac" or ".mp4" or ".mkv" or ".avi" or ".mov" or ".wmv" => "Medien (Audio/Video)",
        ".zip" or ".tar" or ".gz" or ".7z" or ".rar" or ".bz2" or ".xz" or ".iso" => "Archive & ISOs",
        ".cs" or ".py" or ".js" or ".ts" or ".cpp" or ".c" or ".h" or ".java" or ".html" or ".css" or ".sh" or ".ps1" or ".yaml" or ".yml" => "Code & Skripte",
        ".exe" or ".msi" or ".dll" or ".so" or ".dylib" or ".sys" => "Ausführbar & System",
        _ => "Sonstiges"
    };
    private static bool IsImageExtension(string extension) => extension is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif" or ".ico" or ".webp";
    private static void AddTopFile(List<BackupNode> files, BackupNode node) { var index = files.FindIndex(existing => existing.Size < node.Size); if (index >= 0) files.Insert(index, node); else if (files.Count < 15) files.Add(node); if (files.Count > 15) files.RemoveAt(15); }
    private static Dictionary<string, string> BuildEnvironment(SessionCredentials credentials) { var environment = new Dictionary<string, string>(credentials.Environment, StringComparer.OrdinalIgnoreCase) { ["RESTIC_PASSWORD"] = credentials.Password }; return environment; }
    private static string RequireExecutable(RepositoryProfile profile) => !string.IsNullOrWhiteSpace(profile.ResticExecutable) && File.Exists(profile.ResticExecutable) ? profile.ResticExecutable : throw new ResticException("Das ausgewählte Restic-Programm wurde nicht gefunden.");
    private static void EnsureSuccess(ResticProcessResult result) { if (result.ExitCode != 0) throw CreateExitException(result); }
    private static ResticException CreateExitException(ResticProcessResult result)
    {
        var detail = result.StandardError.Trim(); var message = result.ExitCode switch
        {
            10 => "Das Repository wurde nicht gefunden oder ist nicht initialisiert.",
            11 => "Das Repository ist momentan durch einen anderen Vorgang gesperrt.",
            12 => "Das Repository-Passwort ist falsch.",
            130 => "Der Vorgang wurde abgebrochen.",
            _ when detail.Contains("no space", StringComparison.OrdinalIgnoreCase) => "Auf dem Ziellaufwerk ist nicht genügend Speicherplatz.",
            _ when detail.Contains("access", StringComparison.OrdinalIgnoreCase) || detail.Contains("permission", StringComparison.OrdinalIgnoreCase) => "Der Zugriff wurde verweigert.",
            _ => string.IsNullOrWhiteSpace(detail) ? $"Restic wurde mit Fehlercode {result.ExitCode} beendet." : detail
        };
        return new ResticException(message, exitCode: result.ExitCode);
    }
    private static bool PathsEqual(string left, string right) => string.Equals(ResticCommandBuilder.NormalizeSnapshotPath(left).TrimEnd('/'), ResticCommandBuilder.NormalizeSnapshotPath(right).TrimEnd('/'), StringComparison.Ordinal);
}
