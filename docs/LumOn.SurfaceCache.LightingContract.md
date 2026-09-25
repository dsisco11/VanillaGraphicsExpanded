# Surface-cache lighting contract

This is the controlling contract for Sections 4–7 and 9–13 of `LumOn.SurfaceCache.TestCoverage.todo`. It defines the target storage and producer/consumer semantics; lighting producers and publication are implemented in Section 5, and geometry-hit consumers are implemented in Section 6.

## Terrain coordinates and camera motion

Terrain `worldPos` is player-relative before the view transform. Both `LumOnTerrainBridgeUpdateRenderer` and `LumonSceneFeedbackUpdateRenderer` must publish `Entity.Pos` through `LumOnFrameWorldSpaceBridge.Compute`, retaining double precision until the integer chunk and fractional remainder are separated. Camera position and inverse-view translation do not belong in that origin conversion: using `CameraPos - inverseView.translation` makes stationary voxel identities and patch UVs depend on camera bob.

SurfaceCache irradiance, material, page-ready and patch-UV views consume the terrain PatchId buffer. Their alignment therefore depends on this producer contract even when the fullscreen debug shader does not reconstruct a position. Camera movement alone must not change the world cell, patch identity or patch UV of a stationary surface.

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

Surface Cache environment completion uses the geometry owner's authoritative world height, carried in `SurfaceLightingParams.policy.y`; it never substitutes the local volume's upper edge. An upward ray becomes `SKY` only when integer DDA traversal crosses that boundary after a fully supported clear segment, before reading any voxel above the world. This avoids floating-point plane-distance rounding turning a sky ray into a hit above the boundary. A surface encountered first still resolves from the previous outgoing generation. Coverage exits, missing/unsupported geometry and exhausted budgets remain unresolved and preserve history. World height is constant for the loaded world; moving local coverage does not change this boundary.

Under the current effective vanilla sunlight policy, established sky contributes **zero additional indirect radiance**: its direct sky/sun energy was already seeded. That zero is a resolved sample, included in the full ray count and completed-batch weight; mixed sky/surface batches must not normalize only over surface hits. This permits sky-facing indirect updates to complete without adding the sunlight contribution twice. It does not replace the game's sunlight model with a separately sampled environment. Block-light and material-emission accounting remain unchanged.

There is no independent distant-surface lighting provider. The supported distant endpoint is the proven world-top environment; rays that leave coverage before reaching it continue to retry, not sample an unverified sky or confident black fallback. No world-probe feedback dependency is introduced. Adding distant geometry/radiance coverage or replacing effective sunlight with a physical environment producer requires a separate source and validity contract.

The current weight counts completed equal-sized batches, not individual rays. The publication owner invalidates history for ray-count, traversal-budget, batch-size and source-policy changes. With capped weight `w' = min(w+1,1024)`, update `mean' = mix(mean,sample,1/w')`; multiplying the old mean by a capped weight and adding a new sample before dividing by the same cap causes energy growth.

Indirect updates sample the previous published outgoing-radiance generation. They must never read the lighting image being written by the same dispatch. At initial allocation, valid direct lighting plus emission may seed outgoing radiance with an explicitly zero, unconverged indirect term. Convergence and resource validity are separate states.

## Atlas and publication design

Retain one shared physical-page allocation and page-table mapping across all layers. Add no independently resident lighting pages.

| Layer | Storage | Lifetime |
| --- | --- | --- |
| Captured material | Existing normal/surface identity plus linear diffuse/emission material data; emission must retain HDR range | Captured geometry/material identity |
| Direct irradiance | RGBA16F, RGB irradiance; alpha state (0 unavailable, 1 initialized exposed/empty, 2 initialized hidden face) | Captured surface identity; lighting changes make retained values stale |
| Indirect irradiance | RGBA16F, RGB irradiance; alpha completed-batch weight (0 no samples) | Captured surface identity; distant geometry/light changes make retained values stale |
| Outgoing radiance | Two RGBA16F generations, RGB radiance; alpha validity | Coherent published generation |

The current `IrradianceAtlas` is the indirect estimator destination. It is not already a combined outgoing-radiance cache. Name new resources and debug modes by their actual term. Captured face identities resolve immutable diffuse and HDR emission data through the explicitly extended surface LUT format.

The pool owns GPU allocation and disposal; lighting updates own writes; the render-thread publication owner exposes immutable resource references, layout, generation, mapping and validity to consumers. Publication occurs only after required image/texture/storage barriers and completed page writes. A pending or failed page cannot publish readiness solely because it was allocated. Retain the last coherent generation only while its geometry/material identity is still valid; stale identities are unavailable, not black. Chunk-slot reuse and atlas recreation invalidate all associated generations.

