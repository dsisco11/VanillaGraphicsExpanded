# Resolve every support source to a reviewed responsibility group. The map is
# explicit for domain support; assertion and common helper trees have one owner.
param(
    [string]$RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
)

$inventoryDirectory = Join-Path $RepositoryRoot 'docs\testing-inventory'
$mapping = @{}
Get-Content (Join-Path $inventoryDirectory 'Phase1.SupportGroups.txt') |
    Where-Object { $_ -and -not $_.StartsWith('#') } | ForEach-Object {
        $parts = $_ -split '\|', 2
        if ($parts.Count -ne 2) { throw "Malformed support group: $_" }
        foreach ($name in ($parts[1] -split ';')) {
            if ($mapping.ContainsKey($name)) { throw "Duplicate support group member: $name" }
            $mapping[$name] = $parts[0]
        }
    }

$source = @(Import-Csv (Join-Path $inventoryDirectory 'Phase1.SupportSources.csv'))
$matched = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$rows = foreach ($item in $source) {
    $name = [System.IO.Path]::GetFileNameWithoutExtension($item.SourcePath)
    $owner = if ($mapping.ContainsKey($name)) {
        [void]$matched.Add($name)
        $mapping[$name]
    } elseif ($item.SourcePath -like 'VanillaGraphicsExpanded.Tests/GPU/Assertions/*') {
        'GPU observation framework'
    } elseif ($item.SourcePath -like 'VanillaGraphicsExpanded.Tests/Helpers/*') {
        'Shared asset, source and diagnostic support'
    } else {
        throw "Support source has no reviewed route: $($item.SourcePath)"
    }
    $observation = switch ($name) {
        'CloudOitSurface' { 'Owned by CloudStoredVolumeViewScene; retired in reverse resource order' }
        'ObjectParamsUbo' { 'No active test-source consumer found; retained pending any later removal decision' }
        'RasterCameraExposureShader' { 'No active test-source consumer found; retained pending any later removal decision' }
        'SkipOnCIAttribute' { 'No active attribute application found; retained pending any later removal decision' }
        'RasterDepthHierarchyReference' { 'Borrows pipelines owned and retired by PostprocessDraw' }
        default { '' }
    }
    [pscustomobject]@{
        SourcePath = $item.SourcePath
        DeclaredTypes = $item.DeclaredTypes
        CurrentOwner = $item.SourcePath
        ProposedOwner = $owner
        Role = $item.RoleHint
        LifetimeCue = "IDisposable=$($item.IDisposableCue); static=$($item.StaticStateCue)"
        Evidence = 'Phase1.SupportOwnershipDecisions.md; source and representative consumer review'
        Observation = $observation
        Uncertainty = ''
    }
}
foreach ($name in $mapping.Keys) {
    if (-not $matched.Contains($name)) { throw "Mapped support member absent from source inventory: $name" }
}
$rows | Sort-Object SourcePath |
    Export-Csv (Join-Path $inventoryDirectory 'Phase1.SupportRouting.csv') -NoTypeInformation -Encoding utf8
Write-Output "Support routing rows: $($rows.Count); groups: $(@($rows | Select-Object -ExpandProperty ProposedOwner -Unique).Count)"
