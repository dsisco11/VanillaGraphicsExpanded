# LumOn world-probe lighting: architecture, findings, and repair tracking

Date: 2026-09-20  
Status: Near-field tracing, direct irradiance visibility and supported screen-hit lighting implemented; live validation remains open.
Scope: World-probe generation, screen-probe tracing, filtering, projection, gather, and contribution diagnostics.

## Summary

LumOn uses a screen-probe pipeline with directional radiance storage, temporal accumulation, filtering, and integration. World probes already contribute through screen-trace misses before final gather. They are not restricted to the near-zero-confidence replacement in the final gather shader.

The current implementation has several limitations that can explain missing or apparently missing lighting:

1. Supported screen hits now share local voxel hit lighting; outside that supported domain, reflected lighting remains unavailable.
2. SH projection weights angular samples by confidence, favoring screen hits over many world-probe samples.
3. Whole-probe confidence can be published before all directional texels contain valid radiance.
4. World-probe lighting uses a separate, simplified CPU lighting model rather than the existing LumonScene surface-cache lighting.

These findings do not establish which mechanism dominates the reported scene. Fixing the observed problem does not inherently require an architectural rewrite or an unconditional world-light term in final gather.

## Evidence and limitations

- Findings are based on local source inspection, not a reproduction of the affected scene.
- Configuration values below are source defaults, not verified live settings.
- Source locations describe the inspected revision and may move as implementation changes.
- Older architecture documents are historical context. Filtering, confidence metadata, HZB, and world fallback now exist in source.

## LumOn data flow

```text
CPU block-world tracing + simplified lighting
    -> world directional atlas + per-probe metadata
    -> distant directional lookup after a screen miss and a clear local world segment
    -> screen-probe radiance atlas + metadata
    -> temporal accumulation -> spatial filtering
    -> SH9 projection and gather OR direct atlas gather
    -> upsample -> lighting composition

Local opaque hits instead supply normalized voxel-light radiance and emission.

Separate final-gather recovery:
    collapsed screen-probe interpolation weight
    -> world irradiance sampled at shaded pixel
    -> replacement of screen gather result
```

| Area | Current implementation | Assessment |
| --- | --- | --- |
| Screen-probe representation | Octahedral atlas, temporal/filter passes, SH9 or atlas gather | Main pipeline is present |
| Cache entry point | World directional lookup after a clear near-field segment | Contributes before final gather |
| Accepted screen-hit lighting | Shared local voxel-light shading for supported opaque hits; explicit screen emission fallback | On/off-screen parity covered by controlled tests; live appearance and traversal cost remain open |
| World tracing and cache handoff | Screen miss traverses published local voxels; opaque hits stop the ray | Explicit clear/unavailable/budget outcomes |
| Cache interpolation | Spacing-based sphere reprojection of lighting direction; near cache hits excluded | Distant-domain approximation; direct irradiance remains separate |
| World lighting source | CPU block light plus approximate sky bounce | Separate lighting model |
| Directional readiness | Whole-probe confidence with sliced atlas updates | Incomplete readiness contract |
| Final gather | Combined screen-probe lighting plus low-weight world replacement | Additional recovery path |

Relevant LumOn sources:

- [Renderer orchestration and bindings](../VanillaGraphicsExpanded/LumOn/LumOnRenderer.cs): `RenderProbeAtlasTracePass`, `RenderProbeAtlasTemporalPass`, `RenderProbeAtlasFilterPass`, `RenderProbeAtlasProjectSh9Pass`, and `TryBindWorldProbeClipmapCommon`.
- [Screen tracing](../VanillaGraphicsExpanded/assets/vanillagraphicsexpanded/shaders/lumon_probe_atlas_trace.fsh): screen hit versus directional world-cache miss handling.
- [World-cache sampling](../VanillaGraphicsExpanded/assets/vanillagraphicsexpanded/shaders/includes/lumon_worldprobe.glsl): clipmap selection, trilinear interpolation, confidence, and sky handling.
- [Final-gather recovery](../VanillaGraphicsExpanded/assets/vanillagraphicsexpanded/shaders/includes/lumon_worldprobe_gather.glsl): `screenWeight < 0.001` gates direct world irradiance replacement.

