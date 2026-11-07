# Index direct references from attributed test source files to support-only types.
# References are ownership cues; they do not prove fixture cleanup or transitive use.
param(
    [string]$RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
)

$inventoryDirectory = Join-Path $RepositoryRoot 'docs\testing-inventory'
$support = Import-Csv (Join-Path $inventoryDirectory 'Phase1.SupportSources.csv')
$testPaths = @(Import-Csv (Join-Path $inventoryDirectory 'Phase1.SourceMethods.csv') |
    Select-Object -ExpandProperty SourcePath -Unique)
$testContents = @{}
foreach ($path in $testPaths) {
    $testContents[$path] = [System.IO.File]::ReadAllText((Join-Path $RepositoryRoot $path))
}

$rows = [System.Collections.Generic.List[object]]::new()
foreach ($item in $support) {
    $types = @($item.DeclaredTypes -split ';' | Where-Object { $_ })
    $consumers = [System.Collections.Generic.List[string]]::new()
    foreach ($path in $testPaths) {
        foreach ($type in $types) {
            if ($testContents[$path].Contains($type, [StringComparison]::Ordinal)) {
                $consumers.Add($path)
                break
            }
        }
    }
    $rows.Add([pscustomobject]@{
        Project = $item.Project
        SourcePath = $item.SourcePath
        DeclaredTypes = $item.DeclaredTypes
        RoleHint = $item.RoleHint
        IDisposableCue = $item.IDisposableCue
        StaticStateCue = $item.StaticStateCue
        DirectTestConsumerCount = $consumers.Count
        DirectTestConsumers = $consumers -join ';'
        OwnershipCue = if ($consumers.Count -eq 1) { $consumers[0] } elseif ($consumers.Count -gt 1) { 'Shared by multiple test files' } else { 'No direct test-file reference found; inspect transitive or reflection use' }
        Unresolved = 'Verify actual owner, transitive consumers, external effects, and cleanup from source'
    })
}

$outputPath = Join-Path $inventoryDirectory 'Phase1.SupportOwnership.csv'
$rows | Sort-Object Project, SourcePath | Export-Csv -LiteralPath $outputPath -NoTypeInformation -Encoding utf8
Write-Output "Support ownership cues: $($rows.Count)"
