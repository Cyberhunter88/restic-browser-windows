using System.Text.Json.Serialization;

namespace ResticBrowser.Models;

public enum RepositoryType { Local, SFTP, S3, REST, Other }

public sealed class RepositoryProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Repository { get; set; } = "";
    public string? ResticExecutable { get; set; }
    public RepositoryType Type { get; set; } = RepositoryType.Local;
    public string SftpHost { get; set; } = "";
    public int SftpPort { get; set; } = 22;
    public string SftpUser { get; set; } = "";
    public string SftpPath { get; set; } = "";
    public string SftpKeyFile { get; set; } = "";

    public override string ToString() => string.IsNullOrWhiteSpace(Name) ? Repository : Name;

    public string BuildRepositoryString()
    {
        if (Type == RepositoryType.SFTP && !string.IsNullOrWhiteSpace(SftpHost))
        {
            var userHost = string.IsNullOrWhiteSpace(SftpUser) ? SftpHost : $"{SftpUser}@{SftpHost}";
            var portPart = SftpPort > 0 && SftpPort != 22 ? $":{SftpPort}" : "";
            var pathPart = SftpPath.StartsWith('/') ? SftpPath : "/" + SftpPath;
            return $"sftp:{userHost}{portPart}:{pathPart}";
        }
        return Repository;
    }
}

public sealed class SessionCredentials : IDisposable
{
    public string Password { get; private set; }
    public Dictionary<string, string> Environment { get; } = new(StringComparer.OrdinalIgnoreCase);

    public SessionCredentials(string password, IDictionary<string, string>? environment = null)
    {
        Password = password;
        if (environment is not null)
            foreach (var pair in environment.Where(p => !string.IsNullOrWhiteSpace(p.Key)))
                Environment[pair.Key.Trim()] = pair.Value;
    }

    public void Dispose()
    {
        Password = string.Empty;
        foreach (var key in Environment.Keys.ToList())
            Environment[key] = string.Empty;
        Environment.Clear();
    }
}

public sealed class SnapshotInfo
{
    private static readonly string[] ByteUnits = ["B", "KB", "MB", "GB", "TB"];
    private List<string> _paths = [];
    private List<string> _tags = [];
    private string? _pathText;
    private string? _tagText;

    [JsonPropertyName("time")] public DateTimeOffset Time { get; set; }
    [JsonPropertyName("hostname")] public string Hostname { get; set; } = "";
    [JsonPropertyName("username")] public string Username { get; set; } = "";
    [JsonPropertyName("paths")]
    public List<string> Paths
    {
        get => _paths;
        set { _paths = value ?? []; _pathText = null; }
    }

    [JsonPropertyName("tags")]
    public List<string> Tags
    {
        get => _tags;
        set { _tags = value ?? []; _tagText = null; }
    }
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("short_id")] public string ShortId { get; set; } = "";
    [JsonPropertyName("summary")] public SnapshotSummary? Summary { get; set; }
    public string DisplayId => string.IsNullOrWhiteSpace(ShortId) ? Id[..Math.Min(8, Id.Length)] : ShortId;
    public string PathText => _pathText ??= string.Join(", ", Paths);
    public string TagText => _tagText ??= Tags.Count == 0 ? "Keine Tags" : string.Join(", ", Tags);
    public string SizeText => FormatBytes(Summary?.TotalBytesProcessed ?? 0);

    public static string FormatBytes(long bytes)
    {
        double value = bytes;
        var index = 0;
        while (value >= 1024 && index < ByteUnits.Length - 1) { value /= 1024; index++; }
        return $"{value:0.#} {ByteUnits[index]}";
    }
}

public sealed class SnapshotSummary
{
    [JsonPropertyName("total_bytes_processed")] public long TotalBytesProcessed { get; set; }
    [JsonPropertyName("total_files_processed")] public long TotalFilesProcessed { get; set; }
}

public sealed class BackupNode
{
    [JsonPropertyName("message_type")] public string MessageType { get; set; } = "";
    [JsonPropertyName("struct_type")] public string StructType { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("type")] public string Type { get; set; } = "";
    [JsonPropertyName("path")] public string Path { get; set; } = "";
    [JsonPropertyName("size")] public long Size { get; set; }
    [JsonPropertyName("permissions")] public string Permissions { get; set; } = "";
    [JsonPropertyName("mtime")] public DateTimeOffset? Modified { get; set; }
    public bool IsDirectory => Type.Equals("dir", StringComparison.OrdinalIgnoreCase);
    public string TypeText => Type switch { "dir" => "Ordner", "file" => "Datei", "symlink" => "Verknüpfung", _ => Type };
    public string SizeText => IsDirectory ? "—" : SnapshotInfo.FormatBytes(Size);
    public string Icon => IsDirectory ? "📁" : Type == "symlink" ? "🔗" : GetFileIcon(Name);

