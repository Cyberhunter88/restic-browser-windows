param([string]$Path = (Join-Path (Split-Path -Parent $PSScriptRoot) "rust/Cargo.toml"))

$ErrorActionPreference = "Stop"
$text = Get-Content -LiteralPath $Path -Raw
$section = [regex]::Match($text, '(?ms)^\[workspace\.package\]\s*\r?\n(.*?)(?=^\[|\z)')
if (-not $section.Success) {
    throw "Der Abschnitt [workspace.package] fehlt in '$Path'."
}
$found = [regex]::Matches($section.Groups[1].Value, '(?m)^version\s*=\s*"([^"]*)"\s*$')
if ($found.Count -ne 1) {
    throw "Die Cargo-Datei muss genau eine zentrale Produktversion enthalten."
}
$version = $found[0].Groups[1].Value
if ($version -notmatch '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$') {
    throw "Die zentrale Version '$version' ist nicht MAJOR.MINOR.PATCH."
}
return $version
