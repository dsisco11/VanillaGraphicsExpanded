# Shader variant contract migration inventory

Planning baseline for [ShaderVariantContracts.todo](ShaderVariantContracts.todo), phase 1; reviewed 2026-09-22. Authority: [approved proposal](ShaderVariantContracts.proposal.md). This is a source inventory, not a new build/GPU/runtime receipt. No production behavior changes in this inventory.

## Traceability and scope

| Task | Proposal sections | Requirement | Evidence in this inventory |
| --- | --- | --- | --- |
| 1. Entry points and owners | Programs and stages; Main objects and ownership | Explicit production and fixture identities, shared stages, bindings | Program catalog and pairing exceptions below; source catalog excludes includes |
| 2. Writers and consumers | Settings and their stage uses; Runtime flow | Trace application configuration to effective stage inputs | Writer map, no-effect classifications, source references |
| 3. Types and domains | Aliases and defaults; Conditional specialization constants | Defaults, aliases, numeric IDs, availability and range evidence | Structural and numeric tables; compatibility decisions |
| 4. Assignments and budgets | Supported configurations and binary identity | Preserve supported choices, defaults, sharing and paths | Per-program catalog budgets, full Boolean products, identity rules |
| 5. Migration baseline | Verification and diagnostics; Runtime flow | Representative behavioral and ownership proof targets | Scenario matrix and evidence boundaries |

Sources reviewed: `Rendering/Contracts/GpuShaderContracts{,.Stages,.Specializations,.Locations}.cs`, `ShaderStageContract.cs`, `Rendering/Shaders/VgeShaderDefine.cs`, `GpuProgram{,.Spirv}.cs`, `Stages/Shader.cs`, `Rendering/Spirv/SpirvStageLoader.cs`, `Rendering/GpuComputePipeline.cs`, `ShaderBuildTool/Spirv/ShaderVariantBuild.cs`, `ShaderVariantSource.cs`, `ShaderBuildTool/Tests/ValidateBuildContract.ps1`, `LumOn/VgeConfig.cs`, `LumOn/LumOnRenderer.cs`, `LumOn/LumOnDebugRenderer.cs`, shader owners listed below, shader source/includes, and `VanillaGraphicsExpanded.Tests/GPU` helpers/fixtures. Paths without a project prefix are relative to `VanillaGraphicsExpanded`.

## Structural options and assignments

All current structural settings are Boolean values represented by `0`/`1`. Preserve their complete Cartesian products, including override combinations whose outputs happen to coincide. No supported-subset pruning is justified by this inventory. Defaults below are contract defaults, not necessarily the application's configured startup values. Application PIS defaults to enabled (`EnableProbePIS=true`), with both override flags false; the renderer sets I from `EnableProbePIS || ForceUniformMask`. N and D are enabled by their runtime binding paths. W follows resource availability/configuration. Keep these application selections separate from the safe offline defaults. Every vertex and compute stage currently has an empty structural assignment. All configurable stage uses below are fragment-stage uses.

Abbreviations in the program catalog expand to these exact canonical macros:

| Key | Macro | Default | Owners / meaning |
| --- | --- | --- | --- |
| E | VGE_LUMON_ENABLED | 1 | Combine, PBR composite, global-defines smoke; include indirect contribution |
| P | VGE_LUMON_PBR_COMPOSITE | 1 | Combine, debug composite/dispatcher, PBR composite, smoke; composition branch |
| A | VGE_LUMON_ENABLE_AO | 1 | Combine, debug composite/dispatcher, smoke; no PBR-composite stage use |
| S | VGE_LUMON_ENABLE_SHORT_RANGE_AO | 1 | Combine, debug composite/dispatcher, PBR composite, smoke; legacy alias VGE_LUMON_ENABLE_BENT_NORMAL |
| D | VGE_LUMON_DIRECT_LOCAL_VISIBILITY | 0 | Debug family, trace, atlas/SH9 gather, orbs; near-field visibility branch |
| N | VGE_LUMON_NEAR_FIELD_ENABLED | 0 | Trace; geometry continuation after screen trace |
| W | VGE_LUMON_WORLDPROBE_ENABLED | 0 | Trace, atlas/SH9 gather, debug dispatcher/worldprobe |
| I | VGE_LUMON_PROBE_PIS_ENABLED | 0 | Trace, temporal, PIS mask |
| B | VGE_LUMON_PROBE_PIS_FORCE_BATCH_SLICING | 0 | Trace, temporal, PIS mask |
| U | VGE_LUMON_PROBE_PIS_FORCE_UNIFORM_MASK | 0 | PIS mask only |
| DN | VGE_LUMON_UPSAMPLE_DENOISE | 1 | Upsample |
| HF | VGE_LUMON_UPSAMPLE_HOLEFILL | 1 | Upsample |

