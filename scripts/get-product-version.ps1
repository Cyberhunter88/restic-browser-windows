param([string]$Path = (Join-Path (Split-Path -Parent $PSScriptRoot) "Directory.Build.props"))

$ErrorActionPreference = "Stop"
$properties = [xml](Get-Content -LiteralPath $Path -Raw)
$nodes = $properties.SelectNodes('/Project/PropertyGroup/ResticBrowserProductVersion')
if ($nodes.Count -ne 1) {
    throw "Die Build-Datei muss genau eine zentrale Produktversion enthalten."
}
$version = $nodes[0].InnerText.Trim()
if ($version -notmatch '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$') {
    throw "Die zentrale Version '$version' ist nicht MAJOR.MINOR.PATCH."
}
return $version
