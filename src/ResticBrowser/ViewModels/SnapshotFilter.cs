using ResticBrowser.Models;

namespace ResticBrowser.ViewModels;

internal sealed class SnapshotFilter
{
    private readonly Dictionary<SnapshotInfo, Entry> _index = new();
    private readonly Dictionary<string, SnapshotInfo> _latestByGroup = new(StringComparer.OrdinalIgnoreCase);

    public void Clear()
    {
        _index.Clear();
        _latestByGroup.Clear();
    }

    public bool TrackLatest(SnapshotInfo snapshot, out SnapshotInfo? replaced)
    {
        var groupKey = Get(snapshot).GroupKey;
        if (_latestByGroup.TryGetValue(groupKey, out var current))
        {
            if (current.Time >= snapshot.Time)
            {
                replaced = null;
                return false;
            }

            _latestByGroup[groupKey] = snapshot;
            replaced = current;
            return true;
        }

        _latestByGroup[groupKey] = snapshot;
        replaced = null;
        return true;
    }

    public bool Matches(SnapshotInfo snapshot, string text, string host, string tag) =>
        (string.IsNullOrWhiteSpace(text) || Get(snapshot).SearchText.Contains(text.Trim(), StringComparison.CurrentCultureIgnoreCase)) &&
        (string.IsNullOrWhiteSpace(host) || host == "Alle Hosts" || snapshot.Hostname.Equals(host, StringComparison.OrdinalIgnoreCase)) &&
        (string.IsNullOrWhiteSpace(tag) || tag == "Alle Tags" || snapshot.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase));

    public IReadOnlyList<SnapshotInfo> Apply(IEnumerable<SnapshotInfo> snapshots, string text, string host, string tag,
        bool onlyLatest, bool snapshotsAreSorted = false)
    {
        if (onlyLatest)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (snapshotsAreSorted)
            {
                snapshots = snapshots.Where(s => seen.Add(Get(s).GroupKey));
            }
            else
            {
                var latestByGroup = new Dictionary<string, SnapshotInfo>(StringComparer.OrdinalIgnoreCase);
                foreach (var snapshot in snapshots)
                {
                    var groupKey = Get(snapshot).GroupKey;
                    if (!latestByGroup.TryGetValue(groupKey, out var current) || snapshot.Time > current.Time)
                        latestByGroup[groupKey] = snapshot;
                }
                snapshots = latestByGroup.Values.OrderByDescending(snapshot => snapshot.Time);
            }
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
