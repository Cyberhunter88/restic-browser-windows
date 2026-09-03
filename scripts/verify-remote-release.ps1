param(
    [Parameter(Mandatory = $true)]
    [string]$Tag,

    [Parameter(Mandatory = $true)]
    [string]$Directory,

    [switch]$RequireDraft,
    [switch]$RequirePublished
)

$ErrorActionPreference = "Stop"
$directoryPath = (Resolve-Path -LiteralPath $Directory).Path
$localFiles = @(Get-ChildItem -LiteralPath $directoryPath -File | Sort-Object Name)
$downloadDirectory = Join-Path $env:TEMP "restic-browser-remote-release-$PID"

$json = @(& gh release view $Tag --repo $env:GITHUB_REPOSITORY --json isDraft,isPrerelease,assets 2>&1)
if ($LASTEXITCODE -ne 0) {
    throw "Der Release '$Tag' konnte remote nicht gelesen werden: $($json -join ' ')"
}
$release = ($json -join "`n") | ConvertFrom-Json

if ($RequireDraft -and -not $release.isDraft) {
    throw "Der Release '$Tag' ist vor der Artefaktprüfung bereits veröffentlicht."
}
if ($RequirePublished -and $release.isDraft) {
    throw "Der Release '$Tag' ist nach der Veröffentlichung noch ein Draft."
}
if ($release.isPrerelease) {
    throw "Der Release '$Tag' ist unerwartet als Pre-Release markiert."
}

$remoteAssets = @($release.assets | Sort-Object name)
$localNames = @($localFiles | ForEach-Object { $_.Name })
$remoteNames = @($remoteAssets | ForEach-Object { $_.name })
if (($localNames -join "`n") -ne ($remoteNames -join "`n")) {
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
    $downloadOutput = @(& gh release download $Tag --repo $env:GITHUB_REPOSITORY --dir $downloadDirectory --clobber 2>&1)
    if ($LASTEXITCODE -ne 0) {
        throw "Die Remote-Artefakte für '$Tag' konnten nicht erneut heruntergeladen werden: $($downloadOutput -join ' ')"
    }

    $downloadedFiles = @(Get-ChildItem -LiteralPath $downloadDirectory -File | Sort-Object Name)
    $downloadedNames = @($downloadedFiles | ForEach-Object { $_.Name })
    if (($localNames -join "`n") -ne ($downloadedNames -join "`n")) {
        throw "Die erneut heruntergeladenen Artefakte stimmen nicht exakt mit dem lokalen Manifest überein."
    }

    foreach ($localFile in $localFiles) {
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

Write-Host "Remote-Release '$Tag' enthält exakt die geprüften Artefakte. Draft=$($release.isDraft)"