## Findings

### WP-02: Screen-hit outgoing radiance

**Reproduced and repaired for supported local opaque geometry; live validation remains open.**

Previously, accepted screen hits returned only albedo times emission and received confidence 1. A controlled non-emissive room reproduced zero on-screen radiance versus approximately 0.251 or 1.0 off screen. Unpublished local data also produced confidence 1 for black screen hits.

The trace shader now resolves supported screen-hit segments through the existing local voxel traversal and evaluates the same outgoing-radiance source used off screen: normalized block light, bounded sky bounce and material emission. A screen hit bounds the geometry segment; a miss retains the existing cache-handoff segment. A valid dark near-field hit stays confidently dark. Unavailable non-emissive lighting stays unresolved. Explicit visible emission remains a fallback when no supported near-field hit can be resolved; a known opaque hit with missing material/light data cannot borrow emission from the screen sample.

The separate PBR direct-light renderer returns early while LumOn is enabled, so its outputs are not a current-frame source for this path. No new buffer or sampler is introduced, and the shader does not sample final composition or add world lighting behind an accepted opaque hit.

Sources: [screen tracer](../VanillaGraphicsExpanded/assets/vanillagraphicsexpanded/shaders/lumon_probe_atlas_trace.fsh), [shared hit-lighting evaluator](../VanillaGraphicsExpanded/assets/vanillagraphicsexpanded/shaders/includes/lumon_near_field_hit_lighting.glsl), [reproduction controls](../VanillaGraphicsExpanded.Tests/GPU/LumOnNearFieldFunctionalTests.ScreenHits.cs).

Limits: this retains the voxel-light approximation and published full-cube geometry requirements. Screen hits now incur bounded local traversal and shading work; live GPU cost remains unmeasured. Near-field hit lighting is unchanged by world-cache suppression, so a black WP lighting-effect view does not mean this local lighting is absent.
### WP-03: SH projection biases angular energy by confidence

**Confirmed weighting behavior; attenuation magnitude is scene-dependent.**

SH9 projection uses per-direction confidence as its angular accumulation weight, then normalizes by total confidence. Screen hits use `1.0`; the world confidence heuristic returns `0.25` for all-hit and all-miss sample sets. Mixed screen/world directions can therefore favor black screen hits over bright world directions.

Total-weight normalization does not remove this directional bias. Conversely, uniformly reducing confidence for every direction does not uniformly scale the result down: the normalization cancels that common factor. The concern is mixed confidence across directions.

Sources: [SH9 projection](../VanillaGraphicsExpanded/assets/vanillagraphicsexpanded/shaders/lumon_probe_atlas_project_sh9.fsh), approximately lines 76–96; [world confidence calculation](../VanillaGraphicsExpanded/LumOn/WorldProbes/Tracing/LumOnWorldProbeTraceIntegrator.cs), `ComputeUnifiedConfidence`; [configuration](../VanillaGraphicsExpanded/LumOn/VgeConfig.cs), default `EvaluateProjectedSH` gather mode.

Repair direction: separate sample validity, reconstruction reliability, and angular integration weights. Compare SH and direct-atlas gather using identical directional input. The atlas gather does not use the same confidence-weighted SH projection.

### WP-04: Probe confidence does not establish directional readiness

**Confirmed publication gap; contribution to the reported scene remains unmeasured.**

Source defaults use 16×16 world tiles and 32 texels per update. Whole-probe confidence is published with each partial update. The current directional samplers reject zero/nonfinite distance encodings, including untouched texels, but there is still no per-direction generation contract for slot reuse or incremental edits.

Batch selection hashes the frame index and probe identity. Eight updates do not guarantee eight different batches. No bounded complete-tile warm-up follows from the default slicing alone.

The uploader also allows metadata publication when the radiance-tile program is unavailable or tile upload capacity is exhausted. The update renderer marks successful trace results complete before upload and ignores the uploader's return value. These are conditional loss/publication paths, not evidence that those conditions occurred in the reported run.

