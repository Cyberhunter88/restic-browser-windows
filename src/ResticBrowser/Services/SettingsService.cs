using System.Diagnostics;
using System.Text.Json;
using ResticBrowser.Models;

namespace ResticBrowser.Services;

public sealed class AppSettings
{
    public List<RepositoryProfile> Profiles { get; set; } = [];
}

public sealed class SettingsException(string message, Exception? inner = null) : Exception(message, inner);

public sealed class SettingsService
{
    private readonly string _settingsPath;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(5);
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    public SettingsService(string? settingsPath = null)
    {
        _settingsPath = Path.GetFullPath(settingsPath ?? Path.Combine(GetDataDirectory(), "ResticBrowser", "settings.json"));
    }

    public Task<AppSettings> LoadSettingsAsync() => WithLockAsync(async () =>
    {
        var settings = await ReadSettingsAsync();
        // Numerische Typkennungen bleiben für die Erkennung entfernter Profile stabil.
        if (settings.Profiles.RemoveAll(profile => profile.Type != RepositoryType.Local
            || profile.Repository.StartsWith("sftp:", StringComparison.OrdinalIgnoreCase)
            || profile.Repository.StartsWith("s3:", StringComparison.OrdinalIgnoreCase)
            || profile.Repository.StartsWith("rest:", StringComparison.OrdinalIgnoreCase)) > 0)
            await WriteSettingsAsync(settings);
        return settings;
    });

    public async Task SaveSettingsAsync(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        await WithLockAsync(async () =>
        {
            // Auch direkte Speicheraufrufe dürfen eine beschädigte Datei nicht ersetzen.
            await ReadSettingsAsync();
            ValidateSettings(settings);
            await WriteSettingsAsync(settings);
            return true;
        });
    }

    private async Task<AppSettings> ReadSettingsAsync()
    {
        try
        {
            await using var stream = File.OpenRead(_settingsPath);
            using var doc = await JsonDocument.ParseAsync(stream);
            var settings = doc.RootElement.ValueKind == JsonValueKind.Array
                ? new AppSettings { Profiles = doc.RootElement.Deserialize<List<RepositoryProfile>>(Options) ?? [] }
                : doc.RootElement.Deserialize<AppSettings>(Options)
                    ?? throw new JsonException("Einstellungen dürfen nicht null sein.");
            ValidateSettings(settings);
            return settings;
        }
        catch (FileNotFoundException) { return new AppSettings(); }
    }

    private static void ValidateSettings(AppSettings settings)
    {
        if (settings.Profiles is null || settings.Profiles.Any(profile => profile is null || profile.Repository is null))
            throw new JsonException("Ungültige Repository-Profile.");
    }

    private async Task WriteSettingsAsync(AppSettings settings)
    {
        var temporary = _settingsPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await JsonSerializer.SerializeAsync(stream, settings, Options);
                await stream.FlushAsync();
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, _settingsPath, overwrite: true);
        }
        finally
        {
            // Eine fehlgeschlagene Bereinigung darf den ursprünglichen Fehler nicht verdecken.
            try { File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    private async Task<T> WithLockAsync<T>(Func<Task<T>> action)
    {
        var started = Stopwatch.StartNew();
        if (!await _gate.WaitAsync(LockTimeout)) throw BusyException();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
            await using var fileLock = await AcquireFileLockAsync(started);
            return await action();
        }
        catch (JsonException ex)
        {
            throw new SettingsException("Die Profildatei ist beschädigt oder ungültig. Sie bleibt unverändert. Bitte die Datei settings.json im Benutzerdatenordner prüfen oder aus einer Sicherung wiederherstellen.", ex);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new SettingsException("Die Profile konnten nicht geladen oder gespeichert werden. Bitte Zugriffsrechte und freien Speicherplatz im Benutzerdatenordner prüfen.", ex);
        }
        finally { _gate.Release(); }
    }

    private async Task<FileStream> AcquireFileLockAsync(Stopwatch started)
    {
        // Die Sperrdatei bleibt bestehen: Löschen würde parallele Sperren auf verschiedene Dateien ermöglichen.
        while (true)
        {
            try { return new FileStream(_settingsPath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException ex) when ((ex.HResult & 0xffff) is 11 or 32 or 33)
            {
                var remaining = LockTimeout - started.Elapsed;
                if (remaining <= TimeSpan.Zero) throw BusyException();
                await Task.Delay(remaining < TimeSpan.FromMilliseconds(50) ? remaining : TimeSpan.FromMilliseconds(50));
            }
        }
    }

    private static SettingsException BusyException() => new("Die Profildatei wird gerade von einer anderen Aktion oder Instanz verwendet. Bitte nach Abschluss erneut versuchen.");

    public async Task<List<RepositoryProfile>> LoadAsync() => (await LoadSettingsAsync()).Profiles;

    public Task SaveAsync(IEnumerable<RepositoryProfile> profiles) => SaveSettingsAsync(new AppSettings { Profiles = profiles.ToList() });

    internal static string GetDataDirectory()
    {
        if (OperatingSystem.IsWindows())
            return Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var xdgData = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        return !string.IsNullOrWhiteSpace(xdgData)
            ? xdgData
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
    }
}