Outgoing-radiance generations use ping-pong storage. Before switching generations, preserve unchanged valid tiles in the destination through budgeted copy/carry-forward; do not expose uninitialized tiles. A published output tile initializes every required texel/border. Page readiness admits a fully captured page with at least one initialized lighting texel; outgoing alpha independently rejects each unresolved texel. Old generations remain alive until submitted readers are finished. Partial updates must retain coherent mapping and completion state.

Four RGBA16F lighting layers (direct, indirect, two outgoing generations) cost 32 bytes per physical texel, excluding existing material/depth. A 4096² layer set costs 512 MiB, so allocation must use an explicit total-byte budget and account for recreation overlap. Reduce admitted pages/layers or defer work when the budget is exhausted; do not silently multiply existing memory caps. Preserve page, texel, ray, traversal, upload and copy budgets. The implemented pool uses 1024² allocation granularity, 38 bytes per texel including depth/material, and a 256 MiB shared live-allocation cap. The planner splits this cap between near/far pools (at most three layers per field). Allocation reservations include recreation overlap; runtime admission defers creation when credit is unavailable. Old resources are disposed before replacement allocation. The cold disposal path finishes submitted readers before returning byte credit, so deferred GL deletion cannot hide recreation overlap; ordinary lighting updates do not wait this way.

## Estimator audit and implementation boundary

Section 4 corrects the current compatibility estimator's missing Lambert/PDF normalization, double distance attenuation, outside-cell material lookup, partial-ray averaging, and capped-history growth. That standalone estimator retains the effective direct-light adapter as a calibration reference. The production renderer now uses the separate surface-lighting producer described below, with previous-generation hit lighting, emission, direct storage and publication. Geometry-hit consumers use the published outgoing radiance as described below.

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

The render-thread relight owner seeds direct plus emission with zero unconverged indirect. Successful seed buckets and texels remain initialized while unresolved buckets retry; partial publication does not require indirect convergence or world-probe lighting. Partially seeded published pages alternate seed retries with round-robin indirect work so neither can starve the other within the configured page/texel/ray budget. Fully seeded pages receive round-robin indirect updates. Hidden voxel faces (the adjacent cell is solid) are valid zero texels. Direct alpha 2 preserves that identity during later combination so material emission is not added to a hidden face. Captured empty source cells initialize zero without querying an irrelevant exterior cell. A missing geometry/cache hit is still unavailable and never inferred sky.

Direct and indirect layers are progressive diagnostic resources. Only outgoing radiance plus readiness constitute coherent lighting publication. Any batch with successful texel progress can combine its captured page into the other outgoing texture. The combine writes every output address: initialized finite lighting receives alpha 1, unresolved or nonfinite lighting receives zero RGB and alpha 0. The generation is swapped and changed whole tiles are copied back; unchanged valid tiles remain identical in both textures. Failed ray batches retain their prior texel indirect mean and weight without withholding successful neighboring texels. An incomplete capture is never eligible for lighting publication. New or invalidated page identities first reset direct validity, indirect history and pending outgoing data through the producer compute shader. This reset is bounded by the same admitted page count and a separate 65,536-texel ceiling; it adds no atlas storage. The copy and combine budgets are each capped at 65,536 texels per frame in addition to configured page, texel, ray and traversal budgets. Readers borrow resources for immediate render-thread submission; they must query the provider again after lifecycle changes.

The producer checks resource identity, capture content revisions, residency mappings and sampling settings. Ordinary source invalidations mark lighting stale; they do not clear all lighting or request all captures again. Material registry generation changes recreate shared geometry and immutable material tables. Reassigned pages and changed capture inputs clear their readiness before new seeds. The provider rejects obsolete atlas/mapping/capture state even when recapture finishes before the next relight callback.

## Surface identity and lighting freshness

Surface validity and lighting freshness are separate. A valid initialized sample can be outdated and remain usable while new lighting is computed. `SurfaceLightingSnapshot.LightingIsStale` conservatively reports a source invalidation since the lighting owner's last hard initialization; it does not claim per-page convergence. `DependencyRevision` identifies incompatible publication lifetimes and remains stable across ordinary dirty notifications. Settings that change estimator/source-policy meaning still reset lighting under the existing policy.

Each successful four-by-four voxel patch capture retains sixteen exact geometry words, its virtual-page key, chunk-slot generation and a monotonically increasing capture revision. Geometry words identify occupancy classification and the material/block entry in immutable scene-local tables. Comparison excludes legacy and normalized light data: a light-only update cannot alter captured material identity. There are no probabilistic hashes. Changing one owning voxel invalidates its containing page, not all pages in that chunk. An unchanged dirty notification preserves capture and lighting progress.

