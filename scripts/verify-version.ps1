param(
    [string]$Tag,
    [string]$ExecutablePath
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$versionFile = Join-Path $root "version.txt"
if (-not (Test-Path -LiteralPath $versionFile)) {
    throw "Die zentrale Versionsdatei '$versionFile' fehlt."
}

$version = (Get-Content -LiteralPath $versionFile -Raw).Trim()
if ($version -notmatch '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$') {
    throw "Die zentrale Version '$version' ist nicht MAJOR.MINOR.PATCH."
}

$validTags = @(git tag --list 'v*' | Where-Object {
    $_ -match '^v(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$'
})
if ($validTags.Count -gt 0) {
    $highestTag = $validTags | ForEach-Object {
        [pscustomobject]@{ Tag = $_; Version = [version]$_.Substring(1) }
    } | Sort-Object Version -Descending | Select-Object -First 1
    if ([version]$version -lt $highestTag.Version) {
        throw "Version '$version' ist älter als der vorhandene höchste SemVer-Tag '$($highestTag.Tag)'."
    }
}

$projectPaths = @(
    (Join-Path $root "src\ResticBrowser\ResticBrowser.csproj"),
    (Join-Path $root "src\ResticBrowser.Remote\ResticBrowser.Remote.csproj")
)

foreach ($projectPath in $projectPaths) {
    $project = [xml](Get-Content -LiteralPath $projectPath -Raw)
    $propertyGroup = $project.SelectSingleNode('/Project/PropertyGroup[Version]')
    if ($null -eq $propertyGroup) {
        throw "Keine Versionseigenschaften in '$projectPath' gefunden."
    }

    $expectedProperties = @{
        Version = '$(ResticBrowserProductVersion)'
        AssemblyVersion = '$(ResticBrowserAssemblyVersion)'
        FileVersion = '$(ResticBrowserAssemblyVersion)'
        InformationalVersion = '$(ResticBrowserProductVersion)'
    }

    foreach ($propertyName in $expectedProperties.Keys) {
        $node = $propertyGroup.SelectSingleNode($propertyName)
        if ($null -eq $node -or $node.InnerText.Trim() -ne $expectedProperties[$propertyName]) {
            throw "'$projectPath' verwendet für $propertyName nicht die zentrale Versionsquelle."
        }
    }

    $msbuildOutput = @(& dotnet msbuild $projectPath -getProperty:Version 2>&1)
    if ($LASTEXITCODE -ne 0) {
        throw "Die ausgewertete Version von '$projectPath' konnte nicht gelesen werden."
    }
    $versionLine = $msbuildOutput | Where-Object {
        $_ -match '^\s*Version\s*=\s*\S+\s*$' -or $_ -match '^\s*\d+\.\d+\.\d+\s*$'
    } | Select-Object -Last 1
    if ($null -eq $versionLine) {
        throw "MSBuild hat für '$projectPath' keine auswertbare Version geliefert. Ausgabe: $($msbuildOutput -join ' ')"
    }
    $versionValue = if ($versionLine.ToString() -match '^\s*Version\s*=\s*(?<value>\S+)\s*$') {
        $Matches.value
    } elseif ($versionLine.ToString() -match '^\s*(?<value>\d+\.\d+\.\d+)\s*$') {
        $Matches.value
    } else {
        ""
    }
    if ($versionValue -ne $version) {
        throw "MSBuild-Version '$versionValue' in '$projectPath' stimmt nicht mit '$version' überein."
    }
}

$installerPath = Join-Path $root "installer\windows\ResticBrowser.iss"
$installerText = Get-Content -LiteralPath $installerPath -Raw
if ($installerText -notmatch '(?m)^\s*#ifndef\s+MyAppVersion\b') {
    throw "Der Inno-Setup-Quelltext muss MyAppVersion als Build-Definition erwarten."
}
if ($installerText -notmatch '(?m)^\s*#error\s+.*MyAppVersion') {
    throw "Der Inno-Setup-Quelltext darf keine eigene Versionsquelle enthalten."
}
if ($installerText -notmatch '(?m)^\s*AppVersion=\{#MyAppVersion\}') {
    throw "Der Inno-Setup-Quelltext verwendet MyAppVersion nicht für AppVersion."
}

$installerBuildScriptPath = Join-Path $root "scripts\publish-windows-installer.ps1"
$installerBuildScript = Get-Content -LiteralPath $installerBuildScriptPath -Raw
if ($installerBuildScript -notmatch '/DMyAppVersion=\$version') {
    throw "Der Installer-Build muss MyAppVersion aus version.txt an Inno Setup übergeben."
}

if (-not [string]::IsNullOrWhiteSpace($Tag) -and $Tag -ne "v$version") {
    throw "Tag '$Tag' stimmt nicht mit der zentralen Version 'v$version' überein."
}

if (-not [string]::IsNullOrWhiteSpace($ExecutablePath)) {
    $resolvedExecutable = Resolve-Path -LiteralPath $ExecutablePath
    $fileVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($resolvedExecutable.Path)
    if ($fileVersion.FileVersion -ne "$version.0") {
        throw "Dateiversion '$($fileVersion.FileVersion)' der Produktionsdatei stimmt nicht mit '$version.0' überein."
    }
    if ($fileVersion.ProductVersion -notmatch "^$([regex]::Escape($version))(\+|$)") {
        throw "Produktversion '$($fileVersion.ProductVersion)' der Produktionsdatei stimmt nicht mit '$version' überein."
    }
}

Write-Host "Versionsprüfung erfolgreich: $version"
