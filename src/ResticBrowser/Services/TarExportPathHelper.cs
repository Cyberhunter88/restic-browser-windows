using System.Text;

namespace ResticBrowser.Services;

public static class TarExportPathHelper
{
    private const int MaxGeneratedFileNameBytes = 220;

    public static string BuildFileName(string name, string snapshotId)
    {
        var safeSnapshot = SanitizeFileName(snapshotId[..Math.Min(8, snapshotId.Length)]);
        var suffix = $"_{safeSnapshot}.tar";
        var safeName = TruncateUtf8(SanitizeFileName(name), MaxGeneratedFileNameBytes - Encoding.UTF8.GetByteCount(suffix));
        return $"{safeName}{suffix}";
    }

    public static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var sanitized = new string(name.Select(character => invalid.Contains(character) ? '_' : character).ToArray())
            .Trim().TrimEnd('.', ' ');
        return string.IsNullOrWhiteSpace(sanitized) ? "Export" : sanitized;
    }

    public static string GetUniquePath(string directory, string fileName, ISet<string>? reservedPaths = null)
    {
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        var candidate = Path.Combine(directory, fileName);
        var suffix = 2;
        while (File.Exists(candidate) || reservedPaths?.Contains(candidate) == true)
            candidate = Path.Combine(directory, $"{stem}_{suffix++}{extension}");
        return candidate;
    }

    private static string TruncateUtf8(string value, int maximumBytes)
    {
        var builder = new StringBuilder(value.Length);
        var byteCount = 0;
        foreach (var rune in value.EnumerateRunes())
        {
            if (byteCount + rune.Utf8SequenceLength > maximumBytes) break;
            builder.Append(rune);
            byteCount += rune.Utf8SequenceLength;
        }
        return builder.Length == 0 ? "Export" : builder.ToString();
    }
}
