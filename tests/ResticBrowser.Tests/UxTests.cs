using System.Text.Json;
using ResticBrowser.Models;
using ResticBrowser.Services;

internal static partial class TestSuite
{
    internal static void RelativeTimeText()
    {
        var now = new DateTimeOffset(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);
        Equal("gerade eben", SnapshotInfo.FormatRelativeTime(now.AddSeconds(-20), now));
        Equal("vor 5 Minuten", SnapshotInfo.FormatRelativeTime(now.AddMinutes(-5), now));
        Equal("vor 1 Stunde", SnapshotInfo.FormatRelativeTime(now.AddHours(-1), now));
        Equal("vor 3 Tagen", SnapshotInfo.FormatRelativeTime(now.AddDays(-3), now));
        Equal("vor 2 Monaten", SnapshotInfo.FormatRelativeTime(now.AddDays(-65), now));
        Equal("vor 1 Jahr", SnapshotInfo.FormatRelativeTime(now.AddDays(-400), now));
        Equal("gerade eben", SnapshotInfo.FormatRelativeTime(now.AddMinutes(10), now));
    }

    internal static void PathSegmentsBuilt()
    {
        var root = PathSegment.Build("/");
        Equal(1, root.Count);
        Equal("/", root[0].Path);
        var deep = PathSegment.Build("/home/user/docs");
        Equal(4, deep.Count);
        Equal("/home", deep[1].Path);
        Equal("/home/user/docs", deep[3].Path);
        var search = PathSegment.Build("Suchergebnisse: *.txt");
        Equal(1, search.Count);
        Equal("Suchergebnisse: *.txt", search[0].Path);
    }

    internal static void ErrorGuidanceAddsHints()
    {
        True(ErrorGuidance.Describe(new ResticException("Das Repository-Passwort ist falsch.", exitCode: 12)).Contains("Tippfehler"));
        True(ErrorGuidance.Describe(new ResticException("Das Repository wurde nicht gefunden oder ist nicht initialisiert.", exitCode: 10)).Contains("Repository-Pfad"));
        Equal("Etwas anderes.", ErrorGuidance.Describe(new ResticException("Etwas anderes.", exitCode: 1)));
    }

    internal static async Task SettingsKeepUxState()
    {
        var directory = Path.Combine(Path.GetTempPath(), "rb-ux-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "settings.json");
            var service = new SettingsService(path);
            var id = Guid.NewGuid();
            await service.SaveSettingsAsync(new AppSettings
            {
                Profiles = [new RepositoryProfile { Id = id, Name = "A", Repository = "repo" }],
                LastProfileId = id,
                LastRestoreTarget = "D:\\Ziel",
                Window = new WindowSettings { Width = 1200, Height = 700, X = 10, Y = 20, IsMaximized = true }
            });
            var loaded = await service.LoadSettingsAsync();
            Equal(id, loaded.LastProfileId!.Value);
            Equal("D:\\Ziel", loaded.LastRestoreTarget);
            Equal(1200d, loaded.Window!.Width);
            True(loaded.Window.IsMaximized);

            // Ältere Dateien ohne die neuen Felder bleiben lesbar.
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new { Profiles = Array.Empty<RepositoryProfile>() }));
            var legacy = await service.LoadSettingsAsync();
            True(legacy.LastProfileId is null && legacy.Window is null && legacy.LastRestoreTarget is null);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