Budget proposal: each program's maximum equals its current complete product, as listed in the catalog. This catches expansion without imposing new setting restrictions. Numeric specializations do not multiply binaries. Unconfigured programs have budget 1. Alternate fixture pairings retain the fragment's budget. Changes to these budgets require an explained contract change; these are proposed limits, not limits enforced today.

## Numeric specializations

Names in the following table are exact macro names. Unless stated otherwise there is no alias. Current contracts accept parseable numeric overrides without explicit domain validation; `SpirvStageLoader` encodes declared int or float values. The typed system must retain 32-bit scalar values and reject nonfinite floats/fractional integers rather than passing all types through double. There are no current uint/bool specialization consumers; their support needs source-emission tests before use, not a guessed conversion to int/float.

Application sanitation is evidence for ordinary application values, **not** proof that narrower bounds may be imposed on public setters or fixtures. Preserve existing shader defaults and fixture inputs, including zero topology sentinels. Until a narrower contract domain is justified, use the representable scalar domain (finite for float), recording application bounds as guidance. Resource consistency still belongs to resource owners.

| ID | Macro | Type / contract default | Stage use and condition | Application evidence |
| --- | --- | --- | --- | --- |
| 0 | LUMON_EMISSIVE_BOOST | float / 1 | trace, always | EmissiveGiBoost config default 3, sanitized 0..64; renderer also max(0,value) |
| 1 | VGE_LUMON_ATLAS_TEXELS_PER_FRAME | int / 16 | trace, temporal, PIS mask; always | ProbeAtlasTexelsPerFrame 1..64 |
| 2 | VGE_LUMON_HZB_COARSE_MIP | int / 4 | trace, always | config default 3, 0..12; renderer further clamps to available mip levels |
| 3 | VGE_LUMON_RAY_MAX_DISTANCE | float / 4 | trace, always | config default 2; 0.25..256 |
| 4 | VGE_LUMON_RAY_STEPS | int / 10 | trace, always | 1..512 |
| 5 | VGE_LUMON_RAY_THICKNESS | float / 0.5 | trace, always | config default 0.1; 0.01..16 |
| 6 | VGE_LUMON_SKY_MISS_WEIGHT | float / 0.5 | trace, only N=0 | config default 0.1; 0..1 |
| 7 | VGE_LUMON_PROBE_PIS_MIN_CONFIDENCE_WEIGHT | float / 0.1 | PIS mask, always | 0..1 |
| 8 | VGE_LUMON_PROBE_PIS_EXPLORE_COUNT | int / -1 | PIS mask, I=1 AND B=0 AND U=0 | -1..64; negative means derive from fraction |
| 9 | VGE_LUMON_PROBE_PIS_EXPLORE_FRACTION | float / 0.25 | same condition as ID 8 | 0..1 |
| 10 | VGE_LUMON_PROBE_PIS_WEIGHT_EPSILON | float / 1e-6 | same condition as ID 8 | 1e-8..1 |
| 11 | VGE_LUMON_WORLDPROBE_BASE_SPACING | float / 0 | trace, atlas/SH9 gather, debug dispatcher/worldprobe; W=1 | world config default 1.5, 0.25..64; disabled writers use 0 |
| 12 | VGE_LUMON_WORLDPROBE_LEVELS | int / 0 | same as ID 11 | config default 3, 1..8; disabled writers use 0 |
| 13 | VGE_LUMON_WORLDPROBE_OCTAHEDRAL_SIZE | int / 16 | same as ID 11; orbs unconditional | config 8..64; fixtures may use smaller tiles |
| 14 | VGE_LUMON_WORLDPROBE_RESOLUTION | int / 0 | same as ID 13 | config default 20, 8..128; disabled writers use 0; fixtures use smaller grids |
| 15 | VGE_LUMON_WORLDPROBE_DIFFUSE_STRIDE | int / 2 | atlas/SH9 gather, debug dispatcher/worldprobe; W=1; absent from trace and orbs | setters max(1,value); do not infer an upper bound |

