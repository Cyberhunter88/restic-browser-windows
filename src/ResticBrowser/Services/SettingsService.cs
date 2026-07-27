using System.Text.Json;
using ResticBrowser.Models;

namespace ResticBrowser.Services;

public sealed class AppSettings
{
    public List<RepositoryProfile> Profiles { get; set; } = [];
    public List<Bookmark> Bookmarks { get; set; } = [];
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
        try
        {
            if (!File.Exists(_settingsPath)) return new AppSettings();
            await using var stream = File.OpenRead(_settingsPath);
            using var doc = await JsonDocument.ParseAsync(stream);
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                var profiles = JsonSerializer.Deserialize<List<RepositoryProfile>>(doc.RootElement.GetRawText(), Options) ?? [];
                return new AppSettings { Profiles = profiles };
            }
            return JsonSerializer.Deserialize<AppSettings>(doc.RootElement.GetRawText(), Options) ?? new AppSettings();
        }
        catch { return new AppSettings(); }
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
