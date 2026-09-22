<#
.SYNOPSIS
Checks shader build invalidation with isolated vertex, fragment and compute assets.
.DESCRIPTION
Build ShaderBuildTool and restore the repository's dotnet tools before running this script.
All mutated assets, compiler copies and outputs are retained beneath artifacts/spirv-build-contract.
#>
param([string]$RepositoryRoot = (Resolve-Path "$PSScriptRoot/../..").Path)
$ErrorActionPreference = 'Stop'
$root = Join-Path $RepositoryRoot ('artifacts/spirv-build-contract/run-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
$assets = Join-Path $root 'assets'
$shaders = Join-Path $assets 'vanillagraphicsexpanded/shaders'
$output = Join-Path $root 'output'
$tool = Join-Path $root 'tool'
$config = Join-Path $root '.config'
New-Item -ItemType Directory -Force $shaders,$tool,$config | Out-Null
Copy-Item -Path "$RepositoryRoot/ShaderBuildTool/bin/Debug/net8.0/*" -Destination $tool -Recurse
Copy-Item -LiteralPath "$RepositoryRoot/.config/dotnet-tools.json" -Destination $config
$compilerVersion = (Get-Content "$config/dotnet-tools.json" -Raw | ConvertFrom-Json).tools.'dotnet-shaderc'.version
$originalPackages = $env:NUGET_PACKAGES
$packageCache = if ($originalPackages) { $originalPackages } else { Join-Path ([Environment]::GetFolderPath('UserProfile')) '.nuget/packages' }
$compilerPackage = Join-Path $root "packages/dotnet-shaderc/$compilerVersion"
New-Item -ItemType Directory -Force $compilerPackage | Out-Null
Copy-Item -Path "$packageCache/dotnet-shaderc/$compilerVersion/*" -Destination $compilerPackage -Recurse
try {
$env:NUGET_PACKAGES = Join-Path $root 'packages'
$rows = [Collections.Generic.List[object]]::new()
$registryScope = "build-validation"
<# .SYNOPSIS Runs the copied build tool against bounded isolated fixture inputs. #>
function Invoke-ProbeBuild([string]$Name,[string]$WorkingDirectory = $root) {
    $watch = [Diagnostics.Stopwatch]::StartNew()
    $log = & dotnet "$tool/ShaderBuildTool.dll" --assetsRoot $assets --outputRoot $output --workingDir $WorkingDirectory --registry $registryScope --clean --incremental 2>&1
    $status = $LASTEXITCODE
    $log | Set-Content "$root/$Name.log"
    if ($status -ne 0) { throw "Build $Name failed: $($log -join [Environment]::NewLine)" }
    $rows.Add([pscustomobject]@{Name=$Name;Seconds=$watch.Elapsed.TotalSeconds;Log="$Name.log"})
    return ($log -join "`n")
}
<# .SYNOPSIS Fails immediately instead of publishing a partially verified receipt. #>
function Assert-Probe([bool]$Condition,[string]$Message) { if (!$Condition) { throw $Message } }
<# .SYNOPSIS Captures hashes and write times, excluding transient compiler inputs. #>
function Get-PublishedSnapshot {
    $result = @{}
    Get-ChildItem -LiteralPath $output -File -Recurse | Where-Object { $_.FullName -notlike "$output\_tmp\*" } | ForEach-Object {
        $result[[IO.Path]::GetRelativePath($output,$_.FullName)] = ((Get-FileHash -LiteralPath $_.FullName).Hash + '|' + $_.LastWriteTimeUtc.Ticks)
    }
    return $result
}
@'
#version 450 core
layout(location=0) in vec3 position;
layout(location=0) out vec3 color;
void main(){ gl_Position=vec4(position,1); color=vec3(1); }
'@ | Set-Content "$shaders/fixture.vsh"
@'
#version 450 core
@import "fixture.inc"
#ifndef VGE_LUMON_ENABLED
#define VGE_LUMON_ENABLED 1
#endif
layout(location=0) in vec3 color;
layout(location=0) out vec4 result;
void main(){
#if VGE_LUMON_ENABLED
result=vec4(color*FACTOR,1);
#else
result=vec4(0,0,0,1);
#endif
}
'@ | Set-Content "$shaders/fixture.fsh"
'#define FACTOR 0.5' | Set-Content "$shaders/fixture.inc"
@'
#version 450 core
layout(local_size_x=1) in;
layout(std430,binding=0) buffer Data { uint value; };
void main(){value=1;}
'@ | Set-Content "$shaders/fixture.csh"
$cleanLog = Invoke-ProbeBuild 'clean'
Assert-Probe ($cleanLog.Contains('Programs: 2 programs, 2 combinations')) 'Program configuration count changed'
Assert-Probe ($cleanLog.Contains('Stages: 3 stages, 3 variants')) 'Distinct stage binary count changed'
Assert-Probe (@(Get-ChildItem "$output/vanillagraphicsexpanded/shaders" -Filter '*.spv' -File -Recurse).Count -eq 3) 'Expected exactly three published stage binaries'
Assert-Probe (@(Get-ChildItem "$output/_tmp" -Filter '*.glsl' -File -Recurse).Count -eq 3) 'Expected one compiler input per distinct stage binary'
# Coverage must reject both unknown owned entry points and registered missing sources before compilation.
'not valid GLSL; coverage must reject this first' | Set-Content "$shaders/unregistered.csh"
$unknownLog = & dotnet "$tool/ShaderBuildTool.dll" --assetsRoot $assets --outputRoot $output --workingDir $root --registry $registryScope --clean --incremental 2>&1
$unknownStatus = $LASTEXITCODE
$unknownLog | Set-Content "$root/unregistered-source.log"
Assert-Probe ($unknownStatus -ne 0 -and ($unknownLog -join "`n").Contains('unregistered.csh')) 'Unknown source did not fail with its identity'
Assert-Probe (@(Get-ChildItem $output -File -Recurse -ErrorAction SilentlyContinue | Where-Object { $_.Extension -in '.glsl', '.spv' }).Count -eq 0) 'Unknown source validation produced compiler inputs or binaries'
Remove-Item -LiteralPath "$shaders/unregistered.csh"
$missingSource = [IO.File]::ReadAllText("$shaders/fixture.csh")
Remove-Item -LiteralPath "$shaders/fixture.csh"
$coverageLog = & dotnet "$tool/ShaderBuildTool.dll" --assetsRoot $assets --outputRoot $output --workingDir $root --registry $registryScope --clean --incremental 2>&1
$coverageStatus = $LASTEXITCODE
$coverageLog | Set-Content "$root/missing-registered-source.log"
Assert-Probe ($coverageStatus -ne 0 -and ($coverageLog -join "`n").Contains('fixture.csh')) 'Missing registered source did not fail with its identity'
Assert-Probe (@(Get-ChildItem $output -File -Recurse -ErrorAction SilentlyContinue | Where-Object { $_.Extension -in '.glsl', '.spv' }).Count -eq 0) 'Missing source validation produced compiler inputs or binaries'
[IO.File]::WriteAllText("$shaders/fixture.csh", $missingSource)
Invoke-ProbeBuild 'coverage-restored' | Out-Null
Assert-Probe (!(Get-ChildItem "$output/vanillagraphicsexpanded" -Filter '*.spirv.json' -Recurse)) 'Unexpected runtime manifests'
# Recompile the identical emitted source independently: publication must preserve compiler bytes.
$variantHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes(''))).ToLowerInvariant()
$compilerInput = Join-Path $output "_tmp/fixture.fsh.$variantHash.glsl"
$referenceBinary = Join-Path $root 'compiler-reference.spv'
Push-Location $root
try {
    $compilerLog = & dotnet tool run dotnet-shaderc -- --shader-stage=fragment --target-env=opengl4.5 -g -x=glsl -o $referenceBinary $compilerInput 2>&1
    $compilerStatus = $LASTEXITCODE
    $compilerLog | Set-Content "$root/compiler-reference.log"
} finally { Pop-Location }
Assert-Probe ($compilerStatus -eq 0) 'Independent compiler invocation failed'
$publishedBinary = Join-Path "$output/vanillagraphicsexpanded/shaders" "fixture.fsh.spv"
Assert-Probe ((Get-FileHash $referenceBinary).Hash -eq (Get-FileHash $publishedBinary).Hash) 'Published SPIR-V differs from compiler output'
$initial = Get-PublishedSnapshot
$log = Invoke-ProbeBuild 'unchanged'
$current = Get-PublishedSnapshot
Assert-Probe ($log.Contains('are current')) 'Unchanged build did not skip compilation'
Assert-Probe (($initial.Keys.Count -eq $current.Keys.Count) -and (@($initial.Keys | Where-Object { $initial[$_] -ne $current[$_] }).Count -eq 0)) 'Unchanged build rewrote outputs'
New-Item -ItemType Directory -Force "$root/path-alias" | Out-Null
$log = Invoke-ProbeBuild 'equivalent-working-directory' "$root/path-alias/.."
Assert-Probe ($log.Contains('are current')) 'Equivalent working-directory spelling rebuilt outputs'
$before = (Get-Content "$output/build-receipt.json" -Raw | ConvertFrom-Json).Inputs
Add-Content "$shaders/fixture.vsh" '// changed source'
$log = Invoke-ProbeBuild 'source-change'
Assert-Probe (!$log.Contains('are current')) 'Source mutation not rebuilt'
Assert-Probe ((Get-Content "$output/build-receipt.json" -Raw | ConvertFrom-Json).Inputs -ne $before) 'Source fingerprint unchanged'
'#define FACTOR 0.75' | Set-Content "$shaders/fixture.inc"
$log = Invoke-ProbeBuild 'include-change'
Assert-Probe (!$log.Contains('are current')) 'Include mutation not rebuilt'
(Get-Content "$shaders/fixture.fsh" -Raw).Replace('#define VGE_LUMON_ENABLED 1','#define VGE_LUMON_ENABLED 0') | Set-Content "$shaders/fixture.fsh"
Invoke-ProbeBuild 'define-change' | Out-Null
Assert-Probe ((Get-Content $compilerInput -Raw).Contains('#define VGE_LUMON_ENABLED 0')) 'Source default not refreshed'
$binary = (Get-ChildItem "$output/vanillagraphicsexpanded/shaders" -Filter '*.spv' -File -Recurse | Select-Object -First 1).FullName
Remove-Item -LiteralPath $binary
Invoke-ProbeBuild 'missing-binary' | Out-Null
Assert-Probe (Test-Path -LiteralPath $binary) 'Missing binary not regenerated'
[IO.File]::WriteAllText($binary,'corrupt')
Invoke-ProbeBuild 'corrupt-binary' | Out-Null
Assert-Probe ((Get-Item -LiteralPath $binary).Length -gt 7) 'Corrupted binary not regenerated'
'outdated' | Set-Content "$output/vanillagraphicsexpanded/shaders/stale.spv"
Invoke-ProbeBuild 'extra-output' | Out-Null
Assert-Probe (!(Test-Path "$output/vanillagraphicsexpanded/shaders/stale.spv")) 'Stale output retained'
Remove-Item -LiteralPath "$shaders/fixture.csh"
$registryScope = "build-validation-graphics"
Invoke-ProbeBuild 'removed-source' | Out-Null
Assert-Probe (!(Test-Path "$output/vanillagraphicsexpanded/shaders/fixture.csh.spv")) 'Removed compute alias retained'
# A harmless extra DLL exercises the tool-content input set without corrupting dependencies.
[IO.File]::WriteAllBytes("$tool/fingerprint-probe.dll",[byte[]](1,2,3,4))
$log = Invoke-ProbeBuild 'tool-input-change'
Assert-Probe (!$log.Contains('are current')) 'Tool input mutation not rebuilt'
# An unused platform native library still belongs to the compiler identity and must invalidate the build.
$nativeCompiler = Get-ChildItem "$compilerPackage/tools" -Filter '*.so' -Recurse | Select-Object -First 1
$nativeBytes = [IO.File]::ReadAllBytes($nativeCompiler.FullName)
$nativeBytes[$nativeBytes.Length - 1] = $nativeBytes[$nativeBytes.Length - 1] -bxor 1
[IO.File]::WriteAllBytes($nativeCompiler.FullName,$nativeBytes)
$log = Invoke-ProbeBuild 'compiler-content-change'
Assert-Probe (!$log.Contains('are current')) 'Compiler content mutation not rebuilt'
Add-Content "$config/dotnet-tools.json" ' '
$log = Invoke-ProbeBuild 'compiler-config-change'
Assert-Probe (!$log.Contains('are current')) 'Compiler manifest mutation not rebuilt'
$package = Join-Path $root 'package/assets/vanillagraphicsexpanded'
New-Item -ItemType Directory -Force $package | Out-Null
Copy-Item -Path "$output/vanillagraphicsexpanded/*" -Destination $package -Recurse
$publishedRoot = Join-Path $output 'vanillagraphicsexpanded'
foreach ($file in (Get-ChildItem $publishedRoot -Filter '*.spv' -Recurse)) {
    $packaged = Join-Path $package ([IO.Path]::GetRelativePath($publishedRoot, $file.FullName))
    Assert-Probe (Test-Path -LiteralPath $packaged) 'Packaged binary missing'
    Assert-Probe ((Get-FileHash -LiteralPath $packaged).Hash -eq (Get-FileHash -LiteralPath $file.FullName).Hash) 'Packaged binary differs'
}
Assert-Probe (!(Get-ChildItem $package -Filter '*.json' -Recurse)) 'Runtime metadata or build receipts were packaged'
# Absence of the complete input directory must fail, never report stale outputs as current.
$resolvedShaders = [IO.Path]::GetFullPath($shaders)
if (!$resolvedShaders.StartsWith($root + [IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe fixture path' }
Move-Item -LiteralPath $resolvedShaders -Destination (Join-Path $root 'removed-shaders')
$missingLog = & dotnet "$tool/ShaderBuildTool.dll" --assetsRoot $assets --outputRoot $output --workingDir $root --registry $registryScope --clean --incremental 2>&1
$missingStatus = $LASTEXITCODE
$missingLog | Set-Content "$root/removed-directory.log"
Assert-Probe ($missingStatus -ne 0) 'Missing shader directory incorrectly succeeded'
$rows.Add([pscustomobject]@{Name='removed-directory-explicit-failure';ExitCode=$missingStatus;Log='removed-directory.log'})
$rows | ConvertTo-Json | Set-Content "$root/receipts.json"
'PASS: declared program/stage counts, unique compiler inputs, unknown/missing source rejection, unchanged compiler binary, clean, unchanged hash+mtime, source/include/define, missing/corrupt/extra output, removed source, tool/config fingerprint, metadata-free isolated package variant integrity.' | Set-Content "$root/result.txt"
Write-Output $root
} finally {
    $env:NUGET_PACKAGES = $originalPackages
}