Source availability evidence: `includes/lumon_worldprobe.glsl`, `lumon_worldprobe_atlas.glsl`, `lumon_worldprobe_gather.glsl`, `lumon_worldprobe_visibility.glsl`, `vge_global_defines.glsl`, and trace/PIS/temporal entry points. Conditions above describe the existing declarations and are the preservation baseline; later GPU branch tests remain required. Availability does not imply that every numeric value produces useful lighting.

## Writer destinations and ineffective settings

| Writer / owner | Destination | Migration disposition |
| --- | --- | --- |
| `LumOnCombineShaderProgram`, `PBRCompositeShaderProgram` feature setters | E/P/A/S projected to declared consumers | Preserve combine's four; PBR composite consumes E/P/S; its EnableAO write has no current effect |
| `LumOnDebugShaderProgramFamily.ApplyCompositeDefines`, debug setters | P/A/S on dispatcher and composite | Current family broadcasts to all debug programs; project only to members accepting these settings |
| `LumOnDebugShaderProgramFamily.ApplyWorldProbeClipmapDefines`, debug topology setters | W and IDs 11/12/14 on dispatcher/worldprobe | Ignore no longer by accident: remove irrelevant broadcast writes through explicit group membership |
| `LumOnScreenProbeAtlasTraceShaderProgram` trace properties | IDs 1..6 | Preserve types and effective numeric changes |
| `LumOnRenderer` emissive writes | ID 0 on trace | The write during velocity-pass setup has no contract consumer and no binary effect; remove that write, retain trace write |
| `LumOnProbeAtlasPisMaskShaderProgram` PIS setters | I/B/U, IDs 1 and 7..10 | Preserve all and conditional availability |
| `LumOnScreenProbeAtlasTraceShaderProgram` and `LumOnScreenProbeAtlasTemporalShaderProgram` PIS setters | I/B and ID 1 | Explore fraction/count/confidence/epsilon and force-uniform writes have no stage use here; remove these writes while preserving PIS-mask writes |
| Trace/atlas gather/SH9 gather/debug `EnsureWorldProbeDefines` and topology properties | W and supported IDs 11..15 | Trace does not consume stride; preserve stage-specific projection |
| `VgeWorldProbeOrbsPointsShaderProgram.EnsureWorldProbeDefines` | IDs 13/14 only | W, levels, base spacing, atlas update count, diffuse stride and bind-atlas writes do not change its binary or specializations; retain relevant resource binding separately |
| `LumOnUpsampleShaderProgram` setters | DN/HF | Preserve |
| `LumOnScreenProbeAtlasTraceShaderProgram.NearField.EnsureNearFieldDefines` | N=1 | Preserve explicit trace membership |
| `LumOnRenderer`, `LumOnDebugRenderer` using `LumOnNearFieldVisibilityBindings.EnabledDefine` | D=1 | Preserve on visibility-consuming stages listed in structural table |
| `GpuProgram.SetDefine` / `RemoveDefine` | Generic per-instance dictionary | Compatibility adapter; unknown keys currently tolerated, later reject explicit unknown settings; null currently stored but resolver falls back to default |
| `GpuComputePipeline.TryCreateFromAssets` / `TryCreateFromAssetsPreferSpirv`, `GpuShaderModule` compute forwarding | Optional dictionary forwarded to loader | Current production compute callers use no configurable settings; declare explicit empty program contracts |
| `GpuComputePipeline.TryLoadFromSpirv` | Direct file, entry main and empty specialization arguments; binding lookup derives basename | Preserve supported direct-file loading; supply explicit fixture/program identity in migration, no adjacent metadata |
| GPU helpers `ShaderTestHelper`, `BuiltShaderFixture`, functional test base and dictionary callers | Stage load dictionaries, including irrelevant legacy keys | Migrate tests to explicit fixture program settings; preserve intentional negative/unknown-option tests separately |

