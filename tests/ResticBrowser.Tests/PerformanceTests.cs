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
    internal static async Task StreamingDirectory()
    {
        var lines = Enumerable.Range(0, 1000).Select(i =>
            $"{{\"message_type\":\"node\",\"name\":\"{i:D4}.txt\",\"path\":\"/{i:D4}.txt\",\"type\":\"file\",\"size\":{i}}}");
        var runner = new LineRunner(lines);
        var service = new ResticRepositoryService(runner);
        using var credentials = new SessionCredentials("secret");
        var profile = new RepositoryProfile { Repository = "repo", ResticExecutable = Environment.ProcessPath! };
        var nodes = await service.GetDirectoryAsync(profile, credentials, "snapshot", "/");
        Equal(1000, nodes.Count);
        Equal(1000, runner.LinesDelivered);
    }

    internal static async Task NewestSearchSingleProcess()
    {
        const string json = """
            [{"snapshot":"newest-snapshot","matches":[{"name":"probe.txt","path":"/probe.txt","type":"file","size":12}]},
             {"snapshot":"older-snapshot","matches":[{"name":"probe.txt","path":"/probe.txt","type":"file","size":10}]}]
            """;
        var runner = new JsonRunner(json);
        var service = new ResticRepositoryService(runner);
        using var credentials = new SessionCredentials("secret");
        var profile = new RepositoryProfile { Repository = "repo", ResticExecutable = Environment.ProcessPath! };

        var result = await service.FindNewestAsync(profile, credentials, "probe.txt");

        Equal(1, runner.JsonCalls);
        Equal(1, runner.ItemsDelivered);
        True(runner.StoppedEarly);
        Equal("newest-snapshot", result!.SnapshotId);
        Equal("probe.txt", result.Node.Name);
        True(!runner.LastArguments.Contains("--snapshot"));
    }

    internal static async Task SearchResultLimit()
    {
        var entries = string.Join(',', Enumerable.Range(0, ResticRepositoryService.MaximumSearchMatches + 1)
            .Select(index => $"{{\"snapshot\":\"snapshot\",\"matches\":[{{\"name\":\"{index}.txt\",\"path\":\"/{index}.txt\",\"type\":\"file\"}}]}}"));
        var runner = new JsonRunner($"[{entries}]");
        var service = new ResticRepositoryService(runner);
        using var credentials = new SessionCredentials("secret");
        var profile = new RepositoryProfile { Repository = "repo", ResticExecutable = Environment.ProcessPath! };

        var result = await service.FindAsync(profile, credentials, "snapshot", "*.txt");

        Equal(ResticRepositoryService.MaximumSearchMatches, result.Matches.Count);
        True(result.IsTruncated);
        True(runner.StoppedEarly);
        Equal(10_001, runner.ItemsDelivered);
    }

    internal static async Task OperationRace()
    {
        var repository = new ControlledRepositoryService();
        using var viewModel = CreateConnectedViewModel(repository);
        var oldOperation = viewModel.LoadDirectoryAsync("/alt");
        var newOperation = viewModel.LoadDirectoryAsync("/neu");

        repository.CompleteDirectory("/neu", [new BackupNode { Name = "neu.txt", Path = "/neu/neu.txt", Type = "file" }]);
        await newOperation;
        repository.CompleteDirectory("/alt", [new BackupNode { Name = "alt.txt", Path = "/alt/alt.txt", Type = "file" }]);
        await oldOperation;

        Equal("/neu", viewModel.CurrentPath);
        Equal("neu.txt", viewModel.Nodes.Single().Name);
        True(!viewModel.IsBusy);
    }

    internal static async Task ConnectStateBeforeSnapshotLoad()
    {
        var settingsPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        var repository = new ControlledRepositoryService
        {
            Snapshots = [new SnapshotInfo { Id = "snapshot", Hostname = "host", Time = DateTimeOffset.UtcNow }]
        };
        try
        {
            using var viewModel = new MainViewModel(repository, new SettingsService(settingsPath));
            var connectedNotifications = 0;
            viewModel.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(MainViewModel.IsConnected)) connectedNotifications++;
            };
            var profile = new RepositoryProfile { Repository = "repo", ResticExecutable = Environment.ProcessPath! };
            await viewModel.ConnectAsync(profile, new SessionCredentials("secret"));

            True(viewModel.IsConnected);
            Equal(1, connectedNotifications);
            Equal("snapshot", viewModel.SelectedSnapshot!.Id);
            Equal(0, repository.StatsCalls);
        }
        finally
        {
            if (File.Exists(settingsPath)) File.Delete(settingsPath);
        }
    }

    internal static async Task StaleRepositoryStats()
    {
        var repository = new ControlledRepositoryService();
        using var viewModel = CreateConnectedViewModel(repository);
        var load = viewModel.LoadRepositoryStatsAsync();
        viewModel.Disconnect();
        repository.CompleteStats(new RepositoryStats { TotalFileCount = 123 });
        await load;
        True(viewModel.RepoStats is null);
    }

    internal static MainViewModel CreateConnectedViewModel(ControlledRepositoryService repository)
    {
        var viewModel = new MainViewModel(repository,
            new SettingsService(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json")));
        typeof(MainViewModel).GetField("_activeProfile", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(viewModel, new RepositoryProfile { Repository = "repo", ResticExecutable = Environment.ProcessPath! });
        typeof(MainViewModel).GetField("_credentials", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(viewModel, new SessionCredentials("secret"));
        typeof(MainViewModel).GetField("_selectedSnapshot", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(viewModel, new SnapshotInfo { Id = "snapshot" });
        return viewModel;
    }

    internal static void BatchCollectionReset()
    {
        var collection = new BatchObservableCollection<int>();
        var notifications = 0;
        collection.CollectionChanged += (_, _) => notifications++;
        collection.ReplaceWith(Enumerable.Range(0, 100_000));
        Equal(1, notifications);
        Equal(100_000, collection.Count);
    }

    internal static async Task StorageAnalysisProgressReporting()
    {
        var lines = Enumerable.Range(0, 1_000).Select(index =>
            $"{{\"message_type\":\"node\",\"name\":\"{index}.txt\",\"path\":\"/ordner/{index}.txt\",\"type\":\"file\",\"size\":10}}");
        var service = new ResticRepositoryService(new LineRunner(lines));
        using var credentials = new SessionCredentials("secret");
        var profile = new RepositoryProfile { Repository = "repo", ResticExecutable = Environment.ProcessPath! };
        StorageAnalysisProgress? latest = null;
        var result = await service.AnalyzeSnapshotStorageAsync(profile, credentials, "snapshot",
            new InlineProgress<StorageAnalysisProgress>(value => latest = value));
        Equal(1_000L, latest!.FilesProcessed);
        Equal(10_000L, latest.BytesProcessed);
        Equal(1_000L, result.TotalFileCount);
    }

    internal static async Task StorageAnalysisFolderLimit()
    {
        var lines = Enumerable.Range(0, ResticRepositoryService.MaximumTrackedFolders + 1).Select(index =>
            $"{{\"message_type\":\"node\",\"name\":\"{index}.txt\",\"path\":\"/folder-{index}/file.txt\",\"type\":\"file\",\"size\":1}}");
        var service = new ResticRepositoryService(new LineRunner(lines));
        using var credentials = new SessionCredentials("secret");
        var profile = new RepositoryProfile { Repository = "repo", ResticExecutable = Environment.ProcessPath! };

        var result = await service.AnalyzeSnapshotStorageAsync(profile, credentials, "snapshot");

        Equal(ResticRepositoryService.MaximumTrackedFolders + 1L, result.TotalFileCount);
        True(result.FolderAnalysisIsTruncated);
        True(result.TopFolders.Count <= 15);
    }

    internal static async Task LargeDatasetPerformance()
    {
        using var viewModel = new MainViewModel(new ControlledRepositoryService(),
            new SettingsService(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json")));
        viewModel.Snapshots.ReplaceWith(Enumerable.Range(0, 10_000).Select(index => new SnapshotInfo
        {
            Id = index.ToString("D64"),
            Hostname = $"host-{index % 100}",
            Paths = [$"/daten/{index % 50}"],
            Tags = [$"tag-{index % 20}"],
            Time = DateTimeOffset.UtcNow.AddMinutes(-index)
        }));
        typeof(MainViewModel).GetField("_snapshotFilter", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(viewModel, "host-99");
        var applyFilter = typeof(MainViewModel).GetMethod("ApplySnapshotFilter", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var filterWatch = System.Diagnostics.Stopwatch.StartNew();
        applyFilter.Invoke(viewModel, null);
        filterWatch.Stop();
        Equal(100, viewModel.VisibleSnapshots.Count);

        var lines = Enumerable.Range(0, 100_000).Select(index =>
            $"{{\"message_type\":\"node\",\"name\":\"{index}.bin\",\"path\":\"/daten/{index % 100}/gruppe/{index}.bin\",\"type\":\"file\",\"size\":1024}}");
        var service = new ResticRepositoryService(new LineRunner(lines));
        using var credentials = new SessionCredentials("secret");
        var profile = new RepositoryProfile { Repository = "repo", ResticExecutable = Environment.ProcessPath! };
        var analysisWatch = System.Diagnostics.Stopwatch.StartNew();
        var result = await service.AnalyzeSnapshotStorageAsync(profile, credentials, "snapshot");
        analysisWatch.Stop();
        Equal(100_000L, result.TotalFileCount);
        Console.WriteLine($"      METRIK Filter-10k={filterWatch.ElapsedMilliseconds} ms; Analyse-100k={analysisWatch.ElapsedMilliseconds} ms");
    }

    internal static Task DirectoryCacheBounded()
    {
        using var viewModel = new MainViewModel(new ResticRepositoryService(new FailingRunner()), new SettingsService(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json")));
        var cacheDirectory = typeof(MainViewModel).GetMethod("CacheDirectory", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var cache = typeof(MainViewModel).GetField("_directoryCache", BindingFlags.NonPublic | BindingFlags.Instance)!;
        for (var i = 0; i < 30; i++) cacheDirectory.Invoke(viewModel, [$"snapshot\n/{i}", Array.Empty<BackupNode>()]);
        Equal(24, ((System.Collections.IDictionary)cache.GetValue(viewModel)!).Count);
        return Task.CompletedTask;
    }

    internal static Task DirectoryCacheNodeBounded()
    {
        using var viewModel = new MainViewModel(new ResticRepositoryService(new FailingRunner()),
            new SettingsService(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json")));
        var cacheDirectory = typeof(MainViewModel).GetMethod("CacheDirectory", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var nodeCount = typeof(MainViewModel).GetField("_directoryCacheNodeCount", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var nodes = Enumerable.Range(0, 3_000).Select(index => new BackupNode { Name = index.ToString() }).ToArray();
        for (var index = 0; index < 24; index++) cacheDirectory.Invoke(viewModel, [$"snapshot\n/{index}", nodes]);
        True((int)nodeCount.GetValue(viewModel)! <= 50_000);
        return Task.CompletedTask;
    }
}
