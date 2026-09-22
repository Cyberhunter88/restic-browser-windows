param(
    [Parameter(Mandatory = $true)]
    [string]$Tag,

    [Parameter(Mandatory = $true)]
    [string]$Directory,

    [switch]$RequirePublished,
    [switch]$AllowMissingRelease,
    [switch]$AllowMissingAssets,
    [string]$GhCommand = "gh"
)

$ErrorActionPreference = "Stop"
$directoryPath = (Resolve-Path -LiteralPath $Directory).Path
$localFiles = @(Get-ChildItem -LiteralPath $directoryPath -File | Sort-Object Name)
$tempRoot = if (-not [string]::IsNullOrWhiteSpace($env:RUNNER_TEMP)) {
    $env:RUNNER_TEMP
} else {
    [System.IO.Path]::GetTempPath()
}
$downloadDirectory = Join-Path $tempRoot "restic-browser-remote-release-$PID"

$json = @(& $GhCommand release view $Tag --repo $env:GITHUB_REPOSITORY --json tagName,isDraft,isPrerelease,assets 2>&1)
if ($LASTEXITCODE -ne 0) {
    if ($AllowMissingRelease -and (($json -join "`n") -match '(?i)release not found|could not find release')) {
        Write-Host "Für '$Tag' existiert noch kein Release; die Vorabprüfung ist erfüllt."
        exit 0
    }
    throw "Der Release '$Tag' konnte remote nicht gelesen werden: $($json -join ' ')"
}
$release = ($json -join "`n") | ConvertFrom-Json

if ($RequirePublished -and $release.isDraft) {
    throw "Der Release '$Tag' ist nach der Veröffentlichung noch ein Draft."
}
if ($release.isPrerelease) {
    throw "Der Release '$Tag' ist unerwartet als Pre-Release markiert."
}
if ($release.tagName -ne $Tag) {
    throw "Der Remote-Release gehört zum Tag '$($release.tagName)' statt zu '$Tag'."
}

$remoteAssets = @($release.assets | Sort-Object name)
$localNames = @($localFiles | ForEach-Object { $_.Name })
$remoteNames = @($remoteAssets | ForEach-Object { $_.name })
if ($AllowMissingAssets) {
    $unexpectedNames = @($remoteNames | Where-Object { $_ -notin $localNames })
    if ($unexpectedNames.Count -gt 0) {
        throw "Der vorhandene Release enthält nicht erwartete Artefakte: $($unexpectedNames -join ', ')"
    }
} elseif (($localNames -join "`n") -ne ($remoteNames -join "`n")) {
    throw "Remote-Artefakte stimmen nicht exakt mit den lokalen Artefakten überein. Lokal: $($localNames -join ', '); remote: $($remoteNames -join ', ')"
}

foreach ($localFile in $localFiles) {
    $remoteAsset = $remoteAssets | Where-Object { $_.name -eq $localFile.Name } | Select-Object -First 1
    if ($null -eq $remoteAsset -or [int64]$remoteAsset.size -ne [int64]$localFile.Length) {
        throw "Größe des Remote-Artefakts '$($localFile.Name)' stimmt nicht mit der lokalen Datei überein."
    }
}

New-Item -ItemType Directory -Path $downloadDirectory -Force | Out-Null
try {
    $downloadOutput = @(& $GhCommand release download $Tag --repo $env:GITHUB_REPOSITORY --dir $downloadDirectory --clobber 2>&1)
    if ($LASTEXITCODE -ne 0) {
        throw "Die Remote-Artefakte für '$Tag' konnten nicht erneut heruntergeladen werden: $($downloadOutput -join ' ')"
    }

    $downloadedFiles = @(Get-ChildItem -LiteralPath $downloadDirectory -File | Sort-Object Name)
    $downloadedNames = @($downloadedFiles | ForEach-Object { $_.Name })
    if ($AllowMissingAssets) {
        $unexpectedDownloadedNames = @($downloadedNames | Where-Object { $_ -notin $remoteNames })
        if ($unexpectedDownloadedNames.Count -gt 0) {
            throw "Der Download enthält nicht erwartete Remote-Artefakte: $($unexpectedDownloadedNames -join ', ')"
        }
    } elseif (($localNames -join "`n") -ne ($downloadedNames -join "`n")) {
        throw "Die erneut heruntergeladenen Artefakte stimmen nicht exakt mit dem lokalen Manifest überein."
    }

    foreach ($localFile in $localFiles | Where-Object { $_.Name -in $remoteNames }) {
        $downloadedFile = Join-Path $downloadDirectory $localFile.Name
        $localHash = (Get-FileHash -LiteralPath $localFile.FullName -Algorithm SHA256).Hash
        $remoteHash = (Get-FileHash -LiteralPath $downloadedFile -Algorithm SHA256).Hash
        if ($localHash -ne $remoteHash) {
            throw "SHA-256 des erneut heruntergeladenen Artefakts '$($localFile.Name)' stimmt nicht mit dem lokalen Build überein."
        }
    }
} finally {
    Remove-Item -LiteralPath $downloadDirectory -Recurse -Force -ErrorAction SilentlyContinue
}

if ($AllowMissingAssets) {
    Write-Host "Vorhandene Artefakte des Remote-Release '$Tag' stimmen mit dem lokalen Build überein. Draft=$($release.isDraft)"
} else {
    Write-Host "Remote-Release '$Tag' enthält exakt die geprüften Artefakte. Draft=$($release.isDraft)"
}
