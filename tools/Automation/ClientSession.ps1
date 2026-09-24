#region Readiness and shutdown

function Wait-AutomationReadiness {
    <#
    .SYNOPSIS
    Tails only this run's logs until its readiness marker appears or startup fails.
    #>
    param(
        [Parameter(Mandatory)][Diagnostics.Process]$ClientProcess,
        [Parameter(Mandatory)][string]$LogPath,
        [Parameter(Mandatory)][string]$RunId,
        [Parameter(Mandatory)][int]$TimeoutSeconds
    )

    $marker = "[VGE.Automation] PlayerReady RunId=$RunId"
    $positions = @{}
    $suffixes = @{}
    $timer = [Diagnostics.Stopwatch]::StartNew()
    while ($timer.Elapsed.TotalSeconds -lt $TimeoutSeconds) {
        if ($ClientProcess.HasExited) { throw "Client exited before readiness (exit code $($ClientProcess.ExitCode)). Logs: $LogPath" }
        foreach ($file in Get-ChildItem -LiteralPath $LogPath -File) {
            if ($file.Extension -notin @('.log', '.txt')) { continue }
            $path = $file.FullName
            if (-not $positions.ContainsKey($path)) { $positions[$path] = 0L; $suffixes[$path] = '' }
            $stream = $null
            $reader = $null
            try {
                # Reopen each poll to tolerate rotation; retain partial markers between reads.
                $stream = [IO.File]::Open($path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete)
                if ($stream.Length -lt $positions[$path]) { $positions[$path] = 0L; $suffixes[$path] = '' }
                $null = $stream.Seek($positions[$path], [IO.SeekOrigin]::Begin)
                $reader = [IO.StreamReader]::new($stream)
                $text = $suffixes[$path] + $reader.ReadToEnd()
                $positions[$path] = $stream.Position
                if ($text.Contains($marker)) { return }
                $suffixes[$path] = $text.Substring([Math]::Max(0, $text.Length - $marker.Length))
            } catch [IO.IOException] {
                # The logger can briefly hold an exclusive handle while creating a file.
            } finally {
                if ($null -ne $reader) { $reader.Dispose() }
                elseif ($null -ne $stream) { $stream.Dispose() }
            }
        }
        Start-Sleep -Milliseconds 250
    }
    throw "PlayerReady was not logged within $TimeoutSeconds seconds. Check login, character selection, mod loading, and logs: $LogPath"
}

function Stop-AutomationClient {
    <#
    .SYNOPSIS
    Requests normal window closure on the owned process, with optional forced cleanup.
    #>
    param(
        [Parameter(Mandatory)][Diagnostics.Process]$ClientProcess,
        [Parameter(Mandatory)][int]$TimeoutSeconds,
        [switch]$ForceOnTimeout
    )

    $timer = [Diagnostics.Stopwatch]::StartNew()
    $closeRequested = $false
    while (-not $ClientProcess.HasExited -and $timer.Elapsed.TotalSeconds -lt $TimeoutSeconds) {
        # Startup failures may occur before a window exists. Retry discovery, not process lookup.
        if (-not $closeRequested) {
            $ClientProcess.Refresh()
            if (-not $ClientProcess.HasExited) { $closeRequested = $ClientProcess.CloseMainWindow() }
        }
        if (-not $ClientProcess.HasExited) { $null = $ClientProcess.WaitForExit(250) }
    }
    if ($ClientProcess.HasExited) { return }
    if ($ForceOnTimeout) {
        $ClientProcess.Kill()
        $null = $ClientProcess.WaitForExit(10000)
        throw "Client PID $($ClientProcess.Id) required forced termination; this run did not close cleanly."
    }
    throw "Client PID $($ClientProcess.Id) did not close within $TimeoutSeconds seconds and remains running. Close it manually, or opt into -ForceOnTimeout on a subsequent run."
}

#endregion

#region Session ownership

