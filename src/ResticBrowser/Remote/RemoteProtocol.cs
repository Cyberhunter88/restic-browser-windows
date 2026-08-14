using System.Text.Json.Serialization;

namespace ResticBrowser.Remote;

public static class RemoteProtocol
{
    public const int Version = 1;
    public const int MaximumFrameLength = 1024 * 1024;
    public const int MaximumErrorLength = 64 * 1024;
}

public sealed class RemoteRestoreCommand
{
    [JsonPropertyName("protocol_version")] public int ProtocolVersion { get; set; } = RemoteProtocol.Version;
    [JsonPropertyName("operation")] public string Operation { get; set; } = "validate";
    [JsonPropertyName("restic_executable")] public string ResticExecutable { get; set; } = "restic";
    [JsonPropertyName("repository")] public string Repository { get; set; } = "";
    [JsonPropertyName("repository_password")] public string RepositoryPassword { get; set; } = "";
    [JsonPropertyName("environment")] public Dictionary<string, string> Environment { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    [JsonPropertyName("allowed_root")] public string AllowedRoot { get; set; } = "";
    [JsonPropertyName("snapshot_id")] public string SnapshotId { get; set; } = "";
    [JsonPropertyName("target")] public string Target { get; set; } = "";
    [JsonPropertyName("includes")] public List<string> Includes { get; set; } = [];
    [JsonPropertyName("overwrite")] public string Overwrite { get; set; } = "never";
}

public sealed class RemoteProtocolMessage
{
    [JsonPropertyName("message_type")] public string MessageType { get; set; } = "";
    [JsonPropertyName("protocol_version")] public int ProtocolVersion { get; set; }
    [JsonPropertyName("helper_version")] public string HelperVersion { get; set; } = "";
    [JsonPropertyName("percent_done")] public double PercentDone { get; set; }
    [JsonPropertyName("total_files")] public long TotalFiles { get; set; }
    [JsonPropertyName("files_restored")] public long FilesRestored { get; set; }
    [JsonPropertyName("files_skipped")] public long FilesSkipped { get; set; }
    [JsonPropertyName("total_bytes")] public long TotalBytes { get; set; }
    [JsonPropertyName("bytes_restored")] public long BytesRestored { get; set; }
    [JsonPropertyName("exit_code")] public int ExitCode { get; set; }
    [JsonPropertyName("message")] public string Message { get; set; } = "";
}

public static class RemotePathValidator
{
    public static string Validate(string allowedRoot, string target, bool requireWritableRoot = true)
    {
        if (!OperatingSystem.IsLinux())
            throw new InvalidOperationException("Der Remote-Helfer unterstützt ausschließlich Linux.");
        if (string.IsNullOrWhiteSpace(allowedRoot) || !Path.IsPathFullyQualified(allowedRoot))
            throw new InvalidOperationException("Der erlaubte Basisordner muss ein absoluter Linux-Pfad sein.");
        if (string.IsNullOrWhiteSpace(target) || !Path.IsPathFullyQualified(target))
            throw new InvalidOperationException("Der Zielordner muss ein absoluter Linux-Pfad sein.");
        if (HasParentTraversal(allowedRoot) || HasParentTraversal(target))
            throw new InvalidOperationException("Basis- und Zielordner dürfen keine '..'-Segmente enthalten.");

        var root = Path.GetFullPath(allowedRoot).TrimEnd('/');
        var destination = Path.GetFullPath(target).TrimEnd('/');
        if (root.Length == 0 || root == "/" || destination.Length == 0 || destination == "/")
            throw new InvalidOperationException("Das Linux-Wurzelverzeichnis darf nicht als Basis oder Ziel verwendet werden.");
        if (!Directory.Exists(root))
            throw new InvalidOperationException("Der erlaubte Basisordner existiert auf dem Server nicht.");

        var resolvedRoot = ResolveExistingPath(root);
        var existingAncestor = destination;
        while (!Directory.Exists(existingAncestor) && !File.Exists(existingAncestor))
        {
            var parent = Path.GetDirectoryName(existingAncestor);
            if (string.IsNullOrEmpty(parent) || parent == existingAncestor)
                throw new InvalidOperationException("Der Zielordner konnte nicht sicher aufgelöst werden.");
            existingAncestor = parent;
        }
        var resolvedAncestor = ResolveExistingPath(existingAncestor);
        if (!IsWithin(resolvedRoot, resolvedAncestor))
            throw new InvalidOperationException("Der Zielordner liegt außerhalb des erlaubten Basisordners oder führt über eine symbolische Verknüpfung hinaus.");
        if (!IsWithin(root, destination))
            throw new InvalidOperationException("Der Zielordner liegt außerhalb des erlaubten Basisordners.");

        if (requireWritableRoot)
        {
            var probe = Path.Combine(root, $".restic-browser-write-test-{Guid.NewGuid():N}");
            try
            {
                using var stream = new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Der erlaubte Basisordner ist nicht beschreibbar: {ex.Message}");
            }
            finally
            {
                try { if (File.Exists(probe)) File.Delete(probe); } catch { }
            }
        }

        return destination;
    }

    private static string ResolveExistingPath(string path)
    {
        var info = Directory.Exists(path) ? new DirectoryInfo(path) as FileSystemInfo : new FileInfo(path);
        return Path.GetFullPath(info.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? info.FullName).TrimEnd('/');
    }

    private static bool IsWithin(string root, string path) =>
        string.Equals(root, path, StringComparison.Ordinal) || path.StartsWith(root + "/", StringComparison.Ordinal);

    private static bool HasParentTraversal(string path) =>
        path.Split('/', StringSplitOptions.RemoveEmptyEntries).Any(segment => segment == "..");
}
