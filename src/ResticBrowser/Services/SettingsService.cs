using System.Text.Json;
using ResticBrowser.Models;

namespace ResticBrowser.Services;

public sealed class SettingsService
{
    private readonly string _settingsPath;
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public SettingsService(string? settingsPath = null)
    {
        _settingsPath = settingsPath ?? Path.Combine(GetDataDirectory(), "ResticBrowser", "settings.json");
    }

    public async Task<List<RepositoryProfile>> LoadAsync()
    {
        try
        {
            if (!File.Exists(_settingsPath)) return [];
            await using var stream = File.OpenRead(_settingsPath);
            return await JsonSerializer.DeserializeAsync<List<RepositoryProfile>>(stream, Options) ?? [];
        }
        catch { return []; }
    }

    public async Task SaveAsync(IEnumerable<RepositoryProfile> profiles)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
        var temporary = _settingsPath + ".tmp";
        await using (var stream = File.Create(temporary))
            await JsonSerializer.SerializeAsync(stream, profiles, Options);
        File.Move(temporary, _settingsPath, overwrite: true);
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
