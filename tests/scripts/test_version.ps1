$ErrorActionPreference = "Stop"
$sourceRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$fixture = Join-Path ([IO.Path]::GetTempPath()) ("restic-version-tests-" + [guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory $fixture | Out-Null
function Assert-Throws([scriptblock]$Action) {
    $failed = $false
    try { & $Action | Out-Null } catch { $failed = $true }
    if (-not $failed) { throw "Ein erwarteter Versionsfehler wurde nicht erkannt." }
}
function Set-Version([string]$Value) {
    "<Project><PropertyGroup><ResticBrowserProductVersion>$Value</ResticBrowserProductVersion><ResticBrowserAssemblyVersion>$Value.0</ResticBrowserAssemblyVersion></PropertyGroup></Project>" | Set-Content Directory.Build.props
}
try {
    Copy-Item (Join-Path $sourceRoot "scripts") (Join-Path $fixture "scripts") -Recurse
    New-Item -ItemType Directory (Join-Path $fixture "src/ResticBrowser"),(Join-Path $fixture "installer/windows") -Force | Out-Null
    Copy-Item (Join-Path $sourceRoot "src/ResticBrowser/ResticBrowser.csproj") (Join-Path $fixture "src/ResticBrowser")
    Copy-Item (Join-Path $sourceRoot "installer/windows/ResticBrowser.iss") (Join-Path $fixture "installer/windows")
    Push-Location $fixture
    git init --quiet
    if ($LASTEXITCODE -ne 0) { throw "Fixture-Repository konnte nicht erstellt werden." }
    foreach ($invalid in @("01.0.3", "1.0", "1.0.3-beta", "", "1.0.3.0")) {
        Set-Version $invalid
        Assert-Throws { ./scripts/get-product-version.ps1 }
    }
    '<Project><PropertyGroup><ResticBrowserProductVersion>1.0.3</ResticBrowserProductVersion><ResticBrowserProductVersion>1.0.4</ResticBrowserProductVersion></PropertyGroup></Project>' | Set-Content Directory.Build.props
    Assert-Throws { ./scripts/get-product-version.ps1 }
    Set-Version "1.0.3"
    if ((./scripts/get-product-version.ps1) -ne "1.0.3") { throw "Gültige Version wurde nicht gelesen." }
    Add-Content Directory.Build.props '<!-- assembly version deliberately mismatches -->'
    ./scripts/verify-version.ps1 -Tag v1.0.3
    Assert-Throws { ./scripts/verify-version.ps1 -Tag v1.0.2 }
    $projectPath = 'src/ResticBrowser/ResticBrowser.csproj'
    $project = Get-Content $projectPath -Raw
    $project.Replace('<FileVersion>$(ResticBrowserAssemblyVersion)</FileVersion>', '<FileVersion>9.0.0.0</FileVersion>') | Set-Content $projectPath
    Assert-Throws { ./scripts/verify-version.ps1 }
    $project | Set-Content $projectPath
    Set-Version "1.0.3"
    (Get-Content Directory.Build.props -Raw).Replace('<ResticBrowserAssemblyVersion>1.0.3.0</ResticBrowserAssemblyVersion>', '<ResticBrowserAssemblyVersion>9.0.0.0</ResticBrowserAssemblyVersion>') | Set-Content Directory.Build.props
    Assert-Throws { ./scripts/verify-version.ps1 }
    Set-Version "1.0.3"
    $installerPath = 'installer/windows/ResticBrowser.iss'
    $installer = Get-Content $installerPath -Raw
    $installer.Replace('AppVersion={#MyAppVersion}', 'AppVersion=9.0.0') | Set-Content $installerPath
    Assert-Throws { ./scripts/verify-version.ps1 }
    $installer | Set-Content $installerPath
    $builtAssembly = Join-Path $sourceRoot "src/ResticBrowser/bin/Release/net10.0/ResticBrowser.dll"
    if (Test-Path $builtAssembly) {
        Set-Version (& (Join-Path $sourceRoot "scripts/get-product-version.ps1"))
        ./scripts/verify-version.ps1 -ExecutablePath $builtAssembly
        Set-Version "999.0.0"
        Assert-Throws { ./scripts/verify-version.ps1 -ExecutablePath $builtAssembly }
        Set-Version "1.0.3"
    }
    Write-Host "Versions-Szenarien bestanden."
} finally {
    Pop-Location
    $resolved = [IO.Path]::GetFullPath($fixture)
    if ($resolved.StartsWith([IO.Path]::GetFullPath([IO.Path]::GetTempPath()), [StringComparison]::OrdinalIgnoreCase)) {
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
}
