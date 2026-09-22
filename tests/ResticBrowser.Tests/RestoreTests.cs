using System.Text.Json;
using System.Reflection;
using System.IO.Pipes;
using System.Security.Cryptography;
using ResticBrowser.Models;
using ResticBrowser.Services;
using ResticBrowser.ViewModels;


internal static partial class TestSuite
{
    internal static void RestoreArguments()
    {
        var request = new RestoreRequest("abc", @"C:\Ziel mit Leerzeichen", ["/Dokumente/a.txt", @"Bilder\b.jpg"], OverwritePolicy.Never);
        var args = ResticCommandBuilder.Restore("s3:https://server/bucket", request);
        True(args.Contains(@"C:\Ziel mit Leerzeichen"));
        Equal(2, args.Count(a => a == "--include"));
        True(args.Contains("/Bilder/b.jpg"));
        Equal("never", args[args.IndexOf("--overwrite") + 1]);
    }

    internal static async Task PermissionError()
    {
        var service = new ResticRepositoryService(new FailingRunner());
        using var credentials = new SessionCredentials("secret");
        var profile = new RepositoryProfile { Repository = "repo", ResticExecutable = Environment.ProcessPath! };
        try
        {
            await service.GetSnapshotsAsync(profile, credentials);
            throw new Exception("Ein Fehler wurde erwartet.");
        }
        catch (ResticException ex) { Equal("Der Zugriff wurde verweigert.", ex.Message); }
    }

    internal static async Task SymbolicLinkPermissionError()
    {
        const string error = "{\"message_type\":\"error\",\"error\":{\"message\":\"symlink \\\\usr\\\\bin\\\\mail: A required privilege is not held by the client.\"},\"during\":\"restore\"}";
        var service = new ResticRepositoryService(new RestoreFailingRunner(error));
        using var credentials = new SessionCredentials("secret");
        var profile = new RepositoryProfile { Repository = "repo", ResticExecutable = Environment.ProcessPath! };
        var result = await service.RestoreAsync(profile, credentials,
            new RestoreRequest("snapshot", "target", ["/etc"], OverwritePolicy.Never), null);
        True(result.Success);
        True(result.Message.Contains("symbolische Verknüpfung", StringComparison.Ordinal));
        True(!result.Message.Contains("message_type", StringComparison.Ordinal));
    }

    internal static async Task MixedRestoreErrors()
    {
        const string errors = """
            {"message_type":"error","error":{"message":"symlink \\usr\\bin\\mail: A required privilege is not held by the client."},"during":"restore"}
            {"message_type":"error","error":{"message":"open \\etc\\secret: permission denied"},"during":"restore"}
            """;
        var service = new ResticRepositoryService(new RestoreFailingRunner(errors));
        using var credentials = new SessionCredentials("secret");
        var profile = new RepositoryProfile { Repository = "repo", ResticExecutable = Environment.ProcessPath! };
        try
        {
            await service.RestoreAsync(profile, credentials,
                new RestoreRequest("snapshot", "target", ["/etc"], OverwritePolicy.Never), null);
            throw new Exception("Ein Fehler wurde erwartet.");
        }
        catch (ResticException ex)
        {
            Equal("Der Zugriff wurde verweigert.", ex.Message);
        }
    }

    internal static async Task RestoreErrorLimit()
    {
        var service = new ResticRepositoryService(new ManyRestoreErrorsRunner(150));
        using var credentials = new SessionCredentials("secret");
        var profile = new RepositoryProfile { Repository = "repo", ResticExecutable = Environment.ProcessPath! };
        try
        {
            await service.RestoreAsync(profile, credentials,
                new RestoreRequest("snapshot", "target", ["/probe"], OverwritePolicy.Never), null);
            throw new Exception("Die Wiederherstellung hätte fehlschlagen müssen.");
        }
        catch (ResticException ex)
        {
            True(ex.Message.Contains("weitere Fehlermeldung", StringComparison.Ordinal));
            True(ex.Message.Length < 4_000);
        }
    }
}
