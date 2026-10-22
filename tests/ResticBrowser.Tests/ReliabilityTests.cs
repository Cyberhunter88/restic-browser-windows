using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Json;
using ResticBrowser.Models;
using ResticBrowser.Services;

internal static partial class TestSuite
{
    internal static async Task SettingsFailuresPreserveOriginal()
    {
        var root = Path.Combine(Path.GetTempPath(), "ResticBrowserSettings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "settings.json");
            var service = new SettingsService(path);
            Equal(0, (await service.LoadSettingsAsync()).Profiles.Count);
            foreach (var invalid in new[] { "{broken", "null", "{\"Profiles\":[null]}", "{\"Profiles\":[{\"Repository\":null}]}" })
            {
                await File.WriteAllTextAsync(path, invalid);
                await ExpectSettingsFailure(() => service.LoadSettingsAsync());
                await ExpectSettingsFailure(() => service.SaveSettingsAsync(new AppSettings()));
                Equal(invalid, await File.ReadAllTextAsync(path));
                Equal(0, Directory.GetFiles(root, "*.tmp").Length);
            }
            File.Delete(path);
            Directory.CreateDirectory(path);
            await ExpectSettingsFailure(() => service.LoadSettingsAsync());
            await ExpectSettingsFailure(() => service.SaveSettingsAsync(new AppSettings()));
            Directory.Delete(path);
            await service.SaveSettingsAsync(new AppSettings { Profiles = [new RepositoryProfile { Name = "Erhalten", Repository = "repo" }] });
            var original = await File.ReadAllTextAsync(path);
            using (var blocked = new FileStream(path + ".lock", FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                var watch = Stopwatch.StartNew();
                await ExpectSettingsFailure(() => service.SaveSettingsAsync(new AppSettings()));
                True(watch.Elapsed >= TimeSpan.FromSeconds(4));
                True(watch.Elapsed < TimeSpan.FromSeconds(8));
                Equal(original, await File.ReadAllTextAsync(path));
            }
            // Ein fehlgeschlagener Austausch muss seine eigene temporäre Datei beseitigen.
            File.Delete(path);
            Directory.CreateDirectory(path);
            await ExpectSettingsFailure(() => service.SaveSettingsAsync(new AppSettings()));
            Equal(0, Directory.GetFiles(root, "*.tmp").Length);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    internal static async Task SettingsConcurrentWrites()
    {
        var root = Path.Combine(Path.GetTempPath(), "ResticBrowserConcurrent-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "settings.json");
            var first = new SettingsService(path);
            var second = new SettingsService(path);
            await Task.WhenAll(Enumerable.Range(0, 40).Select(i => (i % 2 == 0 ? first : second).SaveSettingsAsync(
                new AppSettings { Profiles = [new RepositoryProfile { Name = $"Profil {i}", Repository = new string('x', 4096) }] })));
            var saved = await new SettingsService(path).LoadSettingsAsync();
            Equal(1, saved.Profiles.Count);
            Equal(4096, saved.Profiles[0].Repository.Length);
            Equal(0, Directory.GetFiles(root, "*.tmp").Length);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static async Task ExpectSettingsFailure(Func<Task> action)
    {
        try { await action(); }
        catch (SettingsException) { return; }
        throw new Exception("Erwarteter Profilfehler fehlt.");
    }

    internal static async Task RunnerFailuresStopProcesses()
    {
        var runner = new ResticProcessRunner();
        var callbackError = new InvalidOperationException("Testfehler im Callback");
        await WithRunnerFixture("lines", async (command, token) =>
        {
            try { await runner.RunLinesAsync(command, _ => throw callbackError, token); }
            catch (InvalidOperationException ex) when (ReferenceEquals(ex, callbackError)) { return; }
            throw new Exception("Ursprünglicher Callback-Fehler fehlt.");
        });
        await WithRunnerFixture("array", async (command, token) =>
        {
            try { await runner.RunJsonArrayAsync<JsonElement>(command, _ => throw callbackError, cancellationToken: token); }
            catch (InvalidOperationException ex) when (ReferenceEquals(ex, callbackError)) { return; }
            throw new Exception("Ursprünglicher Array-Callback-Fehler fehlt.");
        });
        await WithRunnerFixture("find", async (command, token) =>
        {
            try { await runner.RunFindMatchesAsync(command, _ => throw callbackError, cancellationToken: token); }
            catch (InvalidOperationException ex) when (ReferenceEquals(ex, callbackError)) { return; }
            throw new Exception("Ursprünglicher Such-Callback-Fehler fehlt.");
        });
        foreach (var mode in new[] { "json", "array", "find" })
        {
            await WithRunnerFixture("invalid", async (command, token) =>
            {
                try
                {
                    if (mode == "json") await runner.RunJsonAsync<JsonElement>(command, cancellationToken: token);
                    else if (mode == "array") await runner.RunJsonArrayAsync<JsonElement>(command, _ => Task.CompletedTask, cancellationToken: token);
                    else await runner.RunFindMatchesAsync(command, _ => Task.CompletedTask, cancellationToken: token);
                }
                catch (JsonException) { return; }
                catch (ResticException ex) when (mode == "find" && ex.InnerException is JsonException) { return; }
                throw new Exception("Ungültiges JSON wurde akzeptiert.");
            });
        }
        await WithRunnerFixture("lines", async (command, token) =>
        {
            using var cancel = CancellationTokenSource.CreateLinkedTokenSource(token);
            try { await runner.RunLinesAsync(command, _ => { cancel.Cancel(); return Task.CompletedTask; }, cancel.Token); }
            catch (OperationCanceledException) when (cancel.IsCancellationRequested) { return; }
            throw new Exception("Benutzerabbruch wurde nicht gemeldet.");
        });
        foreach (var mode in new[] { "array", "find" })
        {
            await WithRunnerFixture(mode, async (command, token) =>
            {
                var result = mode == "array"
                    ? await runner.RunJsonArrayUntilAsync<JsonElement>(command, _ => Task.FromResult(true), cancellationToken: token)
                    : await runner.RunFindMatchesUntilAsync(command, _ => Task.FromResult(true), cancellationToken: token);
                True(result.StoppedEarly);
            });
        }
        await WithRunnerFixture("failed", async (command, token) =>
        {
            var result = await runner.RunJsonAsync<JsonElement>(command, cancellationToken: token);
            Equal(42, result.ExitCode);
            True(result.StandardError.Contains("regulären Fehler"));
        });
        await WithRunnerFixture("settings-lock", async (command, token) =>
        {
            var pidPath = command.Arguments[^1];
            var settingsPath = pidPath + ".settings";
            try
            {
                await runner.RunLinesAsync(command, async _ =>
                {
                    await ExpectSettingsFailure(() => new SettingsService(settingsPath).SaveSettingsAsync(new AppSettings()));
                    True(!File.Exists(settingsPath));
                    throw callbackError;
                }, token);
            }
            catch (InvalidOperationException ex) when (ReferenceEquals(ex, callbackError)) { }
            finally { File.Delete(settingsPath + ".lock"); }
        });
        await WithRunnerFixture("lines", async (command, token) =>
        {
            try { await runner.RunBinaryAsync(command, 16, token); }
            catch (ResticException) { return; }
            throw new Exception("Binärlimit wurde nicht angewendet.");
        });
    }

    private static async Task WithRunnerFixture(string mode, Func<ResticCommand, CancellationToken, Task> action)
    {
        var pidPath = Path.Combine(Path.GetTempPath(), "restic-runner-" + Guid.NewGuid().ToString("N"));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var executable = Environment.ProcessPath!;
        var arguments = new List<string>();
        if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            arguments.Add(Assembly.GetExecutingAssembly().Location);
        arguments.AddRange(["--runner-fixture", mode, pidPath]);
        try
        {
            await action(new ResticCommand(executable, arguments), timeout.Token);
            True(!timeout.IsCancellationRequested);
            var pid = int.Parse(await File.ReadAllTextAsync(pidPath));
            try { using var process = Process.GetProcessById(pid); True(process.HasExited); }
            catch (ArgumentException) { /* Prozess wurde bereits vollständig entfernt. */ }
        }
        finally
        {
            timeout.Cancel();
            if (File.Exists(pidPath))
            {
                var pid = int.Parse(await File.ReadAllTextAsync(pidPath));
                try
                {
                    using var process = Process.GetProcessById(pid);
                    if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
                }
                catch (ArgumentException) { }
                File.Delete(pidPath);
            }
        }
    }

    internal static async Task CheckDetailsAreBoundedAndStreamed()
    {
        static IEnumerable<string> Lines()
        {
            yield return "Keine JSON-Meldung";
            yield return "[]";
            yield return "{\"message_type\":\"future\",\"num_errors\":\"unknown\"}";
            for (var i = 0; i < 1000; i++) yield return new string('ä', 200) + "😀";
            yield return "{\"num_errors\":3,\"suggest_repair_index\":true,\"suggest_prune\":true,\"extra\":1}";
        }
        var runner = new CheckLineRunner(Lines(), 1, "Zusätzliche Fehlerausgabe");
        var service = new ResticRepositoryService(runner);
        using var credentials = new SessionCredentials("test-only");
        var profile = new RepositoryProfile { Repository = "repo", ResticExecutable = Environment.ProcessPath };
        var result = await service.CheckAsync(profile, credentials, CheckMode.Full);
        Equal(1004, runner.LinesDelivered);
        Equal(3, result.ErrorCount);
        True(result.SuggestRepairIndex && result.SuggestPrune && !result.IsHealthy);
        True(Encoding.UTF8.GetByteCount(result.Details) <= BoundedCheckDetails.MaximumBytes);
        True(result.Details.Contains("gekürzt"));
        True(result.DetailsTruncated);
        var healthy = await new ResticRepositoryService(new CheckLineRunner(["{\"num_errors\":0}"], 0, ""))
            .CheckAsync(profile, credentials, CheckMode.Quick);
        True(healthy.IsHealthy);
        True(!healthy.Details.Contains("gekürzt"));
        True(!healthy.DetailsTruncated);
        try
        {
            await new ResticRepositoryService(new CheckLineRunner(["Fehler bei Prüfung"], 2, "Zugriff verweigert"))
                .CheckAsync(profile, credentials, CheckMode.Quick);
        }
        catch (ResticException ex) { Equal(2, ex.ExitCode); return; }
        throw new Exception("Fehlerhafter Exitcode wurde akzeptiert.");
    }
}

sealed class CheckLineRunner(IEnumerable<string> lines, int exitCode, string stderr) : IResticProcessRunner
{
    public int LinesDelivered { get; private set; }
    public async Task<ResticProcessResult> RunLinesAsync(ResticCommand command, Func<string, Task> onOutputLine,
        CancellationToken cancellationToken = default)
    {
        foreach (var line in lines)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await onOutputLine(line);
            LinesDelivered++;
        }
        return new ResticProcessResult(exitCode, "", stderr);
    }
    public Task<ResticProcessResult> RunAsync(ResticCommand command, Func<string, Task>? onOutputLine = null,
        CancellationToken cancellationToken = default) => throw new Exception("Prüfausgabe muss gestreamt werden.");
    public Task<ResticBinaryProcessResult> RunBinaryAsync(ResticCommand command, int maximumOutputBytes,
        CancellationToken cancellationToken = default) => throw new NotSupportedException();
}
