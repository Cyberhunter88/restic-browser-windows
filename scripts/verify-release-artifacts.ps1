param(
    [Parameter(Mandatory = $true)]
    [string]$Directory,

    [Parameter(Mandatory = $true)]
    [string]$Version,

    [ValidateSet("Windows", "Linux", "All")]
    [string]$Platform = "All",

    [switch]$RequireInstaller,
    [switch]$AllowChecksumFile,

    # FileVersionInfo kann Windows-PE-Versionen unter Linux nicht zuverlässig
    # auslesen. Der Windows-Build prüft die Dateiversion bereits auf Windows;
    # der plattformübergreifende Sammeljob kann diese Prüfung daher explizit
    # auslassen und prüft weiterhin Namen, Inhalte, Größen und Prüfsummen.
    [switch]$SkipWindowsExecutableVersionCheck
)

$ErrorActionPreference = "Stop"
$directoryPath = (Resolve-Path -LiteralPath $Directory).Path
$expected = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)

function Add-Expected([string]$Name) {
    [void]$expected.Add($Name)
}

if ($Platform -in @("Windows", "All")) {
    Add-Expected "ResticBrowser.exe"
    Add-Expected "ResticBrowserWindows-$Version-win-x64.zip"
    if ($RequireInstaller -or $Platform -eq "All") {
        Add-Expected "ResticBrowser-Setup.exe"
    }
}
if ($Platform -in @("Linux", "All")) {
    Add-Expected "ResticBrowser-linux-x64.tar.gz"
}

$actualFiles = @(Get-ChildItem -LiteralPath $directoryPath -File | Select-Object -ExpandProperty Name)
if ($AllowChecksumFile -and $actualFiles -contains "SHA256SUMS.txt") {
    Add-Expected "SHA256SUMS.txt"
}

$expectedNames = @($expected | Sort-Object)
$actualNames = @($actualFiles | Sort-Object)
if (($expectedNames -join "`n") -ne ($actualNames -join "`n")) {
    throw "Release-Artefakte stimmen nicht exakt überein. Erwartet: $($expectedNames -join ', '); vorhanden: $($actualNames -join ', ')"
}

foreach ($name in $actualNames) {
    $path = Join-Path $directoryPath $name
    if ((Get-Item -LiteralPath $path).Length -le 0) {
        throw "Release-Artefakt '$name' ist leer."
    }
}

function Assert-ZipContents([string]$Path, [string[]]$RequiredEntries) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [System.IO.Compression.ZipFile]::OpenRead($Path)
    try {
        $entries = @($archive.Entries | ForEach-Object { $_.FullName.TrimStart('./') })
        foreach ($entry in $RequiredEntries) {
            if ($entries -notcontains $entry) {
                throw "ZIP '$Path' enthält den erforderlichen Eintrag '$entry' nicht."
            }
        }
    } finally {
        $archive.Dispose()
    }
}

function Assert-TarGzContents([string]$Path, [string[]]$RequiredEntries) {
    $entries = @(& tar -tzf $Path 2>&1)
    if ($LASTEXITCODE -ne 0) {
        throw "TAR.GZ '$Path' konnte nicht gelesen werden."
    }
    $normalized = @($entries | ForEach-Object { $_.ToString().TrimEnd('/') })
    foreach ($entry in $RequiredEntries) {
        if ($normalized -notcontains $entry) {
            throw "TAR.GZ '$Path' enthält den erforderlichen Eintrag '$entry' nicht."
        }
    }
}

if ($Platform -in @("Windows", "All")) {
    $zipPath = Join-Path $directoryPath "ResticBrowserWindows-$Version-win-x64.zip"
    Assert-ZipContents $zipPath @("ResticBrowser.exe", "LICENSE", "README.md")
    if ($SkipWindowsExecutableVersionCheck) {
        Write-Host "Windows-Dateiversion wird in diesem plattformübergreifenden Sammeljob nicht gelesen; sie wurde im Windows-Build geprüft."
    } else {
        $exePath = Join-Path $directoryPath "ResticBrowser.exe"
        $fileVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($exePath)
        if ($fileVersion.FileVersion -ne "$Version.0") {
            throw "Dateiversion '$($fileVersion.FileVersion)' in '$($exePath)' stimmt nicht mit '$Version.0' überein."
        }
    }
}

if ($Platform -in @("Linux", "All")) {
    $archivePath = Join-Path $directoryPath "ResticBrowser-linux-x64.tar.gz"
    Assert-TarGzContents $archivePath @("ResticBrowser", "LICENSE", "README.md")
}

if ($AllowChecksumFile) {
    $checksumPath = Join-Path $directoryPath "SHA256SUMS.txt"
    if (Test-Path -LiteralPath $checksumPath) {
        $checksumEntries = @{}
        foreach ($line in Get-Content -LiteralPath $checksumPath) {
            if ($line -notmatch '^(?<hash>[A-Fa-f0-9]{64})  (?<name>.+)$') {
                throw "Ungültige Prüfsummen-Zeile: '$line'"
            }
            if ($checksumEntries.ContainsKey($Matches.name)) {
                throw "Doppelte Prüfsumme für '$($Matches.name)'."
            }
            $checksumEntries[$Matches.name] = $Matches.hash.ToUpperInvariant()
        }
        foreach ($name in ($actualNames | Where-Object { $_ -ne "SHA256SUMS.txt" })) {
            if (-not $checksumEntries.ContainsKey($name)) {
                throw "Keine Prüfsumme für '$name' vorhanden."
            }
            $actualHash = (Get-FileHash -LiteralPath (Join-Path $directoryPath $name) -Algorithm SHA256).Hash
            if ($actualHash -ne $checksumEntries[$name]) {
                throw "Prüfsumme für '$name' stimmt nicht."
            }
        }
    }
}

Write-Host "Produktionsartefakte geprüft: $($actualNames -join ', ')"
