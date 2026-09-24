#region Environment configuration

function Get-AutomationEnvironment {
    <#
    .SYNOPSIS
    Resolves game paths from the process environment and literal local dotenv assignments.
    #>
    param([Parameter(Mandatory)][string]$RepositoryPath)

    $values = @{}
    # Read only the two supported keys; never execute dotenv content or expose credentials.
    foreach ($fileName in @('.env', '.env.local')) {
        $filePath = Join-Path $RepositoryPath $fileName
        if (-not (Test-Path -LiteralPath $filePath)) { continue }
        foreach ($line in [IO.File]::ReadLines($filePath)) {
            if ($line -notmatch '^\s*(?:export\s+)?(VINTAGE_STORY|VGE_DATA_PATH)\s*=\s*(.*?)\s*$') { continue }
            $key = $Matches[1]
            $value = $Matches[2]
            if ($value -match '^(["''])(.*?)\1\s*(?:#.*)?$') {
                $value = $Matches[2]
            } else {
                $value = ($value -replace '\s+#.*$', '').Trim()
            }
            $values[$key] = $value
        }
    }
    foreach ($key in @('VINTAGE_STORY', 'VGE_DATA_PATH')) {
        $processValue = [Environment]::GetEnvironmentVariable($key)
        if (-not [string]::IsNullOrWhiteSpace($processValue)) { $values[$key] = $processValue }
    }
    return $values
}

#endregion
