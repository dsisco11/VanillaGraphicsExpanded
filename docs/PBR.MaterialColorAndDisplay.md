# Material color and display boundaries

Opaque terrain and topsoil now publish unlit, linear material RGB before the direct and LumOn
passes. The scene-linear composite remains RGBA16F. A separate handoff writes the engine's
primary target before OIT and final grading. Primary and bloom blur storage use RGBA16F.
Runtime scene rendering preserves linear radiance through this handoff; owned final composition
performs display conversion. Missing mandatory resources report rendering errors. The runtime
ownership and UI/offscreen isolation contract is [PBR.SharedDisplay.md](PBR.SharedDisplay.md).

Animated entities, standard/instanced meshes, transparent terrain and late held items are covered
by the subsequent [entity and late-surface integration](PBR.EntityAndLateCoverage.md). The
terrain-specific capture and display contracts described here continue to apply.

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
implement linear-space atlas filtering. Primary attachment zero temporarily carries material
color; the scene-color allocation adapter now supplies RGBA16F storage for it.

The capture patch checks its engine-source boundary and reports unsupported layouts instead of
silently assuming the capture succeeded. It adds no per-pixel texture samples. Its preservation
tests use the installed terrain sources, rather than an invented replacement main function.
Restoration uses the AST's `main.InnerEnd("body")` boundary and does not depend on the final
output statement or an `outGlow` assignment. TinyAst 0.11.3 replaces the 0.11.2 dependency whose
binder duplicated block delimiters when recognizing directives inside a body. The former
final-statement workaround and test normalization for those duplicate delimiters are removed.

## Lighting and display

Source evaluation now uses calibrated atmosphere lux/nits, shared block E/pi, explicitly adapted
engine point candela and a 32-nit emission reference. Shared direct diffuse response divides by pi
for sun and punctual sources alike. This source conversion preserves material capture, Fresnel/
metallic partition and shadow ownership. Cache/probe and atmosphere lighting now use fixed-scale
persistent storage, decoded at their declared consumers. The connected transient pipeline uses
one frozen frame scale P for lighting storage and decodes it before physical evaluation or display.
Camera/configuration compatibility preserves saved stops and uses the updated defaults described
in PBR.CameraExposure.md, without a version marker or automatic conversion. Source/material fields retain their existing
data and receive the fixed calibrated shader interpretation; no engine asset rewrite is required.
Remaining qualification is tracked in
`PBR.PhotometricLighting.todo`; this intermediate pipeline is not a release candidate.

Direct diffuse/specular, emission and gathered indirect lighting remain unexposed linear
signals, stored with P in transient targets. Surface Cache and probe images store their physical quantities times 1/16; material
coefficients remain data. `pbr_composite` adds lighting contributions and converts authored fog
through the 32-nit compatibility reference before blending. It does not clamp radiance to one or
apply a display transform.

The material atlas owner admits finite binary16 parameters before replacing or uploading an
image. It retains the maximum neutral emission decoded from admitted coefficients,
including binary16 rounding and accepted pending uploads, through the matching atlas lifetime.
Cached restores and asynchronous artifacts use that same page admission. Queued transfers retain
the concrete texture owner and discard retired destinations before uploading; reused numeric
texture names cannot redirect an old job into a replacement image. A lower replacement cannot
erase the bound of compatible cached lighting. Invalid replacement data leaves the previous
texture and bound available; page retirement removes both. The compact Surface Cache material
LUT retains its own decoded emission bound and C-encoded emission payload.

`pbr_display_resolve` copies linear radiance on an HDR frame, including sky, and composites
the captured premultiplied particle layer over the transported background once. On the retained
legacy route it performs the opaque display boundary instead. The retained legacy conversion
uses the following baseline policy:

1. Fixed unit exposure, common to LumOn-enabled and standalone composition (see [PBR.LightingModes.md](PBR.LightingModes.md)).
2. Nonnegative RGB-ratio-preserving mapping: `c / (1 + max(c.r, c.g, c.b))`.
3. Exact linear-to-sRGB encoding.

