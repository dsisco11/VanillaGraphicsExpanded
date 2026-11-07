# Materialize reviewed GPU A-M file profiles and method exceptions into a joinable
# inventory. A nonempty Unresolved column preserves the remaining per-method audit.
param(
    [string]$RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
)

$inventoryDirectory = Join-Path $RepositoryRoot 'docs\testing-inventory'
$source = Import-Csv (Join-Path $inventoryDirectory 'Phase1.SourceMethods.csv') |
    Where-Object { $_.SourcePath -like 'VanillaGraphicsExpanded.Tests/GPU/*' -and
        [System.IO.Path]::GetFileName($_.SourcePath) -match '^[A-Ma-m]' }
$profiles = @{}
Get-Content (Join-Path $inventoryDirectory 'GpuProfiles-AM.txt') |
    Where-Object { $_ -and -not $_.StartsWith('#') } | ForEach-Object {
        $parts = $_ -split '\|', 2
        if ($profiles.ContainsKey($parts[0])) { throw "Duplicate GPU profile: $($parts[0])" }
        $profiles[$parts[0]] = $parts[1]
    }
$overrides = @(
    Get-Content (Join-Path $inventoryDirectory 'GpuOverrides-AM.txt') |
        Where-Object { $_ -and -not $_.StartsWith('#') } | ForEach-Object {
            $parts = $_ -split '\|', 4
            if ($parts.Count -ne 4) { throw "Malformed override: $_" }
            [pscustomobject]@{ File = $parts[0]; MethodPattern = $parts[1]; Profile = $parts[2]; Reason = $parts[3] }
        }
)
$verifiedFiles = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
Get-Content (Join-Path $inventoryDirectory 'GpuVerifiedFiles-AM.txt') |
    Where-Object { $_ -and -not $_.StartsWith('#') } | ForEach-Object { [void]$verifiedFiles.Add($_) }
$reconciledFiles = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
Get-Content (Join-Path $inventoryDirectory 'GpuReconciledFiles-AM.txt') |
    Where-Object { $_ -and -not $_.StartsWith('#') } | ForEach-Object { [void]$reconciledFiles.Add($_) }
$fieldCorrections = @(
    Get-Content (Join-Path $inventoryDirectory 'GpuFieldCorrections-AM.txt') |
        Where-Object { $_ -and -not $_.StartsWith('#') } | ForEach-Object {
            $parts = $_ -split '\|', 4
            if ($parts.Count -ne 4) { throw "Malformed GPU field correction: $_" }
            [pscustomobject]@{ File = $parts[0]; MethodPattern = $parts[1]; Field = $parts[2]; Value = $parts[3] }
        }
)

$definitions = @{
    S = @{ Level = 'Component'; Boundary = 'Production shader numerical or pipeline output'; Real = 'Precompiled shader owners; RHI texture/buffer/target backend'; Doubles = 'Authored scene textures, camera, lighting, and input arrays'; Effects = 'GPU draw or dispatch and readback'; Oracle = 'Authored numeric/reference/pixel invariants'; Lifetime = 'GPU collection context; case-owned program, inputs and targets' }
    R = @{ Level = 'Component'; Boundary = 'Renderer, admission, or publication subsystem'; Real = 'Named renderer/publication owner and its internal GPU resources'; Doubles = 'Controlled engine API, work completion, or scene inputs outside the subsystem'; Effects = 'GPU resources and publication callbacks'; Oracle = 'Admission, revision, retention, immutability, and pixel invariants'; Lifetime = 'GPU collection context; case-owned owner and resources' }
    N = @{ Level = 'Integration'; Boundary = 'Native resource, state, or executable interoperability'; Real = 'OpenGlStateCache/RHI/native driver resource and state calls'; Doubles = 'Selective native callback/failure stand-ins where the source specifies them'; Effects = 'Native GL state, storage, errors, or deletion'; Oracle = 'Method-specific native or owner-state observation; independent GL query/pixel checks only where exercised'; Lifetime = 'GPU collection context; case-owned native names with method-specific disposal and detached-name cleanup' }
    E = @{ Level = 'Integration'; Boundary = 'Installed engine, Harmony, or native boundary'; Real = 'Installed engine assembly/helper or Harmony with native backend'; Doubles = 'Controlled API/world callbacks where the source specifies them'; Effects = 'Installed-engine interaction and native state/output'; Oracle = 'Installed signature/IL compatibility or native effect as exercised by the method'; Lifetime = 'GPU collection context where required; method-specific patch and resource cleanup' }
    C = @{ Level = 'Component'; Boundary = 'Controlled driver, interception, or context subsystem'; Real = 'Production callback/registry and manager behavior'; Doubles = 'Deterministic optional-driver or fabricated context inputs'; Effects = 'Controlled callbacks and metadata publication'; Oracle = 'Authored callback arguments, guard state, and published metadata'; Lifetime = 'GPU collection context; case-owned controlled driver/owner' }
    H = @{ Level = 'Component'; Boundary = 'GPU observation/checker subsystem'; Real = 'Production GPU checker and observation programs'; Doubles = 'Authored positive and negative image/buffer inputs'; Effects = 'Checker GPU dispatch/readback'; Oracle = 'Authored predicate/reduction outcomes'; Lifetime = 'GPU collection context; fixture-scoped checker owner and case-owned subjects' }
    U = @{ Level = 'Unit'; Boundary = 'Narrow policy, guard, or formatting rule'; Real = 'Named production rule or controlled test-support contract'; Doubles = 'Authored values or controlled lifetime inputs'; Effects = 'No native effect required by the asserted rule'; Oracle = 'Authored exact values or rejection outcome'; Lifetime = 'Case-owned inputs; GPU setup, if present, is incidental' }
    X = @{ Level = 'Component/Integration'; Boundary = 'Checker component with native lifetime or selector interoperability'; Real = 'Production checker plus native registration/context/selector state'; Doubles = 'Authored checker inputs and controlled failure conditions'; Effects = 'GPU checker dispatch and native lifetime/selector state'; Oracle = 'Checker result and native owner/selector invariants'; Lifetime = 'GPU collection context; fixture checker and case-owned native resources' }
}

