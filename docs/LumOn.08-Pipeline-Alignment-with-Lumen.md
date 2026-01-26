# LumOn Pipeline Alignment with UE5 Lumen (Screen-Space Focus)

> **Document**: LumOn.08-Pipeline-Alignment-with-Lumen.md  
> **Status**: Draft  
> **Dependencies**:
>
> - Current pipeline + goals: [LumOn.01-Core-Architecture.md](LumOn.01-Core-Architecture.md) through [LumOn.06-Gather-Upsample.md](LumOn.06-Gather-Upsample.md)
> - Comparison baseline: [LumOn.07-LumOn-vs-Lumen.md](LumOn.07-LumOn-vs-Lumen.md)
> - Runtime pipeline source: `LumOnRenderer` (Pass 1–6 orchestration)

---

## 1. Goal

Explain what needs to change in the **current LumOn render stages** to more closely match the _screen-space portion_ of UE5 Lumen’s approach.

- This intentionally ignores the biggest missing Lumen systems (world-space fallback tracing + Surface Cache), except where those constraints shape what is realistic.
- The focus is: **are we already “stage-equivalent” to Lumen SPG, or are there missing shader stages / missing data products?**

---

## 2. Current LumOn pipeline (as implemented)

From `LumOnRenderer`:

### 2.1 Pass 1: Probe Anchor

- Shader: `lumon_probe_anchor.fsh`
- Output: probe anchors (posWS + validity, normalWS)
- Notes:
  - Probes sample the center of their screen cell.
  - Validity includes a partial “edge” state (0.5) from depth discontinuity.

### 2.2 Pass 2: Probe Trace

- Shader: `lumon_probe_atlas_trace.fsh`
- Output: octahedral atlas texel RGBA16F where RGB=hit radiance, A=log hit distance
- Temporal distribution: only traces a subset of texels per probe per frame
- For non-traced texels, it copies history (`octahedralHistory`) into the trace output

### 2.3 Pass 3: Temporal Accumulation

- Shader: `lumon_probe_atlas_temporal.fsh`
- Per-texel history blending only for traced texels
- Disocclusion detection via hit-distance delta
- Neighborhood clamping within each probe tile

### 2.4 Pass 4: Gather (half-res)

Two strategies:

- **Integrate Atlas**: `lumon_probe_atlas_gather.fsh`
  - For each pixel, selects 4 probes and integrates each probe’s octahedral tile over the hemisphere aligned to the pixel normal
  - Uses edge-aware weighting and hit-distance heuristics
- **Evaluate Projected SH9**: `lumon_probe_sh9_gather.fsh` (after `lumon_probe_atlas_project_sh9.fsh`)
  - Cheaper gather by evaluating SH9 at the pixel normal, at the cost of an extra atlas→SH projection pass

### 2.5 Pass 5: Upsample (full-res)

- Shader: `lumon_upsample.fsh`
- Bilateral 2×2 upsample with depth/normal weights
- Optional 3×3 spatial denoise exists but is currently disabled/commented

### 2.6 Pass 6: Combine (optional)

- Shader: `lumon_combine.fsh`
- Combines indirect with direct scene using albedo + metallic rejection

---

## 3. “How close is this to Lumen SPG?” (screen-space only)

### 3.1 What is already aligned

Even ignoring off-screen systems, this pipeline already matches several core SPG ideas:

- Screen-space probes with world-space anchors
- Directional radiance storage (octahedral) + amortized updates
- Temporal stabilization pass
- Half-res gather + edge-aware upsample

### 3.2 Where we diverge (still screen-space)

The biggest practical divergence is not “we don’t have octahedrals” — we do.

The key differences are:

1. **We don’t have a dedicated probe-space filter/denoise stage** (separate from temporal).
2. **We don’t produce or propagate a robust confidence signal**, so later stages can collapse to near-zero weights and produce black.
3. **Our final gather is doing too much work per pixel** (4 probes × 16/64 directional taps + edge stopping + leak logic), while Lumen pushes more stability into probe-space reuse/filters.
4. **Our tracing is a simple depth-buffer marcher without hierarchical depth (HZB) acceleration**, which affects both performance and hit robustness.

---

## 4. Additional “Lumen-like” screen-space stages to add

These are the stages Lumen-style SPG pipelines usually include (in some form) that LumOn currently lacks or only partially implements.

### 4.1 Probe placement jitter (blue-noise / subpixel)

**Why**: reduces structured aliasing and makes temporal accumulation converge instead of “strobing”.

**Where to implement**:

- Modify Pass 1 (`lumon_probe_anchor.fsh`) to offset the sample point within each probe cell by a small jitter.
- Requires new uniforms:
  - `frameIndex`
  - a small noise pattern / hash

**Cost / risk**: low; mostly additive.

### 4.2 Probe-space spatial filtering (edge-stopped)

**Why**: Lumen doesn’t depend on the _pixel gather_ to fix noisy/unstable probe radiance. It filters in probe space.

