# LumOn world-probe lighting: architecture, findings, and repair tracking

Date: 2026-09-20  
Status: Remaining lighting findings tracked; runtime cause of the original lighting symptom not yet isolated.  
Scope: World-probe generation, screen-probe tracing, filtering, projection, gather, and contribution diagnostics.

## Summary

LumOn uses a screen-probe pipeline with directional radiance storage, temporal accumulation, filtering, and integration. World probes already contribute through screen-trace misses before final gather. They are not restricted to the near-zero-confidence replacement in the final gather shader.

The current implementation has several limitations that can explain missing or apparently missing lighting:

1. Accepted screen hits supply emissive radiance only; ordinary illuminated surfaces return black with full confidence.
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
    -> directional lookup on screen-trace miss
    -> screen-probe radiance atlas + metadata
    -> temporal accumulation -> spatial filtering
    -> SH9 projection and gather OR direct atlas gather
    -> upsample -> lighting composition

Separate final-gather recovery:
    collapsed screen-probe interpolation weight
    -> world irradiance sampled at shaded pixel
    -> replacement of screen gather result
```

| Area | Current implementation | Assessment |
| --- | --- | --- |
| Screen-probe representation | Octahedral atlas, temporal/filter passes, SH9 or atlas gather | Main pipeline is present |
| Cache entry point | World directional lookup on screen miss | Contributes before final gather |
| Accepted screen-hit lighting | Emissive contribution only | Missing reflected lighting from illuminated non-emissive surfaces |
| World tracing and cache handoff | Screen miss directly samples world cache | No local world-trace/cache-distance boundary |
| Cache interpolation | Same-direction interpolation between neighboring probes | No depth-based parallax correction |
| World lighting source | CPU block light plus approximate sky bounce | Separate lighting model |
| Directional readiness | Whole-probe confidence with sliced atlas updates | Incomplete readiness contract |
| Final gather | Combined screen-probe lighting plus low-weight world replacement | Additional recovery path |

Relevant LumOn sources:

- [Renderer orchestration and bindings](../VanillaGraphicsExpanded/LumOn/LumOnRenderer.cs): `RenderProbeAtlasTracePass`, `RenderProbeAtlasTemporalPass`, `RenderProbeAtlasFilterPass`, `RenderProbeAtlasProjectSh9Pass`, and `TryBindWorldProbeClipmapCommon`.
- [Screen tracing](../VanillaGraphicsExpanded/assets/vanillagraphicsexpanded/shaders/lumon_probe_atlas_trace.fsh): screen hit versus directional world-cache miss handling.
- [World-cache sampling](../VanillaGraphicsExpanded/assets/vanillagraphicsexpanded/shaders/includes/lumon_worldprobe.glsl): clipmap selection, trilinear interpolation, confidence, and sky handling.
- [Final-gather recovery](../VanillaGraphicsExpanded/assets/vanillagraphicsexpanded/shaders/includes/lumon_worldprobe_gather.glsl): `screenWeight < 0.001` gates direct world irradiance replacement.

## Findings

### WP-02: Non-emissive screen hits return fully confident black

**Confirmed lighting gap; strongest source-level explanation for view-dependent darkness.**

The accepted screen-hit expression is:

```glsl
result.color = albedo * emissiveStrength * LUMON_EMISSIVE_BOOST;
```

An ordinary directly illuminated non-emissive surface returns zero. The hit bypasses world-cache sampling and receives confidence `1.0`.

This can produce the sequence: lit geometry becomes visible, a ray becomes a screen hit, and its previously world-derived lighting is replaced by black. Confirming this in the affected scene still requires a capture or controlled regression.

Sources: [screen tracer](../VanillaGraphicsExpanded/assets/vanillagraphicsexpanded/shaders/lumon_probe_atlas_trace.fsh), hit evaluation near line 177 and confidence assignment near line 308; [direct-light renderer](../VanillaGraphicsExpanded/PBR/DirectLightingRenderer.cs), render order 9; [LumOn renderer](../VanillaGraphicsExpanded/LumOn/LumOnRenderer.cs), render order 10.

Repair direction: supply appropriate outgoing lit radiance at accepted hits. Existing direct diffuse and emissive outputs are available before LumOn. Verify their units, material factors, exposure, and binding ownership. Do not add world radiance behind an opaque hit merely because the hit is dark, and do not create uncontrolled feedback from final composite lighting.

### WP-03: SH projection biases angular energy by confidence

**Confirmed weighting behavior; attenuation magnitude is scene-dependent.**

SH9 projection uses per-direction confidence as its angular accumulation weight, then normalizes by total confidence. Screen hits use `1.0`; the world confidence heuristic returns `0.25` for all-hit and all-miss sample sets. Mixed screen/world directions can therefore favor black screen hits over bright world directions.

Total-weight normalization does not remove this directional bias. Conversely, uniformly reducing confidence for every direction does not uniformly scale the result down: the normalization cancels that common factor. The concern is mixed confidence across directions.

Sources: [SH9 projection](../VanillaGraphicsExpanded/assets/vanillagraphicsexpanded/shaders/lumon_probe_atlas_project_sh9.fsh), approximately lines 76–96; [world confidence calculation](../VanillaGraphicsExpanded/LumOn/WorldProbes/Tracing/LumOnWorldProbeTraceIntegrator.cs), `ComputeUnifiedConfidence`; [configuration](../VanillaGraphicsExpanded/LumOn/VgeConfig.cs), default `EvaluateProjectedSH` gather mode.

Repair direction: separate sample validity, reconstruction reliability, and angular integration weights. Compare SH and direct-atlas gather using identical directional input. The atlas gather does not use the same confidence-weighted SH projection.

### WP-04: Probe confidence does not establish directional readiness

**Confirmed publication gap; contribution to the reported scene remains unmeasured.**

Source defaults use 16×16 world tiles and 32 texels per update. Whole-probe confidence is published with each partial update, while the sampler does not check readiness of the requested direction. Untouched texels begin at zero and can be interpreted as black data from a confident probe.

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

### WP-06: Local tracing and parallax correction remain limited

**Spatial visibility repair implemented; local tracing and parallax remain separate work.**

LumOn goes directly from a screen miss to directional world-probe interpolation. The sampler now rejects neighbors whose recorded geometry blocks the probe-to-sample segment and renormalizes visible neighbors. It still has no explicit local world-trace/cache-distance handoff or lighting-direction parallax correction; accepted neighbors sample the same lighting direction despite different origins.

This affects occlusion, spatial correspondence, and off-screen detail. It is not a reason to add world irradiance unconditionally at final gather, and need not block proving basic world-to-screen lighting transport.

The [controlled sealed-room reproduction](LumOn.WorldProbeLighting.ReproductionTests.md) originally confirmed across-wall interpolation: dark interior probes mixed with bright exterior probes to produce 0.125 directional radiance. The visibility repair changes that regression to require zero lighting and neutral gray through both gather modes. Covered but rejected or unpublished neighbors cannot trigger approximate sky fallback. Open-doorway, visible-neighbor and ring-index controls preserve valid lighting. The focused suite passed 71 tests with no failures or skips. Directional depth remains approximate; this does not establish correctness in every live scene.

## Validation and repair tracker

All checkboxes represent remaining work, not completed validation.

- [ ] Reproduce the reported symptom and record active gather mode, world-cache settings, camera position, and cache warm-up state.
- [ ] Determine whether the claim of zero contribution comes from final lighting, the contribution debug view, or both.
- [ ] Inject known bright world-atlas radiance with valid metadata and forced screen misses; verify trace output and the world-fallback metadata flag.
- [ ] Follow that signal through temporal output, filtering, SH projection, half-resolution gather, and final composition.
- [ ] Verify world lighting survives a high-confidence final screen-probe gather in both gather modes.
- [ ] Test a mixed set of dark screen hits and bright world misses to quantify confidence-induced angular bias.
- [ ] Test a directly lit, non-emissive screen hit and compare with the same surface when resolved off screen.
- [ ] Verify world radiance population, directional readiness, and metadata at startup and during slot reuse.
- [ ] Exercise upload-budget exhaustion and unavailable tile-program handling without losing pending radiance or publishing misleading validity.
- [ ] Correct accepted-hit radiance and validate material/exposure consistency.
- [ ] Correct angular confidence weighting and readiness/publication contracts where the reproduction confirms impact.
- [ ] Decide separately whether local world tracing, parallax correction, and shared scene-lighting integration are required next.

The [world-fallback GPU tests](../VanillaGraphicsExpanded.Tests/GPU/LumOnProbeAtlasTraceWorldProbeFallbackFunctionalTests.cs) now also accept voxel-derived atlas data and verify separate histories, filtering, both gather modes, and signed diagnostic output. The [reproduction report](LumOn.WorldProbeLighting.ReproductionTests.md) records focused validation and its limits. Scheduler behavior, production upload/publication, upsampling, final composition and live-game correctness remain unverified by this fixture.

The first decision point is whether known world radiance survives the existing pipeline. If it does, prioritize diagnostic attribution, screen-hit lighting, and cache content/readiness rather than replacing gather architecture.
