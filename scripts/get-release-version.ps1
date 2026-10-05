param(
    [Parameter(Mandatory)][ValidateSet("push", "workflow_dispatch")][string]$EventName,
    [string]$Before
)

$ErrorActionPreference = "Stop"
$version = & (Join-Path $PSScriptRoot "get-product-version.ps1")
$shouldRelease = $EventName -eq "workflow_dispatch"
if (-not $shouldRelease) {
    if ($Before -notmatch '^[0-9a-f]{40}$' -or $Before -eq ('0' * 40)) {
        throw "Der vorherige Commit des Pushs fehlt oder ist ungültig."
    }
    $previousProperties = @(& git show "${Before}:Directory.Build.props")
    if ($LASTEXITCODE -ne 0) { throw "Die vorherige Build-Datei konnte nicht gelesen werden." }
    $previousXml = [xml]($previousProperties -join "`n")
    $nodes = $previousXml.SelectNodes('/Project/PropertyGroup/ResticBrowserProductVersion')
    if ($nodes.Count -ne 1) { throw "Die vorherige Build-Datei enthält keine eindeutige Produktversion." }
    $previousVersion = $nodes[0].InnerText.Trim()
    # Übergang vom bisherigen version.txt-Vertrag; später ausschließlich die Build-Eigenschaft.
    if ($previousVersion -like '*ReadAllText*') {
        $previousVersion = (@(& git show "${Before}:version.txt") -join "`n").Trim()
        if ($LASTEXITCODE -ne 0) { throw "Die bisherige Versionsdatei konnte nicht gelesen werden." }
    }
    if ($previousVersion -notmatch '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$') {
        throw "Die vorherige Produktversion ist ungültig."
    }
    $shouldRelease = $version -ne $previousVersion
}
[pscustomobject]@{ Version = $version; Tag = "v$version"; ShouldRelease = $shouldRelease }
