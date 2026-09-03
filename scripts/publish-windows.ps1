param([string]$Configuration = "Release")

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$output = Join-Path $root "dist"
$staging = Join-Path $env:TEMP "restic-browser-publish-windows-$PID"
New-Item -ItemType Directory -Path $staging -Force | Out-Null
try {
    dotnet publish (Join-Path $root "src\ResticBrowser\ResticBrowser.csproj") -c $Configuration -r win-x64 --self-contained true -o $staging
    if ($LASTEXITCODE -ne 0) { throw "Der Windows-Publish ist fehlgeschlagen (Exitcode $LASTEXITCODE)." }

    $stagedExecutable = Join-Path $staging "ResticBrowser.exe"
    if (-not (Test-Path -LiteralPath $stagedExecutable)) { throw "Die Windows-Ausgabe wurde nicht erzeugt." }
    New-Item -ItemType Directory -Path $output -Force | Out-Null
    Copy-Item -LiteralPath $stagedExecutable -Destination (Join-Path $output "ResticBrowser.exe") -Force
    Get-FileHash (Join-Path $output "ResticBrowser.exe") -Algorithm SHA256
} finally {
    Remove-Item -LiteralPath $staging -Recurse -Force -ErrorAction SilentlyContinue
}
