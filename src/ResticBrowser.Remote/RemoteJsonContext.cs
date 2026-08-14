using System.Text.Json.Serialization;
using ResticBrowser.Remote;

namespace ResticBrowser.RemoteHost;

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(RemoteRestoreCommand))]
[JsonSerializable(typeof(RemoteProtocolMessage))]
internal sealed partial class RemoteJsonContext : JsonSerializerContext;
