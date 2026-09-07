using System.Text;
using System.Text.Json;
using System.Collections.Specialized;
using ResticBrowser.Models;
using ResticBrowser.Services;
using ResticBrowser.ViewModels;

internal static partial class TestSuite
{
    internal static async Task FindMatchesBeforeGroupEnds()
    {
        var seen = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var stream = new GatedJsonStream(
            "[{\"matches\":[{\"name\":\"first.txt\",\"path\":\"/first.txt\"},",
            "{\"name\":\"last.txt\"}],\"snapshot\":\"id\"}]");
        var names = new List<string>();
        var read = FindMatchReader.ReadAsync(stream, node =>
        {
            names.Add(node.Name);
            seen.TrySetResult();
            return Task.CompletedTask;
        }, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, default);
        try
        {
            await seen.Task.WaitAsync(TimeSpan.FromSeconds(3));
            True(!read.IsCompleted);
            Equal("first.txt", names.Single());
        }
        finally { stream.Release.TrySetResult(); }
        await read;
        Equal(2, names.Count);
    }

    internal static async Task FindMatchesChunkBoundaries()
    {
        const string json = """[{"extra":{"matches":[{"name":"ignore"}]},"matches":[{"name":"größer.txt","path":"/größer.txt","extra":[1,2,3]},{"name":"last"}],"snapshot":"s"},{"matches":[]}]""";
        using var stream = new SmallChunkStream(Encoding.UTF8.GetBytes(json));
        var names = new List<string>();
        await FindMatchReader.ReadAsync(stream, node =>
        {
            names.Add(node.Name);
            return Task.CompletedTask;
        }, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, default);
        Equal("größer.txt", names[0]);
        Equal("last", names[1]);
        Equal(2, names.Count);
        using var broken = new SmallChunkStream(Encoding.UTF8.GetBytes("[{\"matches\":[{"));
        try
        {
            await FindMatchReader.ReadAsync(broken, _ => Task.CompletedTask, null, default);
            throw new Exception("Ungültiges JSON wurde akzeptiert.");
        }
        catch (JsonException) { }
    }

