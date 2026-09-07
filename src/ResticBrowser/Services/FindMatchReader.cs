using System.Text.Json;
using ResticBrowser.Models;

namespace ResticBrowser.Services;

// Reads individual matches, including within a single very large snapshot group.
internal sealed class FindMatchReader
{
    private JsonReaderState _state;
    private bool _matchesProperty;
    private bool _inMatches;

    public static async Task ReadAsync(Stream stream, Func<BackupNode, Task> onMatch,
        JsonSerializerOptions? options, CancellationToken token)
    {
        var parser = new FindMatchReader();
        var buffer = new byte[64 * 1024];
        var buffered = 0;
        while (true)
        {
            token.ThrowIfCancellationRequested();
            if (buffered == buffer.Length) Array.Resize(ref buffer, checked(buffer.Length * 2));
            var read = await stream.ReadAsync(buffer.AsMemory(buffered), token);
            buffered += read;
            var items = new List<BackupNode>();
            var consumed = parser.Parse(buffer.AsSpan(0, buffered), read == 0, items, options);
            foreach (var item in items)
            {
                token.ThrowIfCancellationRequested();
                await onMatch(item);
            }
            buffered -= consumed;
            buffer.AsSpan(consumed, buffered).CopyTo(buffer);
            if (read == 0) break;
        }
    }

    private int Parse(ReadOnlySpan<byte> bytes, bool final, List<BackupNode> items, JsonSerializerOptions? options)
    {
        var reader = new Utf8JsonReader(bytes, final, _state);
        while (true)
        {
            var before = reader;
            if (!reader.Read()) break;
            if (_inMatches && reader.TokenType == JsonTokenType.StartObject && reader.CurrentDepth == 3)
            {
                if (!JsonDocument.TryParseValue(ref reader, out var document))
                {
                    reader = before;
                    break;
                }
                using (document)
                {
                    var node = document.RootElement.Deserialize<BackupNode>(options);
                    if (node is not null) items.Add(node);
                }
            }
            else if (reader.TokenType == JsonTokenType.PropertyName && reader.CurrentDepth == 2)
                _matchesProperty = reader.ValueTextEquals("matches");
            else if (reader.TokenType == JsonTokenType.StartArray && reader.CurrentDepth == 2)
            {
                _inMatches = _matchesProperty;
                _matchesProperty = false;
            }
            else if (reader.TokenType == JsonTokenType.EndArray && reader.CurrentDepth == 2)
                _inMatches = false;
        }
        _state = reader.CurrentState;
        return checked((int)reader.BytesConsumed);
    }
}