The geometry owner retains immutable geometry arrays only for published physical cells, sharing the detached publication array rather than copying light data. Payload retention is bounded by `Resolution^3 * 4` bytes (about 11.4 MiB at resolution 144), plus sixteen words per captured physical page and collection metadata. Runtime geometry metrics report `CaptureIdentityBytes` separately from GPU texture bytes. Page identity comparisons run when the geometry revision changes; no GPU readback or second source capture is needed.

After a dirty notification, cell readiness is withdrawn until fresh geometry is published. A page whose owning inputs cannot currently be read becomes unavailable for lighting lookup and relight admission, but its captured identity and lighting storage remain retained. Once inputs return, an exact match restores availability without recapture; a mismatch marks only that page `NeedsCapture | NeedsRelight` and queues its bounded capture retry. A successful recapture gets a new content revision, forcing that page's lighting initialization even if its flags were cleared within the same frame. Previously captured pages elsewhere remain available. Uncaptured or uninitialized texels never become valid merely because old storage exists.

Page reassignment, slot-generation changes and removed/replaced surface identities reject old data. New storage is explicitly initialized. Scene/material-generation or atlas replacement still requires hard invalidation; temporary absence of a scene does not erase a retained atlas. Delayed world-probe GPU query answers are rejected if their queried geometry instance or invalidation revision changed before completion. Current hit validation and scheduler tickets continue to reject obsolete source descriptors; ordinary lighting freshness does not reset all probe history.

Implementation boundary: this change establishes identity-based retention and rejection. The subsequent tasks in `LumOn.WorldProbeSurfaceLighting.todo` implement recurring direct-light refresh, fair freshness scheduling and responsive temporal weighting. Fully seeded pages currently continue their existing indirect schedule, but retained direct lighting is not yet refreshed solely because its source changed. Retention tests establish availability and safety, not convergence after lighting changes. Runtime light-toggle tests using explicit resource recreation establish hard replacement only, not ordinary refresh. The separate capture/indirect rejection backlog also remains unresolved.

Production indirect tracing now uses the exact voxel patch layout, signed integer chunk coordinates, slot ring, current slot generation, captured face identity, page mapping and published readiness to sample outgoing radiance. Screen and world-probe integration use that same lookup through the consumer paths below. The older standalone relight estimator and its historical tests remain normalization/reference coverage; they are no longer the runtime lighting producer.

| Section 5 task | Controlling sections | Implementation and planned evidence |
| --- | --- | --- |
| Material/direct producer | Lighting terms; Supported light adaptation | HDR surface LUT extension; source-policy and albedo tests using real capture and direct production |
| Indirect updates | Indirect estimator and history | Previous-generation producer, per-texel complete-ray-batch retries, partial-page publication, enclosure bounce/normalization tests |
| Publication | Atlas and publication design | Read-only provider, captured-page admission and per-texel radiance validity, bounded tile carry-forward; runtime recreation tests |
| Invalidation | History; Atlas and publication | Scene/material/residency/settings identities; removal/restoration and runtime lifecycle tests |
| Numerical fixtures | Lighting terms; History | Reusable captured enclosure, isolated direct/emission/indirect, valid black and stable multi-bounce tests |

Executed receipts and independent audit results are recorded in the task list after verification.

## Geometry-hit consumers

Screen-probe near-field hits sample the same outgoing-radiance lookup as surface indirect tracing. The lookup uses signed integer chunk coordinates, the exact axial face/patch layout, local hit fractions, current chunk-slot generation, resident page mapping, captured surface identity, and captured-page admission and per-texel radiance validity. A surface ID of zero, absent page, stale slot, mismatched captured material, or invalid outgoing texel is unavailable. Valid black returns success. No consumer reapplies diffuse albedo, emission boost, or Lambert normalization to outgoing radiance.

An unavailable opaque hit retains its hit classification and distance. Screen probes publish zero radiance with zero lighting confidence; they do not sample a world probe through that occluder. Existing screen-only emission remains available when no supported near-field hit supersedes it. Gather and composition continue to consume probe radiance, not surface-cache textures.

The current mixed-probe policy has two distinct decisions. Tracing may use directional world-probe radiance after a clear supported local segment, and stores that result in the screen atlas. Final gather independently selects a world replacement only when its surviving screen interpolation weight is below 0.001 and world confidence exceeds 0.001. Gather weight reflects anchor validity, orientation, depth and (for direct atlas gather) probe-to-pixel visibility; it is not the raw per-ray lighting-confidence channel. A usable screen gather is not supplemented by an extra world irradiance term. Paired suppression diagnostics must zero world radiance in both tracing and gather while preserving the corresponding validity decisions in both gather modes.

