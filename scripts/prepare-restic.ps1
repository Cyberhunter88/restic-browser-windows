[CmdletBinding()]
param(
    [Parameter(Mandatory)] [ValidateSet("windows", "linux")] [string]$Platform,
    [Parameter(Mandatory)] [string]$Destination
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$manifest = Get-Content -LiteralPath (Join-Path $root "build/restic-manifest.json") -Raw | ConvertFrom-Json
$entry = $manifest.$Platform
$temporary = Join-Path ([System.IO.Path]::GetTempPath()) "restic-browser-prepare-$PID"
if (-not (Get-Command gpg -ErrorAction SilentlyContinue))
{
    throw "GnuPG (gpg) wird für die Signaturprüfung von Restic benötigt. Bitte GnuPG installieren oder die GitHub-Actions-Paketierung verwenden."
}

function Assert-Hash([string]$Path, [string]$Expected, [string]$Description) {
    $actual = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
    if ($actual -ne $Expected) { throw "$Description hat nicht die erwartete SHA-256-Prüfsumme." }
}

try {
    New-Item -ItemType Directory -Path $temporary -Force | Out-Null
    $archive = Join-Path $temporary $entry.archive
    $sums = Join-Path $temporary "SHA256SUMS"
    $signature = Join-Path $temporary "SHA256SUMS.asc"
    Invoke-WebRequest -UseBasicParsing -Uri "$($manifest.releaseUrl)/$($entry.archive)" -OutFile $archive
    Invoke-WebRequest -UseBasicParsing -Uri "$($manifest.releaseUrl)/SHA256SUMS" -OutFile $sums
    Invoke-WebRequest -UseBasicParsing -Uri "$($manifest.releaseUrl)/SHA256SUMS.asc" -OutFile $signature

    $sumLine = Select-String -LiteralPath $sums -Pattern "  $([regex]::Escape($entry.archive))$" | Select-Object -First 1
    if ($null -eq $sumLine) { throw "Die offizielle Prüfsumme für $($entry.archive) fehlt." }
    if ((($sumLine.Line -split '\s+')[0]).ToUpperInvariant() -ne $entry.archiveSha256) { throw "Das Prüfmanifest weicht von der signierten Restic-Prüfsumme ab." }
    Assert-Hash $archive $entry.archiveSha256 "Das Restic-Archiv"

    $gpgHome = Join-Path $temporary "gnupg"
    New-Item -ItemType Directory -Path $gpgHome -Force | Out-Null
    & gpg --batch --homedir $gpgHome --keyserver hkps://keys.openpgp.org --recv-keys $manifest.signingFingerprint
    if ($LASTEXITCODE -ne 0) { throw "Der fest hinterlegte Restic-Signaturschlüssel konnte nicht geladen werden." }
    $fingerprint = (& gpg --batch --homedir $gpgHome --with-colons --fingerprint $manifest.signingFingerprint | Where-Object { $_.StartsWith("fpr:") } | Select-Object -First 1).Split(':')[9]
    if ($fingerprint -ne $manifest.signingFingerprint) { throw "Der geladene Restic-Signaturschlüssel hat einen unerwarteten Fingerprint." }
    & gpg --batch --homedir $gpgHome --verify $signature $sums
    if ($LASTEXITCODE -ne 0) { throw "Die Signatur der offiziellen Restic-Prüfsummen ist ungültig." }

    $extracted = Join-Path $temporary "extracted"
    New-Item -ItemType Directory -Path $extracted -Force | Out-Null
    if ($Platform -eq "windows") { Expand-Archive -LiteralPath $archive -DestinationPath $extracted -Force }
    else {
        & bzip2 -dk $archive
        if ($LASTEXITCODE -ne 0) { throw "Das Linux-Restic-Archiv konnte nicht entpackt werden." }
        Move-Item -LiteralPath ($archive -replace '\.bz2$') -Destination (Join-Path $extracted $entry.binary)
    }
    $binary = Join-Path $extracted $entry.binary
    if (-not (Test-Path -LiteralPath $binary)) { throw "Die erwartete Restic-Datei fehlt im offiziellen Archiv." }
    if ($entry.binarySha256) { Assert-Hash $binary $entry.binarySha256 "Die Restic-Binärdatei" }
    $parent = Split-Path -Parent $Destination
    New-Item -ItemType Directory -Path $parent -Force | Out-Null
    Copy-Item -LiteralPath $binary -Destination $Destination -Force
    if ($Platform -eq "linux") { & chmod 755 $Destination }
}
finally { Remove-Item -LiteralPath $temporary -Recurse -Force -ErrorAction SilentlyContinue }
