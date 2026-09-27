# Material-controlled terrain relief

`MaterialAtlas.TerrainSurfaceDetailMode` selects `Disabled`, `Relief`, or `Tessellation`.
The default is Disabled. ConfigLib exposes the enum as a mapped integer; JSON also accepts enum
names. Relief is implemented for opaque terrain and topsoil. Tessellation uses the
[integrated terrain and shadow path](PBR.Tessellation.RenderingConsumers.md); it does not enable relief
as a substitute. Existing identity-tessellation validation remains independent.

## Material and shader contract

Relief changes texture coordinates only. It does not change positions, silhouettes, fragment depth,
shadow geometry, or collision. Topsoil uses its primary texture for height and leaves its secondary
UV stream unchanged. Entity, liquid and transparent shader families do not receive relief enablement.

The authored `displacement.amplitudeMetres` is the maximum indentation in metres. Zero or missing
amplitude opts out. The shader uses `amplitude * clamp(1 - 2 * height, 0, 1)`; neutral and raised
height samples leave UVs unchanged. `scale.depth` already affected the baked atlas and is not applied
again. No full-height POM runs in the Tessellation selection.

Both relief and tessellation resolve tile bounds/amplitude through `vge_displacement_metadata.glsl`.
The existing R32F tile index and compact RGBA32F records avoid face-rotation metadata and work without
engine SSBO face data. Missing or invalid records return the original UV. Height samples use LOD zero
with a half-texel tile inset; small tiles cannot produce inverted clamp bounds.

Position/UV derivatives reconstruct the actual surface metric before material-dependent branches.
The eye direction is converted to atlas-UV displacement per metre using that metric, retaining UV
scale, rotation and mirroring. The existing eye-relative view helper preserves camera bob offsets.
Degenerate/non-finite metrics leave the original UV unchanged.

The existing minimum/maximum step and refinement settings bound the loops. Relief fades with distance,
at grazing angles, and near tile edges; total offset also respects `ParallaxMaxTexels`. Configuration
sanitization makes fade bounds finite and strictly ordered. A complete settings snapshot triggers
shader reload for mode, step, refinement, fade, texel-limit and debug changes.

## Resources and lifetime

Height-atlas allocation, baking and cache identity use `RequiresNormalDepthAtlas`, independently of
normal-map shading. Enabling Relief with normal maps disabled still produces height data. A change in
height demand queues an atlas rebuild through the existing material owner on the main thread.

`TerrainReliefCompilationHook` registers the explicit opaque/topsoil interface after engine uniform
collection. Registration is tied to the managed shader and linked program ID. The existing terrain
atlas setter hook binds the current index/record/height trio through the engine shader abstraction;
sampler state is owned through `GlStateCache`. Draws do not scan shader names for arbitrary uniforms.

A missing page receives zero metadata plus a neutral normal/height texture, never the previous page's
textures. Neutral normals remain `(0,0,1)` and neutral height remains `0.5`. Resource absence affects
the effective result without rewriting the requested mode. Fallback textures retire at the existing
shader reload/material-owner disposal boundary.

## Configuration

TerrainSurfaceDetailMode defaults to Relief, enabling POM. Explicit mode selections are preserved. Old POM settings are
ignored; no legacy values are migrated or persisted. The obsolete boolean and UV-scale properties
have been removed. Step/refinement/fade/texel settings remain the active relief controls.

## Validation boundary

129 distinct focused tests passed. Receipts under `artifacts/PbrColor`: `terrain-relief.trx`,
`terrain-relief-lifecycle.trx`, and `terrain-relief-neutral-final.trx`; the four additional installed
terrain variants passed in `terrain-relief-neutral.trx`. That intermediate receipt also contains two
superseded test assertions which expected the metadata and height fallback to share a texture; the
final fallback receipt verifies their separate resources and neutral height/normal contents.

Focused validation covers explicit configuration selection and serialization, finite settings, physical UV
scaling and mirroring, neutral/raised/missing material behavior, grazing/distance fading, camera-origin
regressions, installed terrain variants with normal-map shading disabled, and resource fallback.
The numerical and installed shader checks use a headless context; they are not live-game appearance
or performance acceptance. Displaced terrain integration is documented in PBR.Tessellation.RenderingConsumers.md.
