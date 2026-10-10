<#
.SYNOPSIS
Runs the maintained shader build contract tests against isolated fixture assets.
.DESCRIPTION
Integration tests own compiler invocation, cached incremental work assertions, repair,
publication and runtime manifest validation. Fixtures use unique temporary directories;
production shader assets and outputs are not rebuilt. Run the shader-enabled mod build
separately to validate MSBuild asset copying.
.PARAMETER RepositoryRoot
Repository containing ShaderBuildTool.Tests and the pinned compiler tool manifest.
.PARAMETER Configuration
Managed build configuration, which also selects shader debug-information policy.
#>
param(
    [string]$RepositoryRoot = (Resolve-Path "$PSScriptRoot/../..").Path,
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug'
)
$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath($RepositoryRoot)
$project = Join-Path $repository 'ShaderBuildTool.Tests/ShaderBuildTool.Tests.csproj'
if (!(Test-Path -LiteralPath $project -PathType Leaf)) {
    throw "Shader build test project is missing: $project"
}
# Reuse owning fixtures rather than duplicating cache keys, compiler policy or manifest rules.
$families = @(
    'ShaderIncrementalBuildTests', 'ShaderSelectiveBuildTests', 'ShaderMigrationRepairTests',
    'ShaderParallelBuildTests', 'ShaderPublicationTests', 'ShaderPublicationJournalTests',
    'ShaderOutputLeaseTests', 'ShaderDigestReceiptTests', 'ShaderReceiptShortcutTests', 'PackagedInterfaceValidationTests',
    'ShaderCompilerPathIdentityTests'
)
$filter = ($families | ForEach-Object { "FullyQualifiedName~$_" }) -join '|'
Push-Location $repository
try {
    & dotnet tool restore
    if ($LASTEXITCODE -ne 0) { throw "Shader compiler tool restore failed with exit code $LASTEXITCODE." }
    & dotnet test $project -c $Configuration --filter $filter -p:UseSharedCompilation=false -nr:false -m:1 --tl:off -v:minimal
    if ($LASTEXITCODE -ne 0) { throw "Shader build contract tests failed with exit code $LASTEXITCODE." }
} finally {
    Pop-Location
}
