using System.Diagnostics;
using System.Text.Json;

internal static partial class TestSuite
{
    internal static async Task ReleasePreflightScenarios()
    {
        var root = Path.Combine(Path.GetTempPath(), $"restic-browser-release-test-{Guid.NewGuid():N}");
        var localDirectory = Path.Combine(root, "local");
        var fakeDirectory = Path.Combine(root, "fake-gh");
        Directory.CreateDirectory(localDirectory);
        Directory.CreateDirectory(fakeDirectory);

        var localAsset = Path.Combine(localDirectory, "ResticBrowser.exe");
        var remoteAsset = Path.Combine(root, "remote-asset");
        var releaseJson = Path.Combine(root, "release.json");
        await File.WriteAllTextAsync(localAsset, "local");
        await File.WriteAllTextAsync(remoteAsset, "local");
        await File.WriteAllTextAsync(releaseJson, JsonSerializer.Serialize(new
        {
            tagName = "v0.3.10",
            isDraft = true,
            isPrerelease = false,
            assets = new[] { new { name = "ResticBrowser.exe", size = 5 } }
        }));

        try
        {
            var fakeGh = await CreateFakeGhAsync(fakeDirectory);
            var script = Path.Combine(Directory.GetCurrentDirectory(), "scripts", "verify-remote-release.ps1");
            True(File.Exists(script));

            var missing = await RunReleasePreflightAsync(script, localDirectory, fakeGh, releaseJson, remoteAsset, "missing");
            if (missing.ExitCode != 0) throw new Exception($"Die Prüfung ohne Release ist fehlgeschlagen: {missing.Output}");
            var matching = await RunReleasePreflightAsync(script, localDirectory, fakeGh, releaseJson, remoteAsset, "present");
            if (matching.ExitCode != 0) throw new Exception($"Die Prüfung mit passenden Assets ist fehlgeschlagen: {matching.Output}");

            await File.WriteAllTextAsync(remoteAsset, "other");
            var mismatch = await RunReleasePreflightAsync(script, localDirectory, fakeGh, releaseJson, remoteAsset, "present");
            True(mismatch.ExitCode != 0);
        }
        finally
        {
            if (Directory.Exists(root) && Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase))
                Directory.Delete(root, recursive: true);
        }
    }

    private static async Task<string> CreateFakeGhAsync(string directory)
    {
        if (OperatingSystem.IsWindows())
        {
            var path = Path.Combine(directory, "gh.cmd");
            await File.WriteAllTextAsync(path, """
@echo off
if /I "%2"=="view" goto view
if /I not "%2"=="download" exit /b 1
set target=
:next
if "%~1"=="" goto copy
if /I "%~1"=="--dir" (set target=%~2& shift& shift& goto next)
shift
goto next
:copy
copy /Y "%RESTIC_BROWSER_FAKE_GH_ASSET%" "%target%\ResticBrowser.exe" > nul
exit /b 0
:view
if /I "%RESTIC_BROWSER_FAKE_GH_MODE%"=="missing" goto missing
type "%RESTIC_BROWSER_FAKE_GH_JSON%"
exit /b 0
:missing
echo release not found 1>&2
exit /b 1
""");
            return path;
        }

        var shellPath = Path.Combine(directory, "gh");
        await File.WriteAllTextAsync(shellPath, """
#!/usr/bin/env bash
set -euo pipefail
if [[ "$2" == "view" ]]; then
  if [[ "${RESTIC_BROWSER_FAKE_GH_MODE}" == "missing" ]]; then
    echo "release not found" >&2
    exit 1
  fi
  cat "$RESTIC_BROWSER_FAKE_GH_JSON"
  exit 0
fi
if [[ "$2" != "download" ]]; then exit 1; fi
while [[ $# -gt 0 ]]; do
  if [[ "$1" == "--dir" ]]; then target="$2"; break; fi
  shift
done
cp "$RESTIC_BROWSER_FAKE_GH_ASSET" "$target/ResticBrowser.exe"
""");
        File.SetUnixFileMode(shellPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return shellPath;
    }

    private static async Task<(int ExitCode, string Output)> RunReleasePreflightAsync(string script, string directory, string fakeGh, string releaseJson, string remoteAsset, string mode)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "pwsh",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(script);
        startInfo.ArgumentList.Add("-Tag");
        startInfo.ArgumentList.Add("v0.3.10");
        startInfo.ArgumentList.Add("-Directory");
        startInfo.ArgumentList.Add(directory);
        startInfo.ArgumentList.Add("-AllowMissingRelease");
        startInfo.ArgumentList.Add("-AllowMissingAssets");
        startInfo.ArgumentList.Add("-GhCommand");
        startInfo.ArgumentList.Add(fakeGh);
        startInfo.Environment["GITHUB_REPOSITORY"] = "example/restic-browser";
        startInfo.Environment["RESTIC_BROWSER_FAKE_GH_MODE"] = mode;
        startInfo.Environment["RESTIC_BROWSER_FAKE_GH_JSON"] = releaseJson;
        startInfo.Environment["RESTIC_BROWSER_FAKE_GH_ASSET"] = remoteAsset;
        using var process = new Process { StartInfo = startInfo };
        if (!process.Start()) throw new Exception("Die simulierte GitHub-CLI konnte nicht gestartet werden.");
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        await Task.WhenAll(standardOutput, standardError, process.WaitForExitAsync());
        return (process.ExitCode, $"{standardOutput.Result}{standardError.Result}");
    }
}
