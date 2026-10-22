using System.Text;

namespace ResticBrowser.Services;

internal sealed class BoundedCheckDetails
{
    internal const int MaximumBytes = 64 * 1024;
    private const string TruncatedMessage = "\n… Weitere Prüfdetails wurden aus Platzgründen gekürzt.";
    private readonly StringBuilder _text = new();
    private int _bytes;
    private bool _truncated;

    public bool IsTruncated => _truncated;

    public void AppendLine(string value)
    {
        if (_truncated || string.IsNullOrWhiteSpace(value)) return;
        var remaining = MaximumBytes - Encoding.UTF8.GetByteCount(TruncatedMessage) - _bytes;
        var separator = _text.Length == 0 ? "" : Environment.NewLine;
        if (Encoding.UTF8.GetByteCount(separator) + Encoding.UTF8.GetByteCount(value) <= remaining)
        {
            _text.Append(separator).Append(value);
            _bytes += Encoding.UTF8.GetByteCount(separator) + Encoding.UTF8.GetByteCount(value);
            return;
        }
        foreach (var rune in (separator + value).EnumerateRunes())
        {
            if (rune.Utf8SequenceLength > remaining) break;
            _text.Append(rune.ToString());
            remaining -= rune.Utf8SequenceLength;
        }
        _truncated = true;
    }

    public override string ToString() => _text.ToString() + (_truncated ? TruncatedMessage : "");
}
