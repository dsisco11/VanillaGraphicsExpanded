# Normal/Height Sidecar Atlas Bake (VGE)

This mod can generate a per-texture _tileable_ height field (and derived normals) from albedo during the loading/atlas-build stage.

The result is written into the VGE sidecar atlas texture:

- Format: `RGBA16F`
- `RGB`: normal (packed 0..1)
- `A`: signed height

> Note: this is a purely procedural heuristic from albedo; it cannot recover true geometry.

## Enable

In `ModConfig/VanillaGraphicsExpanded.json`:

- `MaterialAtlas.EnableNormalMaps`: `true`

Async throttling (when `MaterialAtlas.EnableAsync` is enabled):

- `MaterialAtlas.AsyncBudgetMs`: `1.0` (default)
- `MaterialAtlas.AsyncMaxUploadsPerFrame`: `32` (default)

A restart / re-entering the world is required.

## Tuning

All bake parameters are under:

- `MaterialAtlas.NormalDepthBake`

Key knobs:

- `SigmaBig`: removes low-frequency albedo ramps (baked lighting)
- `Sigma1..Sigma4` + `W1..W3`: controls multi-scale detail bands
- `Gain`, `MaxSlope`, `EdgeT0`, `EdgeT1`: shapes the desired slope field
- `Multigrid*`: controls the Poisson solve quality
- `HeightStrength`, `Gamma`, `NormalStrength`: final shaping and normal-from-height strength

## Implementation notes

The pipeline is implemented as a sequence of fullscreen fragment passes (no compute required), with periodic boundary conditions per texture rect.

Main entrypoints:

- `PbrMaterialAtlasTextures.Initialize(...)` triggers bake when enabled
- `MaterialAtlasNormalDepthGpuBuilder.BakePerTexture(...)` runs the per-rect pipeline

Shader passes live under:

- `assets/vanillagraphicsexpanded/shaders/pbr_heightbake_*.{vsh,fsh}`