    private static string GetFileIcon(string filename)
    {
        var ext = System.IO.Path.GetExtension(filename).ToLowerInvariant();
        return ext switch
        {
            ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif" or ".ico" or ".webp" => "🖼️",
            ".txt" or ".md" or ".json" or ".xml" or ".log" or ".ini" or ".yaml" or ".yml" => "📝",
            ".pdf" => "📕",
            ".zip" or ".tar" or ".gz" or ".7z" or ".rar" => "📦",
            ".exe" or ".msi" or ".bat" or ".cmd" or ".sh" or ".ps1" => "⚙️",
            ".mp3" or ".wav" or ".flac" or ".ogg" => "🎵",
            ".mp4" or ".mkv" or ".avi" or ".mov" => "🎬",
            _ => "📄"
        };
    }
}

public enum OverwritePolicy { Never, IfNewer, IfChanged, Always }

public sealed record RestoreRequest(
    string SnapshotId,
    string Target,
    IReadOnlyList<string> Includes,
    OverwritePolicy Overwrite);

public sealed class RestoreProgress
{
    [JsonPropertyName("message_type")] public string MessageType { get; set; } = "";
    [JsonPropertyName("percent_done")] public double PercentDone { get; set; }
    [JsonPropertyName("total_files")] public long TotalFiles { get; set; }
    [JsonPropertyName("files_restored")] public long FilesRestored { get; set; }
    [JsonPropertyName("files_skipped")] public long FilesSkipped { get; set; }
    [JsonPropertyName("total_bytes")] public long TotalBytes { get; set; }
    [JsonPropertyName("bytes_restored")] public long BytesRestored { get; set; }
}

public sealed record RestoreResult(bool Success, int ExitCode, long FilesRestored, long FilesSkipped, string Message);

public sealed record LatestFileMatch(string SnapshotId, BackupNode Node);

public enum RemoteAuthenticationType { Agent, PrivateKey, Password }

public sealed class RemoteRestoreTarget
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Host { get; set; } = "";
    public int Port { get; set; } = 22;
    public string User { get; set; } = "";
    public RemoteAuthenticationType AuthenticationType { get; set; }
    public string PrivateKeyFile { get; set; } = "";
    public string ResticExecutable { get; set; } = "restic";
    public string Repository { get; set; } = "";
    public string AllowedRoot { get; set; } = "";
    public override string ToString() => string.IsNullOrWhiteSpace(Name) ? $"{User}@{Host}" : Name;
}

public sealed class RemoteSshCredentials : IDisposable
{
    public string Password { get; private set; }
    public string PrivateKeyPassphrase { get; private set; }

    public RemoteSshCredentials(string password = "", string privateKeyPassphrase = "")
    {
        Password = password;
        PrivateKeyPassphrase = privateKeyPassphrase;
    }

    public void Dispose()
    {
        Password = string.Empty;
        PrivateKeyPassphrase = string.Empty;
    }
}

public sealed class TrustedSshHost
{
    public string Host { get; set; } = "";
    public int Port { get; set; } = 22;
    public string Algorithm { get; set; } = "";
    public string PublicKey { get; set; } = "";
    public string Fingerprint { get; set; } = "";
}

public sealed record RemoteHostKeyInfo(string Host, int Port, string Algorithm, string PublicKey, string Fingerprint, bool Changed);

public sealed record TarExportRequest(string SnapshotId, string SnapshotPath, string TargetFile);

public sealed record TarExportResult(string SnapshotPath, string TargetFile, bool Success, string? ErrorMessage = null);

public enum CheckMode { Quick, Full }

public sealed class RepositoryCheckResult
{
    public CheckMode Mode { get; set; }
    public int ErrorCount { get; set; }
    public bool SuggestRepairIndex { get; set; }
    public bool SuggestPrune { get; set; }
    public string Details { get; set; } = "";
    public bool IsHealthy => ErrorCount == 0 && !SuggestRepairIndex;
}

