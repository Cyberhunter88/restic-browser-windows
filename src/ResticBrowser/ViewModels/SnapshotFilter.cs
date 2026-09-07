using ResticBrowser.Models;

namespace ResticBrowser.ViewModels;

internal sealed class SnapshotFilter
{
    private readonly Dictionary<SnapshotInfo, Entry> _index = new();
    public void Clear() => _index.Clear();

    public bool Matches(SnapshotInfo snapshot, string text, string host, string tag) =>
        (string.IsNullOrWhiteSpace(text) || Get(snapshot).SearchText.Contains(text.Trim(), StringComparison.CurrentCultureIgnoreCase)) &&
        (string.IsNullOrWhiteSpace(host) || host == "Alle Hosts" || snapshot.Hostname.Equals(host, StringComparison.OrdinalIgnoreCase)) &&
        (string.IsNullOrWhiteSpace(tag) || tag == "Alle Tags" || snapshot.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase));

    public IReadOnlyList<SnapshotInfo> Apply(IEnumerable<SnapshotInfo> snapshots, string text, string host, string tag, bool onlyLatest)
    {
        if (onlyLatest)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            snapshots = snapshots.OrderByDescending(s => s.Time).Where(s => seen.Add(Get(s).GroupKey));
        }
        return snapshots.Where(s => Matches(s, text, host, tag)).ToList();
    }

    private Entry Get(SnapshotInfo snapshot)
    {
        if (_index.TryGetValue(snapshot, out var entry)) return entry;
        entry = new Entry(
            string.Join('\n', snapshot.Hostname, snapshot.PathText, snapshot.TagText, snapshot.DisplayId),
            string.Join('\n', snapshot.Hostname, snapshot.PathText));
        _index[snapshot] = entry;
        return entry;
    }

    private sealed record Entry(string SearchText, string GroupKey);
}