No-effect catalog (do not register as functional owned-program options):

- `VGE_LUMON_TEMPORAL_USE_VELOCITY_REPROJECTION` (default 1) and `VGE_LUMON_RAYS_PER_PROBE` (default 12): named constants and guarded source defaults, no algorithm use/current production setter. Tests injecting them do not prove a working switch.
- `VGE_LUMON_WORLDPROBE_ATLAS_TEXELS_PER_UPDATE` (source default 32): written by trace/gathers/debug/orbs; only an unused derived constant remains in the atlas include. CPU scheduling/update budgets remain real functionality outside variants.
- `VGE_LUMON_BIND_WORLDPROBE_RADIANCE_ATLAS`: written with enable state by the same owners, absent as an effective shader option. Actual sampler binding must remain.
- `VGE_PBR_DEBUG_VIEW_MODE` (default 0): `PBRCompositeShaderProgram.DebugViewMode` writes it, but only its guarded default remains in GLSL. Current debug output uses other routing/UBO state; do not invent a finite debug-mode variant domain for it.
- PIS fields and world-probe fields broadcast to stages not listed as consumers above are **locally ineffective**, not globally obsolete settings.

`VgeShaderDefines` also names PBR settings that are **live outside the owned SPIR-V program catalog**: `VGE_PBR_ENABLE_POM`, `VGE_PBR_ENABLE_NORMAL_MAPS`, `VGE_PBR_NORMAL_MAP_SCALE`, `VGE_PBR_POM_SCALE`, `VGE_PBR_POM_MIN_STEPS`, `VGE_PBR_POM_MAX_STEPS`, `VGE_PBR_POM_REFINEMENT_STEPS`, `VGE_PBR_POM_FADE_START`, `VGE_PBR_POM_FADE_END`, `VGE_PBR_POM_MAX_TEXELS`, `VGE_PBR_POM_DEBUG_MODE`. `PBR/VanillaShaderPatches.cs` emits these from PBR configuration into engine-owned shader patches. Preserve that path; this inventory does not migrate engine-owned programs, enumerate their variants, or delete those constants. They are not unexplained unused settings.

Global graphics configuration is presently applied through renderer/program setters and family broadcasts, not an authoritative program-membership registry. `VgeConfig` sanitation, `LumOnRenderer`, `LumOnDebugRenderer`, `PBRCompositeRenderer`, and debug-family setters are the source of application values. Per-frame uniforms/UBOs (including debug view mode, matrices, current frame and lighting resources) stay outside settings.

## Identities, fixed source configuration, and bindings

All listed entry points are `main`. Current assets use vertex/fragment/compute stages only; there are no owned geometry/tessellation entries to register. Includes are not programs. Shared stages must be declared once by their exact source identity, not merged merely because two fullscreen shaders currently look alike.

Binding destination for each catalog row is the existing `GpuShaderContracts.Create(source without extension)` and domain declaration methods. `GpuShaderContracts.Locations.cs` supplies shared standalone/stage locations. `lumon_debug_*` maps to the `lumon_debug` binding family; `pbr_heightbake_*` maps to `pbr_heightbake`. Compute entries and fixtures without a named switch arm retain explicit source layout declarations plus common optional blocks/locations; they still need explicit empty configuration entries. Ring-buffer fixture 1 has `TestParams` binding 0 under both full asset and basename aliases. Preserve bindings; an empty options list does not mean an empty binding contract.

`ShaderVariantSource.Emit` injects the fixed define `VGE_SPIRV_BUILD=1` into every owned stage, upgrades source to GLSL 450 core and requires `GL_EXT_control_flow_attributes`. Preserve this build profile explicitly; it is not a mutable setting. There are currently no external fixed-define configurations applied differently by two consumers of the same stage source. Stage-local `#define` values, import guards and debug dispatch macros remain source implementation, not automatically public settings. Debug entry wrappers import different domain functions (`includes/lumon_debug_*.glsl`) and call the selected renderer; keep their distinct source/stage identities. No known current source reuse requires splitting one identity for incompatible fixed defines or layouts. Validate this invariant when constructing declarations; future incompatible reuse must get a distinct identity/output path.

