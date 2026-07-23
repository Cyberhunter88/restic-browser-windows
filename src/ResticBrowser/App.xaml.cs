using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace ResticBrowser;

public partial class App : Application
{
    public static bool IsDark { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        var systemUsesLight = Registry.GetValue(
            @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
            "AppsUseLightTheme", 1) as int? ?? 1;
        SetTheme(systemUsesLight == 0);
        base.OnStartup(e);
    }

    public static void SetTheme(bool dark)
    {
        IsDark = dark;
        var resources = Current.Resources;
        resources["AppBackground"] = Brush(dark ? "#10131A" : "#F4F6FA");
        resources["PanelBackground"] = Brush(dark ? "#181D27" : "#FFFFFF");
        resources["PanelAltBackground"] = Brush(dark ? "#202633" : "#F7F8FC");
        resources["TextPrimary"] = Brush(dark ? "#F2F4F7" : "#172033");
        resources["TextSecondary"] = Brush(dark ? "#AAB3C5" : "#667085");
        resources["BorderBrush"] = Brush(dark ? "#303949" : "#E2E7F0");
        foreach (Window window in Current.Windows)
            WindowTheme.Apply(window, dark);
    }

    private static SolidColorBrush Brush(string value) =>
        new((Color)ColorConverter.ConvertFromString(value));
}