This is an explicit baseline display policy, not adaptive exposure or a calibrated filmic
transform. It preserves highlight ordering above one instead of clipping all highlights during
a blit. All channels share a shoulder denominator to retain linear RGB ratios.
See [PBR.SharedDisplay.md](PBR.SharedDisplay.md) for the sky/sun/surface contract.

On the legacy route, sky pixels already used the shared display helper and bypass this resolve.
Both composition and resolve use `lumonIsSky`, including its exact depth threshold. On HDR
frames the owned final shader adds scene and effects, applies camera exposure and samples
the scene-linear grading/filmic display lookup described in [PBR.Postprocessing.md](PBR.Postprocessing.md).
The legacy shoulder is not applied again to that HDR scene. The renderer uses a distinct
source texture and disables depth testing/writes for the fullscreen draws, restoring fixed
function state afterwards. The new GPU scope is `PBR.DisplayResolve`.

## Remaining ownership

Solar surface specular reflection uses `includes/pbr_finite_disk_brdf.glsl` in deferred, forward and
liquid surface paths. The finite emitter uses the existing visible disk angular radius. A
representative direction within that extent determines half-vector alignment, while a
view-dependent energy factor compensates the larger reflection footprint. Material roughness,
center-direction diffuse and masking, solar irradiance and shadow ownership retain their
existing meaning. Punctual lights continue to use the directional Cook-Torrance response.
Deferred and forward sunlight call `addFiniteDiskDirectLight` explicitly; `addDirectLight` retains
the punctual response. Angular radius describes source extent and does not select the light type.
This finite-source approximation does not establish completed photometric calibration or
change the disk's display policy, water volume scattering or transmission models.
Focused installed/GPU validation passes 142 tests, including independent cone-boundary and
normal-incidence references, grazing/minimum-roughness precision and existing surface/shadow/liquid
contracts. Production Release compilation passes. Live appearance and performance remain unmeasured.

This document owns opaque material capture and its color boundary. Entity, forward-surface
and late-item coverage is documented in [PBR.EntityAndLateCoverage.md](PBR.EntityAndLateCoverage.md).
Runtime scene-HDR ownership is implemented as specified in `PBR.SharedDisplay.md`; owned sky
visual acceptance is recorded in `PBR.BaselineShading.todo`. Water transport acceptance is tracked in
`PBR.WaterRefraction.todo`. Engine-authored fog and effect colors are decoded for
linear composition but do not constitute calibrated physical radiance.

## Shared HDR handoff work

`PBR/SceneColor/SceneColorAllocation` selects RGBA16F for primary color and the existing bloom
blur targets at the engine's allocation calls. Glow, revealage, depth and SSAO data keep their
formats. Engine-owned texture names, framebuffer publication and retirement are unchanged.
Luma and god-ray storage were already RGBA16F in the inspected installation. This removes an
intermediate storage limit; it does not by itself change the scene's color convention.

`VgeSceneOutput`, the liquid frame's `SceneLinear` field and the opaque handoff shader's
`SceneLinear` input support linear radiance without per-draw display conversion. Runtime binding
owners select HDR for scene contributors and retain UI/offscreen isolation. Surface programs
use `vge_pbrRoute`; authored engine effects use the separate scene-color binding described in
`PBR.SharedDisplay.md`. The experimental coordinator and global shader-use hook were removed.
Sky-specific HDR patches and vanilla sky G-buffer injection are removed. The VGE-owned
`pbr_sky` program samples the existing atmospheric lookup, retains spatial sky effects and
always emits scene-linear radiance at runtime. It writes color/glow only; background metadata
comes from framebuffer clearing. Lifecycle and user-run visual acceptance are
documented in [PBR.Atmosphere.Publication.md](PBR.Atmosphere.Publication.md).
The historical producer inventory and current HDR contract are in
[PBR.WaterRefraction.md](PBR.WaterRefraction.md#hdr-producer-and-consumer-contract).

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
