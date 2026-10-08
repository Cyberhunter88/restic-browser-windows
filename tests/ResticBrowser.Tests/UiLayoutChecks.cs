using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ResticBrowser;
using ResticBrowser.Views;

internal static class UiLayoutChecks
{
    internal static int Run(string[] args) => AppBuilder.Configure<UiCheckApp>().UsePlatformDetect()
        .StartWithClassicDesktopLifetime(args, ShutdownMode.OnExplicitShutdown);

    internal sealed class UiCheckApp : App
    {
        public override void OnFrameworkInitializationCompleted()
        {
            base.OnFrameworkInitializationCompleted();
            Dispatcher.UIThread.Post(async () =>
            {
                var desktop = (IClassicDesktopStyleApplicationLifetime)ApplicationLifetime!;
                var output = Environment.GetEnvironmentVariable("RESTIC_BROWSER_UI_CHECK_DIR")!;
                Directory.CreateDirectory(output);
                try
                {
                    var main = (MainWindow)desktop.MainWindow!;
                    Console.WriteLine($"UI-Skalierung: {main.RenderScaling}");
                    if (double.TryParse(Environment.GetEnvironmentVariable("AVALONIA_GLOBAL_SCALE_FACTOR"),
                        System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var scale))
                        Require(Math.Abs(main.RenderScaling - scale) < 0.01, "Skalierungsfaktor wurde nicht übernommen");
                    foreach (var dark in new[] { false, true })
                    {
                        SetTheme(dark);
                        var theme = dark ? "dark" : "light";
                        foreach (var width in new[] { 760, 1099, 1100, 1360 })
                        {
                            main.Width = width;
                            main.Height = width == 760 ? 540 : 800;
                            await Task.Delay(150);
                            var compact = main.ClientSize.Width < 1100;
                            var browser = main.FindControl<Grid>("BrowserGrid")!;
                            var search = main.FindControl<TextBox>("SearchBox")!;
                            var actions = main.FindControl<StackPanel>("SearchActions")!;
                            var files = main.FindControl<DataGrid>("FileList")!;
                            Require(browser.ColumnDefinitions[0].Width.Value == (compact ? 210 : 280), "Snapshot-Spaltenbreite");
                            Require(Grid.GetRow(actions) == (compact ? 1 : 0), "Suchaktionen");
                            Require(files.Columns[5].IsVisible == !compact, "Berechtigungsspalte");
                            Require(files.Columns[1].ActualWidth >= 120, "Namensspalte");
                            if (compact) Require(search.Bounds.Width >= 250, "Suchfeld zu schmal");
                            Capture(main, Path.Combine(output, $"main-{theme}-{width}.png"));
                        }
                        // Focus and a synthetic pointer enter exercise the real theme states.
                        var button = main.FindControl<StackPanel>("SearchActions")!.Children.OfType<Button>().First();
                        button.Focus();
                        button.RaiseEvent(new PointerEventArgs(InputElement.PointerEnteredEvent, button,
                            new Pointer(1, PointerType.Mouse, true), main, new Point(10, 10), 0,
                            new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.Other), KeyModifiers.None));
                        await Task.Delay(100);
                        Require(button.IsPointerOver, "Simulierter Hover fehlt");
                        Capture(main, Path.Combine(output, $"main-{theme}-hover-focus.png"));
                        var connection = new ConnectionWindow();
                        connection.Show(main);
                        Require(connection.FindControl<ComboBox>("RepoTypeBox")!.SelectedIndex == 0, "Verbindungstyp fehlt");
                        await Task.Delay(150);
                        Capture(connection, Path.Combine(output, $"connection-{theme}.png"));
                        connection.Height = 580;
                        await Task.Delay(150);
                        Capture(connection, Path.Combine(output, $"connection-{theme}-short.png"));
                        connection.Close();
                    }
                    Console.WriteLine("PASS  Fensterlayout bei 760/1099/1100/1360 DIP, Hell/Dunkel, Dialog und Hover/Fokus");
                    desktop.Shutdown(0);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine(ex);
                    desktop.Shutdown(1);
                }
            });
        }
    }

    private static void Require(bool condition, string detail)
    {
        if (!condition) throw new Exception("UI-Prüfung: " + detail);
    }

    private static void Capture(Window window, string path)
    {
        using var bitmap = new RenderTargetBitmap(new PixelSize((int)window.ClientSize.Width, (int)window.ClientSize.Height));
        bitmap.Render(window);
        bitmap.Save(path, new PngBitmapEncoderOptions());
    }
}
