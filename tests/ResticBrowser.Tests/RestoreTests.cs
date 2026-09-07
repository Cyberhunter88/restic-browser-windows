using System.Text.Json;
using System.Reflection;
using System.IO.Pipes;
using System.Security.Cryptography;
using ResticBrowser.Models;
using ResticBrowser.Remote;
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

    internal static void TarExportArguments()
    {
        var request = new TarExportRequest("abc", @"/Ordner mit Leerzeichen/prüfung.txt", @"C:\Ziel mit Leerzeichen\prüfung.tar");
        var args = ResticCommandBuilder.DumpTar("s3:https://server/bucket", request);
        Equal("dump", args[2]);
        Equal("tar", args[args.IndexOf("--archive") + 1]);
        Equal(request.TargetFile, args[args.IndexOf("--target") + 1]);
        True(args.Contains("/Ordner mit Leerzeichen/prüfung.txt"));
    }

    internal static void TarExportNames()
    {
        var invalid = Path.GetInvalidFileNameChars()[0];
        var fileName = TarExportPathHelper.BuildFileName($"Da{invalid}tei", "1234567890abcdef");
        True(!fileName.Contains(invalid));
        True(fileName.EndsWith("_12345678.tar", StringComparison.Ordinal));

        var longName = string.Concat(Enumerable.Repeat("prüfung-😀", 80));
        var boundedName = TarExportPathHelper.BuildFileName(longName, "1234567890abcdef");
        True(System.Text.Encoding.UTF8.GetByteCount(boundedName) <= 220);
        True(boundedName.EndsWith("_12345678.tar", StringComparison.Ordinal));
        True(!boundedName.EnumerateRunes().Any(rune => rune == System.Text.Rune.ReplacementChar));

        var directory = Path.Combine(Path.GetTempPath(), "tar-export-tests");
        var reserved = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        var first = TarExportPathHelper.GetUniquePath(directory, "Datei.tar", reserved);
        reserved.Add(first);
        var second = TarExportPathHelper.GetUniquePath(directory, "Datei.tar", reserved);
        True(!string.Equals(first, second, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal));
        True(second.EndsWith("Datei_2.tar", StringComparison.Ordinal));
    }

    internal static async Task TarExportCleanup()
    {
        var root = Path.Combine(Path.GetTempPath(), "ResticBrowserTarCleanup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var target = Path.Combine(root, "export.tar");
        var profile = new RepositoryProfile { Repository = "repo", ResticExecutable = Environment.ProcessPath! };
        using var credentials = new SessionCredentials("secret");
        try
        {
            var service = new ResticRepositoryService(new TarTargetRunner(exitCode: 1));
            try
            {
                await service.ExportTarAsync(profile, credentials, new TarExportRequest("snapshot", "/data", target));
                throw new Exception("Ein TAR-Exportfehler wurde erwartet.");
            }
            catch (ResticException) { }
            True(!File.Exists(target));

            await File.WriteAllTextAsync(target, "bestehend");
            try
            {
                await service.ExportTarAsync(profile, credentials, new TarExportRequest("snapshot", "/data", target));
                throw new Exception("Eine vorhandene TAR-Datei hätte abgewiesen werden müssen.");
            }
            catch (ResticException) { }
            Equal("bestehend", await File.ReadAllTextAsync(target));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
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
