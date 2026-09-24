<#
.SYNOPSIS
Installs or removes the development package in the configured automation data directory.
#>
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('Install', 'Uninstall')]
    [string]$Action
)

$archiveName = 'vanillagraphicsexpanded_999.99.9.zip'
# Require an explicit directory so development commands never target the normal profile.
if ([string]::IsNullOrWhiteSpace($env:VGE_DATA_PATH) -or
    -not [System.IO.Path]::IsPathFullyQualified($env:VGE_DATA_PATH)) {
    throw 'Set VGE_DATA_PATH to an absolute automation data directory and restart the IDE.'
}
$modsDirectory = Join-Path $env:VGE_DATA_PATH 'Mods'
$archivePath = Join-Path $modsDirectory $archiveName

if ($Action -eq 'Install') {
    $sourcePath = Join-Path $PSScriptRoot "..\Releases\$archiveName"
    if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
        throw "CakeBuild did not produce the development archive: $sourcePath"
    }

    New-Item -ItemType Directory -Force -Path $modsDirectory | Out-Null
    Copy-Item -LiteralPath $sourcePath -Destination $archivePath -Force
    Write-Host "Installed $archiveName to $modsDirectory"
    return
}

Remove-Item -LiteralPath $archivePath -Force -ErrorAction SilentlyContinue
Write-Host "Uninstalled development mod from $archivePath"
