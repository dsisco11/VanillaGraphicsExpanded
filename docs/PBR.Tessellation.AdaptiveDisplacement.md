# Adaptive subdivision and displacement core

The material contract and GPU stage implementation exist; production still publishes the undisplaced
terrain path. Selecting displaced production programs remains gated on matching shadow, culling,
resource binding and temporal-consumer integration. The global detail-mode selector belongs to the
remaining relief/integration tasks. This document does not claim live-game displacement acceptance.

## Material metadata

Materials may author `displacement.amplitudeMetres` separately from BRDF properties. Missing values
are zero. Only finite values in [0, 0.05] metres are accepted; invalid values emit a material diagnostic
and resolve to zero. Defaults and mapping overrides cannot introduce displacement. Existing built-in
materials remain opted out.

Resolved atlas plans publish a separate nearest-filtered R32F amplitude texture for each opted-in
page. No displacement texture is allocated on pages without opted-in material tiles. Unassigned pixels
are zero. Metadata is rebuilt from the current material plan for warmup and normal rebuild routes;
it is never restored from a height or BRDF disk cache. Changing only amplitude therefore does not
require rebaking unchanged normal/height data. Removing/resizing pages discards amplitude metadata,
and lookup requires a valid neutral-initialized height page. BRDF channels and baked height are unchanged.

Current storage costs four GPU bytes per atlas pixel on opted-in pages and one transient CPU page
array during upload. Upload is at build time, not per draw. Compact metadata storage is a possible
future optimization; no throughput improvement is claimed.

## Adaptive stage contract

The control and evaluation bodies live in `shaders/includes/tessellation/terrain.tcsh` and
`terrain.tesh`, with shared sampling/subdivision helpers in `terrain_displacement.glsl`.
These are runtime terrain templates, excluded from standalone SPIR-V entry points because they
depend on the engine's patched vertex interface. The asset manager and shared GLSL import resolver
load them during shader preparation, including reloads. C# generates only interface declarations,
copy/interpolation statements and variant defines; TinyAst inserts these into the asset bodies.

`TerrainTessellationStages.Generate(..., adaptiveDisplacement: true)` produces the bounded stage pair.
The engine's existing preprocessor guards, output interpolation and primary/secondary UV streams
are preserved. Required terrain outputs are validated before generation. Topsoil height uses only
the primary `uv`; its secondary UV remains interpolated for color layering.

The control stage requires a consistent face ID, render flags and atlas rectangle. Wind-mode flags
exclude deformed foliage. Invalid/mixed metadata, zero amplitude and invalid distance configuration
produce level one and zero displacement. The current safe rectangle source is the existing SSBO face
metadata. Ordinary terrain with the invalid-rectangle sentinel stays undisplaced; inferring a tile
rectangle from an arbitrary triangle is deliberately not attempted.

Each outer level depends only on its two world-position endpoints, the projection, viewport, screen
target and distance fade. Reversing the endpoints produces the same result. Fractional-odd spacing
allows gradual subdivision changes; the maximum is rounded down to an odd segment count so the GPU
cannot round beyond the configured cap. The current maximum requested level is eight (effective odd
cap seven). Near-plane crossings use the bounded cap without dividing by nonpositive clip W.

`MaterialAtlas.TerrainSubdivision` provides validated maximum level, target edge pixels, and fade
start/end metres for later uniform publication. The stage uniform contract is:

| Input | Meaning |
| --- | --- |
| `vge_displacementTex` | Current amplitude metadata for this atlas page |
| `vge_normalDepthTex` | Matching normal/height page, alpha height |
| `vge_tessellationPixels` | Viewport width/height, target pixels, maximum level |
| `vge_tessellationDistance` | Fade start/end in metres |
| `modelViewMatrix`, `projectionMatrix` | Engine transforms for this draw |

## Height, normals and seams

The evaluation stage interpolates the unprojected world position, then applies bounded signed height
along the geometric normal before projection. Height is sampled at LOD zero because the existing
atlas has one initialized mip. Coordinates are clamped within the face rectangle with a half-texel
inset; samples are never allowed to use an adjacent tile. Non-finite samples, invalid rectangles and
degenerate tangent frames produce zero displacement. Neutral alpha 0.5 produces zero offset.

A smooth zero-slope ramp pins the one-texel face boundary band and corners to the original surface.
This constrains incompatible material/normal boundaries, including chunk boundaries, instead of
trying to stitch unrelated displacement functions. Both triangles of a face evaluate the same
UV-based height function: the internal diagonal is not pinned. Rotated/mirrored UVs use the actual
world-position/UV metric. Finite differences of this height function produce the displaced normal.

Available camera-position, vertex-position and G-buffer normal varyings are updated. Shadow-coordinate
re-evaluation, displaced shadow passes, culling expansion, previous-frame reprojection and LumOn trace
representation remain integration work. Until those consumers and bindings are coherent, the production
program selector continues using the identity stage pair even for authored amplitudes.

## Focused verification

54 distinct tests pass across `artifacts/PbrColor/adaptive-displacement-final.trx` and
`artifacts/PbrColor/adaptive-displacement-numerical-final.trx`. The latter supersedes two initial
numerical failures: an outdated ramp expectation and a driver-sensitive NaN comparison fixed with
an explicit `isnan` guard. The combined run passed the other 42 cases; all 12 final numerical cases pass.

Coverage includes material validation/schema/settings, amplitude page updates/removal/resize,
four installed opaque/topsoil/SSAO/SSBO adaptive link cases, six actual two-triangle TES raster cases,
neutral and extreme heights, rotated/degenerate UVs, excluded wind faces, complete diagonal coverage,
reversed shared-edge levels, adjacent-tile isolation, boundary pinning and non-finite samples.
This is headless verification, not live appearance, production publication or cost measurement.

The shader-asset migration passes 45 distinct focused tests in
`artifacts/PbrColor/tessellation-assets.trx` and `tessellation-assets-missing.trx`.
These exercise the actual stage assets, installed terrain variants, engine draws, adaptive raster
and numerical behavior, import expansion, asset reload and removal of prepared metadata when a
required asset is missing.

Tessellation fixtures compile stages through `GpuShaderModule` and link baseline programs through
`GpuProgramObject`. Submitted sources stay on the CPU for interface preparation. Program binding
and patch-size mutations use `GlStateCache`; uniform lookup and writes use `GpuProgramLayout` and
the shared shader test helpers. Direct driver lifetime/state assertions still check the real GL
objects and restored patch size, independently of the wrappers.
The abstraction refactor passes all 37 focused tests in
`artifacts/PbrColor/tessellation-abstractions.trx` (9 seconds test duration).
