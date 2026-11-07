param(
    [string] $InventoryRoot = 'docs/testing-inventory'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

<#
.SYNOPSIS
Returns the principal level for a route with configuration-specific or mixed native behavior.
#>
function Get-PrincipalLevel {
    param([string] $RecordedLevel)
    if ($RecordedLevel -match '^Unit') { return 'Unit' }
    if ($RecordedLevel -match '^Component') { return 'Component' }
    if ($RecordedLevel -match '^Integration') { return 'Integration' }
    throw "Unrecognized level: $RecordedLevel"
}

<#
.SYNOPSIS
Assigns a stable subject directory using the reviewed owner and source location.
#>
function Get-Domain {
    param([string] $Project, [string] $SourcePath, [string] $Owner)
    if ($Project -eq 'OpenGLInterceptionGenerator.Tests' -or $Project -eq 'ShaderContractGenerator.Tests') { return 'Generation' }
    if ($Project -eq 'ShaderBuildTool.Tests') {
        if ($Owner -match 'receipt' ) { return 'Spirv/Build' }
        if ($Owner -match 'cache|record|identity|hash' ) { return 'Spirv/Cache' }
        if ($Owner -match 'compiler|compilation|process' ) { return 'Spirv/Compiler' }
        if ($Owner -match 'source|preprocess|dependency|GLSL|shader text' ) { return 'Spirv/Source' }
        return 'Spirv/Build'
    }
    $relative = $SourcePath.Substring($Project.Length + 1)
    if ($SourcePath -eq 'VanillaGraphicsExpanded.Tests/AtmosphereLookupTests.cs' -and $Owner -match 'diagnostic') { return 'Atmosphere/Diagnostics' }
    if ($relative -match '^Unit/([^/]+)(?:/([^/]+))?/' -and $Matches[1] -notin @('Rendering', 'PBR')) {
        $first = $Matches[1]
        $second = $Matches[2]
        if ($first -eq 'LumOn' -and $second -eq 'LumonScene') { return 'LumOn/Scene' }
        if ($first -eq 'LumOn' -and $second -eq 'WorldProbes') { return 'LumOn/WorldProbes' }
        if ($first -eq 'LumOn' -and $second -eq 'Scene') { return 'LumOn/Scene' }
        if ($second -and $first -notin @('WorldPartition', 'Noise', 'Cache')) { return "$first/$second" }
        return $first
    }
    if ($relative -match '^Unit/PBR/Materials/') { return 'PBR/Materials' }
    if ($relative -match '^Unit/Rendering/Contracts/') { return 'Rendering/Contracts' }
    $subject = [System.IO.Path]::GetFileNameWithoutExtension($relative) + ' ' + $Owner
    switch -Regex ($subject) {
        'Atmosphere|Scattering|AerialLookup' { return 'Atmosphere' }
        'AmbientOcclusion|\bAO\b|^Ao|Ssao' { return 'AmbientOcclusion' }
        'Water|Liquid' { return 'Water' }
        'Cloud|DynamicVolume' { return 'Clouds' }
        'LumOn|Lumon|WorldProbe|NearField|SurfaceLighting|SurfaceCache|TraceGeometry|SurfaceCapture|SurfaceFallback|SurfaceHit|SurfaceTraversal|SurfaceUpdate|SurfaceWork|SurfacePage|SharedTrace|SharedGeometry|RuntimeWait' { return 'LumOn' }
        'Terrain|Tessellation' { return 'Terrain' }
        'Celestial|Solar|Moon|SunDisk|Sky' { return 'Celestial' }
        'Material|Pbr|Microfacet|Brdf|BaseColor' { return 'PBR/Materials' }
        'Exposure|Bloom|ColorGrading|FinalDisplay|Filmic|VisualAdaptation|DepthHierarchy|SceneColor|Oit|Fog|Postprocess|Composite' { return 'Postprocessing' }
        'Config|Setting|ShadingQuality' { return 'Configuration' }
        'Vector|Matrix|SIMD|PooledArray' { return 'Numerics' }
        'DebugView|DebugViewer|DebugPanel|Debug-view' { return 'Rendering/Debug' }
        'Photometric|HeldLight|FrameLighting|DirectLighting|Lighting|FrameCamera' { return 'Lighting' }
        'Resource|Owner|Uniform|Ubo|Submission|Format|Intercept|OpenTK|GlError|Viewport|Raster|Prepared|Compute|ArrayTexture|Binding|Storage|Collection|Lifecycle|Knowledge|EngineEarlyDeletion|EngineStartupContext|ReadOnlyDepth|SceneCompatibility|StandaloneNumeric|StaticTarget' { return 'Rendering' }
        'Shader|Spirv|Program|Binding|Pipeline' { return 'Rendering/Shaders' }
        'Gpu|RHI|Framebuffer|Texture|Buffer|State|Render|Graphics|Image|Vao|Vbo|Ebo' { return 'Rendering' }
        'Partition|Chunk|Voxel' { return 'WorldPartition' }
        'Profil|EventSource|Counter' { return 'Profiling' }
        'Cache' { return 'Cache' }
        'Noise|Sample' { return 'Noise' }
        default {
            if ($relative -match '^Unit/Rendering/') { return 'Rendering' }
            if ($relative -match '^Unit/PBR/') { return 'PBR' }
            return 'Core'
        }
    }
}

<#
.SYNOPSIS
Forms a valid behavior suffix without treating punctuation in owner labels as a namespace.
#>
function Get-OwnerSuffix {
    param([string] $Owner)
    $words = [regex]::Matches($Owner, '[A-Za-z0-9]+') | ForEach-Object { $_.Value }
    return ($words | ForEach-Object { $_.Substring(0, 1).ToUpperInvariant() + $_.Substring(1) }) -join ''
}

<#
.SYNOPSIS
Applies the reviewed behavior names for the first mixed migration batches.
#>
function Get-ReviewedClassName {
    param([string] $SourcePath, [string] $Owner, [string] $Level, [string] $DefaultName)
    switch -Regex ($SourcePath) {
        'VanillaGraphicsExpanded.Tests/AtmosphereLookupTests.cs$' {
            if ($Owner -match 'diagnostic') { return 'AtmosphereLookupCostDiagnosticTests' }
            return 'AtmosphereLookupPublicationTests'
        }
        'VanillaGraphicsExpanded.Tests/AtmosphereScatteringQualityTests.cs$' {
            if ($Owner -match 'quality policy') { return 'AtmosphereQualityPolicyTests' }
            if ($Level -eq 'Unit') { return 'AtmosphereTransportRulesTests' }
            return 'AtmosphereLookupQualityLifecycleTests'
        }
        'VanillaGraphicsExpanded.Tests/GPU/GpuImageAssertionTests.cs$' {
            if ($Level -eq 'Integration') { return 'GpuImageNativeTransferTests' }
        }
        'VanillaGraphicsExpanded.Tests/GPU/LumOnNearFieldFunctionalTests(?:\.Ring)?.cs$' {
            if ($Level -eq 'Integration') { return 'NearFieldNativeBoundaryTests' }
        }
        'VanillaGraphicsExpanded.Tests/GPU/GpuEboIntegrationTests.cs$' {
            if ($Level -eq 'Unit') { return 'GpuEboManagedRulesTests' }
        }
        'VanillaGraphicsExpanded.Tests/GPU/GpuVboIntegrationTests.cs$' {
            if ($Level -eq 'Unit') { return 'GpuVboManagedRulesTests' }
        }
        'VanillaGraphicsExpanded.Tests/GPU/GpuVaoIntegrationTests.cs$' {
            if ($Level -eq 'Unit') { return 'GpuVaoManagedRulesTests' }
        }
        'VanillaGraphicsExpanded.Tests/GPU/GpuMappedBufferScopeIntegrationTests.cs$' {
            if ($Level -eq 'Unit') { return 'GpuMappedBufferScopeGuardTests' }
        }
        'VanillaGraphicsExpanded.Tests/GPU/LumonScenePipelineSmokeTests.cs$' { return 'LumonScenePipelinePublicationTests' }
        'VanillaGraphicsExpanded.Tests/GPU/ShaderDefineInjectionTests.cs$' { return 'DeclaredShaderVariantLoadingTests' }
        'VanillaGraphicsExpanded.Tests/Unit/PBR/Materials/MaterialAtlasParamsSyncAsyncParityTests.cs$' { return 'MaterialAtlasParamsPageTileParityTests' }
        'VanillaGraphicsExpanded.Tests/Unit/Rendering/ShaderDigestIndexCacheUnitTests.cs$' {
            if ($Level -eq 'Component') { return 'ShaderDigestIndexCacheTests' }
        }
        'VanillaGraphicsExpanded.Tests/Unit/LumOn/LumonScene/LumonSceneRegionSchedulerIntegrationSmokeTests.cs$' { return 'LumonSceneRegionSchedulingTests' }
        'VanillaGraphicsExpanded.Tests/Unit/LumOn/WorldProbes/WorldProbeTraceEndToEndVoxelArrangementTests.cs$' { return 'WorldProbeVoxelTracingTests' }
        'VanillaGraphicsExpanded.Tests/Unit/WorldPartition/PartitionCoverageTests.cs$' {
            if ($Level -eq 'Unit') { return 'PartitionCoordinateRulesTests' }
            return 'PartitionCoverageTests'
        }
        'ShaderBuildTool.Tests/ShaderCacheKeyTests.cs$' {
            if ($Level -eq 'Unit') { return 'ShaderCacheKeyTests' }
            return 'CompilerCachePersistenceTests'
        }
        'ShaderBuildTool.Tests/ShaderCompilerProcessTests.cs$' {
            if ($Level -eq 'Unit') { return 'ShaderCompilerCancellationGuardTests' }
            if ($Owner -match 'receipt') { return 'BuildReceiptReuseTests' }
            return 'ShaderCompilerProcessTests'
        }
    }
    return $DefaultName
}

$routes = @(Import-Csv (Join-Path $InventoryRoot 'Phase1.MigrationRouting.csv'))
$fileGroups = @{}
# Group methods before assigning class names so a multi-owner source is split consistently.
foreach ($route in $routes) {
    if (-not $fileGroups.ContainsKey($route.SourcePath)) { $fileGroups[$route.SourcePath] = @() }
    $fileGroups[$route.SourcePath] += $route
}
$moves = foreach ($route in $routes) {
    $project = $route.SourcePath.Split('/')[0]
    $level = Get-PrincipalLevel $route.Level
    $domain = Get-Domain $project $route.SourcePath $route.ProposedOwner
    $fileStem = [System.IO.Path]::GetFileNameWithoutExtension($route.SourcePath)
    $stem = ($fileStem -split '\.')[0]
    $owners = @($fileGroups[$route.SourcePath] | ForEach-Object { "$(Get-PrincipalLevel $_.Level)|$($_.ProposedOwner)" } | Sort-Object -Unique)
    $split = $owners.Count -gt 1
    $baseStem = $stem -replace 'Tests$', ''
    $ownerSuffix = Get-OwnerSuffix $route.ProposedOwner
    $sameLevelOwners = @($fileGroups[$route.SourcePath] | Where-Object { (Get-PrincipalLevel $_.Level) -eq $level } | Select-Object -ExpandProperty ProposedOwner -Unique)
    $defaultClass = if ($route.SourcePath -match '/Gpu(?:Ebo|Vbo)UnitTests.cs$' -and $route.ProposedOwner -match 'witness gap') { "${baseStem}${ownerSuffix}Tests" }
        elseif ($sameLevelOwners.Count -eq 1 -or $ownerSuffix -eq $baseStem) { $stem }
        elseif ($ownerSuffix.StartsWith($baseStem, [StringComparison]::Ordinal)) { "${ownerSuffix}Tests" }
        else { "${ownerSuffix}Tests" }
    $pendingCorrection = ($route.SourcePath -match '/Unit/Gpu(?:Ebo|Vbo)UnitTests.cs$') -or
        ($route.SourcePath -eq 'VanillaGraphicsExpanded.Tests/PbrGlobstarTests.cs' -and $route.ProposedOwner -match 'current case does not reach selector') -or
        ($route.SourcePath -eq 'VanillaGraphicsExpanded.Tests/Squirrel3NoiseTests.cs' -and $route.ProposedOwner -match 'current name overclaims shader parity')
    $class = if ($pendingCorrection) { $stem } else { Get-ReviewedClassName $route.SourcePath $route.ProposedOwner $level $defaultClass }
    $fileName = if ($fileStem.Contains('.')) { "$class.$($fileStem.Substring($stem.Length + 1)).cs" } else { "$class.cs" }
    $target = if ($pendingCorrection) { $route.SourcePath } else { "$project/$level/$domain/$fileName" }
    $targetNamespace = if ($pendingCorrection -and $route.SourcePath -match '/Unit/') { "$project.Unit" }
        elseif ($pendingCorrection) { $project }
        else { "$project.$level.$($domain.Replace('/', '.'))" }
    [pscustomobject]@{
        SourceKey = $route.SourceKey
        SourcePath = $route.SourcePath
        Method = $route.Method
        RecordedLevel = $route.Level
        ProposedOwner = $route.ProposedOwner
        TargetPath = $target
        TargetNamespace = $targetNamespace
        TargetClass = $class
        Disposition = if ($pendingCorrection) { 'RetainPendingOracleDecision' } else { 'Move' }
        SplitSource = $split
        CurrentRuntimeCases = $route.RuntimeCases
    }
}
$moves | Export-Csv (Join-Path $InventoryRoot 'Phase2.MethodMoves.csv') -NoTypeInformation

# Keep each support source's lifetime cue alongside the proposed owning area.
$support = foreach ($source in Import-Csv (Join-Path $InventoryRoot 'Phase1.SupportRouting.csv')) {
    $project = $source.SourcePath.Split('/')[0]
    $group = $source.ProposedOwner
    $area = if ($project -ne 'VanillaGraphicsExpanded.Tests') { 'Support/Generation' }
        elseif ($group -match 'Water support') { 'Component/Water/Fixtures' }
        elseif ($group -match 'Ambient occlusion support') { 'Component/AmbientOcclusion/Fixtures' }
        elseif ($group -match 'Cloud support') { 'Component/Clouds/Fixtures' }
        elseif ($group -match 'Near-field support') { 'Component/LumOn/Fixtures' }
        elseif ($group -match 'Precompiled terrain|Terrain stage|Engine terrain') { 'Component/Terrain/Fixtures' }
        elseif ($group -match 'CPU multiple-scattering numerics') { 'Unit/Atmosphere/Fixtures' }
        elseif ($group -match 'Owning test partials') { 'Component/LumOn' }
        elseif ($group -match 'World partition') { 'Component/WorldPartition/Fixtures' }
        elseif ($group -match 'Controlled voxel world|World-probe room') { 'Component/LumOn/WorldProbes/Fixtures' }
        elseif ($group -match 'Authored shader declarations') { 'Unit/Rendering/Contracts/Fixtures' }
        elseif ($group -match 'Unreferenced legacy uniform helper') { 'Support/Rendering/Uniforms' }
        elseif ($group -match 'GPU observation') { 'Support/GPU/Observations' }
        elseif ($group -match 'Shared GPU harness') { 'Support/GPU/Harness' }
        elseif ($group -match 'Built SPIR-V|Installed-engine shader|Native link|Driver-binary') { 'Support/GPU/Shaders' }
        elseif ($group -match 'Numerical shader|Prepared shader interface') { 'Support/GPU/Shaders' }
        elseif ($group -match 'Shared CPU references') { 'Support/Numerics' }
        elseif ($group -match 'RHI architecture') { 'Support/Architecture' }
        elseif ($group -match 'Shared asset|source and diagnostic') { 'Support/Assets' }
        elseif ($group -match 'Collection policy|Runner environment') { 'Support/Policy' }
        elseif ($group -match 'Native|GPU|Framebuffer|Frame and uniform') { 'Support/GPU/Native' }
        elseif ($group -match 'Lighting|sky|Surface|World-probe|Trace geometry') { 'Support/Lighting' }
        elseif ($group -match 'cache|Cache|Material') { 'Support/Cache' }
        else { 'Support/Core' }
    $file = [System.IO.Path]::GetFileName($source.SourcePath)
    [pscustomobject]@{
        SourcePath = $source.SourcePath
        ProposedOwner = $group
        TargetPath = "$project/$area/$file"
        TargetNamespace = "$project.$($area.Replace('/', '.'))"
        LifetimeCue = $source.LifetimeCue
        DependencyCheck = 'Review direct and transitive consumers, partial types, collection fixtures, disposal, and linked assets before move'
    }
}
$support | Export-Csv (Join-Path $InventoryRoot 'Phase2.SupportMoves.csv') -NoTypeInformation

$duplicateTargets = @($moves | Group-Object TargetPath | Where-Object { @($_.Group.SourcePath | Sort-Object -Unique).Count -gt 1 })
$duplicateSupport = @($support | Group-Object TargetPath | Where-Object { @($_.Group.SourcePath | Sort-Object -Unique).Count -gt 1 })
if ($duplicateTargets.Count -gt 0 -or $duplicateSupport.Count -gt 0) {
    throw "Duplicate destination paths: test=$($duplicateTargets.Count), support=$($duplicateSupport.Count)"
}
Write-Output "Mapped $($routes.Count) test methods from $($fileGroups.Count) files and $($support.Count) support files."