Current paths relative to `assets/vanillagraphicsexpanded/shaders`: default `<source>.spv`; nondefault `variants/<source>/<lowercase SHA256(canonical key)>.spv`. Key is ordinal-name-sorted `NAME=value` pairs joined by semicolons, including default structural values. Numeric values/aliases are excluded after normalization. Empty/default configuration always uses the direct path. Contract/source/binding invalidation stays in build receipts.

Three explicit fixture pairings supplement the normal catalog: `tests/fullscreen_uv.vsh` + `pbr_direct_lighting.fsh` (`GPU/Helpers/PbrShaderPrograms.cs`, budget 1), and `lumon_probe_anchor.vsh` + `lumon_probe_atlas_trace.fsh` (`GPU/ShaderDefineInjectionTests.cs`, budget 32), and `lumon_debug.vsh` + `lumon_debug_worldprobe.fsh` (`GPU/Fixtures/DirectWorldProbeVisibilityTestBase.cs`, budget 4). Keep fixture program identities distinct while sharing the existing stage identities.

Isolated build-validator programs are additional to packaged assets: `fixture.vsh` + `fixture.fsh` (budget 1) and `fixture.csh` (budget 1), all entry `main`. `ValidateBuildContract.ps1` creates their source with location-0 position/color/result and compute SSBO binding 0. `FACTOR` is an imported fixed source value (0.5, mutated to 0.75); `VGE_LUMON_ENABLED` is a guarded source default (1, mutated to 0) in this fixture, **not** currently a registered structural setting. Preserve that source-invalidation test by registering explicit empty fixture options, with a fixture-only registry scope so production builds do not demand these isolated files.

## Migration verification scenarios

| Scenario | Existing source/fixture baseline | Required migration evidence |
| --- | --- | --- |
| Alias/default selection | `ShaderStageContract.Value`, global include, `ShaderDefineInjectionTests`, `SpirvStageLoaderTests` | Omission/default, alias-only, equal pair and null; current canonical wins conflicting pair, approved replacement rejects conflict |
| Trace continuation and world probes | `LumOnNearFieldFunctionalTests*`, `LumOnProbeAtlasTraceWorldProbeFallbackFunctionalTests*`, `LumOnDirectWorldProbeVisibilityTests*` | N/W combinations, sealed interior/bright exterior, visibility across walls and camera motion; retain representative small fixture topology |
| Numeric tracing | trace contract IDs 0..6; config bounds above | Default and representative boundary values; N=1 omits sky-miss argument; disabled/re-enabled selection retained |
| PIS overrides | `SpirvStageLoaderTests`, `SpirvInventoryTests`, trace/PIS-mask/temporal sources (dedicated PIS rendering assertions still needed) | All 8 mask assignments and 4 temporal assignments; mask exploration arguments only I=1/B=0/U=0; negative count sentinel and fraction selection |
| Composite and upsample | `LumOnCombineFunctionalTests`, `PbrLumOnFullPipelineIntegrationTests`, `LumOnUpsampleFunctionalTests`, global smoke | Feature defaults/aliases, PBR vs legacy composition, DN/HF combinations; no claims that no-effect AO setter changes PBR output |
| Shared vertices | `SpirvInventoryTests.VertexFor`, named heightbake owner and explicit pairing exceptions | Link every registered combination; fragment choice does not multiply vertex binaries |
| Binding/activity | `ShaderContractBindingTests`, `LumOnUniformTests`, ring buffer tests | Active-resource lookup and uniform/buffer behavior preserved, without compiler-obedience reflection |
| Direct compute | `GpuComputePipelineSpirvIntegrationTests`, ring-buffer direct-file fixture | Explicit identity, span slices, specialization forwarding, no metadata dependency |
| Replacement ownership | `SpirvGraphicsLifecycleTests`, `SpirvInventoryTests` | Numeric/structural effective changes, coalescing, no-op aliases, failed candidate keeps installed program; repeated disposal |
| Build/package | `ValidateBuildContract.ps1`, stage inventory | Declared counts, deduplication, compiler-byte identity, source/contract invalidation, fresh package assets and no manifests |

