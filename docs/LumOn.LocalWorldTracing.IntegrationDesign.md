# Local world tracing: geometry and hit-lighting integration

## Decision

Reuse the existing GPU TraceScene occupancy infrastructure for local screen-probe ray traversal. Add explicit readiness and bounded trace outcomes before allowing a clear segment to authorize radiance-cache sampling.

Initial local-hit lighting will use normalized outside-cell voxel light plus hit-face material data through the existing world-probe lighting approximation. Do not use the current surface-cache irradiance atlas as arbitrary-hit outgoing radiance.

The local tracing implementation now follows this contract. Automated correctness coverage is recorded below; live appearance and performance validation remain open. Direct irradiance sampling now reuses the geometry traversal as described below.

## Existing resources

| Resource | Reusable capability | Missing contract or limitation |
| --- | --- | --- |
| GPU occupancy clipmap | R32UI 3D textures, integer origins, rings, region snapshots and compute upload | Zero represents both dark air and unwritten storage. In-bounds does not establish readiness. |
| Cell payload | Nonzero material index distinguishes solids from lit air | Live snapshots classify every nonzero block ID as solid. Partial geometry and transmission are not represented. |
| Relight DDA | Cell stepping and entered-face normals | Steps before checking the starting cell; no distance-limit completion outcome; bounds exit and step exhaustion return false. |
| Material palette and surface LUT | Per-face material lookup, diffuse RGB and roughness | Masked block IDs can alias palette entries; emission is absent from the surface LUT. |
| Light payload and LUTs | Existing snapshot infrastructure | Quantized, bounded color IDs; relight units differ from CPU world-probe shading. |
| Surface cache | Irradiance/material atlases and page tables | Current helper uses placeholder patch-ID-to-page mapping. DDA hits lack the required patch identity. Irradiance is not outgoing radiance. |
| Screen-probe trace pass | Existing ray invocation, radiance/meta/history output and cache binding | No local occupancy, readiness or arbitrary-hit lighting inputs. |
| Coordinate bridge | Integer chunk origin and fractional remainder | New traversal must preserve precision instead of converting large absolute positions to float. |

Sources:

- [Occupancy resources](../VanillaGraphicsExpanded/LumOn/Scene/LumonSceneOccupancyClipmapGpuResources.cs), InitializeDefaults; [packing](../VanillaGraphicsExpanded/LumOn/Scene/LumonSceneOccupancyPacking.cs).
- [Live snapshots](../VanillaGraphicsExpanded/LumOn/Scene/LumonSceneTraceSceneChunkSnapshotSource.cs), TryCreateSnapshotAsync; [region processor](../VanillaGraphicsExpanded/LumOn/Scene/LumonSceneTraceSceneRegionProcessor.cs).
- [Occupancy lookup](../VanillaGraphicsExpanded/assets/vanillagraphicsexpanded/shaders/includes/lumonscene_trace_scene_occupancy.glsl); [relight shader](../VanillaGraphicsExpanded/assets/vanillagraphicsexpanded/shaders/lumonscene_relight_voxel_dda.csh), TraceDdaL0 and ShadeHitFromOutsideCell.
- [Palette registry](../VanillaGraphicsExpanded/LumOn/Scene/LumonSceneTraceSceneMaterialPaletteRegistry.cs); [surface LUT](../VanillaGraphicsExpanded/LumOn/Scene/LumonScenePbrSurfaceLutRegistry.cs), WriteSurfaceUnsafe; [light registry](../VanillaGraphicsExpanded/LumOn/Scene/LumonSceneTraceSceneLightIdRegistry.cs).
- [Surface-cache lookup](../VanillaGraphicsExpanded/assets/vanillagraphicsexpanded/shaders/includes/lumonscene_surface_cache.glsl), VgeLumonSceneTrySampleIrradiance_NearFieldV1.
- [Trace layout](../VanillaGraphicsExpanded/LumOn/Shaders/LumOnScreenProbeAtlasTraceProgramLayout.cs); [coordinate bridge](../VanillaGraphicsExpanded/assets/vanillagraphicsexpanded/shaders/includes/vge_worldspace_bridge.glsl).

## Geometry and publication

Start with L0 block-resolution geometry. Do not treat coarser occupancy as equivalent geometry.

Use a region readiness texture with CPU-owned world identities and generations. Each visited cell must belong to a ready slot in the active ring window; the CPU invalidates evicted or dirty owners before consumption and rejects stale uploads. Zero occupancy is not sufficient to establish known air.

Classify snapshot geometry as known empty, supported full opaque cells, or unsupported geometry using actual block geometry/material properties. Partial blocks, decorative non-colliding blocks and transmissive blocks must not silently become full opaque cubes or empty space. Until their representation is supported, return unresolved. Full-block tests alone cannot establish general-scene correctness.