**What to implement**:

- New Pass 3.5 (between temporal and gather): `lumon_octahedral_filter.fsh` (name suggestion)
- Operates on the octahedral atlas (or on a per-probe “irradiance” representation)
- Uses guide data to stop across edges:
  - probe anchor depth/normal/validity
  - hit distance and/or derived confidence

**Cost / risk**: moderate; additive pass + new FBO/texture.

### 4.3 Explicit confidence / validity textures

**Why**: Lumen-style pipelines propagate confidence and use it in:

- temporal blend weight
- spatial filter weight
- final gather weight

**What we have today**:

- `probeAnchorPosition.w` validity (0/0.5/1)
- hit distance stored per direction (alpha)

**What’s missing**:

- A dedicated confidence scalar (0..1) that represents “how trustworthy is this radiance right now?”

**How to implement (screen-space only)**:

- In octahedral trace, output an additional channel/texture for confidence, or pack it into existing storage if feasible.
- Confidence could be a function of:
  - whether the ray hit something vs sky
  - whether the ray exited screen early
  - depth thickness test margin
  - history rejection events (disocclusions)

**Cost / risk**: moderate; requires extra texture or repacking.

### 4.4 Hierarchical depth for tracing (HZB)

**Why**: screen tracing benefits a lot from a hierarchical depth buffer:

- faster traversal
- better stability (less sensitivity to step size)
- more coherent misses/hits

**What to implement**:

- New Pass 0 (before trace): build a downsampled depth pyramid from `primaryDepth`
- Modify octahedral trace to use HZB for ray marching / binary refinement

**Cost / risk**: moderate-high; multiple mip passes and additional sampling logic.

### 4.5 Split “integration” from “gather” (optional but very Lumen-like)

Right now, octahedral gather integrates directional data per pixel. Lumen-style pipelines often prefer a representation that makes the final gather cheaper.

Two options:

1. **Project the octahedral tile to low-order SH per probe** (e.g., SH2/SH3) _after temporal/filtering_, then evaluate SH per pixel.

   - Pros: final gather becomes cheap, stable, and similar to your SH mode.
   - Cons: requires a new “octahedral → SH” pass and new textures.

2. **Precompute cosine-convolved irradiance** into a small per-probe representation (not full SH, could be a handful of lobes).

**Cost / risk**: medium; this is the first step that starts to change the “center of gravity” of your gather stage.

---

## 5. What can be done without fundamental restructuring?

If “fundamental restructuring” means rewriting the pipeline architecture, the answer is: **most of the alignment can be added incrementally**.

### 5.1 Mostly additive changes

- Add probe jitter to Pass 1
- Add an explicit confidence channel
- Add a probe-space filter pass (new Pass 3.5)
- Add optional post-upsampling denoise (already present as code paths)

These are new textures/passes but they don’t require throwing out the current design.

### 5.2 Changes that are a structural refactor (but still localized)

- Introducing an HZB pass and changing the tracer to use it
- Adding “octahedral → SH” (or irradiance) conversion and then using that in gather

These are still feasible within the existing renderer orchestration, but they change the performance profile and the dataflow enough that you’d treat them as a refactor.

---

## 6. Recommended target pipeline (Lumen-like screen-space)

A realistic “Lumen-ish” screen-space target for LumOn in this repo (still without world fallback) would look like:

0. (New) Depth pyramid / HZB (optional, recommended)
1. Probe Anchor (with jitter)
2. Octahedral Trace (screen trace; outputs radiance + hit dist + confidence)
3. Octahedral Temporal (existing, expanded to use confidence)
   3.5 (New) Probe-space filter/denoise (edge-stopped)
4. Gather (ideally cheaper; either sample fewer directions or use a compact irradiance basis)
5. Upsample (existing; optional spatial denoise enabled as needed)
6. Combine (optional)

---

## 7. Why this matters for our current near-camera black cutoff

Your recent diagnostics strongly suggest the black cutoff is a symptom of “not enough trustworthy radiance/confidence reaching the gather”.

Even if we tune gather weights, a Lumen-like pipeline avoids this by:

- ensuring probe radiance is stable before gather,
- using confidence-weighted blending and filters,
- and relying less on brittle per-pixel edge penalties.

Without world fallback, some cases will remain fundamentally screen-space limited, but the above changes should eliminate the worst “hard cutoff” behaviors.

---

## 8. Suggested work breakdown (incremental)

1. Add probe jitter in `lumon_probe_anchor.fsh` and propagate `frameIndex` uniform.
2. Add a confidence signal to the octahedral trace output.
3. Teach `lumon_probe_atlas_temporal.fsh` to blend based on confidence and/or accumulate count.
4. Add a probe-space filtering pass (new shader + new FBO/texture).
5. Optionally: add octahedral → SH projection pass and switch gather to SH evaluation.
6. Optionally: add depth pyramid/HZB and update tracing.
