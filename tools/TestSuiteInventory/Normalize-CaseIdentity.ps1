# Anchor the discovery snapshot to the native xUnit identity. Duplicate
# truncated display names have no distinguishable xUnit-listing occurrence.
param(
    [string]$RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
)

$inventoryDirectory = Join-Path $RepositoryRoot 'docs\testing-inventory'
$sourceLines = @{}
Get-Content (Join-Path $inventoryDirectory 'Phase1.DuplicateTheorySourceRows.txt') |
    Where-Object { $_ -and -not $_.StartsWith('#') } | ForEach-Object {
        $parts = $_ -split '\|', 2
        if ($parts.Count -ne 2 -or $sourceLines.ContainsKey($parts[0])) { throw "Invalid duplicate theory source entry: $_" }
        $sourceLines[$parts[0]] = $parts[1]
    }

$path = Join-Path $inventoryDirectory 'Phase1.DiscoveredCases.jsonl'
$output = [System.Collections.Generic.List[string]]::new()
$matched = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$keys = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
Get-Content $path | ForEach-Object {
    $case = $_ | ConvertFrom-Json
    if (-not $case.nativeUniqueId -or -not $case.vstestId) { throw "Discovery case lacks native identity: $($case.displayName)" }
    $identity = "$($case.project)`0$($case.nativeUniqueId)"
    $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($identity))).ToLowerInvariant()
    if (-not $keys.Add($hash)) { throw "Duplicate native case identity: $identity" }
    $case.caseKey = $hash
    if ($sourceLines.ContainsKey($case.nativeUniqueId)) {
        [void]$matched.Add($case.nativeUniqueId)
        $case | Add-Member -NotePropertyName theorySourceLine -NotePropertyValue $sourceLines[$case.nativeUniqueId] -Force
    }
    $output.Add(($case | ConvertTo-Json -Depth 20 -Compress))
}
if ($matched.Count -ne 22 -or $sourceLines.Count -ne 22) { throw "Duplicate theory identity mapping incomplete: $($matched.Count)/$($sourceLines.Count)" }
Set-Content -LiteralPath $path -Value $output -Encoding utf8
Write-Output "Native-keyed cases: $($output.Count); verified duplicate theory source rows: $($matched.Count)"