Sources:

- [Direction slicing](../VanillaGraphicsExpanded/LumOn/WorldProbes/Tracing/LumOnWorldProbeAtlasDirectionSlicing.cs), `FillTexelIndicesForUpdate`.
- [GPU uploader](../VanillaGraphicsExpanded/LumOn/WorldProbes/Gpu/LumOnWorldProbeClipmapGpuUploader.cs), `Upload`.
- [Update renderer](../VanillaGraphicsExpanded/LumOn/WorldProbes/LumOnWorldProbeUpdateRenderer.cs), result drain and upload.
- [GPU resources](../VanillaGraphicsExpanded/LumOn/WorldProbes/Gpu/LumOnWorldProbeClipmapGpuResources.cs), `ClearAll`.
- [Sampling](../VanillaGraphicsExpanded/assets/vanillagraphicsexpanded/shaders/includes/lumon_worldprobe.glsl), `lumonWorldProbeAccumulateCornerScalars` and `lumonWorldProbeSampleLevelTrilinearRadiance`.

Repair direction: define directional readiness explicitly or guarantee complete initial population before publication; ensure eventual coverage; preserve pending work across upload limits; publish metadata only for committed radiance. Include slot reuse and world changes in the readiness design.

### WP-05: World-cache lighting is a separate approximation

**Confirmed architectural difference; not proof of a broken gather.**

The active world-probe update path constructs `BlockAccessorWorldProbeTraceScene`. Its integrator approximates hit radiance as block light plus diffuse albedo multiplied by estimated sky bounce. The sky estimate uses two secondary directions. It does not consume LumonScene surface-cache lighting.

Thus, the gather can correctly transport weak or dark world-cache values. Better cache lookup alone cannot restore lighting that the cache never generated.

Sources: [update renderer](../VanillaGraphicsExpanded/LumOn/WorldProbes/LumOnWorldProbeUpdateRenderer.cs), trace-scene construction; [integrator](../VanillaGraphicsExpanded/LumOn/WorldProbes/Tracing/LumOnWorldProbeTraceIntegrator.cs), `EvaluateHitRadiance` and `EstimateSkyBounceFactor`.

Repair direction: first measure actual cache radiance. Treat shared scene-lighting integration and a richer bounce model as separately scoped work, unless the reproduction proves this is the immediate limiting factor.

### WP-06: Near-field tracing and direct irradiance visibility implemented

**Local screen-probe tracing, directional cache handoff and direct irradiance visibility implemented. Live validation remains open.**

Screen misses now traverse a published local voxel scene. A supported opaque hit supplies normalized outside-cell block light, bounded sky bounce and hit-face emission. Only a fully clear near-field segment permits distant world-cache sampling. Missing data, unsupported geometry, exhausted steps and premature bounds exits remain unresolved.

The cache handoff selects a covered level, uses a sphere radius of sqrt(3) times that level's spacing, and traces twice that radius locally. Each neighbor's lighting direction is reprojected onto its sphere. Cache texels whose recorded distance is inside the radius are excluded because the existing atlas also contains near hits. The new path does not apply binary probe-to-surface rejection. This preserves constant radiance while correcting lookup direction; it does not establish exact visibility of every distant feature.

The direct irradiance fallback in both gather modes and the irradiance debug view now trace probe-to-receiver segments through the published local voxel grid. Only a completed clear segment accepts that probe. These consumers no longer use the nearest directional cache depth as a visibility threshold.

The [controlled sealed-room reproduction](LumOn.WorldProbeLighting.ReproductionTests.md) originally confirmed across-wall interpolation: dark interior probes mixed with bright exterior probes to produce 0.125 directional radiance. The visibility repair changes that regression to require zero lighting through both gather modes and black in the paired luminance diagnostic. Covered but rejected or unpublished neighbors cannot trigger approximate sky fallback. Open-doorway, visible-neighbor and ring-index controls preserve valid lighting. The focused suite passed 71 tests with no failures or skips. Directional depth remains approximate; this does not establish correctness in every live scene.

