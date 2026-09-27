# Material color and display boundaries

Opaque terrain and topsoil now publish unlit, linear material RGB before the direct and LumOn
passes. The scene-linear composite remains RGBA16F. A separate display resolve writes the
engine's RGBA8 primary target before OIT and final grading.

## Material capture

`PbrTerrainColorPatches` clears only the RGB component of the vertex `rgba` lighting varying.
It retains alpha, vertex deformation, fog metadata, glow and point-light side outputs. The
fragment shader still performs the engine's atlas selection, climate/season/frost color mapping,
topsoil layer blending and material effects. Capture occurs before the `murkiness` declaration
that begins the forward fog/reflection section in both installed terrain shaders.

The captured RGB is decoded with the exact sRGB transfer function used by the CPU's
representative-albedo LUT. Original alpha tests/discards and alpha output remain intact.
After the forward section, RGB is replaced with that unlit linear value. Consequently vanilla
vertex light, reflections and underwater color attenuation no longer become BRDF albedo.
Normal-view output is preserved. Roughness, metallic, emissive, normal and height textures are
data and receive no sRGB conversion. Direct shading remains responsible for sun visibility,
view-space point lights and emission; these formulas have not changed.

Color mapping and layered blending retain the engine's authored color-space behavior; the
result is decoded once for lighting. This does not redefine the engine's texture filtering or
implement linear-space atlas filtering. Primary attachment zero remains RGBA8 while temporarily
carrying material color, so dark linear albedo still has that target's quantization limit.

The capture patch checks its engine-source boundary and reports unsupported layouts instead of
silently assuming the capture succeeded. It adds no per-pixel texture samples. Its preservation
tests use the installed terrain sources, rather than an invented replacement main function.
Restoration uses the AST's `main.InnerEnd("body")` boundary and does not depend on the final
output statement or an `outGlow` assignment. TinyAst 0.11.3 replaces the 0.11.2 dependency whose
binder duplicated block delimiters when recognizing directives inside a body. The former
final-statement workaround and test normalization for those duplicate delimiters are removed.

## Lighting and display

Direct diffuse/specular, emission, Surface Cache and indirect lighting remain unexposed linear
signals. `pbr_composite` adds their contributions and blends fog after decoding the engine fog
color to linear. It does not clamp radiance to one or apply a display transform.

`pbr_display_resolve` performs the explicit opaque display boundary:

1. Fixed unit exposure, common to LumOn-enabled and direct-only composition.
2. Nonnegative per-channel Reinhard mapping: `c / (1 + c)`.
3. Exact linear-to-sRGB encoding for the ordinary RGBA8 primary attachment.

This is an explicit baseline display policy, not adaptive exposure or a calibrated filmic
transform. It preserves highlight ordering above one instead of clipping all highlights during
a blit. Per-channel compression can desaturate bright colors. Future atmosphere/exposure work can
replace the shared resolve without changing lighting-cache units or material decoding.

Vanilla sky pixels are already display-referred and bypass this resolve. Both composition and
resolve use `lumonIsSky`, including its exact depth threshold. Vanilla final gamma, brightness,
contrast and color grading remain subsequent display adjustments. The renderer uses a distinct
source texture and disables depth testing/writes for the fullscreen draws, restoring fixed
function state afterwards. The new GPU scope is `PBR.DisplayResolve`.

## Remaining ownership

This change addresses opaque terrain/topsoil. Animated entities, standard/instanced items,
OIT liquids/transparency and late held items remain assigned to the separate coverage task in
`PBR.BaselineShading.todo`. Their existing mixed color/material contracts are not claimed fixed.
Underwater attenuation needs a lighting/composition owner in that work rather than being baked
into albedo. A physically based sky and unified atmospheric fog are also separate tasks; the
legacy fog/sky colors do not constitute calibrated radiance.

## Validation

Focused validation covers installed-source capture placement and alpha/body preservation,
HDR composite sums, fog color conversion, RGBA8 resolve, sky-depth classification and registered
renderer integration. Existing pre-display numerical assertions observe the retained RGBA16F
intermediate; primary-output assertions observe the separate resolve. No game was launched and
no live appearance or performance result is claimed.

The driver also compiles and executes transformed multiline opaque/topsoil fixtures with the
production color helper, checking decoded RGB and preserved alpha. Installed-source tests check
the real statements and matching lexical scope. This is not a full compilation of every engine
include/define combination.

Validation receipts (subagent-run):

- ShaderBuildTool Release succeeded: 153 stages and 387 variants.
- `artifacts/PbrColor/pbr-color-boundaries.trx`: 38 passed; two initial terrain AST failures
  prompted the scope correction and are superseded below. Passing coverage includes direct
  lighting/shadows, registered composition, display grading isolation and lighting restoration.
- `artifacts/PbrColor/pbr-color-boundaries-final.trx`: 12 passed; the same two terrain failures
  are superseded below. This run covers the additional HDR emission case, display transfer,
  linear fog and registered display output after the framebuffer state-cache correction.
- `artifacts/PbrColor/pbr-terrain-production.trx`: final terrain checks **12/12 passed**,
  including the complete production patch chain and actual transformed GLSL execution.
- These initial receipts predate the TinyAst 0.11.3 update. Current preservation tests compare
  exact source text, including comments and whitespace, without normalizing extra block wrappers.
  Separate assertions retain capture/use scope coverage.