Replace masked block-ID material indices with collision-free bounded palette assignment. Exhaustion marks affected data unavailable rather than selecting another material.

Publication protocol:

1. Invalidate dirty, reassigned and newly exposed regions before consumption.
2. Upload matching geometry, normalized light and material dependencies.
3. Publish readiness for the completed version after GPU memory barriers.
4. Reject stale asynchronous results by region identity and generation.
5. Invalidate relevant screen-probe history on geometry changes; a conservative global scene-generation reset is acceptable initially.

The current dispatcher supplies image/texture barriers, but these do not establish semantic freshness. Readiness and invalidation are new work.

Sources: [occupancy owner](../VanillaGraphicsExpanded/LumOn/Scene/LumonSceneOccupancyClipmapUpdateRenderer.cs), TryGetLevel0RuntimeParams and completion handling; [dispatcher](../VanillaGraphicsExpanded/LumOn/Scene/LumonSceneTraceSceneClipmapGpuBuildDispatcher.cs), UploadAndDispatchBatch.

## Trace API and coordinates

Extract traversal mechanics into a dedicated GLSL include with no implicit lighting policy. Do not reuse the existing boolean relight tracer unchanged.

Inputs: integer world start cell, fractional position, normalized direction, maximum distance, step budget and immutable scene snapshot.

| Outcome | Meaning | Next action |
| --- | --- | --- |
| Hit | Supported opaque geometry encountered | Stop and evaluate hit lighting. |
| ClearToLimit | Entire requested segment traversed through ready empty cells | Permit cache handoff when cache coverage is valid. |
| Unavailable | Missing/stale data, unsupported geometry or premature bounds exit | Remain unresolved; no inferred sky or unoccluded cache lighting. |
| BudgetExceeded | Requested segment not fully traversed | Remain unresolved; not a miss. |

Hit records contain cell, face normal, distance, local surface coordinates, material identity and generation. Handle the initial cell, boundary starts, axis-aligned rays, diagonal ties and starting inside solids explicitly. Use a small bounded origin offset, not the existing relighter's 0.51-block offset.

After a screen-space miss, start the world trace at the screen-probe ray origin. Screen traversal does not prove that the world segment before screen exit was unobstructed.

Keep DDA arithmetic local to the integer start cell. Use the existing integer chunk offset and fractional remainder to establish that cell, avoiding large absolute floating-point origins.

## Initial outgoing-radiance source

Use a companion L0 RGBA8 light volume containing normalized block-light RGB and sunlight with the same semantics as the CPU world-probe tracer. Publish it with the occupancy region generation. Reuse snapshot/upload infrastructure rather than the relighter's numerical interpretation.

For a hit, fetch material from the hit cell and light from the outside cell at hitCell + hitNormal. The existing relight helper instead derives material from that outside cell, which may be air; it also multiplies light scalars by 32 and applies distance attenuation. Neither behavior should be copied into the new radiance contract.

The initial shading model is:

- Normalized outside-cell RGB block light, matching the existing world-probe approximation.
- Sky bounce from hit-face diffuse albedo, outside-cell normalized sunlight, inverse pi, and the CPU integrator's bounded secondary visibility estimate.
- Secondary visibility counts only completed clear segments; unavailable data and exhausted budgets do not establish sky visibility.
- No inverse-square attenuation of the returned surface radiance.
- Explicit linear per-face emission through an added material field/resource, using the existing GI emission convention once. Do not infer emission from propagated light.
- Linear HDR output before final gather intensity/tint and display tone mapping.

This remains a voxel-light approximation, not physically complete surface-cache shading or guaranteed parity with visible direct lighting. Source-light spill remains a separate issue. Supported screen hits now use this same evaluator. Establish CPU/GPU parity of block and sky terms and test emission independently.

When outside-cell lighting or hit material is unavailable, retain the opaque hit with unavailable lighting. Never turn a known wall into a miss.

Sources: [CPU lighting](../VanillaGraphicsExpanded/LumOn/WorldProbes/Tracing/LumOnWorldProbeTraceIntegrator.cs), EvaluateHitRadiance and EstimateSkyBounceFactor; [outside-cell sampling](../VanillaGraphicsExpanded/LumOn/WorldProbes/Tracing/BlockAccessorWorldProbeTraceScene.cs), Trace.

## Ownership and binding

| Owner | Responsibility |
| --- | --- |
| Scene snapshot/region pipeline | Geometry classification, material/light snapshots and dirty versions |
| Occupancy resource owner | Geometry, normalized light, readiness and coherent published mappings |
| Material registry | Collision-free face identities, diffuse data and linear emission |
| Local trace GLSL include | Bounded traversal and explicit outcomes |
| Hit-lighting GLSL include | Material and outside-cell light evaluation |
| Screen-probe trace pass | Screen/local/cache selection, radiance metadata and history validity |
| Composition root | Inject a read-only published scene provider; no traversal/shading logic |

