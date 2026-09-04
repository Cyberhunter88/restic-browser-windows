param([string]$Configuration = "Release")

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$csprojPath = Join-Path $root "src\ResticBrowser\ResticBrowser.csproj"

# 1. Version aus csproj auslesen
$csproj = [xml](Get-Content $csprojPath)
$versionNode = $csproj.SelectSingleNode("/Project/PropertyGroup/Version")
$version = if ($null -eq $versionNode) { "" } else { $versionNode.InnerText.Trim() }
if ([string]::IsNullOrWhiteSpace($version)) {
    throw "Konnte Version nicht aus $csprojPath auslesen."
}

# 2. Windows Executable in dist/ erstellen
$publishScript = Join-Path $PSScriptRoot "publish-windows.ps1"
& $publishScript -Configuration $Configuration
if ($LASTEXITCODE -ne 0) { throw "Der portable Windows-Build ist fehlgeschlagen (Exitcode $LASTEXITCODE)." }

$outputDir = Join-Path $root "dist"
$exePath = Join-Path $outputDir "ResticBrowser.exe"
if (-not (Test-Path $exePath)) {
    throw "Die Windows-Ausgabe '$exePath' existiert nicht."
}

# 3. ISCC.exe (Inno Setup Compiler) suchen
$isccPath = $null

$possiblePaths = @(
    "$(Get-Command ISCC.exe -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source)",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles}\Inno Setup 6\ISCC.exe",
    (Join-Path $root "tools\InnoSetup\ISCC.exe")
)

foreach ($path in $possiblePaths) {
    if (-not [string]::IsNullOrWhiteSpace($path) -and (Test-Path $path)) {
        $isccPath = $path
        break
    }
}

if (-not $isccPath) {
    Write-Warning "Inno Setup Compiler (ISCC.exe) wurde nicht gefunden."
    Write-Host "Inno Setup kann beispielsweise per WinGet installiert werden: winget install JRSoftware.InnoSetup"
    Write-Host "Die portable Version 'dist\ResticBrowser.exe' wurde erfolgreich erstellt."
    exit 0
}

# 4. Installer bauen
$issPath = Join-Path $root "installer\windows\ResticBrowser.iss"
Write-Host "Erstelle Windows Installer für Version $version mit Inno Setup ($isccPath)..."

& $isccPath "/DMyAppVersion=$version" $issPath
if ($LASTEXITCODE -ne 0) { throw "Der Windows-Installer-Build ist fehlgeschlagen (Exitcode $LASTEXITCODE)." }

$setupExePath = Join-Path $outputDir "ResticBrowser-Setup.exe"
if (-not (Test-Path $setupExePath)) {
    throw "Die Windows-Installer-Datei '$setupExePath' wurde nicht erzeugt."
}

Write-Host "Windows Installer wurde erfolgreich erstellt: $setupExePath"
Get-FileHash $setupExePath -Algorithm SHA256
