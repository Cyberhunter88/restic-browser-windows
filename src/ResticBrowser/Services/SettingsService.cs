using System.Text.Json;
using ResticBrowser.Models;

namespace ResticBrowser.Services;

public sealed class AppSettings
{
    public List<RepositoryProfile> Profiles { get; set; } = [];
}

public sealed class SettingsService
{
    private readonly string _settingsPath;
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    public SettingsService(string? settingsPath = null)
    {
        _settingsPath = settingsPath ?? Path.Combine(GetDataDirectory(), "ResticBrowser", "settings.json");
    }

    public async Task<AppSettings> LoadSettingsAsync()
    {
        AppSettings settings;
        try
        {
            if (!File.Exists(_settingsPath)) return new AppSettings();
            await using var stream = File.OpenRead(_settingsPath);
            using var doc = await JsonDocument.ParseAsync(stream);
            settings = doc.RootElement.ValueKind == JsonValueKind.Array
                ? new AppSettings { Profiles = JsonSerializer.Deserialize<List<RepositoryProfile>>(doc.RootElement.GetRawText(), Options) ?? [] }
                : JsonSerializer.Deserialize<AppSettings>(doc.RootElement.GetRawText(), Options) ?? new AppSettings();
        }
        catch { return new AppSettings(); }

        settings.Profiles ??= [];

        // Numerische Typkennungen bleiben für vorhandene lokale und REST-Profile stabil.
        if (settings.Profiles.RemoveAll(profile => profile.Type is RepositoryType.SFTP or RepositoryType.S3
            || profile.Repository.StartsWith("sftp:", StringComparison.OrdinalIgnoreCase)
            || profile.Repository.StartsWith("s3:", StringComparison.OrdinalIgnoreCase)) > 0)
            await SaveSettingsAsync(settings);
        return settings;
    }

    public async Task SaveSettingsAsync(AppSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
        var temporary = _settingsPath + ".tmp";
        await using (var stream = File.Create(temporary))
            await JsonSerializer.SerializeAsync(stream, settings, Options);
        File.Move(temporary, _settingsPath, overwrite: true);
    }

    public async Task<List<RepositoryProfile>> LoadAsync()
    {
        var settings = await LoadSettingsAsync();
        return settings.Profiles;
    }

    public async Task SaveAsync(IEnumerable<RepositoryProfile> profiles)
    {
        var settings = await LoadSettingsAsync();
        settings.Profiles = profiles.ToList();
        await SaveSettingsAsync(settings);
    }

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
