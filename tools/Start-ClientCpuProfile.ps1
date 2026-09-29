param(
    [Parameter(Mandatory = $true)][string]$WorkspaceRoot,
    [Parameter(Mandatory = $true)][string]$GameRoot,
    [Parameter(Mandatory = $true)][string]$DataPath
)

$ErrorActionPreference = 'Stop'
$vswhere = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFilesX86)) 'Microsoft Visual Studio/Installer/vswhere.exe'
if (-not (Test-Path -LiteralPath $vswhere -PathType Leaf)) {
    throw "Visual Studio installer locator was not found: $vswhere"
}
$collector = & $vswhere -products '*' -latest -find 'Team Tools/DiagnosticsHub/Collector/VSDiagnostics.exe' |
    Select-Object -First 1
if (-not $collector) {
    throw 'No installed Visual Studio profiler was found.'
}
$collectorRoot = Split-Path -Parent $collector
$cpuConfig = Join-Path $collectorRoot 'AgentConfigs/CpuUsageLow.json'
$game = Join-Path $GameRoot 'Vintagestory.exe'
$artifacts = Join-Path $WorkspaceRoot 'artifacts'
$sessionId = [guid]::NewGuid().ToString()
$output = Join-Path $artifacts ('client-startup-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.diagsession')

foreach ($required in @($collector, $cpuConfig, $game)) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) {
        throw "Required profiler or game executable is missing: $required"
    }
}
New-Item -ItemType Directory -Path $artifacts -Force | Out-Null

# VSDiagnostics owns the launch, so sampling includes the first managed startup work.
$gameArguments = '--openWorld TestWorld --tracelog --dataPath "' + $DataPath + '"' +
    ' --addModPath "' + (Join-Path $WorkspaceRoot 'VanillaGraphicsExpanded/bin/Debug/Mods') + '"' +
    ' --addOrigin "' + (Join-Path $WorkspaceRoot 'VanillaGraphicsExpanded/assets') + '"'
$startedAfter = (Get-Date).AddSeconds(-2)
$sessionStarted = $false
try {
    & $collector start $sessionId "/launch:$game" "/launchArgs:$gameArguments" "/loadConfig:$cpuConfig" "/scratchLocation:$artifacts"
    if ($LASTEXITCODE -ne 0) { throw "VSDiagnostics start failed with exit code $LASTEXITCODE" }
    $sessionStarted = $true

    # Follow this invocation's client rather than an older Vintagestory process.
    $target = $null
    $deadline = (Get-Date).AddSeconds(30)
    while ($null -eq $target -and (Get-Date) -lt $deadline) {
        $target = Get-CimInstance Win32_Process -Filter "Name = 'Vintagestory.exe'" |
            Where-Object {
                $_.ExecutablePath -eq $game -and
                [datetime]$_.CreationDate -ge $startedAfter
            } |
            Sort-Object CreationDate -Descending |
            Select-Object -First 1
        if ($null -eq $target) { Start-Sleep -Milliseconds 200 }
    }
    if ($null -eq $target) { throw 'Profiler started, but the launched client process was not found.' }

    Write-Host "Profiling client process $($target.ProcessId). Close the game to save $output"
    Wait-Process -Id $target.ProcessId
}
finally {
    if ($sessionStarted) {
        & $collector stop $sessionId "/output:$output"
        if ($LASTEXITCODE -ne 0) {
            Write-Error "VSDiagnostics stop failed with exit code $LASTEXITCODE"
        } else {
            Write-Host "CPU profile saved: $output"
        }
    }
}
