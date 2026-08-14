param([string]$Configuration = "Release")

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$output = Join-Path $root "dist"
dotnet publish (Join-Path $root "src\ResticBrowser\ResticBrowser.csproj") -c $Configuration -r win-x64 --self-contained true -o $output
if ($LASTEXITCODE -ne 0) { throw "Der Windows-Publish ist fehlgeschlagen (Exitcode $LASTEXITCODE)." }
if (-not (Test-Path (Join-Path $output "ResticBrowser.exe"))) { throw "Die Windows-Ausgabe wurde nicht erzeugt." }
Get-FileHash (Join-Path $output "ResticBrowser.exe") -Algorithm SHA256
