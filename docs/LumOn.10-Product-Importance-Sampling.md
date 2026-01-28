# LumOn Product Importance Sampling (PIS) — Implementation Plan

> **Document**: `LumOn.10-Product-Importance-Sampling.md`  
> **Status**: Draft (planning)  
> **Related docs**:
>
> - Pipeline / system overview: [LumOn.01-Core-Architecture.md](LumOn.01-Core-Architecture.md)
> - Probe grid + anchors: [LumOn.02-Probe-Grid.md](LumOn.02-Probe-Grid.md)
> - Screen-space radiance cache (octahedral atlas): [LumOn.03-Radiance-Cache.md](LumOn.03-Radiance-Cache.md)
> - Screen probe tracing pass: [LumOn.04-Ray-Tracing.md](LumOn.04-Ray-Tracing.md)
> - Temporal accumulation: [LumOn.05-Temporal.md](LumOn.05-Temporal.md)
> - Gather/integration: [LumOn.06-Gather-Upsample.md](LumOn.06-Gather-Upsample.md)
> - Lumen alignment: [LumOn.08-Pipeline-Alignment-with-Lumen.md](LumOn.08-Pipeline-Alignment-with-Lumen.md)
> - High-level PIS placement: [LumOn_Flowchart.mmd](LumOn_Flowchart.mmd)
> - World-probe direction slicing pain point: [WorldProbes.KnownSimplificationsAndRisks.md](WorldProbes.KnownSimplificationsAndRisks.md)

---

## 1. Goal

Implement **Product Importance Sampling (PIS)** for LumOn’s **direction update selection**, so we spend the limited per-frame ray budget on the directions that matter most for shading (diffuse today, specular later).

In our repo’s terminology, “PIS” means building a per-probe discrete PDF over the probe’s **64 octahedral directions** with:

```text
importance(dir) ∝ Li_est(dir) * BRDF_weight(dir) * max(dot(N, dir), 0)
```

Then selecting which octahedral texels to trace this frame based on that importance.

### Non-goals (initial bring-up)

- Changing the octahedral atlas layout or the 8×8 direction set.
- Making the tracer unbiased in a path-tracing sense (this is **budget scheduling** for a cached field).
- Full world-probe-driven PIS seeding (we’ll design for it, but start with SSRC-only for perf).

---

## 1.1 Phase 10.1 decisions (locked)

This section exists to “pin down” semantics so implementation can proceed without ambiguity.

### 1.1.1 What PIS changes

- PIS changes **which octahedral atlas texels get traced this frame** (per probe).
- It does **not** change:
  - the 8×8 direction set,
  - ray marching/shading behavior,
  - gather integration math.

### 1.1.2 Baseline behavior when PIS is disabled (no-regression)

When PIS is disabled, the traced-texel selection must be equivalent to the current deterministic batch slicing:

- Trace pass selection currently in `lumon_probe_atlas_trace.fsh`.
- Temporal pass selection currently in `lumon_probe_atlas_temporal.fsh`.

Implementation requirement: any PIS plumbing must retain an explicit fallback path so “PIS off” remains identical in behavior and cost.

### 1.1.3 M1 importance definition (diffuse-only)

For initial bring-up (M1), importance uses a diffuse-only product:

```text
w(dir) = luminance(Li_est(dir)) * max(dot(N_probe, dir), 0) * confWeight(dir)
```

Where:

- `dir` is the world-space direction corresponding to the octahedral texel center.
- `N_probe` is the probe’s **anchor normal** (world space) from `probeAnchorNormal`.
- `Li_est(dir)` is a cheap estimate of incoming radiance in direction `dir` from SSRC history (see 1.1.4).
- `confWeight(dir)` is a monotonic weight derived from history confidence (see 1.1.5).

Notes:

- Backfacing directions (`dot(N_probe, dir) <= 0`) have zero importance for diffuse, but may still be selected via exploration (see 1.1.7) to avoid permanently stale texels.

### 1.1.4 `Li_est(dir)` source (M1)

For screen probes, `Li_est(dir)` is taken from the **octahedral history atlas** at the same probe+texel:

- `Li_est(dir) = octahedralHistory[probeCoord, octTexel].rgb`

