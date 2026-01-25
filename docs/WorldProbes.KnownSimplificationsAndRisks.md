# World Probes: Known Simplifications and Risks (Phase 18)

> **Status**: Living document  
> **Scope**: World-probe (world-space clipmap probes) system and associated debug tooling.

This document inventories intentionally “cheap”, placeholder, or otherwise simplified pieces of the world-probe system.

These are not necessarily _bugs_—many were introduced to get Phase 18 running end-to-end—but they _are issues_ in the
sense that they can produce incorrect lighting, unstable behavior, misleading debug output, or performance/scalability
problems.

## Priority overview

Priorities are scoped to **Phase 18 diffuse GI correctness + debuggability**:

- **P0 (Critical)**: likely to produce severe/obvious artifacts, systemic wrong lighting, or misleading debugging.
- **P1 (High)**: major quality limitation; visible but not always catastrophic.
- **P2 (Medium)**: tuning/scalability/feature-completeness issues; important but not the top driver of artifacts.
- **P3 (Low)**: edge cases; still worth fixing but unlikely to dominate visuals.

### P0 (Critical)

- [x] [2.2 Unloaded chunks treated as misses; placeholder chunk objects can abort traces](#wp-2-2) — Implemented (Option A)
- [ ] [2.3 Probe disabled when its center lies inside a solid collision box](#wp-2-3)
- [x] [5.1 ShortRangeAO multiplies irradiance by `aoConf`](#wp-5-1) — Implemented (Option A)
- [ ] [5.5 Screen-probe miss fallback assumes sky radiance (miss != sky)](#wp-5-5)
- [x] [6.1 Orb visualizer samples irradiance with a fixed “up” normal](#wp-6-1) — Resolved (outdated)

### P1 (High)

- [ ] [1.1 Skylight bounce uses limited secondary sky visibility traces](#wp-1-1)
- [x] [1.2 Placeholder sky radiance model (miss shader)](#wp-1-2) — Implemented (Option B)
- [ ] [1.3 Sky visibility/intensity split still heuristic](#wp-1-3)
- [ ] [1.5 Material factors are simplified (base-texture-only resolution)](#wp-1-5)
- [ ] [2.1 “Most solid block + collision boxes” as geometry](#wp-2-1)
- [ ] [2.4 Fixed max trace distance per probe: `spacing * resolution`](#wp-2-4)
- [ ] [2.5 Fixed 64-direction sampling pattern with uniform weights](#wp-2-5)
- [ ] [4.2 Meta flags are written but not consumed by shading](#wp-4-2)
- [ ] [5.2 Outside-clip-volume returns zero contribution (hard cutoff)](#wp-5-2)
- [ ] [5.3 Cross-level blend band is hard-coded](#wp-5-3)

### P2 (Medium)

- [ ] [1.4 Specular GI path is not implemented](#wp-1-4)
- [ ] [3.1 O(N) selection scan over the entire 3D grid each frame](#wp-3-1)
- [ ] [3.2 Hard-coded staleness and retry/backoff constants](#wp-3-2)
- [ ] [3.3 Upload budget is enforced using an estimated bytes-per-probe constant](#wp-3-3)
- [ ] [3.4 Dirty-chunk overflow invalidates an entire L0 volume](#wp-3-4)
- [ ] [4.1 `SkyOnly` flag is a heuristic based on AO+distance](#wp-4-1)
- [ ] [5.4 Screen-first blending is a policy (can hide world-probe problems)](#wp-5-4)
- [ ] [6.2 Debug tone-mapping is non-photographic and compresses differences](#wp-6-2)

### P3 (Low)

- [ ] [2.6 Hit face decoding fallback to `Up`](#wp-2-6)

## 1. Lighting / Integration Heuristics

<a id="wp-1-1"></a>

### 1.1 (P1) Skylight bounce uses limited secondary sky visibility traces

**Where**: `VanillaGraphicsExpanded/LumOn/WorldProbes/Tracing/LumOnWorldProbeTraceIntegrator.cs` (`EvaluateHitRadiance`)

**What**: Skylight “bounce” at hit points is estimated by launching a small, fixed set of **secondary traces** toward the
sky hemisphere (+Y) and computing a cosine-weighted visibility factor. This replaces the earlier “up-only” gating, but
remains simplified (low sample count, fixed max distance).

**Why this is an issue**

- **Limited accuracy**: a low sample count can under/over-estimate sky visibility in tight geometry (thin overhangs,
  small apertures) and can miss distant occluders.
- **Fixed distance clamp**: secondary traces use a clamped max distance, so “sky visibility” becomes a local heuristic
  rather than a true line-of-sight to open sky.
- **Added CPU cost**: secondary traces increase per-probe work and can reduce the number of probes updated per frame if
  budgets are tight.

**Options to address**

- **Option A (tuning)**: Increase sample count and/or max distance; make them configurable and consider per-level scaling.
- **Option B (adaptive)**: Use fewer traces for near-up normals and more for side normals, or early-out when sampled
  skylight intensity is ~0.
- **Option C (hybrid)**: Add a cheap sky-visibility signal derived from voxel skylight levels and reserve ray tracing
  for edge cases (overhangs, near-geometry probes).

<a id="wp-1-2"></a>

### 1.2 (P1) Placeholder sky radiance model (miss shader)

**Where**: `VanillaGraphicsExpanded/LumOn/WorldProbes/Tracing/LumOnWorldProbeTraceIntegrator.cs` (`EvaluateSkyRadiance`)

**Status**: Implemented (Option B)

**What**: Ray misses no longer emit sky radiance into RGB SH; sky is represented via `ShSky` + `worldProbeSkyTint`.

**Why this is an issue**

- **Mismatch vs actual in-game sky/ambient**: the world-probe system can disagree with the rest of the renderer,
  producing inconsistent GI coloration and intensity (especially at sunrise/sunset, storms, caves, etc.).
- **Hard to tune**: any tuning that “looks right” under one sky state may be wrong under another because the sampled sky
  isn’t coupled to the engine’s sky/lighting parameters.

**Options to address**

- **Option A (cheap)**: Replace the gradient with engine-derived sky/ambient colors (time-of-day, weather) so probes match the rest of lighting.
- **Option B (clean separation)**: Represent “sky” only in the dedicated `ShSky` channel (visibility/intensity), and make miss RGB radiance `0` so sky color always comes from `worldProbeSkyTint` in shaders.
- **Option C (simplify)**: Remove the separate sky channel and bake sky radiance entirely into RGB SH (then `worldProbeSkyTint` becomes unnecessary or purely a post-tint).

<a id="wp-1-3"></a>

### 1.3 (P1) Sky visibility/intensity split still heuristic

**Where**:

- CPU: `VanillaGraphicsExpanded/LumOn/WorldProbes/Tracing/LumOnWorldProbeTraceIntegrator.cs` (`ShSky`, `SkyIntensity`)
- GPU: `VanillaGraphicsExpanded/assets/vanillagraphicsexpanded/shaders/lumon_worldprobe_clipmap_resolve.fsh` (`ProbeVis0.z`)
- Shader: `VanillaGraphicsExpanded/assets/vanillagraphicsexpanded/shaders/includes/lumon_worldprobe.glsl` (`skyIntensityAccum`)

**What**: The probe payload now separates:

- **Sky visibility**: stored in `ShSky` (L1 SH), accumulated as **miss = 1, hit = 0**
- **Sky intensity**: stored as a **scalar** (`SkyIntensity`) and packed into `ProbeVis0.z`

**Why this is an issue**

- **`SkyIntensity` is still a heuristic**: it is currently derived from hit sample skylight values, which can be
  unstable when few hits occur or when hit samples are not representative of the probe’s “true” skylight intensity.
- **Consumes a reserved channel** (`ProbeVis0.z`) that could otherwise be used for future visibility/cone metadata.

**Options to address**

- **Option A (probe-sample intensity)**: Sample skylight intensity at the probe position (or a small neighborhood)
  rather than using a hit-average heuristic.
- **Option B (visibility-only)**: Treat sky intensity as purely global (uniform-driven) and set `SkyIntensity = 1`,
  using `ShSky` as sky visibility only.
- **Option C (higher fidelity)**: Store a separate sky _intensity SH_ (or a higher-order sky representation) rather than
  a single scalar multiplier.

<a id="wp-1-4"></a>

### 1.4 (P2) Specular GI path is not implemented

**Where**: `VanillaGraphicsExpanded/LumOn/WorldProbes/Tracing/LumOnWorldProbeTraceIntegrator.cs` (`specularF0` TODO)

**What**: The integrator computes/threads `specularF0` but does not integrate any specular contribution into probe
payloads.

**Why this is an issue**

- **Metals/rough reflections are missing** from world-probe GI, so off-screen specular response is unrepresented.
- **Material response is incomplete**: even if diffuse GI looks plausible, the shading model can’t rely on probes for
  the specular lobe, which can cause discontinuities between screen-space and world-probe fallback.

**Options to address**

- **Option A (cheap)**: Use world-probe diffuse irradiance as a fallback for _very rough_ specular only (high roughness), and keep glossy specular screen-space only.
- **Option B (directional lobe)**: Extend the probe payload with a compact directional specular representation (e.g., dominant direction + RGB intensity + cone/roughness), and evaluate it in shading.
- **Option C (higher fidelity)**: Store higher-order angular data (e.g., L2/L3 SH, spherical Gaussians, or a small prefiltered lobe set) specifically for specular.

<a id="wp-1-5"></a>

### 1.5 (P1) Material factors are simplified (base-texture-only resolution)

**Where**: `VanillaGraphicsExpanded/PBR/Materials/WorldProbes/BlockFaceTextureKeyResolver.cs`

**What**: Per-face material lookup resolves only the base texture (`CompositeTexture.Base`) and ignores overlays,
composite “++0~” variants, RNG/position-dependent variants, etc.

**Why this is an issue**

- **Wrong albedo/F0 for many blocks** that rely on overlays/composites/variants, biasing probe radiance.
- **Systematic color errors**: e.g., a block with an overlay tint may bounce the wrong color.
- **Debugging difficulty**: the probe system appears “physically based” but can silently fall back to defaults or an
  unrelated base texture.

**Options to address**

- **Option A (pragmatic)**: Extend the resolver to understand composite/overlay keys (including “++0~” combiner forms) and resolve the _actual_ authored texture key space used by the material atlas/registry.
- **Option B (approximate)**: When exact per-face mapping is hard, compute a per-block (or per-blockgroup) averaged derived surface across all resolved textures/variants to reduce systematic bias.
- **Option C (override map)**: Add an explicit mapping/override table for blocks/material groups that are known to be mis-resolved, to close gaps incrementally.

## 2. Tracing / Scene Representation Simplifications

<a id="wp-2-1"></a>

### 2.1 (P1) “Most solid block + collision boxes” as geometry

**Where**: `VanillaGraphicsExpanded/LumOn/WorldProbes/Tracing/BlockAccessorWorldProbeTraceScene.cs`

**What**: Tracing treats the scene as the `GetMostSolidBlock()` at each voxel, with collision boxes as the hit geometry.

**Why this is an issue**

- **Translucency/liquids/foliage are not represented correctly**: “solid vs not” is too coarse for GI, and collision
  boxes do not reflect visual opacity.
- **Micro-geometry loss**: collision boxes are an approximation and can miss significant shading surfaces (slabs, fences,
  cutout meshes, etc.), causing incorrect occlusion and light transport.

**Options to address**

- **Option A (cheap, conservative)**: Treat non-air as full-voxel occluders (or use opaque-side flags) to stabilize occlusion, even if fine shape detail is lost.
- **Option B (better match visuals)**: Intersect against a representation closer to rendered geometry (block shape/mesh/voxelized shape) instead of collision boxes.
- **Option C (material-aware)**: Introduce per-block transmittance categories (solid/leaf/liquid/glass) so “hit” can attenuate rather than fully block, improving GI around translucent content.

<a id="wp-2-2"></a>

### 2.2 (P0) Unloaded chunks treated as misses; placeholder chunk objects can abort traces

**Where**: `VanillaGraphicsExpanded/LumOn/WorldProbes/Tracing/BlockAccessorWorldProbeTraceScene.cs`

**Status**: Implemented (Option A)

**What**:

- If `GetChunkAtBlockPos()` returns null, the trace returns **Aborted** (probe is retried later).
- If the accessor surfaces placeholder chunk data that throws `NotImplementedException`, the trace returns **Aborted**
  (probe is retried later).

**Why this is an issue**

- **Can create temporary “holes”**: aborting defers probe updates until world data is readable, which can reduce
  world-probe coverage near rapidly streaming chunk boundaries.
- **Hard to diagnose**: visual artifacts may look like a lighting bug but are actually a data-availability policy.

**Options to address**

- **Option A (safe)**: Treat _unloaded_ as **Aborted** (retry later) rather than **Miss**, so missing world data never turns into “open sky” lighting. **(Implemented)**
- **Option B (conservative)**: Treat unloaded as “occluded” (hit/blocked) to avoid bright leaks, optionally only on near levels where correctness matters most.
- **Option C (streaming-aware)**: Use a policy that distinguishes “truly out of world/sky” from “temporarily missing chunk”, and add per-probe exponential backoff + telemetry so failures are diagnosable.

<a id="wp-2-3"></a>

### 2.3 (P0) Probe disabled when its center lies inside a solid collision box

**Where**: `VanillaGraphicsExpanded/LumOn/WorldProbes/LumOnWorldProbeUpdateRenderer.cs`

**What**: Probes are permanently set to `Disabled` if their center point lies within any collision box of the most-solid
block at that voxel.

**Why this is an issue**

- **Permanent holes**: disabling can create long-lived voids in the clipmap where sampling returns zero/old data even if
  the solid was transient or the classification was wrong.
- **False positives**: collision boxes can be conservative/inaccurate for visual geometry; a “probe center inside” test
  is extremely sensitive and can disable probes near complex blocks.

**Options to address**

- **Option A (minimal)**: Make this a _temporary invalidation_ (e.g., keep as Dirty/Uninitialized) instead of permanent `Disabled`, so it can recover as the world changes.
- **Option B (relocate)**: Offset/relocate the probe sample position to the nearest valid point (push out of solids) rather than disabling the probe.
- **Option C (reduce false positives)**: Only disable on clearly solid/opaque full-cube blocks; for complex collision shapes, keep the probe but clamp confidence or treat results conservatively.

<a id="wp-2-4"></a>

### 2.4 (P1) Fixed max trace distance per probe: `spacing * resolution`

**Where**: `VanillaGraphicsExpanded/LumOn/WorldProbes/LumOnWorldProbeUpdateRenderer.cs`

**What**: Trace max distance is proportional to the clipmap volume extent for that level.

**Why this is an issue**

- **Non-uniform lighting radius** across levels: far levels trace much further, near levels much less, which can bias
  the probe hierarchy and make cross-level blending inconsistent.
- **Occlusion bias**: if max distance is too short for a level, many rays “miss” even in enclosed spaces, inflating sky
  visibility and AO openness.

**Options to address**

- **Option A (configurable)**: Add per-level (or global) clamps for trace distance independent of resolution, so “lighting radius” is explicit and tuneable.
- **Option B (split signals)**: Use a short distance for ShortRangeAO/visibility and a longer distance for radiance, so openness doesn’t depend on how far you trace for lighting.
- **Option C (adaptive)**: Stop tracing early based on convergence (enough hits/misses) or importance, rather than a fixed distance.

<a id="wp-2-5"></a>

### 2.5 (P1) Fixed 64-direction sampling pattern with uniform weights

**Where**:

- `VanillaGraphicsExpanded/LumOn/WorldProbes/Tracing/LumOnWorldProbeTraceDirections.cs`
- `VanillaGraphicsExpanded/LumOn/WorldProbes/Tracing/LumOnWorldProbeTraceIntegrator.cs`

**What**: Directions are an 8×8 octahedral grid (64 directions) with uniform solid-angle weights.

**Why this is an issue**

- **Low angular resolution**: produces noisy/aliased SH coefficients and unstable bent-normal/AO near thin geometry.
- **Directional bias risk**: any structured pattern can alias against voxel grids, causing “sparkle” or banding when
  probes move/retile.

**Options to address**

- **Option A (scale quality)**: Make direction count configurable (or per-level) and increase it for near levels / higher settings.
- **Option B (stochastic)**: Use per-probe/per-update stratified/jittered directions (blue-noise) and rely on temporal accumulation for convergence with less bias.
- **Option C (importance sampling)**: Sample directions with cosine-weighting and/or basis-aware importance so L1 coefficients converge faster for diffuse.

<a id="wp-2-6"></a>

### 2.6 (P3) Hit face decoding fallback to `Up`

**Where**: `VanillaGraphicsExpanded/LumOn/WorldProbes/Tracing/ProbeHitFaceUtil.cs`

**What**: Unknown/invalid normals map to `ProbeHitFace.Up`.

**Why this is an issue**

- **Wrong per-face material lookup** in edge cases: a bad normal becomes “Up”, causing the integrator to use the wrong
  face’s material properties and (worse) to treat the surface as “up-facing” for sky bounce heuristics.
- **Hides bugs**: a fallback prevents a crash but also masks the presence of unexpected normals that may indicate a
  tracing error.

**Options to address**

- **Option A (explicit)**: Add an `Unknown` face and handle it conservatively (no sky-bounce, default material) while logging a diagnostic.
- **Option B (robust fallback)**: Compute face by dominant axis of the normal (largest abs component) instead of defaulting to `Up`.
- **Option C (fail fast in debug)**: Add assertions/counters/tests so unexpected normals are visible during development.

## 3. Scheduler / Update Policy Simplifications

<a id="wp-3-1"></a>

### 3.1 (P2) O(N) selection scan over the entire 3D grid each frame

**Where**: `VanillaGraphicsExpanded/LumOn/WorldProbes/LumOnWorldProbeScheduler.cs`

**What**: Probe selection scans all `resolution^3` probes (per level) to pick the best candidates.

**Why this is an issue**

- **Poor scalability**: costs grow cubically with resolution and linearly with level count, limiting future quality
  increases.
- **Budget interaction**: expensive selection work can dominate even when the trace/upload budgets are small.

**Options to address**

- **Option A (incremental queues)**: Maintain per-state candidate queues/bitsets (dirty/stale/uninitialized) and select from them instead of scanning the whole grid.
- **Option B (spatial partitioning)**: Track dirty regions at chunk/brick granularity and only consider probes within/near those regions plus a camera-neighborhood ring.
- **Option C (stable iterator)**: Use a deterministic scan order with a moving pointer (round-robin) plus priority overrides for dirty/stale, making selection closer to O(budget).

<a id="wp-3-2"></a>

### 3.2 (P2) Hard-coded staleness and retry/backoff constants

**Where**: `VanillaGraphicsExpanded/LumOn/WorldProbes/LumOnWorldProbeScheduler.cs`

**What**:

- Staleness defaults are derived from constants (e.g., `DefaultStaleAfterFramesL0 = 600`) and scaled by spacing.
- Aborted traces back off for a fixed duration (`AbortedRetryDelayFrames = 60`).

**Why this is an issue**

- **Not tied to motion/lighting change rates**: probe refresh is driven by a time heuristic rather than measured error
  or scene dynamics, potentially causing both wasted work and slow convergence.
- **Artifacts under stress**: streaming-heavy scenes can cause repeated abort/backoff cycles, leaving probes stale for
  long periods.

**Options to address**

- **Option A (config)**: Expose staleness/backoff per level in config so it can be tuned for different machines/worlds.
- **Option B (adaptive staleness)**: Scale staleness by camera velocity and lighting change rate (time-of-day/weather), and/or by measured probe deltas.
- **Option C (better retries)**: Use per-probe exponential backoff and a “give up until chunk loaded” signal instead of a fixed delay.

<a id="wp-3-3"></a>

### 3.3 (P2) Upload budget is enforced using an estimated bytes-per-probe constant

**Where**: `VanillaGraphicsExpanded/LumOn/WorldProbes/LumOnWorldProbeScheduler.cs`

**What**: Upload budgeting uses `EstimatedUploadBytesPerProbe = 64` as a coarse proxy.

**Why this is an issue**

- **Can under/over-enforce budgets**: if the real upload cost differs, the system may exceed intended bandwidth or leave
  performance on the table.
- **Makes perf tuning misleading**: configuration values may not correlate with actual GPU upload work.

**Options to address**

- **Option A (accurate constant)**: Budget using the actual packed vertex size / payload size (derived from the current layout) rather than a hand-estimate.
- **Option B (feedback loop)**: Have the uploader return actual bytes uploaded per frame and feed that back into scheduling (moving average).
- **Option C (budget by count)**: Budget purely by probe count (with a separate fixed upper bound) to avoid drifting estimates.

<a id="wp-3-4"></a>

### 3.4 (P2) Dirty-chunk overflow invalidates an entire L0 volume

**Where**: `VanillaGraphicsExpanded/LumOn/WorldProbes/LumOnWorldProbeUpdateRenderer.cs`

**What**: If too many dirty chunks are queued, the system marks the entire level-0 clip volume dirty as a fallback.

**Why this is an issue**

- **Burst re-tracing**: causes sudden spikes in trace demand and visible “GI churn” as large regions refresh at once.
- **Over-invalidates**: many edits may be local but trigger a full L0 refresh anyway.

**Options to address**

- **Option A (increase capacity)**: Raise the pending-dirty queue size and/or drain more per frame to reduce overflow frequency.
- **Option B (coalesce smarter)**: Merge overflow into coarse AABBs/regions (or a chunk bitset) instead of invalidating the entire L0 volume.
- **Option C (prioritize)**: Prefer near-camera dirty chunks; drop or defer far dirty events when overflowing.

## 4. GPU Upload / Metadata Simplifications

<a id="wp-4-1"></a>

### 4.1 (P2) `SkyOnly` flag is a heuristic based on AO+distance

**Where**: `VanillaGraphicsExpanded/LumOn/WorldProbes/Gpu/LumOnWorldProbeClipmapGpuUploader.cs`

**What**: Marks probes `SkyOnly` if `MeanLogHitDistance <= 0` and `AoConfidence > 0.99`.

**Why this is an issue**

- **Misclassification**: “no hits recorded” and “very open” are not equivalent to “pure sky”—a probe could be open but
  still influenced by nearby geometry, or conversely could be missing data.
- **Downstream ambiguity**: if future shading uses `SkyOnly` to change behavior, a heuristic flag can introduce hard-to-
  debug branching artifacts.

**Options to address**

- **Option A (define it from trace stats)**: Set `SkyOnly` using integrator stats such as `hitCount == 0` (all misses) and no aborts, rather than derived proxies.
- **Option B (store explicit counters)**: Pack miss/hit counts (or a “miss fraction”) into metadata so classification is stable and debuggable.
- **Option C (defer usage)**: Treat `SkyOnly` as debug-only (or remove it) until a consumer actually needs it and semantics are locked.

<a id="wp-4-2"></a>

### 4.2 (P1) Meta flags are written but not consumed by shading

**Where**: `VanillaGraphicsExpanded/assets/vanillagraphicsexpanded/shaders/includes/lumon_worldprobe.glsl`

**What**: The shader reads meta `.x` (confidence) but ignores flags (`SkyOnly`, `Valid`, etc.).

**Why this is an issue**

- **No robust invalidation path**: shading cannot reliably distinguish “valid but black”, “invalid/uninitialized”, “sky
  only”, or “stale”.
- **Makes confidence do too much**: flags are an important orthogonal signal; without them, confidence often becomes a
  catch-all and leads to brittle tuning.

**Options to address**

- **Option A (consume flags)**: Decode flags in shaders and gate behavior: `!Valid -> confidence=0`, `SkyOnly -> ignore block SH`, `Stale/InFlight -> reduce confidence`.
- **Option B (separate channel)**: Store flags in a dedicated integer texture to avoid float packing and make sampling/decoding simpler.
- **Option C (debug tooling)**: Add debug modes that show “invalid vs black vs sky-only” explicitly so tuning doesn’t guess from color alone.

## 5. Shader Sampling / Blending Simplifications

<a id="wp-5-1"></a>

### 5.1 (P0) ShortRangeAO multiplies irradiance by `aoConf`

**Where**: `VanillaGraphicsExpanded/assets/vanillagraphicsexpanded/shaders/includes/lumon_worldprobe.glsl`

**Status**: Implemented (Option A)

**What**: World-probe sampling no longer scales irradiance by `aoConf`; `aoConf` is only used as the bend amount between `normalWS` and the bent direction.

**Why this is an issue**

- **Energy is crushed near geometry**: `aoConf` is derived from a small fixed ray set and is not a physically based
  hemispherical visibility term; multiplying irradiance by it can over-darken probes near any occluders.
- **Double-count risk**: bent-normal evaluation already biases sampling toward open directions; multiplying by the same
  confidence again can “apply AO twice”.

**Options to address**

- **Option A (minimal)**: Remove the `* aoConf` irradiance scale and use `aoConf` only to blend between `normalWS` and `aoDir` (bent normal).
- **Option B (targeted)**: Apply `aoConf` only to the _sky_ term (or apply a much softer curve), so block/indirect contributions aren’t crushed near geometry.
- **Option C (better signal)**: Replace `aoConf` with a more physically interpretable visibility metric (e.g., cone angle + visibility) computed from more rays and distance-aware weighting.

<a id="wp-5-2"></a>

### 5.2 (P1) Outside-clip-volume returns zero contribution (hard cutoff)

**Where**: `VanillaGraphicsExpanded/assets/vanillagraphicsexpanded/shaders/includes/lumon_worldprobe.glsl`

**What**: Sampling returns `{ irradiance=0, confidence=0 }` when the point is outside the selected level volume.

**Why this is an issue**

- **Hard edges**: if level selection or origin parameters jitter, pixels can pop between “some GI” and “no GI”.
- **Makes debugging misleading**: “black” can mean “no data” rather than “dark lighting”.

**Options to address**

- **Option A (fallback level)**: If a point is outside the selected level, fall back to sampling a coarser level (or clamp to the outermost level) instead of returning zero.
- **Option B (fade-out)**: Add an edge fade region that smoothly reduces confidence/weight near clipmap boundaries rather than a hard cutoff.
- **Option C (selection fix)**: Change level selection to prefer levels that actually contain the sample point (within the clip volume) rather than distance-only selection.

<a id="wp-5-3"></a>

### 5.3 (P1) Cross-level blend band is hard-coded

**Where**: `VanillaGraphicsExpanded/assets/vanillagraphicsexpanded/shaders/includes/lumon_worldprobe.glsl`

**What**: Cross-level blending uses a fixed `blendStart=2` and `blendWidth=2` (in probe units) for all configs.

**Why this is an issue**

- **Not resolution/spacing aware**: the transition region may be too thin or too thick depending on configuration,
  leading to visible seams or overly blurry blending.
- **Tuning coupling**: adjusting clipmap resolution/spacing can unintentionally change blend behavior without explicit
  intent.

**Options to address**

- **Option A (config)**: Expose blend start/width as configuration (in probe units or world units) so it can be tuned per setup.
- **Option B (derive from topology)**: Compute the band from resolution/spacing (e.g., a constant world distance, or a fraction of the level extent) instead of fixed “2,2”.
- **Option C (more robust blending)**: Blend more than two levels or use a smoother weighting curve to reduce seams under rapid origin shifts.

<a id="wp-5-4"></a>

### 5.4 (P2) Screen-first blending is a policy (can hide world-probe problems)

**Where**:

- `VanillaGraphicsExpanded/assets/vanillagraphicsexpanded/shaders/lumon_gather.fsh`
- `docs/LumOn.21-Shading-Integration.md`

**What**: `worldW = worldConfidence * (1 - screenW)` means screen-space dominates whenever it has confidence.

**Why this is an issue**

- **World probes can be “working” but invisible** in many pixels, which can delay detection of world-probe regressions.
- **Creates mode-dependent artifacts**: lighting can change substantially when screen confidence drops (disocclusion),
  exposing world-probe limitations abruptly.

**Options to address**

- **Option A (diagnostic)**: Add a config/debug switch to force “world-only”, “screen-only”, or “equal-weight” blending for validation.
- **Option B (smoother mix)**: Use a symmetric blend (`normalize(screenConf, worldConf)`) with a bias term, rather than strictly “screen-first”, to reduce abrupt handoffs.
- **Option C (temporal hysteresis)**: Add hysteresis/temporal smoothing in the blend weights so confidence drops don’t instantly flip sources.

<a id="wp-5-5"></a>

### 5.5 (P0) Screen-probe miss fallback assumes sky radiance (miss != sky)

**Where**:

- `VanillaGraphicsExpanded/assets/vanillagraphicsexpanded/shaders/lumon_probe_trace.fsh`
- `VanillaGraphicsExpanded/assets/vanillagraphicsexpanded/shaders/lumon_probe_atlas_trace.fsh`
- `VanillaGraphicsExpanded/assets/vanillagraphicsexpanded/shaders/includes/lumon_common.glsl` (`lumonGetSkyColor`)
- `VanillaGraphicsExpanded/assets/vanillagraphicsexpanded/shaders/lumon_gather.fsh`
- `VanillaGraphicsExpanded/assets/vanillagraphicsexpanded/shaders/lumon_probe_atlas_gather.fsh`

**What**: Screen-probe tracing is screen-space depth marching. When a ray “misses” it can mean “off-screen”, “occluded by
screen depth”, or “no depth match”, not necessarily “this direction reached the sky”. Today, miss handling injects sky
radiance (`lumonGetSkyColor`) into the screen-probe cache, and the screen↔world blend does not account for miss rates, so
world-probes do not reliably act as a fallback when screen traces fail.

**Why this is an issue**

- **Sky leaking from screen limitations**: off-screen/failed rays can contribute sky radiance, brightening regions that
  are not actually sky-visible.
- **World-probe fallback is not triggered by misses**: screen confidence is derived from geometric interpolation weights,
  so the blend can remain screen-dominated even when screen-space tracing is unreliable for that pixel/probe.
- **Hard to tune globally**: changing `VGE_LUMON_SKY_MISS_WEIGHT` trades “black indirect” vs “sky leaking”, and the
  balance shifts with camera framing/disocclusion.
- **Diverges from hierarchical GI designs (e.g., Lumen)**: screen-trace failure is typically treated as “unknown” and
  followed by a world-space fallback before using sky/environment on final miss.

**Options to address**

- **Option A (cheap)**: Make screen-probe miss radiance less “sky-like” (e.g., ambient-only tint, lower `VGE_LUMON_SKY_MISS_WEIGHT`, or no sun lobe) to reduce sky leaking.
- **Option B (better)**: Store and use miss diagnostics (miss ratio, screen-exit ratio, avg hit distance) to down-weight `screenConfidence` so world probes take over when screen-space is unreliable.
- **Option C (Lumen-like)**: On screen miss, perform a secondary world-space query (hardware RT, distance fields, or a proxy scene) and only fall back to sky/environment when that also misses.

## 6. Debug Visualizer / Tooling Limitations

<a id="wp-6-1"></a>

### 6.1 (P0) Orb visualizer samples irradiance with a fixed “up” normal

**Where**: `VanillaGraphicsExpanded/assets/vanillagraphicsexpanded/shaders/vge_worldprobe_orbs_points.fsh`

**Status**: Resolved (outdated)

**What**: This issue is no longer true: the orb visualizer does not evaluate SH with a fixed “up” normal.

**Note**: The text below is kept as a historical note because it describes a real class of “debug view lies” that can
reappear if the visualizer is refactored.

**Why this is an issue**

- **Misleading brightness near geometry**: probes near any overhead occlusion appear much darker because the visualizer
  is effectively asking “how much light comes from above”, not “what is the probe’s general ambient”.
- **Can mask directional content**: a probe might have strong lateral lighting, but the up-only evaluation won’t show it.

**Options to address**

- **Option A (toggle)**: Add a debug toggle for evaluation direction (up, camera-facing, world axes, or “use stored bent direction”) so the view matches the question being asked.
- **Option B (show components)**: Visualize sky vs block contributions separately (and/or show confidence), so “dark” can be attributed to occlusion vs missing data.
- **Option C (show magnitude)**: Display a direction-independent metric (e.g., total hemisphere irradiance, luminance, or SH energy) rather than irradiance for a single normal.

<a id="wp-6-2"></a>

### 6.2 (P2) Debug tone-mapping is non-photographic and compresses differences

**Where**:

- `VanillaGraphicsExpanded/assets/vanillagraphicsexpanded/shaders/vge_worldprobe_orbs_points.fsh`
- `VanillaGraphicsExpanded/assets/vanillagraphicsexpanded/shaders/lumon_debug.fsh` (legacy dispatcher)
- `VanillaGraphicsExpanded/assets/vanillagraphicsexpanded/shaders/lumon_debug_worldprobe.fsh` (world-probe debug category entrypoint)

**What**: Debug views use a simple `hdr/(hdr+1)` mapping with no exposure/white-point.

**Why this is an issue**

- **Perceptual bias**: dark regions are emphasized and bright differences are compressed, which can exaggerate “too dark”
  impressions and make it harder to compare subtle energy changes.

**Options to address**

- **Option A (exposure control)**: Add an exposure/scale uniform (or UI slider) to debug modes so values can be compared meaningfully.
- **Option B (alternative mapping)**: Use a log/false-color heatmap or ACES-like tonemapping for more readable dynamic range.
- **Option C (numeric probes)**: Add per-probe readouts (min/max/avg luminance, confidence) to avoid interpreting colors alone.
