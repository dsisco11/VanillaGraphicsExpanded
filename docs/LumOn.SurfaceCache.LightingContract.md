# Surface-cache lighting contract

This is the controlling contract for Sections 4–7 of `LumOn.SurfaceCache.TestCoverage.todo`. It defines the target storage and producer/consumer semantics; lighting producers and publication are implemented in Section 5, while probe consumers belong to Section 6.

## Lighting terms and units

All values are scene-linear RGB in consistent engine-relative lighting units, with no camera exposure baked into persistent caches. These are not calibrated SI measurements. Irradiance integrates radiance over solid angle with the receiving cosine. Display exposure is applied downstream, once; an exposure change alone must not invalidate the cache.

| Term | Meaning | Producer | Consumer |
| --- | --- | --- | --- |
| Diffuse albedo | Dimensionless linear diffuse reflectance in [0,1], including metallic rejection | Material capture | Outgoing-radiance combination |
| Emission | Outgoing emitted radiance, independent of incident light and Lambert normalization | Material capture | Outgoing-radiance combination |
| Direct irradiance | Incident lighting supplied by supported primary light sources | Direct-light update | Outgoing-radiance combination |
| Indirect irradiance | Hemisphere integral of outgoing radiance from other surfaces | Budgeted surface tracing | Outgoing-radiance combination |
| Outgoing radiance | Diffuse reflected plus emitted lighting leaving this surface | Combination | Geometry-hit evaluation for surface tracing and probes |

`L_out = (E_direct + E_indirect) * diffuseAlbedo / pi + emission`.

Apply the hit surface's albedo exactly once. Do not multiply emission by albedo or divide it by pi. Irradiance stored on a receiver excludes that receiver's albedo. Use finite nonnegative values for all persisted lighting; reject nonfinite source samples, clamp finite negative components to zero, and clamp finite HDR values to the chosen storage range before half-float conversion (maximum 65504).

## Supported light adaptation

The current block-light and sunlight level fields are prepropagated game lighting, not individually traceable physical lights. Treat their sum as an effective direct-irradiance input at the exterior cell of the receiving face. The compatibility mapping is `32 * (blockScalar * lightColor + sunScalar * white)`. Preserve this scale while changing radiometric normalization; it is an explicit engine-unit conversion, not an extra inverse-square term.

Block-light propagation already includes the game's attenuation and blocking. Sun-level visibility likewise comes from the supplied field. Do not invent a second shadow or distance factor for either field. A future directional sun or environment-sky producer replaces the corresponding effective term; it must not be added on top of the same sunlight contribution. Sky radiance may count as direct illumination only after an explicit environment visibility result. Exiting a bounded geometry volume, unavailable data, or an exhausted traversal budget establishes no sky visibility.

Surface emission is a distinct material term. When emitted-light sources also populate the game block-light field, the direct-light adapter must establish a single accounting policy before enabling both representations: exclude the overlapping source contribution or select one representation. Source-resolved removal is not supported by the current aggregate field. The implemented SurfaceLightingMaterialEmission policy selects one representation globally: false (default) uses aggregate block light and zero material emission; true excludes all aggregate block light and uses material emission. Sunlight is retained in either mode. Material emission is currently neutral scene-linear radiance of 32 * PBR Emissive; colored emission requires a future material-contract extension. The surface LUT stores this value as binary16 in the upper half of its fourth uint, with roughness in the low byte. This preserves HDR without increasing immutable table upload size. Switching the policy invalidates every dependent history.

## Indirect estimator and history

For cosine-weighted sampling, `pdf = cos(theta)/pi`, so `E_indirect = pi * mean(L_hit)`. Free-space radiance has no additional `1/(1+d*d)` factor; source attenuation belongs to the direct-light producer. Material identity comes from the struck face; incident light can come from the adjacent outside cell.

Only complete resolved ray batches contribute to a texel's mean. If any direction is unavailable, preserve that texel's previous history, flag the work for retry, and add no weight. Valid zero radiance is a successful sample. This policy avoids normalizing solely over the subset of resolved directions. Scheduling must revisit unresolved work within existing budgets.

The current weight counts completed equal-sized batches, not individual rays. The publication owner invalidates history for ray-count, traversal-budget, batch-size and source-policy changes. With capped weight `w' = min(w+1,1024)`, update `mean' = mix(mean,sample,1/w')`; multiplying the old mean by a capped weight and adding a new sample before dividing by the same cap causes energy growth.

Indirect updates sample the previous published outgoing-radiance generation. They must never read the lighting image being written by the same dispatch. At initial allocation, valid direct lighting plus emission may seed outgoing radiance with an explicitly zero, unconverged indirect term. Convergence and resource validity are separate states.

## Atlas and publication design

Retain one shared physical-page allocation and page-table mapping across all layers. Add no independently resident lighting pages.

| Layer | Storage | Lifetime |
| --- | --- | --- |
| Captured material | Existing normal/surface identity plus linear diffuse/emission material data; emission must retain HDR range | Material revision |
| Direct irradiance | RGBA16F, RGB irradiance; alpha validity (0 unavailable, 1 valid) | Direct-light revision |
| Indirect irradiance | RGBA16F, RGB irradiance; alpha completed-batch weight (0 no samples) | Geometry/material/light dependency revision |
| Outgoing radiance | Two RGBA16F generations, RGB radiance; alpha validity | Coherent published generation |

The current `IrradianceAtlas` is the indirect estimator destination. It is not already a combined outgoing-radiance cache. Name new resources and debug modes by their actual term. Captured face identities resolve immutable diffuse and HDR emission data through the explicitly extended surface LUT format.