function Invoke-AutomationClient {
    <#
    .SYNOPSIS
    Validates the isolated profile, owns one client process, and records its timed session.
    #>
    param(
        [string]$GamePath,
        [string]$DataPath,
        [Parameter(Mandatory)][string]$RepositoryPath,
        [Parameter(Mandatory)][string]$World,
        [Parameter(Mandatory)][int]$SecondsAfterReady,
        [Parameter(Mandatory)][int]$StartupTimeoutSeconds,
        [Parameter(Mandatory)][int]$ShutdownTimeoutSeconds,
        [switch]$CreateWorld,
        [switch]$ForceOnTimeout
    )

    if (-not $IsWindows) { throw 'Run-Automation currently requires Windows for normal client window closure.' }
    foreach ($entry in @{ VINTAGE_STORY = $GamePath; VGE_DATA_PATH = $DataPath }.GetEnumerator()) {
        if ([string]::IsNullOrWhiteSpace($entry.Value) -or -not [IO.Path]::IsPathFullyQualified($entry.Value)) {
            throw "Set $($entry.Key) to an absolute directory in the environment, .env.local, or .env."
        }
    }
    $GamePath = [IO.Path]::GetFullPath($GamePath)
    $DataPath = [IO.Path]::GetFullPath($DataPath)
    $normalDataPath = Join-Path ([Environment]::GetFolderPath('ApplicationData')) 'VintagestoryData'
    if ($DataPath.TrimEnd('\', '/') -eq $normalDataPath.TrimEnd('\', '/')) {
        throw 'Automation requires a separate data directory, not the normal VintagestoryData profile.'
    }
    $executablePath = Join-Path $GamePath 'Vintagestory.exe'
    $settingsPath = Join-Path $DataPath 'clientsettings.json'
    $modPath = Join-Path $RepositoryPath 'VanillaGraphicsExpanded/bin/Debug/Mods'
    foreach ($requiredFile in @($executablePath, $settingsPath, (Join-Path $modPath 'mod/VanillaGraphicsExpanded.dll'))) {
        if (-not (Test-Path -LiteralPath $requiredFile -PathType Leaf)) { throw "Required file is missing: $requiredFile. Prepare the automation profile and build Debug first." }
    }
    $settings = Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json -AsHashtable
    if ($settings['boolSettings']['multipleInstances'] -ne $true) {
        throw 'Enable boolSettings.multipleInstances in the automation clientsettings.json first.'
    }
    if (Test-Path -LiteralPath (Join-Path $DataPath 'Mods/vanillagraphicsexpanded_999.99.9.zip')) {
        throw 'Uninstall the development package before launching automation with the Debug mod output.'
    }
    # Limit selection to a save filename under this profile, never a world owned by another profile.
    if ([string]::IsNullOrWhiteSpace($World) -or $World -in @('.', '..') -or $World.IndexOfAny([IO.Path]::GetInvalidFileNameChars()) -ge 0) {
        throw 'World must be a save filename within the automation data directory.'
    }
    $saveName = if ($World.EndsWith('.vcdbs', [StringComparison]::OrdinalIgnoreCase)) { $World } else { "$World.vcdbs" }
    # ScreenManager.openWorldFromArgs concatenates Saves + name + .vcdbs itself.
    $worldArgument = $saveName.Substring(0, $saveName.Length - '.vcdbs'.Length)
    $savePath = Join-Path (Join-Path $DataPath 'Saves') $saveName
    if (-not $CreateWorld -and -not (Test-Path -LiteralPath $savePath -PathType Leaf)) {
        throw "Save does not exist: $savePath. Prepare it first or use -CreateWorld for an interactive first run."
    }

    $runId = [Guid]::NewGuid().ToString('N')
    $logPath = Join-Path $DataPath "Logs/Automation/$runId"
    $client = $null
    $profileLease = $null
    $failure = $null
    try {
        # Serialize automation scripts that share a profile. The file can remain after exit;
        # the exclusive handle, not its existence, represents ownership.
        $profileLease = [IO.File]::Open((Join-Path $DataPath '.vge-automation.lock'), [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
        $null = New-Item -ItemType Directory -Path $logPath -Force
        $startInfo = [Diagnostics.ProcessStartInfo]::new($executablePath)
        $startInfo.UseShellExecute = $false
        $startInfo.WorkingDirectory = $GamePath
        foreach ($argument in @('--dataPath', $DataPath, '--logPath', $logPath, '--openWorld', $worldArgument, '--addModPath', $modPath, '--tracelog')) {
            $startInfo.ArgumentList.Add($argument)
        }
        $startInfo.Environment['AUTOMATION_ID'] = $runId
        $startInfo.Environment['VGE_DATA_PATH'] = $DataPath
        $client = [Diagnostics.Process]::Start($startInfo)
        Write-Host "Automation client PID $($client.Id); run $runId; logs: $logPath"
        Wait-AutomationReadiness -ClientProcess $client -LogPath $logPath -RunId $runId -TimeoutSeconds $StartupTimeoutSeconds
        Write-Host "PlayerReady received. Waiting $SecondsAfterReady seconds."
        $timer = [Diagnostics.Stopwatch]::StartNew()
        while ($timer.Elapsed.TotalSeconds -lt $SecondsAfterReady) {
            if ($client.HasExited) { throw "Client exited before the requested duration (exit code $($client.ExitCode))." }
            Start-Sleep -Milliseconds 100
        }
        if ($client.HasExited) { throw 'Client exited before automation requested closure.' }
    } catch {
        $failure = $_
    } finally {
        # Even failed startup gets a normal close request, scoped to the process we created.
        try {
            if ($null -ne $client) {
                Stop-AutomationClient -ClientProcess $client -TimeoutSeconds $ShutdownTimeoutSeconds -ForceOnTimeout:$ForceOnTimeout
                if ($null -eq $failure -and $client.ExitCode -ne 0) { throw "Client exited with code $($client.ExitCode). Logs: $logPath" }
            }
        } catch {
            if ($null -eq $failure) { $failure = $_ } else { Write-Warning $_.Exception.Message }
        } finally {
            if ($null -ne $client) { $client.Dispose() }
            if ($null -ne $profileLease) { $profileLease.Dispose() }
        }
    }
    if ($null -ne $failure) { throw $failure }
    [pscustomobject]@{ RunId = $runId; World = $savePath; SecondsAfterReady = $SecondsAfterReady; LogPath = $logPath; ExitCode = 0 }
}

#endregion
