# Join discovered source methods to reviewed behavior boundaries. The output is
# the exact source-level routing baseline; destination paths are decided later.
param(
    [string]$RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
)

$inventoryDirectory = Join-Path $RepositoryRoot 'docs\testing-inventory'
$source = @(Import-Csv (Join-Path $inventoryDirectory 'Phase1.SourceMethods.csv'))
$groups = @(
    'RootGroups.csv', 'UnitGroups.csv', 'AuxiliaryGroups.csv',
    'GpuGroups-NZ.csv', 'GpuGroups-AM.csv' |
        ForEach-Object { Import-Csv (Join-Path $inventoryDirectory $_) }
)
$byKey = @{}
foreach ($group in $groups) {
    if ($byKey.ContainsKey($group.SourceKey)) { throw "Duplicate classification key: $($group.SourceKey)" }
    $byKey[$group.SourceKey] = $group
}
if ($byKey.Count -ne $source.Count) { throw "Classification/source count mismatch: $($byKey.Count) versus $($source.Count)" }

$caseCounts = @{}
Get-Content (Join-Path $inventoryDirectory 'Phase1.DiscoveredCases.jsonl') | ForEach-Object {
    $case = $_ | ConvertFrom-Json
    $key = "$($case.project)::$($case.class).$($case.method)"
    $caseCounts[$key] = 1 + [int]$caseCounts[$key]
}

# Flag source files with distinct levels or proposed responsibilities so later
# moves can split them deliberately without inferring ownership from names.
$sourceShape = @{}
$source | Group-Object SourcePath | ForEach-Object {
    $owners = @($_.Group | ForEach-Object { $byKey[$_.SourceKey].ProposedOwner } | Sort-Object -Unique)
    $levels = @($_.Group | ForEach-Object { $byKey[$_.SourceKey].Level } | Sort-Object -Unique)
    $sourceShape[$_.Name] = [pscustomobject]@{
        MultipleOwners = $owners.Count -gt 1
        MultipleLevels = $levels.Count -gt 1
    }
}

$rows = foreach ($method in $source) {
    if (-not $byKey.ContainsKey($method.SourceKey)) { throw "Unclassified source method: $($method.SourceKey)" }
    $group = $byKey[$method.SourceKey]
    [pscustomobject]@{
        SourceKey = $method.SourceKey
        SourcePath = $method.SourcePath
        SourceLine = $method.SourceLine
        Class = $method.Class
        Method = $method.Method
        RuntimeCases = [int]$caseCounts[$method.SourceKey]
        CurrentOwner = $method.SourcePath
        ProposedOwner = $group.ProposedOwner
        SourceHasMultipleOwners = $sourceShape[$method.SourcePath].MultipleOwners
        SourceHasMultipleLevels = $sourceShape[$method.SourcePath].MultipleLevels
        Level = $group.Level
        Purpose = $group.Purpose
        Environment = $group.Environment
        Boundary = $group.Boundary
        Uncertainty = $group.Unresolved
    }
}
$rows | Sort-Object SourcePath, SourceLine, Method |
    Export-Csv (Join-Path $inventoryDirectory 'Phase1.MigrationRouting.csv') -NoTypeInformation -Encoding utf8
Write-Output "Migration routing rows: $($rows.Count); runtime cases: $(($rows | Measure-Object RuntimeCases -Sum).Sum)"
