using System.Diagnostics;

namespace ResticBrowser.Services;

internal sealed class ResultBatch<T>(Func<IReadOnlyList<T>, Task> deliver, CancellationToken token)
{
    private readonly List<T> _items = new(256);
    private readonly Stopwatch _watch = Stopwatch.StartNew();
    private bool _first = true;

    public async Task AddAsync(T item)
    {
        token.ThrowIfCancellationRequested();
        _items.Add(item);
        if (_first || _items.Count >= 256 || _watch.ElapsedMilliseconds >= 100)
            await FlushAsync();
    }

    public async Task FlushAsync()
    {
        token.ThrowIfCancellationRequested();
        if (_items.Count == 0) return;
        var batch = _items.ToArray();
        _items.Clear();
        await deliver(batch);
        _first = false;
        _watch.Restart();
    }
}
