<#
.SYNOPSIS
    Baut die Windows-Version der Rust-Anwendung (Windows-only) mit eingebetteter, geprüfter restic.exe.
.DESCRIPTION
    Erzeugt in dist-rust/: ResticBrowser.exe, ResticBrowserWindows-<version>-win-x64.zip, THIRD-PARTY.md, SHA256SUMS.txt.
    Mit -Installer zusätzlich ResticBrowser-Setup.exe (benötigt Inno Setup 6).
    Benötigt GnuPG (Signaturprüfung durch scripts/prepare-restic.ps1) und Python 3 (Drittkomponenten-Liste).
#>
[CmdletBinding()]
param([string]$Version, [switch]$Installer)

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
    cargo build --profile dist --locked -p restic-ui
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
Copy-Item -LiteralPath (Join-Path $rust "target/dist/restic-browser.exe") -Destination $exe -Force
$zip = Join-Path $dist "ResticBrowserWindows-$Version-win-x64.zip"
if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }
Compress-Archive -Path $exe, (Join-Path $repo "LICENSE"), (Join-Path $repo "README.md"), (Join-Path $dist "THIRD-PARTY.md") -DestinationPath $zip
if ($Installer) {
    $iscc = @((Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe"), (Join-Path $env:ProgramFiles "Inno Setup 6\ISCC.exe")) |
        Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    if (-not $iscc) { $iscc = (Get-Command ISCC.exe -ErrorAction Stop).Source }
    & $iscc "/DMyAppVersion=$Version" (Join-Path $repo "installer/windows/ResticBrowser.iss")
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath (Join-Path $dist "ResticBrowser-Setup.exe"))) {
        throw "Der Installer ResticBrowser-Setup.exe wurde nicht erstellt."
    }
}
$names = @("ResticBrowser.exe", (Split-Path -Leaf $zip), "THIRD-PARTY.md")
if ($Installer) { $names += "ResticBrowser-Setup.exe" }
$sums = Get-ChildItem -LiteralPath $dist -File | Where-Object { $_.Name -in $names } |
    ForEach-Object { "{0}  {1}" -f (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.Name }
$sums | Set-Content -LiteralPath (Join-Path $dist "SHA256SUMS.txt") -Encoding ascii
$sums