Expose a published scene provider to LumOnRenderer instead of duplicating resources or locating update renderers inside shader consumers. Add occupancy, readiness, light and material bindings to the trace program layout, with a dedicated mapping/trace UBO.

Verify fragment sampler and UBO limits before choosing bindings: the trace layout already occupies units through 12. Consolidate material resources if needed; do not assume spare texture units.

Source: [renderer](../VanillaGraphicsExpanded/LumOn/LumOnRenderer.cs), RenderProbeAtlasTracePass; [composition root](../VanillaGraphicsExpanded/ModSystems/LumOnModSystem.cs), scene renderer creation.

## Frame lifetime and configuration

The occupancy updater runs at Done; screen tracing runs at Opaque. Consume the previous completed snapshot at the next Opaque pass. Drain dirty invalidations before consumption and bind a coherent origin/ring/generation. Do not imply that same-frame Done updates precede Opaque.

Both [paired diagnostic branches](../VanillaGraphicsExpanded/LumOn/LumOnRenderer.WorldProbeComparison.cs) must use the same snapshot. Local-hit lighting stays identical in both branches; only accepted world-cache radiance is suppressed.

Provision TraceScene when local tracing requires it rather than silently depending on the separate surface-cache capture toggle. Preserve independent capture/relight ownership. Disabled resources produce explicit unavailable results.

## Geometry-to-lighting data flow

Screen-probe ray -> screen-space hit lighting, or local world trace -> opaque local-hit lighting, or completed clear segment -> parallax-corrected world radiance cache -> screen-probe radiance/history -> gather.

Priority 2 must define the cache near-distance contract jointly with local tracing. Current world probes trace from their centers; a spacing-based handoff constant alone does not establish which geometry the cache represents. A bounds exit or insufficient coverage must not shorten the required segment and then claim it was clear.

Direct irradiance fallback and its debug view bypass the directional handoff chain. Their separate segment-visibility integration is described below.

## Implementation gates

- Geometry classification, initial-cell hits, ties, negative coordinates and large-world precision.
- Ready dark air versus missing data, dirty edits, generation changes, ring shifts and upload-budget exhaustion.
- Hit-cell material versus outside-cell light, normalized units, CPU/GPU parity and emission.
- Opaque occlusion preserved when hit lighting is unavailable.
- No sky/cache fallback for step exhaustion or premature bounds exit.
- Sealed-room and doorway cases exercised through actual GPU local tracing.
- Flat-wall reproduction retained until direct irradiance visibility is repaired.
- Traversal cost and texture-unit usage measured with representative update budgets.

## Implemented behavior and remaining validation

The production screen-probe trace program enables local tracing after screen misses. A region-aligned companion scene publishes full opaque cells, normalized light and collision-free per-face materials through the existing snapshot stream. The GPU readiness texture stores one unsigned byte per ring slot. World-region identities and versions remain on the CPU, which clears evicted and dirty slots before Opaque consumption and rejects stale or out-of-window completions. Newly exposed local regions are explicitly scheduled. Publication changes conservatively reset both screen-probe histories.

The companion volume rounds L0 resolution up to a whole 32-cell region. Geometry uses R32UI, lighting RGBA8, readiness R8UI, and paired diffuse/emission texels use RGBA8. Extra texture units are 10, 13, 14 and 15; the complete trace layout fits units 0 through 15. The local UBO uses the existing material binding slot, independently of texture units. Capture and companion artifact retention are restricted to the local window.

The ring uses the recorded integer origin as its anchor. Its physical region offset is positive-modulo(origin / 32, region resolution), derived in the shader rather than supplied as another uniform. Overlapping regions keep their physical slots as the anchor moves. Before the new window is consumed, evicted slots become unready; missing replacement uploads therefore cannot expose old geometry. Readiness is published last, after geometry, lighting and material uploads.

RGBA8 stores linear normalized values with 1/255 increments. This halves lighting/material texture storage relative to RGBA16F; very dim values may round to zero. Material emission remains normalized in storage, with the GI boost applied afterward to produce HDR radiance. CPU snapshot values and float upload staging retain their existing precision; this change reduces GPU storage, not their allocation sizes. R8UI reduces region metadata from 16 bytes to one byte per 32-cubed region.

A supported cell must be a full collision cube with opaque sides and the opaque render pass. Other geometry remains unavailable. An air solid-layer snapshot additionally checks the accessor's most-solid layer. Missing materials preserve opaque occlusion with zero lighting confidence. Palette capacity is 16,384 identities including unavailable identity zero. Emission is stored separately from diffuse albedo, preserving metallic base-color emission.

