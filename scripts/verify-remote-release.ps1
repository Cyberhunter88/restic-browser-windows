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

Write-Host "Remote-Release '$Tag' enthält exakt die geprüften Artefakte. Draft=$($release.isDraft)"
