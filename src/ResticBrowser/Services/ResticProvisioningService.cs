using System.Reflection;
using System.Security.Cryptography;

namespace ResticBrowser.Services;

public sealed record ResticExecutableInfo(string Path, string Version, string Source);

public sealed class ResticProvisioningService
{
    public const string Version = "0.19.1";
    public const string WindowsBinarySha256 = "B0DD1FD21EEA5D8FE1325F55F7118213C21F36DE8A261E04C0624A5AB9FD7830";

    private readonly string _dataDirectory;

    public ResticProvisioningService(string? dataDirectory = null) =>
        _dataDirectory = dataDirectory ?? SettingsService.GetDataDirectory();

    public async Task<ResticExecutableInfo> ResolveAsync(string? configuredPath, CancellationToken token = default)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            if (!IsUsableFile(configuredPath))
                throw new ResticException("Das ausdrücklich ausgewählte Restic-Programm ist nicht vorhanden oder leer.");

            return new ResticExecutableInfo(Path.GetFullPath(configuredPath), "wird beim Verbinden geprüft", "Ausgewähltes Programm");
        }

        if (OperatingSystem.IsWindows())
        {
            var embedded = await ProvisionWindowsAsync(token);
            if (embedded is not null)
                return new ResticExecutableInfo(embedded, Version, "In Restic Browser enthalten");
        }
        else
        {
            var bundled = Path.Combine(AppContext.BaseDirectory, "tools", "restic");
            if (IsUsableFile(bundled))
                return new ResticExecutableInfo(Path.GetFullPath(bundled), "wird beim Verbinden geprüft", "Mit der Anwendung ausgeliefert");
        }

        foreach (var candidate in ResticLocator.PortableCandidates())
        {
            if (OperatingSystem.IsWindows() &&
                candidate.Contains($"{Path.DirectorySeparatorChar}tools{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
                continue;

            if (IsUsableFile(candidate))
                return new ResticExecutableInfo(Path.GetFullPath(candidate), "wird beim Verbinden geprüft", ResticLocator.Describe(candidate));
        }

        foreach (var candidate in ResticLocator.SystemCandidates())
            if (IsUsableFile(candidate))
                return new ResticExecutableInfo(Path.GetFullPath(candidate), "wird beim Verbinden geprüft", ResticLocator.Describe(candidate));

        throw new ResticException("Restic wurde nicht gefunden. Verwende die vollständige portable Ausgabe oder wähle ein Restic-Programm aus.");
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