This is intentionally “in-cache” and avoids additional scene queries.

Fallback behavior:

- If history is invalid/uninitialized or has too-low confidence, PIS must still select a full set of `K` texels via exploration and/or a safe fallback (never “select 0 directions” for valid probes).

### 1.1.5 Confidence weighting (M1)

If meta history is available:

- Use per-texel history confidence from `probeAtlasMetaHistory` to compute `confWeight(dir)`.
- Define a minimum floor so low-confidence history doesn’t fully starve sampling:

```text
confWeight = lerp(minConfWeight, 1, confHistory)
```

If meta history is not available, `confWeight = 1`.

### 1.1.6 Selection constraints (hard requirements)

For each valid probe each frame:

- Select exactly `K = VGE_LUMON_ATLAS_TEXELS_PER_FRAME` **unique** direction indices in `[0, 63]`.
- Selection must be:
  - stable/deterministic for fixed inputs (probe index, frame index, history textures),
  - bounded in cost (no variable ray counts),
  - compatible with GL 3.3 rasterization (per-texel “trace or keep history” decision).

### 1.1.7 Exploration vs exploitation (hard requirement)

To avoid starvation and handle lighting changes/disocclusion:

- Split the budget: `K = K_importance + K_explore` where `K_explore >= 1`.
- `K_explore` is chosen by a deterministic “coverage” scheme that guarantees all 64 directions are eventually revisited.
- `K_importance` is chosen by weighted selection (importance).

### 1.1.8 Backfacing policy (M1)

- Importance selection uses `max(dot(N_probe, dir), 0)` so purely backfacing directions have `w=0`.
- Exploration selection is allowed to include any direction (including backfacing) so texels cannot become permanently stale.

---

## 2. Current screen-probe sampling (what exists today)

This summarizes what the existing docs + implementation do, because PIS must plug into these constraints:

### 2.1 Where directions live

- **Screen probes** are a sparse 2D grid in screen space; each probe anchors to `posWS + normalWS` from the G-buffer. See [LumOn.02-Probe-Grid.md](LumOn.02-Probe-Grid.md).
- Each probe stores directional radiance in a **fixed world-space 8×8 octahedral tile** inside a 2D atlas. See [LumOn.03-Radiance-Cache.md](LumOn.03-Radiance-Cache.md).

### 2.2 How directions get updated (baseline)

- The trace pass (`lumon_probe_atlas_trace.fsh`) currently updates **a fixed subset** of the 64 texels each frame:
  - `K = VGE_LUMON_ATLAS_TEXELS_PER_FRAME`
  - selection is a deterministic “batch” based on `(frameIndex + probeIndex) % numBatches` (see [LumOn.04-Ray-Tracing.md](LumOn.04-Ray-Tracing.md) and shader code).
- Non-updated texels copy from `octahedralHistory`.
- The temporal pass (`lumon_probe_atlas_temporal.fsh`) blends only texels that were traced this frame (it must share the same “which texels were traced” logic).

### 2.3 Key constraints that shape PIS

- The tracer is implemented as a fragment shader over the full atlas resolution (GL 3.3 friendly).
- Each fragment corresponds to exactly one atlas texel; without image load/store we cannot “scatter” results to arbitrary texels.
- Therefore, **PIS must ultimately express selection as a per-texel boolean**: trace or preserve history.

---

## 3. What PIS should change (behavioral intent)

Today’s batch slicing treats all directions as equally valuable. In practice:

- For **diffuse GI**, backfacing directions (relative to the surface normal) contribute nothing to irradiance.
- Many directions will have very low incoming radiance most of the time (dark interiors, occluded directions).
- A small number of directions (sun/bright emissive openings) dominate the energy and drive visible noise/flicker when under-sampled.

PIS should:

- Update high-importance directions **more frequently**.
- Still “explore” occasionally to discover newly bright directions (dynamic changes / disocclusion).
- Remain stable/deterministic enough to avoid sparkling patterns.

---

## 4. Proposed screen-probe implementation (GL 3.3-compatible)

### 4.1 Add a new per-probe “trace mask” pass

Add a pass at **probe resolution** (probeCountX × probeCountY) that outputs a **64-bit mask** per probe indicating which octahedral texels should be traced this frame.