The [flat-wall reproduction](LumOn.WorldProbeLighting.ReproductionTests.md#flat-wall-visibility-artifact-reproduction) originally found 6,228 false rejections among 16,384 unobstructed near-wall samples. The replacement retains the exact-ray ground truth and now requires zero false rejections. Missing, unsupported or out-of-window geometry remains unresolved rather than falling back to approximate visibility.

## Validation and repair tracker

Unchecked boxes represent remaining work. Checked boxes identify completed design or validation work.

### Priority 1: GPU geometry and hit-lighting integration design

Define how unresolved screen-probe rays access local voxel geometry and obtain outgoing radiance at a surface hit. Complete this design before implementing the tracing path.

- [x] Identify reusable GPU geometry and lighting resources.
- [x] Identify missing integration and establish ownership of the tracing inputs.
- [x] Specify the outgoing-radiance source for local surface hits.

Completion criterion: a source-backed design identifies the available resources, required additions, and geometry-to-lighting data flow.

**Design complete:** [Near-field tracing integration design](LumOn.NearFieldTracing.IntegrationDesign.md) specifies resource reuse, readiness, trace outcomes, hit lighting, ownership and frame order. Priority 2 now implements this design; live runtime and performance validation remain open.

### Priority 2: Near-field tracing and radiance-cache handoff

Continue screen-space misses through local world geometry. Nearby opaque surfaces must resolve lighting and stop the ray before distant cached lighting is accepted.

- [x] Implement near-field tracing for unresolved screen-probe rays.
- [x] Resolve near-field hits using the lighting source established by Priority 1.
- [x] Sample the world radiance cache only after the near-field segment is clear.
- [x] Base the handoff distance on cache spacing and coverage, and apply lighting-direction parallax correction.
- [x] Remove binary probe-to-surface rejection from this path only after the replacement preserves sealed-room occlusion.

Completion criterion: near-field hits block exterior cache lighting, while unobstructed rays retain valid cached lighting.

Implemented in the [near-field tracing shaders](../VanillaGraphicsExpanded/assets/vanillagraphicsexpanded/shaders/includes/lumon_near_field_trace.glsl) and [published scene owner](../VanillaGraphicsExpanded/LumOn/Scene/NearField/NearFieldGpuScene.cs). Controlled GPU cases cover sealed interiors, doorway closure, missing data/materials, budget limits, scene generations, stale completions, slot identities, large coordinates, emission, sky lighting and parallax.

- [ ] Validate live scene appearance, snapshot cost and GPU traversal cost under representative update budgets.

Automated correctness evidence is recorded in the [reproduction report](LumOn.WorldProbeLighting.ReproductionTests.md#near-field-tracing-and-cache-handoff). This item does not claim to repair the direct irradiance viewer.

### Priority 3: Direct world-probe irradiance visibility

Replace hard depth rejection in direct world-probe irradiance sampling. This includes final-gather fallback and the irradiance debug view; changing screen-probe tracing alone does not repair these paths.

- [x] Evaluate filtered distance moments or a design using locally traced results.
- [x] Implement the selected visibility approach in both direct sampling consumers.
- [x] Preserve valid lighting while eliminating false near-wall rejection.

Completion criterion: direct irradiance sampling satisfies both the flat-wall visibility and sealed-room leakage requirements.

**Implemented:** shared local voxel traversal now resolves visibility for the debug viewer and both gather fallbacks. Filtered distance moments were considered but not selected: they summarize angular depths and cannot establish exact segment occlusion. Existing voxel geometry provides a direct test without another distance atlas.

[Implementation contract](LumOn.NearFieldTracing.IntegrationDesign.md#direct-irradiance-visibility) and [regression evidence](LumOn.WorldProbeLighting.ReproductionTests.md#direct-irradiance-visibility-repair) describe coverage and limits. Live appearance and GPU cost remain unmeasured. Supported visibility is bounded by the near-field geometry window.

### Priority 4: Visibility regression validation

Validate both sampling-path replacements against the existing reproductions and additional corner geometry.

- [x] Preserve zero exterior leakage in the sealed-room reproduction.
- [x] Eliminate false near-wall rejection in the flat-wall reproduction.
- [x] Retain doorway, blocked-neighbor, unobstructed-lighting, ring-index and clipmap controls.
- [x] Add corner geometry coverage to detect inappropriate visibility acceptance.

Completion criterion: both replacements pass the applicable regression cases without trading surface artifacts for light leakage.

Automated cases now cover both replacements, including doorways, blocked neighbors, ring remapping and corners. New direct-visibility controls exercise fine/coarse blending and selection, blocked coarse probes, unavailable fine probes, near-field-window boundaries, and geometry-ring movement before and after publication. All 57 direct-visibility cases pass with full-resolution guide textures. Live appearance and performance validation remain open.

### Remaining validation and repairs

- [ ] Reproduce the reported symptom and record active gather mode, world-cache settings, camera position, and cache warm-up state.
- [ ] Determine whether the claim of zero contribution comes from final lighting, the contribution debug view, or both.
- [ ] Inject known bright world-atlas radiance with valid metadata and forced screen misses; verify trace output and the world-fallback metadata flag.
- [ ] Follow that signal through temporal output, filtering, SH projection, half-resolution gather, and final composition.
- [ ] Verify world lighting survives a high-confidence final screen-probe gather in both gather modes.
- [ ] Test a mixed set of dark screen hits and bright world misses to quantify confidence-induced angular bias.
- [x] Test a directly lit, non-emissive screen hit and compare with the same surface when resolved off screen using controlled local voxel lighting.
- [ ] Verify world radiance population, directional readiness, and metadata at startup and during slot reuse.
- [ ] Exercise upload-budget exhaustion and unavailable tile-program handling without losing pending radiance or publishing misleading validity.
- [x] Correct accepted-hit radiance for supported near-field geometry and validate lighting, emission and readiness consistency.
- [ ] Validate live material/exposure consistency and screen-hit traversal cost.
- [ ] Correct angular confidence weighting and readiness/publication contracts where the reproduction confirms impact.
- [ ] Assess further shared scene-lighting integration beyond the near-field hit radiance source established by the priority design work.

The [world-fallback GPU tests](../VanillaGraphicsExpanded.Tests/GPU/LumOnProbeAtlasTraceWorldProbeFallbackFunctionalTests.cs) now also accept voxel-derived atlas data and verify separate histories, filtering, both gather modes, and signed diagnostic output. The [reproduction report](LumOn.WorldProbeLighting.ReproductionTests.md) records focused validation and its limits. Scheduler behavior, production upload/publication, upsampling, final composition and live-game correctness remain unverified by this fixture.

The sampling-path replacements and their automated visibility regressions are complete. The next validation is live scene appearance and performance, followed by the remaining end-to-end lighting and readiness investigations above.

### Reported movement regression: coordinate bridge

The player-relative coordinate contract was established in commit 3d4a7f6 and applied to the debug views in 9a2cf80. Reconstructed receiver positions and published clipmap origins already share player-relative space; the shaders must not subtract camera translation again.

The near-field visibility implementation now consumes the frame world-space bridge. That bridge previously derived its absolute offset from CameraPos minus inverse-view translation, while cache origins are published relative to Entity.Pos. This introduced a camera-dependent conversion into visibility even though the earlier cache-sampling fixes remained intact.

Implemented repair: both frame publishers now split Entity.Pos directly into integer chunk coordinates and a fractional remainder, using the same player-origin source as cache publication. The bridge no longer accepts a camera transform. All inspected consumers reconstruct player-relative positions, including local screen tracing and occupancy debug views. A GPU regression varies inverse-view camera bob while holding the reconstructed receiver and geometry stationary, covering the irradiance viewer and both gather modes at zero and large signed world origins. All nine cases failed before the repair and pass afterward; 73 focused and 222 regression tests pass overall. Live validation remains open; this repair does not change cache publication timing.
