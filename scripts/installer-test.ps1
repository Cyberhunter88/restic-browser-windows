param([string]$Configuration = "Release")

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$output = Join-Path $root "dist"

$exePath = Join-Path $output "ResticBrowser.exe"
if (-not (Test-Path $exePath)) {
    throw "Die veröffentlichte Windows-Anwendung wurde nicht gefunden: $exePath"
}

Write-Host "Windows-Anwendung gefunden: $exePath"
Get-FileHash $exePath -Algorithm SHA256

$setupPath = Join-Path $output "ResticBrowser-Setup.exe"
if (Test-Path $setupPath) {
    Write-Host "Windows-Installer gefunden: $setupPath"
    Get-FileHash $setupPath -Algorithm SHA256
} else {
    Write-Warning "Windows-Installer wurde nicht erzeugt. Möglicherweise ist Inno Setup auf dem Runner nicht installiert."
}
