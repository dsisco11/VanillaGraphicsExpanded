# LumOn PMJ Temporal Jitter

LumOn uses a **Progressive Multi-Jittered (PMJ02)** sample sequence as a deterministic, per-frame source of small 2D offsets (“jitter”).

This serves two goals:

- Reduce structured aliasing (regular probe-grid sampling artifacts).
- Help temporal accumulation converge with fewer visible patterns.

## What is jittering in LumOn?

LumOn’s probe pipeline starts by sampling the G-buffer at a discrete grid of probe locations.
When enabled, **anchor jitter** offsets the per-probe sample location by a small amount that changes each frame but remains deterministic.

In code, this is driven by:

- `LumOnConfig.AnchorJitterEnabled` (hot-reloadable)
- `LumOnConfig.AnchorJitterScale` (hot-reloadable)

The shader sees:

- `frameIndex` (an incrementing integer)
- `pmjCycleLength` (sequence length)
- `pmjJitter` (the sequence texture)

## PMJ sequence provisioning (CPU)

The PMJ sequence is uploaded once as a tiny “1D” texture stored as a 2D texture:

- Internal format: `RG16_UNorm`
- Size: `width = cycleLength`, `height = 1`
- Filter: `Nearest`

The runtime code that owns this is:

- `VanillaGraphicsExpanded/LumOn/LumOnPmjJitterTexture.cs`

It generates:

- `PmjVariant.Pmj02`
- Owen scrambling enabled (`OwenScramble = true`)
- `Salt = 0` (currently)

and uploads packed jitter points via `PmjConversions.ToRg16UNormInterleaved(...)`.

## Sampling model (GPU)

On the GPU side, shaders index the sequence by frame:

- `frameIndexMod = frameIndex % pmjCycleLength`
- sample the jitter offset at `x = frameIndexMod`, `y = 0`

The fetched `RG16_UNorm` value decodes to a point in $[0,1)^2$.
Depending on the shader, that may then be remapped to a centered offset in $[-0.5, 0.5)$ before applying scale.

## How cycle length affects stability

Cycle length controls how long it takes for the jitter pattern to repeat:

- **Short cycle** (e.g. 32/64): repeats frequently; can create periodic artifacts when combined with temporal accumulation.
- **Long cycle** (e.g. 128/256): repeats less often; typically reduces obvious repetition.

Memory cost is negligible for typical values:

- `256 * 1 * 2 channels * 2 bytes/channel ≈ 1024 bytes` of texture payload.

Current defaults are defined in:

- `VanillaGraphicsExpanded/LumOn/LumOnConfig.cs` (`PmjJitterCycleLength`, `PmjJitterSeed`)

## How to toggle / tune

In `ModConfig/vanillagraphicsexpanded-lumon.json`:

- Set `AnchorJitterEnabled` to `true` or `false`.
- Adjust `AnchorJitterScale` (recommended range: `0.0 .. 0.49`).

Notes:

- The maximum offset in pixels is approximately: `ProbeSpacingPx * AnchorJitterScale`.
- Very high values approach sampling across probe cell boundaries; that can be useful but may also increase history rejection if validation is strict.

## Debugging

Practical checks if jitter “does nothing” or looks wrong:

- Verify `AnchorJitterEnabled` is `true` (hot-reloadable).
- Confirm `frameIndex` is changing each frame in the relevant shader pass.
- Confirm `pmjCycleLength` matches the uploaded texture width.
- Confirm `pmjJitter` is bound to the expected sampler unit.

Automated regression coverage:

- `VanillaGraphicsExpanded.Tests/GPU/LumOnPmjJitterTextureUploadTests.cs` validates:
  - the texture is created as `RG16` with expected dimensions
  - GPU readback matches CPU packing for a fixed seed

## References

- Pixar: Progressive Multi-Jittered Sampling
  - [https://graphics.pixar.com/library/ProgressiveMultiJitteredSampling/](https://graphics.pixar.com/library/ProgressiveMultiJitteredSampling/)
