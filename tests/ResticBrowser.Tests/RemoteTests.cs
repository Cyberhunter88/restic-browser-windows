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
    internal static void RemoteProtocolJson()
    {
        const string json = """{"message_type":"progress","files_restored":3,"future_field":{"value":42}}""";
        var message = JsonSerializer.Deserialize<RemoteProtocolMessage>(json)!;
        Equal("progress", message.MessageType);
        Equal(3L, message.FilesRestored);

        var request = new RemoteRestoreCommand
        {
            Operation = "restore",
            Repository = "/srv/repo",
            AllowedRoot = "/srv/restore",
            Target = "/srv/restore/test",
            Includes = ["/datei.txt"]
        };
        var serialized = JsonSerializer.Serialize(request);
        True(serialized.Length < RemoteProtocol.MaximumFrameLength);
    }

    internal static void RemoteCredentials()
    {
        var credentials = new RemoteSshCredentials("password", "passphrase");
        credentials.Dispose();
        Equal("", credentials.Password);
        Equal("", credentials.PrivateKeyPassphrase);
    }

    internal static async Task TrustedHostSettings()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ResticBrowserSettings-{Guid.NewGuid():N}.json");
        try
        {
            var settings = new SettingsService(path);
            await settings.TrustSshHostAsync(new TrustedSshHost
            {
                Host = "vps.example.test",
                Port = 2222,
                Algorithm = "ssh-ed25519",
                PublicKey = "AAAA-test-public-key",
                Fingerprint = "SHA256:test"
            });
            var loaded = await settings.LoadSettingsAsync();
            Equal(1, loaded.TrustedSshHosts.Count);
            var json = await File.ReadAllTextAsync(path);
            True(!json.Contains("password", StringComparison.OrdinalIgnoreCase));
            True(!json.Contains("passphrase", StringComparison.OrdinalIgnoreCase));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    internal static void EmbeddedRemoteHelper()
    {
        var assembly = typeof(RemoteProtocol).Assembly;
        True(assembly.GetManifestResourceNames().Contains("ResticBrowser.Remote.linux-x64", StringComparer.Ordinal));
        using var stream = assembly.GetManifestResourceStream("ResticBrowser.Remote.linux-x64")!;
        True(stream.Length > 1024 * 1024);
    }

    internal static async Task RemoteTransportRoundTrips()
    {
        const string host = "example.test";
        const string publicKey = "AQID";
        var fingerprint = "SHA256:" + Convert.ToBase64String(SHA256.HashData([1, 2, 3])).TrimEnd('=');
        var settingsPath = Path.Combine(Path.GetTempPath(), $"ResticBrowser-RemoteTransport-{Guid.NewGuid():N}.json");
        try
        {
            var settings = new SettingsService(settingsPath);
            await settings.TrustSshHostAsync(new TrustedSshHost
            {
                Host = host,
                Port = 22,
                Algorithm = "ssh-ed25519",
                PublicKey = publicKey,
                Fingerprint = fingerprint
            });
            var transport = new RecordingRemoteTransport(host, publicKey);
            var service = new RemoteRestoreService(settings, transport);
            var target = new RemoteRestoreTarget
            {
                Host = host,
                User = "tester",
                AuthenticationType = RemoteAuthenticationType.Agent,
                Repository = "/repo",
                AllowedRoot = "/restore"
            };
            using var sshCredentials = new RemoteSshCredentials();
            using var repositoryCredentials = new SessionCredentials("secret");

            await service.ValidateAsync(target, sshCredentials, repositoryCredentials);
            await service.ValidateAsync(target, sshCredentials, repositoryCredentials);

            Equal(4, transport.SshCalls);
            Equal(0, transport.SftpCalls);
            Equal(2, transport.KeyScanCalls);
        }
        finally
        {
            if (File.Exists(settingsPath)) File.Delete(settingsPath);
        }
    }

    internal static void RemotePaths()
    {
        if (!OperatingSystem.IsLinux()) throw new SkippedTestException("Benötigt Linux.");
        var container = Path.Combine(Path.GetTempPath(), $"ResticBrowserRemotePaths-{Guid.NewGuid():N}");
        var root = Path.Combine(container, "root");
        var outside = Path.Combine(container, "outside");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(outside);
        try
        {
            Equal(Path.Combine(root, "target"), RemotePathValidator.Validate(root, Path.Combine(root, "target"), requireWritableRoot: false));
            try
            {
                RemotePathValidator.Validate(root, outside, requireWritableRoot: false);
                throw new Exception("Ein Ziel außerhalb des Basisordners hätte abgewiesen werden müssen.");
            }
            catch (InvalidOperationException) { }

            try
            {
                RemotePathValidator.Validate(root, root + "/folder/../target", requireWritableRoot: false);
                throw new Exception("Ein Ziel mit '..'-Segment hätte abgewiesen werden müssen.");
            }
            catch (InvalidOperationException) { }

            var link = Path.Combine(root, "link");
            Directory.CreateSymbolicLink(link, outside);
            try
            {
                RemotePathValidator.Validate(root, Path.Combine(link, "target"), requireWritableRoot: false);
                throw new Exception("Ein Symlink-Ausbruch hätte abgewiesen werden müssen.");
            }
            catch (InvalidOperationException) { }
        }
        finally { Directory.Delete(container, recursive: true); }
    }
}
