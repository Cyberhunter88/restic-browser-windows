<#
.SYNOPSIS
    Baut die Windows-Version der Rust-Anwendung (Windows-only) mit eingebetteter, geprüfter restic.exe.
.DESCRIPTION
    Erzeugt in dist-rust/: ResticBrowser.exe, ResticBrowserWindows-<version>-win-x64.zip, THIRD-PARTY.md, SHA256SUMS.txt.
    Benötigt GnuPG (Signaturprüfung durch scripts/prepare-restic.ps1) und Python 3 (Drittkomponenten-Liste).
#>
[CmdletBinding()]
param([string]$Version)

$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$rust = Join-Path $repo "rust"
$dist = Join-Path $repo "dist-rust"
if (-not $Version) { $Version = (& (Join-Path $repo "scripts/get-product-version.ps1")).Trim() }
New-Item -ItemType Directory -Force -Path $dist | Out-Null
$restic = Join-Path $dist "restic-embedded.exe"
& (Join-Path $repo "scripts/prepare-restic.ps1") -Platform windows -Destination $restic
if (-not (Test-Path -LiteralPath $restic)) { throw "Die geprüfte restic.exe wurde nicht bereitgestellt." }

Push-Location $rust
try {
    $env:RESTIC_BROWSER_EMBED_RESTIC = $restic
    cargo build --release --locked -p restic-ui
    if ($LASTEXITCODE -ne 0) { throw "Der Rust-Build ist fehlgeschlagen (Exitcode $LASTEXITCODE)." }
    python scripts/third-party.py (Join-Path $dist "THIRD-PARTY.md") --target x86_64-pc-windows-msvc
    if ($LASTEXITCODE -ne 0) { throw "Die Liste der Drittkomponenten konnte nicht erzeugt werden." }
}
finally {
    Remove-Item Env:\RESTIC_BROWSER_EMBED_RESTIC -ErrorAction SilentlyContinue
    Pop-Location
}
Remove-Item -LiteralPath $restic -Force

$exe = Join-Path $dist "ResticBrowser.exe"
Copy-Item -LiteralPath (Join-Path $rust "target/release/restic-browser.exe") -Destination $exe -Force
$zip = Join-Path $dist "ResticBrowserWindows-$Version-win-x64.zip"
if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }
Compress-Archive -Path $exe, (Join-Path $repo "LICENSE"), (Join-Path $repo "README.md"), (Join-Path $dist "THIRD-PARTY.md") -DestinationPath $zip
$sums = Get-ChildItem -LiteralPath $dist -File | Where-Object { $_.Name -in @("ResticBrowser.exe", (Split-Path -Leaf $zip), "THIRD-PARTY.md") } |
    ForEach-Object { "{0}  {1}" -f (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.Name }
$sums | Set-Content -LiteralPath (Join-Path $dist "SHA256SUMS.txt") -Encoding ascii
$sums