    internal static async Task SnapshotBatchesBeforeCompletion()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var delivered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var repository = new ControlledRepositoryService
        {
            SnapshotBatches = async (batch, token) =>
            {
                await batch([new SnapshotInfo { Id = "old", Hostname = "h", Time = DateTimeOffset.UnixEpoch }]);
                delivered.SetResult();
                await gate.Task.WaitAsync(token);
                await batch([new SnapshotInfo { Id = "new", Hostname = "h", Time = DateTimeOffset.UtcNow }]);
            }
        };
        using var vm = CreateConnectedViewModel(repository);
        var load = vm.RefreshSnapshotsAsync();
        try
        {
            await delivered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Equal("old", vm.VisibleSnapshots.Single().Id);
            True(!load.IsCompleted);
        }
        finally { gate.TrySetResult(); }
        await load;
        Equal("new", vm.Snapshots[0].Id);
        repository.CompleteDirectory("/", []);
    }

    internal static async Task SearchBatchesRaceAndCancel()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var delivered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var repository = new ControlledRepositoryService
        {
            SearchBatches = async (pattern, batch, token) =>
            {
                await batch([new BackupNode { Name = pattern }]);
                if (pattern == "old")
                {
                    delivered.TrySetResult();
                    await gate.Task; // Intentionally ignores cancellation, like a late backend callback.
                    await batch([new BackupNode { Name = "late" }]);
                }
                return false;
            }
        };
        using var vm = CreateConnectedViewModel(repository);
        var old = vm.SearchAsync("old");
        await delivered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Equal("old", vm.Nodes.Single().Name);
        await vm.SearchAsync("new");
        gate.SetResult();
        await old;
        Equal("new", vm.Nodes.Single().Name);

        repository.SearchBatches = async (_, batch, token) =>
        {
            await batch([new BackupNode { Name = "partial" }]);
            vm.Cancel();
            await batch([new BackupNode { Name = "too-late" }]);
            token.ThrowIfCancellationRequested();
            return false;
        };
        try { await vm.SearchAsync("cancel"); }
        catch (OperationCanceledException) { }
        Equal(0, vm.Nodes.Count);
        Equal("Suche abgebrochen", vm.Status);
        True(!vm.IsBusy);
    }

    internal static async Task SnapshotBatchesFailureAndDisconnect()
    {
        var repository = new ControlledRepositoryService
        {
            SnapshotBatches = async (batch, _) =>
            {
                await batch([new SnapshotInfo { Id = "partial" }]);
                throw new ResticException("Lesefehler");
            }
        };
        using var vm = CreateConnectedViewModel(repository);
        try
        {
            await vm.RefreshSnapshotsAsync();
            throw new Exception("Fehler wurde nicht gemeldet.");
        }
        catch (ResticException) { }
        Equal(0, vm.Snapshots.Count);
        Equal(0, vm.VisibleSnapshots.Count);
        True(!vm.IsBusy);

        var delivered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        repository.SnapshotBatches = async (batch, _) =>
        {
            await batch([new SnapshotInfo { Id = "first" }]);
            delivered.SetResult();
            await gate.Task;
            await batch([new SnapshotInfo { Id = "late" }]);
        };
        var loading = vm.RefreshSnapshotsAsync();
        await delivered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        vm.Disconnect();
        gate.SetResult();
        await loading;
        Equal(0, vm.Snapshots.Count);
        Equal(0, vm.VisibleSnapshots.Count);
        Equal("Verbindung getrennt", vm.Status);
    }

    internal static async Task SearchBatchesBounded()
    {
        var nodes = Enumerable.Range(0, 10_001).Select(i => new BackupNode { Name = $"{i}.txt" }).ToList();
        var service = new ResticRepositoryService(new JsonRunner(JsonSerializer.Serialize(
            new[] { new FindSnapshotGroup { Matches = nodes } })));
        using var credentials = new SessionCredentials("secret");
        var count = 0;
        var calls = 0;
        var truncated = await service.FindBatchedAsync(
            new RepositoryProfile { Repository = "repo", ResticExecutable = Environment.ProcessPath! },
            credentials, "s", "*", batch =>
            {
                True(batch.Count <= 256);
                count += batch.Count;
                calls++;
                return Task.CompletedTask;
            });
        Equal(10_000, count);
        True(truncated);
        True(calls > 1);
    }

    internal static void SnapshotFilterOrdering()
    {
        var filter = new SnapshotFilter();
        var snapshots = new[]
        {
            new SnapshotInfo { Id = "old", Hostname = "h", Time = DateTimeOffset.UnixEpoch },
            new SnapshotInfo { Id = "new", Hostname = "h", Time = DateTimeOffset.UtcNow }
        };
        Equal("new", filter.Apply(snapshots, "", "", "", true).Single().Id);
    }

    internal static void BatchCollectionAppend()
    {
        var collection = new BatchObservableCollection<int>();
        collection.Add(1);
        var events = 0;
        collection.CollectionChanged += (_, e) =>
        {
            Equal(NotifyCollectionChangedAction.Add, e.Action);
            Equal(1, e.NewStartingIndex);
            Equal(2, e.NewItems!.Count);
            events++;
        };
        collection.AddRange([2, 3]);
        Equal(1, collection[0]);
        Equal(1, events);
    }
}

sealed class SmallChunkStream(byte[] data) : MemoryStream(data)
{
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
        base.ReadAsync(buffer[..Math.Min(buffer.Length, 7)], cancellationToken);
}

sealed class GatedJsonStream(string first, string last) : MemoryStream(Encoding.UTF8.GetBytes(first + last))
{
    private readonly int _boundary = Encoding.UTF8.GetByteCount(first);
    public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (Position >= _boundary) await Release.Task.WaitAsync(cancellationToken);
        else buffer = buffer[..Math.Min(buffer.Length, _boundary - (int)Position)];
        return await base.ReadAsync(buffer, cancellationToken);
    }
}
