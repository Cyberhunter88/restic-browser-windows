param(
    [switch]$RequireInstaller
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$configuration = "Release"
$version = (Get-Content -LiteralPath (Join-Path $root "version.txt") -Raw).Trim()
$releaseAssets = Join-Path $env:TEMP "restic-browser-release-verify-$PID"
$windowsPackage = Join-Path $env:TEMP "restic-browser-windows-package-$PID"

function Invoke-Checked([string]$File, [string[]]$Arguments) {
    Write-Host "> $File $($Arguments -join ' ')"
    & $File @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Befehl '$File' ist mit Exitcode $LASTEXITCODE fehlgeschlagen."
    }
}

New-Item -ItemType Directory -Path $releaseAssets -Force | Out-Null
New-Item -ItemType Directory -Path $windowsPackage -Force | Out-Null
try {
    Push-Location $root
    Invoke-Checked "dotnet" @("restore", "ResticBrowser.slnx")
    Invoke-Checked "dotnet" @("format", "--verify-no-changes")
    Invoke-Checked "dotnet" @("list", "ResticBrowser.slnx", "package", "--vulnerable", "--include-transitive")
    Invoke-Checked "pwsh" @("-NoProfile", "-File", (Join-Path $root "scripts\verify-version.ps1"))
    Invoke-Checked "pwsh" @("-NoProfile", "-File", (Join-Path $root "scripts\verify-translations.ps1"))
    Invoke-Checked "dotnet" @("build", "ResticBrowser.slnx", "-c", $configuration, "--no-restore")
    Invoke-Checked "dotnet" @("run", "--project", "tests/ResticBrowser.Tests/ResticBrowser.Tests.csproj", "-c", $configuration, "--no-build")

    Invoke-Checked "pwsh" @("-NoProfile", "-File", (Join-Path $root "scripts\publish-windows.ps1"), "-Configuration", $configuration)
    $installerPath = Join-Path $root "dist\ResticBrowser-Setup.exe"
    if (Test-Path -LiteralPath $installerPath) {
        Copy-Item -LiteralPath (Join-Path $root "dist\ResticBrowser.exe") -Destination $releaseAssets
        Copy-Item -LiteralPath $installerPath -Destination $releaseAssets
    } elseif ($RequireInstaller) {
        throw "Inno Setup wurde nicht gefunden; der Installer ist aber erforderlich."
    } else {
        Write-Warning "Inno Setup wurde nicht gefunden; der lokale Check prüft nur die portable Windows-Ausgabe."
        Copy-Item -LiteralPath (Join-Path $root "dist\ResticBrowser.exe") -Destination $releaseAssets
    }

    Copy-Item -LiteralPath (Join-Path $root "dist\ResticBrowser.exe") -Destination $windowsPackage
    Copy-Item -LiteralPath (Join-Path $root "LICENSE"), (Join-Path $root "README.md") -Destination $windowsPackage
    $zipPath = Join-Path $releaseAssets "ResticBrowserWindows-$version-win-x64.zip"
    Compress-Archive -Path (Join-Path $windowsPackage "*") -DestinationPath $zipPath -CompressionLevel Optimal

    $artifactArgs = @("-NoProfile", "-File", (Join-Path $root "scripts\verify-release-artifacts.ps1"), "-Directory", $releaseAssets, "-Version", $version, "-Platform", "Windows")
    if ($RequireInstaller) { $artifactArgs += "-RequireInstaller" }
    Invoke-Checked "pwsh" $artifactArgs
    Write-Host "Lokaler Release-Check erfolgreich für Version $version."
} finally {
    Pop-Location
    Remove-Item -LiteralPath $releaseAssets, $windowsPackage -Recurse -Force -ErrorAction SilentlyContinue
}
