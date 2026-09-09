using System.Reflection;
using System.Security.Cryptography;

namespace ResticBrowser.Services;

public sealed record ResticExecutableInfo(string Path, string Version, string Source);

public sealed class ResticProvisioningService
{
    public const string Version = "0.19.1";
    public const string WindowsBinarySha256 = "B0DD1FD21EEA5D8FE1325F55F7118213C21F36DE8A261E04C0624A5AB9FD7830";

    public async Task<ResticExecutableInfo> ResolveAsync(string? configuredPath, CancellationToken token = default)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            if (!IsUsableFile(configuredPath))
                throw new ResticException("Das ausdrücklich ausgewählte Restic-Programm ist nicht vorhanden oder leer.");
            return new ResticExecutableInfo(Path.GetFullPath(configuredPath), "Benutzerdefiniert", "Ausgewähltes Programm");
        }

        foreach (var candidate in ResticLocator.PortableCandidates())
            if (IsUsableFile(candidate)) return new ResticExecutableInfo(Path.GetFullPath(candidate), Version, ResticLocator.Describe(candidate));

        if (OperatingSystem.IsWindows())
            return new ResticExecutableInfo(await ProvisionWindowsAsync(token), Version, "In Restic Browser enthalten");

        foreach (var candidate in ResticLocator.SystemCandidates())
            if (IsUsableFile(candidate)) return new ResticExecutableInfo(Path.GetFullPath(candidate), Version, ResticLocator.Describe(candidate));

        throw new ResticException("Restic wurde nicht gefunden. Die Linux-Ausgabe muss vollständig mit dem Ordner tools verwendet werden.");
    }

    internal async Task<string> ProvisionWindowsAsync(CancellationToken token = default)
    {
        var directory = Path.Combine(SettingsService.GetDataDirectory(), "ResticBrowser", "tools", "restic", Version, "win-x64");
        var destination = Path.Combine(directory, "restic.exe");
        if (IsVerified(destination)) return destination;
        Directory.CreateDirectory(directory);
        var lockPath = Path.Combine(directory, ".provision.lock");
        await using var lockStream = await AcquireLockAsync(lockPath, token);
        if (IsVerified(destination)) return destination;

        var temporary = Path.Combine(directory, $"restic-{Guid.NewGuid():N}.tmp");
        try
        {
            await using var source = Assembly.GetExecutingAssembly().GetManifestResourceStream("ResticBrowser.EmbeddedRestic.win-x64")
                ?? throw new ResticException("Die enthaltene Restic-Datei fehlt. Bitte Restic Browser erneut aus einer offiziellen Veröffentlichung herunterladen.");
            await using (var target = File.Create(temporary)) await source.CopyToAsync(target, token);
            if (!IsVerified(temporary)) throw new ResticException("Die enthaltene Restic-Datei konnte nicht geprüft werden.");
            File.Move(temporary, destination, overwrite: true);
            return destination;
        }
        finally { try { File.Delete(temporary); } catch { } }
    }

    private static bool IsUsableFile(string path) { try { return File.Exists(path) && new FileInfo(path).Length > 0; } catch { return false; } }
    private static bool IsVerified(string path)
    {
        if (!IsUsableFile(path)) return false;
        using var stream = File.OpenRead(path);
        return string.Equals(Convert.ToHexString(SHA256.HashData(stream)), WindowsBinarySha256, StringComparison.OrdinalIgnoreCase);
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