Suggested shader:

- `assets/vanillagraphicsexpanded/shaders/lumon_probe_atlas_pis_mask.fsh` (new)

Suggested output texture:

- `ProbeTraceMask` @ probe resolution
- Format: **RG32F** (two 32-bit lanes we treat as bit-packed uints via `floatBitsToUint/uintBitsToFloat`)
  - `maskLo` holds bits 0..31
  - `maskHi` holds bits 32..63

### 4.2 Modify trace + temporal passes to use the mask

Replace batch slicing with:

- Trace pass (`lumon_probe_atlas_trace.fsh`): `traceThisTexel = bitIsSet(mask, texelIndex)`
- Temporal pass (`lumon_probe_atlas_temporal.fsh`): `wasTracedThisFrame = bitIsSet(mask, texelIndex)`

Keep the old batch slicing as a fallback when:

- PIS is disabled
- mask is missing/uninitialized
- probe is invalid

### 4.3 Where to get `Li_est(dir)`

**Primary** (cheap, already on-GPU): screen-probe history atlas:

- sample `octahedralHistory` at the probe tile texel for each direction
- optionally include confidence from `probeAtlasMetaHistory` to down-weight unreliable history

**Fallback** (performance-sensitive): when history is invalid/low confidence:

- start with cosine-only / uniform exploration (see Section 6)
- optionally, in a later phase, seed with an approximation from world probes (WSRC) (see Section 8)

---

## 5. Importance function (practical choices)

We have 64 discrete directions; importance is a scalar weight per direction.

### 5.1 Diffuse-only (recommended first milestone)

For direction `dirWS` and probe normal `N`:

```text
cosN = max(dot(N, dirWS), 0)
Li   = luminance(historyRadianceRGB)
conf = historyConfidence (0..1)

w = Li * cosN * lerp(minConfWeight, 1, conf)
```

Notes:

- This matches the “PIS ∝ Li * cosθ” special case (Lambert BRDF is constant).
- It is robust and cheap, and it improves convergence for the gather’s cosine-weighted integration.

### 5.2 Diffuse + specular (future)

Once we have an indirect specular consumer (or want better sampling for rough reflections), incorporate a specular lobe term:

```text
w = Li * ( kd * cosN + ks * SpecLobeWeight(dirWS, N, V, roughness) )
```

Inputs needed:

- `V` at probe anchor: `normalize(cameraPosWS - probePosWS)` (available via UBOs)
- `roughness`, `metallic` (sample from `gBufferMaterial` at the probe anchor UV, or pack into probe anchor outputs)

This is a “PIS-style” product; it intentionally prioritizes directions that are likely to contribute most to the eventual BRDF-weighted evaluation.

---

## 6. Direction selection algorithm (choose exactly K unique texels)

We want:

- Exactly `K = ProbeAtlasTexelsPerFrame` unique direction indices per probe each frame (predictable cost).
- Deterministic given `(frameIndex, probeIndex)` (repeatable and debuggable).
- A selection that respects weights without requiring a full alias table.

### 6.1 Recommended: weighted sampling without replacement (reservoir-style)

Use a standard weighted-without-replacement scheme:

1. For each direction `i`, generate a deterministic random `u ∈ (0,1]` from `(probeIndex, frameIndex, i)`.
2. Compute a key:
   - `key = log(u) / max(w_i, eps)` (more weight → larger key)
3. Select the `K` directions with the largest keys.

This yields `K` unique indices and avoids constructing/normalizing a CDF.

### 6.2 Exploration vs exploitation

Pure “importance-only” can starve directions when history is wrong (lighting changes, disocclusion).

To prevent that:

- Split the budget: `K = K_importance + K_explore`
  - `K_explore = max(1, floor(K * ExploreFraction))`
  - `K_importance = K - K_explore`
- “Explore” samples use a stable uniform-ish pattern (e.g., the existing batch slicing) so every direction eventually refreshes.
- “Importance” samples use the weighted scheme above.

This keeps convergence fast while still guaranteeing coverage.

### 6.3 Backfacing policy

For diffuse:

- Set `w_i = 0` when `dot(N, dirWS) <= 0` to avoid tracing directions that cannot contribute.

For future specular:

- Consider keeping a small exploration fraction over the full sphere (or a specular hemisphere) depending on the consumer.

---

## 7. Integration details (what to touch)

### 7.1 New GPU resources

- `VanillaGraphicsExpanded/LumOn/LumOnBufferManager.cs`
  - allocate the per-probe `ProbeTraceMask` texture and FBO

### 7.2 New shader program plumbing

- `VanillaGraphicsExpanded/LumOn/Shaders/`:
  - add a `GpuProgram` for the mask pass (e.g., `LumOnProbeAtlasPisMaskShaderProgram`)
  - bind:
    - probe anchor textures
    - `octahedralHistory` (+ optional meta history)
    - optional `gBufferMaterial` (for specular future)

### 7.3 Renderer pass order

- `VanillaGraphicsExpanded/LumOn/LumOnRenderer.cs`
  - insert: `RenderProbePisMaskPass()` between Anchor and Trace

### 7.4 Trace + temporal shader edits

- `VanillaGraphicsExpanded/assets/vanillagraphicsexpanded/shaders/lumon_probe_atlas_trace.fsh`
  - replace `shouldTraceThisFrame()` with mask lookup
- `VanillaGraphicsExpanded/assets/vanillagraphicsexpanded/shaders/lumon_probe_atlas_temporal.fsh`
  - replace `wasTracedThisFrame()` with the same mask lookup

---

## 8. World probes: optional PIS extension (CPU-side)

World probes already have a “direction slicing” mechanism for partial updates, but it is currently deterministic and uniform-ish. See [WorldProbes.KnownSimplificationsAndRisks.md](WorldProbes.KnownSimplificationsAndRisks.md) §2.5.

Because world probe tracing runs on the CPU, PIS is easier to apply there:

1. For a probe update, compute weights for all directions `w_i` using:
   - last known per-direction radiance (if available in the world-probe atlas), or
   - a cheap proxy (e.g., per-direction hit/miss + sky intensity + cached dominant direction)
2. Use the same weighted-without-replacement selection to pick `K` indices to trace this update.
3. Preserve the “explore fraction” to guarantee coverage over time.

Implementation hook:

- Replace/augment `LumOnWorldProbeAtlasDirectionSlicing.FillTexelIndicesForUpdate(...)` with an “importance” variant.

Important caveat:

- World probes represent a material-agnostic radiance field, so “BRDF” terms are generally not probe-intrinsic. For world probes, start with **radiance-driven** importance (and/or basis-driven importance if integrating SH).

---

## 9. Debugging, validation, and acceptance criteria

### 9.1 Debug views (high value)

- Per-probe selected count (should be `K` for valid probes).
- “Mask visualizer” for a picked probe: draw its 8×8 tile showing traced vs preserved texels this frame.
- Heatmap of `sum(w_i)` or “effective sample count” to verify PIS reacts to bright sources.

### 9.2 Behavioral checks

- No regression when PIS disabled (bit-for-bit parity with current slicing).
- With PIS enabled:
  - Reduced noise around bright openings/sun patches at same ray budget.
  - No persistent black cutoffs due to starving “important” directions (explore fraction prevents this).
  - Stable behavior under camera motion (no obvious shimmering beyond existing temporal noise).

### 9.3 Performance checks

The PIS mask pass is expected to:

- Run at probe resolution (e.g., 240×135 at 1080p/8px spacing).
- Do up to 64 history samples per probe (≈2M samples total) plus ALU for selection.

We should add a GPU timer for the new pass and verify it stays within a small fixed budget (target: sub-millisecond on typical hardware).

---

## 10. Recommended rollout (milestones)

1. **M1: Diffuse-only PIS for screen probes**
   - new mask pass
   - trace/temporal use mask
   - explore fraction + fallback to old slicing
2. **M2: Better robustness signals**
   - incorporate per-direction confidence into weights
   - clamp / guard against all-zero weights
3. **M3: Specular-aware weights (optional)**
   - add roughness/metallic sampling to weight model
4. **M4: World probe direction PIS (optional)**
   - radiance-driven importance selection on CPU update slicing