$purposeBySourceKey = @{}
Get-Content (Join-Path $inventoryDirectory 'Phase1.DiscoveredCases.jsonl') | ForEach-Object {
    $case = $_ | ConvertFrom-Json
    $key = "$($case.project)::$($case.class).$($case.method)"
    if ($case.traits.Purpose -contains 'Diagnostic') { $purposeBySourceKey[$key] = 'Diagnostic' }
}

$output = [System.Collections.Generic.List[object]]::new()
foreach ($row in $source) {
    $file = [System.IO.Path]::GetFileName($row.SourcePath)
    if (-not $profiles.ContainsKey($file)) { throw "Missing source-reviewed GPU profile for $file" }
    $matches = @($overrides | Where-Object { $_.File -eq $file -and $row.Method -match $_.MethodPattern })
    if ($matches.Count -gt 1) { throw "Overlapping GPU method overrides: $($row.SourceKey)" }
    $profile = if ($matches.Count -eq 1) { $matches[0].Profile } else { $profiles[$file] }
    if (-not $definitions.ContainsKey($profile)) { throw "Unknown GPU profile $profile for $file" }
    $definition = $definitions[$profile]
    $contract = if ($row.Summary) { $row.Summary } else { $row.Method -replace '_', ' ' }
    $fixture = if ($row.Class -eq 'LumOnNearFieldFunctionalTests') {
        'NearFieldNumericalProgramFixture retains keyed executables by class; each case owns fresh scene inputs'
    } else { $definition.Lifetime }
    $entry = [pscustomobject]@{
        SourceKey = $row.SourceKey
        Level = $definition.Level
        Boundary = "$($definition.Boundary): $contract"
        RealCollaborators = $definition.Real
        Substitutes = $definition.Doubles
        ExternalEffects = $definition.Effects
        Oracle = $definition.Oracle
        Purpose = if ($purposeBySourceKey[$row.SourceKey]) { $purposeBySourceKey[$row.SourceKey] } else { 'Correctness' }
        Environment = if ($profile -eq 'U') { 'CPU rule; incidental GPU fixture possible' } else { 'Headless GPU' }
        FixtureLifetime = $fixture
        ProposedOwner = ($row.Class -replace 'Tests$', '')
        Evidence = if ($matches.Count -eq 1) { "$($row.SourcePath):$($row.SourceLine); $($matches[0].Reason)" } else { "$($row.SourcePath):$($row.SourceLine); source-reviewed $profile file profile" }
        Unresolved = if ($reconciledFiles.Contains($file)) { '' } elseif ($verifiedFiles.Contains($file)) { 'Source audited; group-row corrections and reconciliation pending' } else { 'Method action, oracle, and fixture lifetime require individual source review' }
    }
    foreach ($correction in $fieldCorrections) {
        if ($correction.File -ne $file -or $row.Method -notmatch $correction.MethodPattern) { continue }
        if (-not $entry.PSObject.Properties[$correction.Field]) { throw "Unknown group field: $($correction.Field)" }
        $entry.($correction.Field) = $correction.Value
    }
    if ($verifiedFiles.Contains($file)) { $entry.Evidence += '; every method body reviewed against source' }
    $output.Add($entry)
}

foreach ($override in $overrides) {
    $matched = @($source | Where-Object { [System.IO.Path]::GetFileName($_.SourcePath) -eq $override.File -and $_.Method -match $override.MethodPattern })
    if ($matched.Count -eq 0) { throw "Unmatched GPU override: $($override.File) $($override.MethodPattern)" }
}
foreach ($correction in $fieldCorrections) {
    $matched = @($source | Where-Object { [System.IO.Path]::GetFileName($_.SourcePath) -eq $correction.File -and $_.Method -match $correction.MethodPattern })
    if ($matched.Count -eq 0) { throw "Unmatched GPU field correction: $($correction.File) $($correction.MethodPattern)" }
}
foreach ($file in $verifiedFiles) {
    if (-not $profiles.ContainsKey($file)) { throw "Verified GPU file has no source profile: $file" }
}
foreach ($file in $reconciledFiles) {
    if (-not $verifiedFiles.Contains($file)) { throw "Reconciled GPU file was not source-audited: $file" }
}

$outputPath = Join-Path $inventoryDirectory 'GpuGroups-AM.csv'
$output | Sort-Object SourceKey | Export-Csv -LiteralPath $outputPath -NoTypeInformation -Encoding utf8
Write-Output "GPU A-M rows: $($output.Count); unresolved: $(@($output | Where-Object Unresolved).Count)"