Gather world sampling offsets the reconstructed receiver 0.001 blocks along its visible normal. This fixed reconstruction tolerance prevents a receiver a few floating-point units inside its own voxel face from rejecting a probe exactly on that face. It does not scale with probe spacing; intervening voxel walls, unavailable geometry and traversal exhaustion still reject visibility.

World probes keep CPU geometry traversal and directional scheduling. Workers emit integer hit cells, axial normals, within-cell fractions, and engine block identity instead of evaluating the legacy hit-light approximation. Render-thread compute queries validate the current geometry/block identity and invoke the same cache lookup. A bounded batch contains at most 4,096 descriptors (256 KiB); only one batch is pending. GPU fences are polled with zero timeout. CPU workers never receive GPU resource references, and the render thread maps results only after the fence signals. A query covers a ray's known hit; it does not retrace the ray or infer sky from an unavailable cache.

The production CPU trace scene disables vanilla light sampling: neither `GetLightRGBs` nor the neighboring light-cell availability check is required to return a geometry hit. Traversed geometry must still be available. Deferred batches use a neutral sky-intensity multiplier; directional visibility controls sky contribution, so placeholder legacy light values cannot darken previously retained sky directions. The non-deferred compatibility path retains vanilla light sampling.

World-probe cache answers publish ready directional samples immediately and retain only unresolved hit descriptors for up to three additional GPU retries under the same admission ticket. Query batches remain bounded to 4096 hits; uploads share the configured byte budget and require space for metadata plus every admitted ready sample. Retry exhaustion returns the probe to normal scheduling while preserving valid directional history. Successful answers preserve CPU geometry metadata; valid black counts as resolved. Atlas alpha zero means unresolved and consumers exclude it from lighting and confidence; positive hit alpha (including contact hits) and negative established-sky alpha identify valid directions. Admission tickets reject delayed answers after storage reuse, reset, dirtying or disabling. Introduced ring slots, dirtied geometry and suppressed centers clear their directional and scalar history; cache dependency changes clear all history. Overlapping unchanged ring slots retain their samples. Regional invalidations resolve to deduplicated physical slots on the CPU and flush in one compute dispatch before new atlas uploads. The queue is bounded by the allocated slot count; no-op flushes dispatch nothing. Full resets use one compute dispatch without a slot list. Image, texture-fetch, framebuffer and texture-update barriers make the cleared history visible to subsequent writers, consumers and readback. Renderer exits after origin updates also flush pending invalidation when block accessors are unavailable.

### World-probe ray completion

CPU traversal distinguishes `Hit`, `Sky`, `DistanceLimit`, `BudgetExhausted`, `Unavailable`, and `Invalid`. Only `Hit` and `Sky` resolve primary directional samples. A loaded, unobstructed upward ray reaching the authoritative `MapSizeY` boundary establishes sky; unknown world height, unloaded chunks, the lower world boundary, and a finite clear segment do not. The CPU ray budget is 512 visited cells including the starting cell. Geometry hits retain their identity even if subsequent cached lighting is unavailable.

World probes currently have no independent distant-light completion provider. Distance and budget limits therefore produce unsuccessful batches with distinct failure reasons, zero confidence and no atlas samples; existing valid history is retained and work retries. Do not query the same world-probe cache to complete its own rays, infer sky, or publish confident black. An eventual distant-light provider must supply validated radiance/visibility beyond the cleared segment with compatible revisions before publication. Partial retention applies to unresolved Surface Cache lighting; incomplete primary geometry batches still retry without publishing new directions.

Nearby occupancy checks may accept `DistanceLimit` as a clear local segment because they do not calculate environment lighting. Legacy secondary bounce rays contribute sky only for `Sky`. Negative atlas alpha is reserved for established sky visibility. The GPU tracer's existing `CLEAR` likewise means only a finite clear segment: screen probes may complete it with valid world-probe lighting. Surface Cache indirect tracing uses the environment completion and source-accounting rules above without expanding geometry coverage.

Publication exposes a dependency revision separate from the progressively increasing lighting generation. Incompatible cache lifetimes or whole-provider unavailability discard dependent screen history and world-probe atlases/requests. Ordinary source freshness changes and completed bounce generations do not reset probe history. Suspended pages remain in a coherent provider snapshot with zero readiness, so local unavailability does not itself become whole-provider loss. The existing CPU geometry backend remains the world-probe tracing owner; asynchronous GPU hit-query transport is the implemented dependency that bridges it to GPU-owned lighting. A future all-GPU tracing backend is not required for this transport. Hits outside resident cache coverage are explicitly unresolved, including farther world-probe levels; no legacy light approximation silently substitutes for missing cached surfaces.

