using System.Text.Json.Serialization;

namespace ResticBrowser.Models;

public sealed class RepositoryProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Repository { get; set; } = "";
    public string? ResticExecutable { get; set; }
    public override string ToString() => string.IsNullOrWhiteSpace(Name) ? Repository : Name;
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
    [JsonPropertyName("time")] public DateTimeOffset Time { get; set; }
    [JsonPropertyName("hostname")] public string Hostname { get; set; } = "";
    [JsonPropertyName("username")] public string Username { get; set; } = "";
    [JsonPropertyName("paths")] public List<string> Paths { get; set; } = [];
    [JsonPropertyName("tags")] public List<string> Tags { get; set; } = [];
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("short_id")] public string ShortId { get; set; } = "";
    [JsonPropertyName("summary")] public SnapshotSummary? Summary { get; set; }
    public string DisplayId => string.IsNullOrWhiteSpace(ShortId) ? Id[..Math.Min(8, Id.Length)] : ShortId;
    public string PathText => string.Join(", ", Paths);
    public string TagText => Tags.Count == 0 ? "Keine Tags" : string.Join(", ", Tags);
    public string SizeText => FormatBytes(Summary?.TotalBytesProcessed ?? 0);

    public static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        var index = 0;
        while (value >= 1024 && index < units.Length - 1) { value /= 1024; index++; }
        return $"{value:0.#} {units[index]}";
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
    public string Icon => IsDirectory ? "📁" : Type == "symlink" ? "🔗" : "📄";
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
