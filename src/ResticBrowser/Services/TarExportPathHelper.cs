namespace ResticBrowser.Services;

public static class TarExportPathHelper
{
    public static string BuildFileName(string name, string snapshotId)
    {
        var safeName = SanitizeFileName(name);
        var safeSnapshot = SanitizeFileName(snapshotId[..Math.Min(8, snapshotId.Length)]);
        return $"{safeName}_{safeSnapshot}.tar";
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
}
