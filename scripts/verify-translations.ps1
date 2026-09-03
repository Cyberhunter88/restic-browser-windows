$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$sourceRoot = Join-Path $root "src"

$replacementCharacters = @(rg --line-number --no-heading "�" $sourceRoot 2>$null)
if ($replacementCharacters.Count -gt 0) {
    throw "Quelltexte enthalten ungültige Ersatzzeichen: $($replacementCharacters -join '; ')"
}

$catalogs = @(rg --files $sourceRoot | Where-Object { $_ -match '\.(resx|po|pot|strings)$' })
if ($catalogs.Count -eq 0) {
    Write-Host "Keine separaten Übersetzungskataloge vorhanden; die deutschsprachigen UI-Texte liegen im .NET-Quellcode."
    exit 0
}

foreach ($catalog in $catalogs) {
    if ((Get-Item -LiteralPath $catalog).Length -eq 0) {
        throw "Übersetzungskatalog '$catalog' ist leer."
    }
}

Write-Host "Übersetzungskataloge geprüft: $($catalogs.Count)"