public sealed class ResticVersion
{
    [JsonPropertyName("version")] public string Version { get; set; } = "";
    [JsonPropertyName("go_arch")] public string Architecture { get; set; } = "";
}

public sealed class EnvironmentEntry
{
    public string Name { get; set; } = "";
    public string Value { get; set; } = "";
}

public sealed class RepositoryStats
{
    [JsonPropertyName("total_size")] public long TotalSize { get; set; }
    [JsonPropertyName("total_file_count")] public long TotalFileCount { get; set; }
    [JsonPropertyName("total_blob_count")] public long TotalBlobCount { get; set; }
    [JsonPropertyName("snapshots_count")] public int SnapshotsCount { get; set; }
}

public enum DiffChangeType { Added, Modified, Removed }

public sealed class DiffEntry
{
    [JsonPropertyName("message_type")] public string MessageType { get; set; } = "";
    [JsonPropertyName("change")] public string Change { get; set; } = "";
    [JsonPropertyName("path")] public string Path { get; set; } = "";
    [JsonPropertyName("old_size")] public long OldSize { get; set; }
    [JsonPropertyName("new_size")] public long NewSize { get; set; }

    public DiffChangeType ChangeType => Change switch
    {
        "added" => DiffChangeType.Added,
        "removed" => DiffChangeType.Removed,
        _ => DiffChangeType.Modified
    };

    public string Icon => ChangeType switch
    {
        DiffChangeType.Added => "➕",
        DiffChangeType.Removed => "❌",
        _ => "✏️"
    };

    public string ChangeText => ChangeType switch
    {
        DiffChangeType.Added => "Hinzugefügt",
        DiffChangeType.Removed => "Entfernt",
        _ => "Geändert"
    };
}

public sealed class FilePreviewData
{
    public BackupNode Node { get; set; } = new();
    public string Path { get; set; } = "";
    public bool IsText { get; set; }
    public bool IsImage { get; set; }
    public string? TextContent { get; set; }
    public byte[]? ImageBytes { get; set; }
    public string ErrorMessage { get; set; } = "";
}

public sealed record MountRequest(
    string? SnapshotId,
    string MountPoint);

public sealed class ResticMountHandle : IAsyncDisposable, IDisposable
{
    public string MountPoint { get; }
    public string? SnapshotId { get; }
    public System.Diagnostics.Process Process { get; }

    public ResticMountHandle(string mountPoint, string? snapshotId, System.Diagnostics.Process process)
    {
        MountPoint = mountPoint;
        SnapshotId = snapshotId;
        Process = process;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        Process.Dispose();
    }

    public void Dispose()
    {
        StopAsync().GetAwaiter().GetResult();
        Process.Dispose();
    }

    public async Task StopAsync()
    {
        try
        {
            if (!Process.HasExited)
            {
                Process.Kill(entireProcessTree: true);
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                await Process.WaitForExitAsync(timeout.Token);
            }
        }
        catch (OperationCanceledException) { /* Prozess wird beim App-Ende nicht blockierend abgewartet. */ }
        catch (InvalidOperationException) { /* Process has already exited */ }
    }
}

public sealed class StorageCategory
{
    public string Name { get; set; } = "";
    public string Icon { get; set; } = "📁";
    public long TotalSize { get; set; }
    public long FileCount { get; set; }
    public double Percentage { get; set; }
    public string SizeText => SnapshotInfo.FormatBytes(TotalSize);
}

public sealed class FolderSizeNode
{
    public string Path { get; set; } = "";
    public string Name { get; set; } = "";
    public long TotalSize { get; set; }
    public long FileCount { get; set; }
    public bool IsDirectory { get; set; }
    public double Percentage { get; set; }
    public string SizeText => SnapshotInfo.FormatBytes(TotalSize);
    public string Icon => IsDirectory ? "📁" : "📄";
}

public sealed class StorageAnalysisResult
{
    public string SnapshotId { get; set; } = "";
    public long TotalSize { get; set; }
    public long TotalFileCount { get; set; }
    public long TotalDirectoryCount { get; set; }
    public List<StorageCategory> Categories { get; set; } = [];
    public List<FolderSizeNode> TopFolders { get; set; } = [];
    public List<FolderSizeNode> TopFiles { get; set; } = [];
    public string TotalSizeText => SnapshotInfo.FormatBytes(TotalSize);
}

public sealed record StorageAnalysisProgress(long FilesProcessed, long DirectoriesProcessed, long BytesProcessed);

