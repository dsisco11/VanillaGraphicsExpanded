# LumOn PMJ Temporal Jitter

LumOn uses the project's `Pmj02` sequence as a deterministic source of small 2D offsets (“jitter”).
`PmjGenerator` actually constructs Sobol-based points with the optional `PmjOwenScrambler` hash
transform. This is not the published PMJ generation algorithm. The current scrambler hashes a
prefix including the current input bit; this document makes no claim that it is a bijective Owen
tree permutation or establishes the published algorithms' progressive-prefix properties. The
sequence is not a spatial or spatiotemporal blue-noise mask.

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

- `VanillaGraphicsExpanded/Rendering/PmjJitterTexture.cs`

This general GPU owner uses `Noise/PmjCache`, `PmjGenerator` and `PmjConversions`. LumOn and
volumetric clouds own separate texture instances and lifetimes; clouds do not read LumOn settings
or depend on LumOn being enabled.

It generates:

- `PmjVariant.Pmj02`
- Owen scrambling enabled (`OwenScramble = true`)
- `Salt = 0` (currently)

and uploads packed jitter points via `PmjConversions.ToRg16UNormInterleaved(...)`.

## Sampling model (GPU)

On the GPU side, shaders index the sequence by frame:

- `idx = (frameIndex + probeIndex) % pmjCycleLength`
- sample the jitter offset at `x = frameIndexMod`, `y = 0`

CPU packing floors `point*65536` and clamps to 65535. Hardware UNorm reads divide those stored
integers by 65535 and can therefore reach one. LumOn uses those normalized values directly.
Depending on the shader, that may then be remapped to a centered offset in $[-0.5, 0.5)$ before applying scale.

## How cycle length affects stability

Cycle length controls how long it takes for the jitter pattern to repeat:

- **Short cycle** (e.g. 32/64): repeats frequently; can create periodic artifacts when combined with temporal accumulation.
- **Long cycle** (e.g. 128/256): repeats less often. This alone does not establish better convergence or visible stability.

Memory cost is negligible for typical values:

- `256 * 1 * 2 channels * 2 bytes/channel ≈ 1024 bytes` of texture payload.

Current defaults are defined in:

- `VanillaGraphicsExpanded/LumOn/LumOnConfig.cs` (`PmjJitterCycleLength`, `PmjJitterSeed`)

## How to toggle / tune

In `ModConfig/VanillaGraphicsExpanded.json`:

- Set `LumOn.AnchorJitterEnabled` to `true` or `false`.
- Adjust `LumOn.AnchorJitterScale` (recommended range: `0.0 .. 0.49`).

Notes:

- The maximum offset in pixels is approximately: `LumOn.ProbeSpacingPx * LumOn.AnchorJitterScale`.
- Very high values approach sampling across probe cell boundaries; that can be useful but may also increase history rejection if validation is strict.

## Debugging

Practical checks if jitter “does nothing” or looks wrong:

- Verify `AnchorJitterEnabled` is `true` (hot-reloadable).
- Confirm `frameIndex` is changing each frame in the relevant shader pass.
- Confirm `pmjCycleLength` matches the uploaded texture width.
- Confirm `pmjJitter` is bound to the expected sampler unit.

Automated regression coverage:

- `VanillaGraphicsExpanded.Tests/GPU/PmjJitterTextureUploadTests.cs` validates:
  - the texture is created as `RG16` with expected dimensions
  - GPU readback matches CPU packing for a fixed seed

Cloud view sampling has a separate consumed-observation index and pixel digital shift, described
in [PBR.Clouds.md](PBR.Clouds.md). It reuses provisioning rather than LumOn's frame/probe indexing.

## References

- Pixar: Progressive Multi-Jittered Sampling
  - [https://graphics.pixar.com/library/ProgressiveMultiJitteredSampling/](https://graphics.pixar.com/library/ProgressiveMultiJitteredSampling/)