Traversal resolves tied boundaries one face at a time to retain the actual adjacent light cell. The default budget is 256 cells, with a shader hard bound of 512. Bounded sky rays use the same explicit completion outcomes. Initial-cell hits, negative coordinates and integer world offsets are covered by controlled tests.

The distant cache contract uses radius sqrt(3) times selected-level spacing and requires a clear local segment of twice that radius. Neighbor lookups reproject direction onto that sphere; radiance magnitude is unchanged. Existing cache texels with shorter recorded distance are rejected, because they do not represent the distant domain. Missing or near-only cache data remains unresolved. This initial implementation chooses one covered level; it does not introduce a new cross-level blend or rebuild the world atlas as a dedicated far-only cache.

See [local tracing regressions](LumOn.WorldProbeLighting.ReproductionTests.md#local-world-tracing-and-cache-handoff) for executable coverage and receipts. Live camera motion, real asynchronous upload pressure, partial geometry support and representative CPU/GPU performance still require runtime validation. These are not established by a passing small GPU fixture.


## Direct irradiance visibility

The selected replacement traces each contributing world-probe center to the reconstructed receiver through published local geometry. It uses the same DDA and readiness contract as local screen-probe tracing, but does not evaluate hit lighting or sample the local light/material textures.

Filtered distance moments were considered. They could soften nearest-depth discontinuities, but a distribution of neighboring distances does not establish whether a particular segment crosses a wall. Using the existing voxel grid avoids that ambiguity for supported geometry and requires no additional distance atlas.

The shared sampler accepts only ClearToLimit. A hit, unsupported cell, missing publication, bounds exit or exhausted budget rejects the corner. A coincident receiver/probe still requires a published empty cell. The receiver endpoint is shortened by 0.0001 world units to avoid classifying the receiving boundary as an intervening wall; there is no spacing-scaled visibility bias. Integer chunk offsets preserve large-world coordinates.

Accepted corners retain the existing interpolation normalization, directional readiness checks, lighting integration and confidence semantics. Visibility no longer depends on the nearest octahedral depth sample. Directional cache samples still require valid distance encodings to supply lighting.

The renderer enables this path in both atlas and SH9 gather programs. Both paired branches bind the same prepared local snapshot. World-probe debug programs prepare and bind the current published scene independently; unrelated debug categories do not request the local visibility path. Geometry-only bindings occupy units 6/7 for atlas gather, 12/13 for SH9 gather, and 34/35 in the existing debug binding namespace. SH9 remains within sixteen active sampler units.

The compile-time legacy path remains for older directional-sampling controls. Production direct consumers explicitly enable local visibility. Missing local resources do not select the old approximate visibility path.

Supported visibility is limited to full opaque voxel geometry inside the published local window. A coarse world-probe neighbor outside that window can be rejected even when its radiance exists. This is conservative unresolved coverage, not proof that the neighbor is physically occluded. Controlled cross-level and camera-motion coverage is recorded in the reproduction report. Live camera movement, partial geometry and GPU traversal cost require further validation.

### Stable player-origin conversion

Both the lighting renderer and debug renderer publish the frame bridge from Entity.Pos, matching the player-relative coordinate convention used for cache origins. The bridge splits that double-precision position into integer chunk coordinates and a bounded remainder. CameraPos and inverse-view translation are not inputs: inverse-view reconstruction has already produced player-relative coordinates, including the camera transform.

This shared conversion applies to screen-probe local tracing, direct irradiance visibility, and reconstructed occupancy debug queries. The repair removes camera-bob-dependent cell shifts without changing cache publication timing or the local-window coverage policy.

### Screen-hit outgoing radiance

Screen hits now resolve their supported opaque surface through the same local scene and evaluator as screen misses. The local segment ends at the screen-ray hit distance plus the configured screen thickness and the small origin-offset difference, capped by the ray maximum. Misses retain their cache-handoff distance. Sky-light visibility uses the same evaluation radius in both cases.

Ready local shading replaces screen emission rather than adding to it, so emission is counted once. Known dark surfaces retain confidence 1. Missing non-emissive lighting has confidence 0; it is not a fully evaluated black sample. Explicit screen emission remains available when no supported local hit is resolved. A known opaque local hit with unavailable material/light data stays dark and unresolved, preventing emission from a different screen hit from passing through it.

This introduces bounded local traversal for screen hits without extra texture units or render targets. It does not enable the separate PBR direct-light renderer or introduce final-lighting feedback. Lighting still follows the existing voxel approximation; unsupported geometry and live GPU cost remain limitations.
