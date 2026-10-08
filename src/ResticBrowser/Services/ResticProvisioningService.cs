using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace ResticBrowser.Services;

public sealed record ResticExecutableInfo(string Path, string Version, string Source);

public sealed class ResticProvisioningService
{
    public const string Version = "0.19.1";
    public const string WindowsBinarySha256 = "B0DD1FD21EEA5D8FE1325F55F7118213C21F36DE8A261E04C0624A5AB9FD7830";

    private readonly string _dataDirectory;
    private readonly IResticProcessRunner _runner;
    private readonly string _baseDirectory;
    private readonly string _searchPath;
    private readonly TimeSpan _probeTimeout;
    private readonly string _linuxSha256;

    public ResticProvisioningService(string? dataDirectory = null) : this(dataDirectory,
        new ResticProcessRunner(), AppContext.BaseDirectory, Environment.GetEnvironmentVariable("PATH") ?? "")
    { }

    internal ResticProvisioningService(string? dataDirectory, IResticProcessRunner runner, string baseDirectory,
        string searchPath, TimeSpan? probeTimeout = null, string? linuxSha256 = null)
    {
        _dataDirectory = dataDirectory ?? SettingsService.GetDataDirectory();
        _runner = runner;
        _baseDirectory = baseDirectory;
        _searchPath = searchPath;
        _probeTimeout = probeTimeout ?? ResticVersionProbe.Timeout;
        _linuxSha256 = linuxSha256 ?? ReadLinuxSha256();
    }

    private static string ReadLinuxSha256()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("ResticBrowser.ResticManifest")
            ?? throw new ResticException("Das Prüfmanifest für die mitgelieferte Restic-Version fehlt.");
        using var manifest = JsonDocument.Parse(stream);
        return manifest.RootElement.GetProperty("linux").GetProperty("binarySha256").GetString()
            ?? throw new ResticException("Die Linux-Restic-Prüfsumme fehlt im Prüfmanifest.");
    }

    public async Task<ResticExecutableInfo> ResolveAsync(string? configuredPath, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            try { return await ValidateCandidateAsync(configuredPath, "Ausgewähltes Programm", null, token); }
            catch (ResticException ex)
            {
                throw new ResticException($"Das ausdrücklich ausgewählte Restic-Programm ist nicht verwendbar: {ex.Message}", ex);
            }
        }

        var candidates = new List<(string Path, string Source, string? Hash)>();
        if (OperatingSystem.IsWindows())
        {
            var embedded = await ProvisionWindowsAsync(token);
            if (embedded is not null) candidates.Add((embedded, "In Restic Browser enthalten", WindowsBinarySha256));
        }
        else
            candidates.Add((Path.Combine(_baseDirectory, "tools", "restic"), "Mit der Anwendung ausgeliefert", _linuxSha256));

        foreach (var path in ResticLocator.Candidates(OperatingSystem.IsWindows(), _baseDirectory, _searchPath,
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)))
        {
            var source = ResticLocator.Describe(path, _baseDirectory);
            if (!OperatingSystem.IsWindows() && source == "PATH" &&
                (path == "/usr/local/bin/restic" || path == "/usr/bin/restic")) source = "Systempfad";
            candidates.Add((path, source, null));
        }

        var seen = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        var failures = new List<string>();
        foreach (var candidate in candidates)
        {
            token.ThrowIfCancellationRequested();
            var path = Path.GetFullPath(candidate.Path);
            // A rejected bundle must never be retried as an unchecked portable file.
            if (!seen.Add(ResticLocator.GetIdentity(path)) || !File.Exists(path)) continue;
            try { return await ValidateCandidateAsync(path, candidate.Source, candidate.Hash, token); }
            catch (ResticException ex) { failures.Add($"{candidate.Source}: {ex.Message}"); }
        }
        var detail = failures.Count == 0 ? "Keine Restic-Datei gefunden." : string.Join("\n", failures.Distinct());
        throw new ResticException($"Restic konnte nicht automatisch aufgelöst werden. {detail}\nVerwende die vollständige portable Ausgabe oder wähle eine ausführbare Restic-Datei aus.");
    }

    private async Task<ResticExecutableInfo> ValidateCandidateAsync(string path, string source, string? hash, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var error = ResticLocator.GetFileError(path);
        if (error is not null) throw new ResticException(error);
        if (hash is not null && !IsVerified(path, hash))
            throw new ResticException("Die mitgelieferte Restic-Datei hat nicht die erwartete SHA-256-Prüfsumme.");
        var version = await ResticVersionProbe.ValidateAsync(_runner, Path.GetFullPath(path), token, _probeTimeout);
        return new ResticExecutableInfo(Path.GetFullPath(path), version.Version, source);
    }

    internal async Task<string?> ProvisionWindowsAsync(CancellationToken token = default)
    {
        if (!OperatingSystem.IsWindows()) return null;

        var directory = Path.Combine(_dataDirectory, "ResticBrowser", "tools", "restic", Version, "win-x64");
        var destination = Path.Combine(directory, "restic.exe");
        if (IsVerified(destination)) return destination;

        var assembly = Assembly.GetExecutingAssembly();
        await using var source = assembly.GetManifestResourceStream("ResticBrowser.EmbeddedRestic.win-x64");
        if (source is null) return null;

        Directory.CreateDirectory(directory);
        var lockPath = Path.Combine(directory, ".provision.lock");
        await using var lockStream = await AcquireLockAsync(lockPath, token);
        if (IsVerified(destination)) return destination;

        var temporary = Path.Combine(directory, $"restic-{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var target = File.Create(temporary))
                await source.CopyToAsync(target, token);

            if (!IsVerified(temporary))
                throw new ResticException("Die enthaltene Restic-Datei konnte nicht geprüft werden.");

            File.Move(temporary, destination, overwrite: true);
            return destination;
        }
        finally
        {
            try { File.Delete(temporary); } catch { }
        }
    }

    internal bool IsVerified(string path) => IsVerified(path, WindowsBinarySha256);

    internal static bool IsVerified(string path, string expectedSha256)
    {
        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length == 0) return false;
            using var stream = File.OpenRead(path);
            return string.Equals(Convert.ToHexString(SHA256.HashData(stream)), expectedSha256, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    private static bool IsUsableFile(string path)
    {
        try { return File.Exists(path) && new FileInfo(path).Length > 0; }
        catch { return false; }
    }

    private static async Task<FileStream> AcquireLockAsync(string path, CancellationToken token)
    {
        while (true)
        {
            token.ThrowIfCancellationRequested();
            try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) { await Task.Delay(100, token); }
        }
    }
}
