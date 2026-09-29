# Atmospheric aerial perspective

Camera-to-surface transport is stored in two 3D lookup textures: accumulated in-scattered
radiance and attenuation (`1 - transmittance`). The coordinates are world azimuth,
horizon-focused elevation and camera distance. This is an atmospheric lookup table;
it does not scan terrain or allocate world-space voxels.

## Transport and sampling

Each direction integrates the spherical atmosphere from the admitted observer altitude,
including altitude-dependent molecular/aerosol density, ozone extinction, solar attenuation
and the existing multiple-scattering source. Integration stops at the planet or atmosphere
boundary. Twenty-four logarithmic distance layers cover zero to 2500 km with a one-metre
scale. Every direction uses the same physical distance grid; samples past its boundary
repeat the terminal value. This keeps angular interpolation consistent near the horizon.

Two midpoint samples per interval give at most 46 integration samples per direction.
Extinction is integrated analytically within each constant-density segment. CPU construction
uses the existing TensorPrimitives batch integrator; the scalar finite-ray routine provides
an independent reference. GPU construction shares the sky dispatch and completion fence.

The sky and finite-path integrations use different quadrature nodes. Each radiance column
is normalized to the corresponding completed sky texel at its terminal sample, avoiding a
terminal terrain/sky radiance mismatch. This is a numerical approximation, not an exact
finite-path solution; extinction is not normalized. Angular and distance interpolation also
limit accuracy, particularly for sharp sunlight lobes and near-horizon transitions.

## Consumers

Deferred composition and forward material shading sample the same published generation.
View-space receiver displacement is rotated into world space before lookup. Both PBR modes
apply `surface * transmittance + in-scattering` before display conversion. Endpoint sky
visibility gates both terms to preserve sealed interiors. This is an approximation to path
exposure: it does not resolve terrain shadows or openings along the atmospheric segment.

Forward transparent surfaces use their own receiver position and preserve their existing
alpha/blending contract. Ordinary engine air fog is replaced, preventing duplicate haze.
Underwater receivers retain engine water fog instead of applying atmospheric transport.

## Storage and lifetime

Angular dimensions follow atmospheric quality: 32x24, 64x48, 96x72 or 128x96. Distance depth
is fixed at 24. The paired RGBA16F aerial textures consume 432 KiB at default quality and
6.75 MiB at maximum quality, per complete set. Sky storage is additional. Two resource sets
allow upload into the spare set before atomically publishing all three texture IDs and the
matching immutable lighting snapshot. Upload failure retains the previous complete set.

The GPU queue output includes four lighting vectors, sky texels and both aerial arrays in
RGBA32F: 909376 bytes at default quality, 14549056 bytes at maximum. Maximum buffer payload
including the 64-byte parameters and 131072-byte source table is 14680192 bytes. Immutable
CPU snapshots and the two display texture sets are additional. GPU results currently pass
through the existing readback/publication path; this increases transfer costs as well as
integration work. CPU fallback builds complete generations asynchronously with pooled,
bounded batch scratch. No render-thread sleep or positive-duration fence wait is added.

Input changes retain the previous generation until its replacement completes. Quality,
observer altitude, medium, direction mapping and lighting belong to that same generation.
World teardown retires both texture sets. Initial empty transport uses neutral one-voxel
textures, without shader readiness branches. Engine programs receive distinct 3D sampler units
even before world initialization, preventing aliasing with their existing 2D samplers.

## Validation

Release validation passed 218 distinct tests, with four optional measurement tests skipped.
`artifacts/Aerial/aerial-verified.trx` records the broad selection; its obsolete texture-ID
lifetime assertion was corrected and superseded by `artifacts/Aerial/aerial-publication-cost.trx`.
The latter also includes the opt-in matched transport measurement. Final production and SPIR-V
builds passed with zero errors (`artifacts/aerial-build-last.log`).

Coverage includes all voxels in six complete CPU/GPU generations, scalar/Tensor agreement for
1/17/128 directions, identity and monotonic extinction, observer altitude and solar direction,
terminal sky matching, actual GPU angular/distance interpolation, seam and enclosure behavior,
coherent publication and rejected-upload ownership, installed engine shader linking, and forward
and deferred numerical composition. Tests caught missing explicit SPIR-V sampler locations;
the corrected contract now binds the aerial volumes in the production deferred shader.

## Measured transport cost

The opt-in Release measurement uses the existing matched warmed CPU/GPU ABBA workload on the
headless GPU. Six measured samples per path, with identical generation/input sequences:

| Default-quality workload | CPU fallback wall | GPU elapsed sum | GPU harness wall |
| --- | ---: | ---: | ---: |
| Rebuild source and sky/aerial | 198.101–335.548 ms | 1.204–2.252 ms | 1.981–7.325 ms |
| Reuse source, rebuild sky/aerial | 5.341–6.482 ms | .782–1.041 ms | 1.201–4.361 ms |

Cached-source CPU time inside GPU updates was .205–1.186 ms. Submission counts remain ten for
a default source rebuild and two with a cached source. These times include volume generation
and immutable readback, but exclude shader linking, final display texture upload, frame cadence
and contention with terrain rendering. Earlier sky-only measurements were taken in another
session and are not a matched before/after speed comparison.

One maximum-quality run measured 1.239 ms for its first source batch and 324.858 ms device time
for the remaining generation (353.466 ms harness wall). Its largest measured update was 6.716 ms.
That is a material frame-budget cost; bounded storage/work does not imply negligible rendering cost.
Live appearance and production terrain contention remain unverified and require user-run comparison.

The halo reconstruction update packs background and unweighted Mie transport into two
elevation bands of the radiance texture; attenuation retains its original layout.
The memory bounds above include this update. Historical timings above precede it.
See [halo reconstruction](PBR.Atmosphere.HaloSampling.md).
