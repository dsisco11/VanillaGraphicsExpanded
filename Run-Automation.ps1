#requires -Version 7.0
<#
.SYNOPSIS
Launches the debug mod in a test world and closes its client after player readiness.
.DESCRIPTION
Uses VINTAGE_STORY and VGE_DATA_PATH from the process environment, falling back to
.env.local and .env. Requires an existing Debug build. Does not build or modify saves
before launch. Run logs are retained in Data/Logs/Automation/<run id>.
.PARAMETER World
Save filename without its .vcdbs extension, within the configured data directory.
.PARAMETER SecondsAfterReady
Wall-clock seconds to wait after the run-specific PlayerReady log marker.
.PARAMETER CreateWorld
Allows the game to create a missing world. Initial character selection may require interaction.
.PARAMETER ForceOnTimeout
Allows killing only the launched process if normal window closure times out.
.PARAMETER Foreground
Allows normal window activation instead of the default visible background window.
.EXAMPLE
./Run-Automation.ps1 -World TestWorld -SecondsAfterReady 30
.EXAMPLE
./Run-Automation.ps1 -CreateWorld -Foreground -StartupTimeoutSeconds 600
#>
[CmdletBinding()]
param(
    [ValidatePattern('^[^<>:"/\\|?*]+$')][string]$World = 'TestWorld',
    [ValidateRange(0, 86400)][int]$SecondsAfterReady = 30,
    [ValidateRange(1, 86400)][int]$StartupTimeoutSeconds = 300,
    [ValidateRange(1, 3600)][int]$ShutdownTimeoutSeconds = 60,
    [string]$GamePath,
    [string]$DataPath,
    [switch]$CreateWorld,
    [switch]$ForceOnTimeout,
    [switch]$Foreground
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'tools/Automation/Environment.ps1')
. (Join-Path $PSScriptRoot 'tools/Automation/ClientSession.ps1')

$configuration = Get-AutomationEnvironment -RepositoryPath $PSScriptRoot
if (-not $PSBoundParameters.ContainsKey('GamePath')) { $GamePath = $configuration['VINTAGE_STORY'] }
if (-not $PSBoundParameters.ContainsKey('DataPath')) { $DataPath = $configuration['VGE_DATA_PATH'] }

Invoke-AutomationClient -GamePath $GamePath -DataPath $DataPath -RepositoryPath $PSScriptRoot `
    -World $World -SecondsAfterReady $SecondsAfterReady -StartupTimeoutSeconds $StartupTimeoutSeconds `
    -ShutdownTimeoutSeconds $ShutdownTimeoutSeconds -CreateWorld:$CreateWorld -ForceOnTimeout:$ForceOnTimeout -Foreground:$Foreground
