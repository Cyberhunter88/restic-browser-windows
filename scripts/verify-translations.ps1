$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$sourceRoot = Join-Path $root "src"

$textExtensions = @(".cs", ".axaml", ".csproj", ".props", ".targets", ".resx", ".po", ".pot", ".strings")
$sourceFiles = @(Get-ChildItem -LiteralPath $sourceRoot -Recurse -File | Where-Object {
    $textExtensions -contains $_.Extension -and $_.FullName -notmatch "[\\/]bin[\\/]" -and $_.FullName -notmatch "[\\/]obj[\\/]"
})

$replacementCharacters = @($sourceFiles | Where-Object {
    [System.IO.File]::ReadAllText($_.FullName).IndexOf([char]0xfffd) -ge 0
})
if ($replacementCharacters.Count -gt 0) {
    throw "Quelltexte enthalten ungültige Ersatzzeichen: $($replacementCharacters.FullName -join '; ')"
}

$catalogs = @($sourceFiles | Where-Object { $_.Extension -in @(".resx", ".po", ".pot", ".strings") })
if ($catalogs.Count -eq 0) {
    Write-Host "Keine separaten Übersetzungskataloge vorhanden; die deutschsprachigen UI-Texte liegen im .NET-Quellcode."
    exit 0
}

foreach ($catalog in $catalogs) {
    if ($catalog.Length -eq 0) {
        throw "Übersetzungskatalog '$($catalog.FullName)' ist leer."
    }
}

Write-Host "Übersetzungskataloge geprüft: $($catalogs.Count)"