The pool owns GPU allocation and disposal; lighting updates own writes; the render-thread publication owner exposes immutable resource references, layout, generation, mapping and validity to consumers. Publication occurs only after required image/texture/storage barriers and completed page writes. A pending or failed page cannot publish readiness solely because it was allocated. Retain the last coherent generation only while its geometry/material identity is still valid; stale identities are unavailable, not black. Chunk-slot reuse and atlas recreation invalidate all associated generations.

Outgoing-radiance generations use ping-pong storage. Before switching generations, preserve unchanged valid tiles in the destination through budgeted copy/carry-forward; do not expose uninitialized tiles. Page readiness covers every required texel/border. Old generations remain alive until submitted readers are finished. Partial updates must retain coherent mapping and completion state.

Four RGBA16F lighting layers (direct, indirect, two outgoing generations) cost 32 bytes per physical texel, excluding existing material/depth. A 4096² layer set costs 512 MiB, so allocation must use an explicit total-byte budget and account for recreation overlap. Reduce admitted pages/layers or defer work when the budget is exhausted; do not silently multiply existing memory caps. Preserve page, texel, ray, traversal, upload and copy budgets. The implemented pool uses 1024² allocation granularity, 38 bytes per texel including depth/material, and a 256 MiB shared live-allocation cap. The planner splits this cap between near/far pools (at most three layers per field). Allocation reservations include recreation overlap; runtime admission defers creation when credit is unavailable. Old resources are disposed before replacement allocation. The cold disposal path finishes submitted readers before returning byte credit, so deferred GL deletion cannot hide recreation overlap; ordinary lighting updates do not wait this way.

## Estimator audit and implementation boundary

Section 4 corrects the current compatibility estimator's missing Lambert/PDF normalization, double distance attenuation, outside-cell material lookup, partial-ray averaging, and capped-history growth. That standalone estimator retains the effective direct-light adapter as a calibration reference. The production renderer now uses the separate surface-lighting producer described below, with previous-generation hit lighting, emission, direct storage and publication. Section 6 still owns probe integration.

## Task traceability and verification

The selected Section 4 originally links no external document. Its four checklist items are the authority; this contract records their decisions and becomes the explicit reference for subsequent work.

| Task | Contract section | Evidence |
| --- | --- | --- |
| Lighting terms, units, exposure, classification | Lighting terms and units; Supported light adaptation | Source review of material/light producers; explicit compatibility conversion and overlap policy |
| Combined outgoing radiance | Lighting terms and units; Atlas and publication design | Formula, finite-value policy, named producer/consumer boundaries; implemented by SurfaceLightingDispatch and the surface-lighting producer shader |
| Estimator correction | Indirect estimator and history | `lumonscene_relight_voxel_dda.csh`; `SurfaceCacheLightingTermsTests`; existing dynamic-history and shared-surface tests |
| Storage, validity, ownership and budgets | Atlas and publication design | Layer formats, byte costs, revisions, ping-pong lifetime and budget obligations; implemented by the bounded physical atlas pool and publication owner |

Executed results and independent completion review are recorded in the task list after validation.

## Implemented publication and verification boundaries

The render-thread relight owner seeds direct plus emission with zero unconverged indirect, then fairly revisits resident captured pages for successive indirect updates. Hidden voxel faces (the adjacent cell is solid) are valid zero texels, avoiding permanently incomplete pages at voxel corners. A missing geometry/cache hit is still unavailable and never inferred sky.

Direct and indirect layers are progressive diagnostic resources. Only outgoing radiance plus readiness constitute coherent lighting publication. Each completed page is combined into the other outgoing texture, the generation is swapped, and the changed complete tiles are copied back; unchanged valid tiles therefore remain identical in both textures. No partial page is advertised ready. The copy and combine budgets are each capped at 65,536 texels per frame in addition to configured page, texel, ray and traversal budgets. Readers borrow resources for immediate render-thread submission; they must query the provider again after lifecycle changes.

The producer checks resource identity, scene invalidation, capture history, residency mappings and sampling settings. Geometry/material/light changes discard dependent histories conservatively. Material registry generation changes recreate shared geometry and immutable material tables. Residency changes clear readiness before new seeds. The provider also rejects obsolete atlas/mapping/capture state between capture and relight.

Production indirect tracing now uses the exact voxel patch layout, signed integer chunk coordinates, slot ring, current slot generation, captured face identity, page mapping and published readiness to sample outgoing radiance. Probe integration remains Section 6. The older standalone relight estimator and its historical tests remain normalization/reference coverage; they are no longer the runtime lighting producer.

| Section 5 task | Controlling sections | Implementation and planned evidence |
| --- | --- | --- |
| Material/direct producer | Lighting terms; Supported light adaptation | HDR surface LUT extension; source-policy and albedo tests using real capture and direct production |
| Indirect updates | Indirect estimator and history | Previous-generation producer, complete-batch retries, enclosure bounce/normalization tests |
| Publication | Atlas and publication design | Read-only provider, complete-page readiness, bounded tile carry-forward; runtime recreation tests |
| Invalidation | History; Atlas and publication | Scene/material/residency/settings identities; removal/restoration and runtime lifecycle tests |
| Numerical fixtures | Lighting terms; History | Reusable captured enclosure, isolated direct/emission/indirect, valid black and stable multi-bounce tests |

Executed receipts and independent audit results are recorded in the task list after verification.