The historical build baseline is 98 stages / 243 binaries. Source enumeration below independently accounts for those expected counts; it is not a rerun of compilation or GPU testing. Unit/build/GPU/package/live-game receipts from earlier work are not new verification of the future registry. No shader rebuild is needed for this documentation-only inventory. Pure contract tests and registry/build/GPU checks remain later work.

## Program catalog

Every row defines a destination program identity (source stem), exact stage combination and proposed budget. Empty means no accepted current options. Owner names identify production registration/capture or GPU fixture ownership. For normal pairs, fragment is `<program>.fsh`; compute rows explicitly show `.csh`. Fixed values are source-local as described above; bindings follow the rule above.

| Program identity | Vertex / compute source | Options | Budget | Owner / source reference |
| --- | --- | --- | --- | --- |
| lumon_combine | lumon_combine.vsh | E P A S | 16 | LumOnCombineProgramLayout, LumOnCombineShaderProgram |
| lumon_debug_composite | lumon_debug_composite.vsh | D P A S | 16 | LumOnDebugRenderer, LumOnDebugShaderProgramFamily |
| lumon_debug_direct | lumon_debug_direct.vsh | D | 2 | LumOnDebugRenderer, LumOnDebugShaderProgramFamily |
| lumon_debug_gbuffer | lumon_debug_gbuffer.vsh | D | 2 | LumOnDebugRenderer, LumOnDebugShaderProgramFamily |
| lumon_debug_indirect | lumon_debug_indirect.vsh | D | 2 | LumOnDebugRenderer, LumOnDebugShaderProgramFamily |
| lumon_debug_probe_anchors | lumon_debug_probe_anchors.vsh | D | 2 | LumOnDebugRenderer, LumOnDebugShaderProgramFamily |
| lumon_debug_probe_atlas | lumon_debug_probe_atlas.vsh | D | 2 | LumOnDebugRenderer, LumOnDebugShaderProgramFamily |
| lumon_debug_sh | lumon_debug_sh.vsh | D | 2 | LumOnDebugRenderer, LumOnDebugShaderProgramFamily |
| lumon_debug_temporal | lumon_debug_temporal.vsh | D | 2 | LumOnDebugRenderer, LumOnDebugShaderProgramFamily |
| lumon_debug_velocity | lumon_debug_velocity.vsh | D | 2 | LumOnDebugRenderer, LumOnDebugShaderProgramFamily |
| lumon_debug_worldprobe | lumon_debug_worldprobe.vsh | D W | 4 | LumOnDebugRenderer, LumOnDebugShaderProgramFamily |
| lumon_debug | lumon_debug.vsh | D P A S W | 32 | LumOnDebugProgramLayout, LumOnDebugRenderer, LumOnDebugShaderProgramFamily |
| lumon_hzb_copy | lumon_hzb_copy.vsh | empty | 1 | LumOnHzbCopyShaderProgram, LumOnRenderer |
| lumon_hzb_downsample | lumon_hzb_downsample.vsh | empty | 1 | LumOnHzbDownsampleShaderProgram, LumOnRenderer |
| lumon_probe_anchor | lumon_probe_anchor.vsh | empty | 1 | LumOnProbeAnchorShaderProgram, LumOnRenderer |
| lumon_probe_atlas_filter | lumon_probe_atlas_filter.vsh | empty | 1 | LumOnRenderer, LumOnScreenProbeAtlasFilterShaderProgram |
| lumon_probe_atlas_gather | lumon_probe_atlas_gather.vsh | D W | 4 | LumOnRenderer, LumOnScreenProbeAtlasGatherProgramLayout, LumOnScreenProbeAtlasGatherShaderProgram |
| lumon_probe_atlas_pis_mask | lumon_probe_atlas_trace.vsh | I B U | 8 | LumOnProbeAtlasPisMaskShaderProgram, LumOnRenderer |
| lumon_probe_atlas_project_sh | lumon_probe_atlas_project_sh.vsh | empty | 1 | LumOnScreenProbeAtlasProjectSHShaderProgram |
| lumon_probe_atlas_project_sh9 | lumon_probe_atlas_project_sh9.vsh | empty | 1 | LumOnRenderer, LumOnScreenProbeAtlasProjectSh9ShaderProgram |
| lumon_probe_atlas_temporal | lumon_probe_atlas_temporal.vsh | I B | 4 | LumOnRenderer, LumOnScreenProbeAtlasTemporalShaderProgram |
| lumon_probe_atlas_trace | lumon_probe_atlas_trace.vsh | D N I B W | 32 | LumOnProbeAtlasPisMaskShaderProgram, LumOnRenderer, LumOnScreenProbeAtlasTraceProgramLayout, LumOnScreenProbeAtlasTraceShaderProgram |
| lumon_probe_sh9_gather | lumon_probe_sh9_gather.vsh | D W | 4 | LumOnProbeSh9GatherProgramLayout, LumOnProbeSh9GatherShaderProgram, LumOnRenderer |
| lumon_upsample | lumon_upsample.vsh | DN HF | 4 | LumOnRenderer, LumOnUpsampleShaderProgram |
| lumon_velocity | lumon_velocity.vsh | empty | 1 | LumOnRenderer, LumOnVelocityShaderProgram |
| lumon_worldprobe_clipmap_resolve | lumon_worldprobe_clipmap_resolve.vsh | empty | 1 | LumOnWorldProbeClipmapGpuUploader, LumOnWorldProbeClipmapResolveShaderProgram |
| lumon_worldprobe_radiance_tile_resolve | lumon_worldprobe_radiance_tile_resolve.vsh | empty | 1 | LumOnWorldProbeClipmapGpuUploader, LumOnWorldProbeRadianceTileResolveShaderProgram |
| lumonscene_capture_meshcard | lumonscene_capture_meshcard.csh | empty | 1 | LumonSceneCaptureMeshCardComputeShader, LumonSceneComputeProgramLayouts |
| lumonscene_capture_voxel | lumonscene_capture_voxel.csh | empty | 1 | LumonSceneCaptureVoxelComputeShader, LumonSceneComputeProgramLayouts |
| lumonscene_feedback_compact_pages | lumonscene_feedback_compact_pages.csh | empty | 1 | LumonSceneComputeProgramLayouts, LumonSceneFeedbackCompactPagesComputeShader |
| lumonscene_feedback_gather | lumonscene_feedback_gather.csh | empty | 1 | LumonSceneComputeProgramLayouts, LumonSceneFeedbackGatherComputeShader |
| lumonscene_feedback_mark_pages | lumonscene_feedback_mark_pages.csh | empty | 1 | LumonSceneComputeProgramLayouts, LumonSceneFeedbackMarkPagesComputeShader |
| lumonscene_relight_voxel_dda | lumonscene_relight_voxel_dda.csh | empty | 1 | LumonSceneComputeProgramLayouts, LumonSceneRelightVoxelDdaComputeShader |
| lumonscene_reset_irradiance | lumonscene_reset_irradiance.csh | empty | 1 | LumonSceneFeedbackUpdateRenderer.GeometryHistory |
| pbr_composite | pbr_composite.vsh | E P S | 8 | PbrCompositeProgramLayout, PBRCompositeRenderer, PBRCompositeShaderProgram |
| pbr_direct_lighting | pbr_direct_lighting.vsh | empty | 1 | DirectLightingRenderer, PbrDirectLightingProgramLayout, PBRDirectLightingShaderProgram |
| pbr_heightbake_combine | pbr_heightbake_fullscreen.vsh | empty | 1 | MaterialAtlasNormalDepthGpuBuilder |
| pbr_heightbake_copy | pbr_heightbake_fullscreen.vsh | empty | 1 | MaterialAtlasNormalDepthGpuBuilder |
| pbr_heightbake_divergence | pbr_heightbake_fullscreen.vsh | empty | 1 | MaterialAtlasNormalDepthGpuBuilder |
| pbr_heightbake_gauss1d | pbr_heightbake_fullscreen.vsh | empty | 1 | MaterialAtlasNormalDepthGpuBuilder |
| pbr_heightbake_gradient | pbr_heightbake_fullscreen.vsh | empty | 1 | MaterialAtlasNormalDepthGpuBuilder |
| pbr_heightbake_jacobi | pbr_heightbake_fullscreen.vsh | empty | 1 | MaterialAtlasNormalDepthGpuBuilder |
| pbr_heightbake_luminance | pbr_heightbake_fullscreen.vsh | empty | 1 | MaterialAtlasNormalDepthGpuBuilder |
| pbr_heightbake_normalize | pbr_heightbake_fullscreen.vsh | empty | 1 | MaterialAtlasNormalDepthGpuBuilder |
| pbr_heightbake_pack_to_atlas | pbr_heightbake_fullscreen.vsh | empty | 1 | MaterialAtlasNormalDepthGpuBuilder |
| pbr_heightbake_prolongate_add | pbr_heightbake_fullscreen.vsh | empty | 1 | MaterialAtlasNormalDepthGpuBuilder |
| pbr_heightbake_residual | pbr_heightbake_fullscreen.vsh | empty | 1 | MaterialAtlasNormalDepthGpuBuilder |
| pbr_heightbake_restrict | pbr_heightbake_fullscreen.vsh | empty | 1 | MaterialAtlasNormalDepthGpuBuilder |
| pbr_heightbake_sub | pbr_heightbake_fullscreen.vsh | empty | 1 | MaterialAtlasNormalDepthGpuBuilder |
| pbr_normaldepth_bake | pbr_normaldepth_bake.vsh | empty | 1 | Built legacy bake asset; SpirvInventoryTests covers it; no production caller found |
| tests/framebuffer_blend | tests/GpuFramebufferBlendStateIntegrationTests_1.vsh | empty | 1 | GpuFramebufferBlendStateIntegrationTests, SpirvInventoryTests |
| tests/GlStateCacheUnbindIntegrationTests_2 | tests/GlStateCacheUnbindIntegrationTests_1.vsh | empty | 1 | GlStateCacheUnbindIntegrationTests, SpirvInventoryTests |
| tests/GpuProgramLayoutBindingTests_1 | tests/GpuProgramLayoutBindingTests_1.csh | empty | 1 | GpuProgramLayoutBindingTests, SpirvInventoryTests |
| tests/GpuUniformRingBufferIntegrationTests_1 | tests/GpuUniformRingBufferIntegrationTests_1.csh | empty | 1 | GpuShaderContracts, GpuUniformRingBufferIntegrationTests |
| tests/GpuUniformRingBufferIntegrationTests_2 | tests/GpuUniformRingBufferIntegrationTests_2.csh | empty | 1 | GpuUniformRingBufferIntegrationTests |
| tests/PbrMaterialParamsTextureSmokeTests_2 | tests/PbrMaterialParamsTextureSmokeTests_1.vsh | empty | 1 | PbrMaterialParamsTextureSmokeTests, SpirvInventoryTests |
| tests/render_infrastructure | tests/render_infrastructure.vsh | empty | 1 | RenderTestInfrastructureTests, SpirvGraphicsLifecycleTests |
| tests/vge_global_defines_smoke | tests/vge_global_defines_smoke.vsh | E P A S | 16 | GpuShaderContracts.Stages, ShaderDefineInjectionTests |
| vge_debug_lines | vge_debug_lines.vsh | empty | 1 | LumOnDebugRenderer, VgeDebugLinesShaderProgram, VgeWorldCellBoundsDebugView |
| vge_worldprobe_orbs_points | vge_worldprobe_orbs_points.vsh | D | 2 | LumOnDebugRenderer, VgeWorldProbeOrbsPointsShaderProgram |

Catalog accounting: 50 fragment programs + 10 compute programs = 60 ordinary program entries; 38 distinct vertex sources (37 referenced by the ordinary rows plus tests/fullscreen_uv.vsh). The three additional fixture pairs reuse existing stages. Sum of ordinary program budgets is 205; adding 38 vertex binaries yields 243 distinct stage binaries. Including the three alternate fixture programs adds 37 program assignments but zero binaries. The isolated validator adds two programs / three stages / three binaries in its own scope, not the packaged count.
