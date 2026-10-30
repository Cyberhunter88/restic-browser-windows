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
        var args = ResticCommandBuilder.Restore("rest:https://server/repo", request);
        True(args.Contains(@"C:\Ziel mit Leerzeichen"));
        Equal(2, args.Count(a => a == "--include"));
        True(args.Contains("/Bilder/b.jpg"));
        var special = ResticCommandBuilder.Restore("repo", new RestoreRequest("abc", "target", ["/a*[?].txt"], OverwritePolicy.Never));
        True(special.Contains("/a[*][[][?]].txt"));
        Equal("never", args[args.IndexOf("--overwrite") + 1]);
    }

    internal static async Task RestoreRejectsRepositoryTargets()
    {
        var root = Path.Combine(Path.GetTempPath(), "ResticBrowserPaths-" + Guid.NewGuid().ToString("N"));
        var repository = Path.Combine(root, "repository");
        Directory.CreateDirectory(repository);
        var profile = new RepositoryProfile { Repository = repository, ResticExecutable = Environment.ProcessPath! };
        var service = new ResticRepositoryService(new FailingRunner());
        using var credentials = new SessionCredentials("test");
        try
        {
            var targets = new List<string> { repository, Path.Combine(repository, "child"), root };
            if (!OperatingSystem.IsWindows())
            {
                var alias = Path.Combine(root, "alias");
                Directory.CreateSymbolicLink(alias, repository);
                targets.Add(Path.Combine(alias, "child"));
            }
            foreach (var target in targets)
            {
                var request = new RestoreRequest("snapshot", target, ["/file"], OverwritePolicy.Never);
                try { await service.RestoreAsync(profile, credentials, request, null); throw new Exception("Unsicheres Restore-Ziel akzeptiert."); }
                catch (ResticException ex) { True(ex.Message.Contains("überlappen", StringComparison.Ordinal)); }
                try { await service.PreviewRestoreAsync(profile, credentials, request); throw new Exception("Unsicheres Vorschauziel akzeptiert."); }
                catch (ResticException ex) { True(ex.Message.Contains("überlappen", StringComparison.Ordinal)); }
                try { ResticRepositoryService.ValidateMountLocation(profile, target); throw new Exception("Unsicherer Mount-Pfad akzeptiert."); }
                catch (ResticException ex) { True(ex.Message.Contains("überlappen", StringComparison.Ordinal)); }
            }
            ResticRepositoryService.ValidateMountLocation(profile, Path.Combine(root, "repository-other"));
            try { ResticRepositoryService.ValidateMountLocation(profile, Path.Combine(root, "other", "..", "repository")); throw new Exception("Mehrdeutiger Pfad akzeptiert."); }
            catch (ResticException ex) { True(ex.Message.Contains("nicht sicher geprüft", StringComparison.Ordinal)); }
        }
        finally { Directory.Delete(root, true); }
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