| Section 6 task | Controlling sections | Implementation and verification |
| --- | --- | --- |
| Shared hit addressing | Atlas and publication; Geometry-hit consumers | Shared lumon_surface_lighting.glsl; real capture/producer/query tests across all six faces, positive/negative chunk boundaries, stale generations and unavailable pages |
| Screen-probe provider | Lighting terms; Geometry-hit consumers | Composition injects ISurfaceLightingProvider; shader-owned contracts bind outgoing radiance and addressing; screen probes consume produced light once |
| Unavailable versus black | Indirect estimator and history; Geometry-hit consumers | Explicit lookup validity; tests preserve opaque distance while confidence becomes zero; valid black remains confident |
| World-probe ownership | Atlas and publication; Geometry-hit consumers | Worker descriptors, render-thread asynchronous query batch, admission tickets and atomic probe uploads; CPU/GPU transport and stale-request tests |
| Downstream ownership | Lighting terms; Geometry-hit consumers | Positive hit-consumer binding tests and negative gather/combine cache-binding tests; no reflection pipeline introduced |

Executed receipts and final independent review remain recorded in the task list. Full cache-to-final-pixel behavioral coverage is recorded in Section 7.

## Final-lighting verification traceability

The table below records the original shader-chain verification. Current coverage uses mod-owned startup and registered callbacks for broad runtime scenarios, with fixed-ray and temporal numerical assertions retained as explicit component tests. The original test names and final-composition boundary are historical; [Production runtime lighting coverage](LumOn.SurfaceCache.RuntimeCoverage.md) maps every scenario to its current owner and documents the simulated engine and composition boundaries.

| Section 7 task | Controlling sections | Behavioral evidence |
| --- | --- | --- |
| Positive consumer contract | Geometry-hit consumers | Existing positive hit-consumer declarations; gather/composition remain downstream |
| Cache to final pixels | Lighting terms; Indirect estimator and history; Geometry-hit consumers | SurfaceLightingFinalPixelsTests: real captured/produced outgoing radiance, offscreen hit textures, filtering, direct atlas and SH9 gather, upsample and composition; source removal/restoration checked at each boundary |
| Scene and residency scenarios | Supported light adaptation; Atlas and publication design | Emission with zero reflectance, sealed darkness, successive published bounces, unavailable/stale pages, recreated resources; no injected downstream lighting |
| World probes and runtime publication | Geometry-hit consumers; Implemented publication and verification boundaries | SurfaceLightingWorldProbeFlowTests: real CPU traversal, asynchronous GPU lookup, production atlas upload; registered runtime cache callbacks publish lighting changes and recreated resources across frames |

These fixtures substitute controlled engine scene inputs, not cached or downstream radiance. Screen-hit tests bind no world-probe fallback and no screen emission. The GPU attachments produced by tracing are passed directly to downstream shaders. Executed build/test receipts and the successful independent completion audit are recorded in the task list. Headless GPU evidence does not claim live in-game verification.

Runtime transition coverage exposed two scheduling omissions: dependency invalidation set `NeedsCapture` without requesting a recapture sweep, and failed GPU captures were discarded after their first attempt. The feedback owner now schedules invalidated pages and retains unresolved keys for a subsequent bounded sweep. Retired or already resolved pages are skipped; retries resume after the current sweep so later pages keep their admission opportunity. `FailedCaptureRetriesAfterGeometryBecomesAvailable` verifies an actual GPU capture failure followed by successful publication after the controlled source becomes available.

## Batch ownership

World-probe worker results and retained retry samples use `ImmutableArray<LumOnWorldProbeAtlasSample>`. Producers freeze private builders before handing batches to another owner; default immutable arrays are treated as empty at consumption boundaries. Render-thread relight, query and vertex staging lists remain private reusable buffers. Immediate GPU uploads borrow spans and copy their data before returning; no borrowed span is retained across frames or asynchronous work. Relight completion uses a separate committed-work buffer so processing results never clears or grows the submitted list while its span is in use.
Ownership and staging validation: 60 focused tests passed with no skips, including retained immutable batches, default empty results, partial publication, retries and GPU uploads. Receipts: artifacts/TestResults/immutable-lighting-batches.trx and artifacts/TestResults/immutable-lighting-batches-final.trx. Validation used separate bin/ImmutableValidation outputs because the running game held the normal mod DLL locked.
