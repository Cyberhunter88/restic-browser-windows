using System.IO.Pipes;
using Avalonia;

namespace ResticBrowser;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("RESTIC_BROWSER_ASKPASS_PIPE")))
        {
            RunAskPassAsync().GetAwaiter().GetResult();
            return;
        }
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();

    private static async Task RunAskPassAsync()
    {
        var pipeName = Environment.GetEnvironmentVariable("RESTIC_BROWSER_ASKPASS_PIPE");
        if (string.IsNullOrWhiteSpace(pipeName)) return;
        using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.In, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(10000);
        using var reader = new StreamReader(pipe);
        Console.Write(await reader.ReadToEndAsync());
    }
}
