# Shared SDR exposure and tone mapping

`includes/pbr_color.glsl` owns the display policy for atmospheric sky, the solar
disk, deferred PBR surfaces and forward PBR surfaces. Both LumOn and standalone
lighting supply unexposed scene-linear RGB. Fixed unit exposure is declared once
as `VGE_DISPLAY_EXPOSURE`; there is no sky-only or sun-only exposure scale.
Atmospheric extinction, disk size and reflectance remain lighting inputs.

The operator clamps negative radiance, applies exposure, then divides all channels
by `1 + max(R, G, B)`. Exact linear-to-sRGB encoding follows. One denominator
preserves linear RGB ratios through the highlight shoulder without channel
clipping. The old per-channel shoulders pushed bright colored inputs toward white.
Neutral inputs retain their previous response; near black the mapping approaches
linear. This is a simple chromaticity-preserving SDR operator, not adaptive eye
exposure or calibrated HDR presentation.

## Current draw boundaries

The table describes the retained legacy color route. Scene storage is now floating point,
and selectable shader branches support scene-linear output and a single final conversion.
Runtime activation is deferred to existing binding owners in the full-scene HDR task; the global
shader-use hook and experimental coordinator are removed. The current sky retains display output. Its HDR patch
adapters were removed; the planned VGE-owned sky replacement must supply compatible output.
See [PBR.WaterRefraction.md](PBR.WaterRefraction.md#hdr-producer-and-consumer-contract).

| Path | Input | Conversion point |
| --- | --- | --- |
| Atmospheric sky | Scene-linear sky LUT radiance | Patched `getSkyColorAt`, before dome blending |
| Sun | Atmosphere-attenuated disk radiance | Solar fragment path, before coverage blending |
| Deferred terrain/entities | Composed lighting and aerial transport | `pbr_display_resolve`, before display-referred primary |
| Forward/OIT/held surfaces | Forward lighting and aerial transport | `pbr_forward_surface.glsl`, before existing display-space blending |

Composition and display resolve bypass sky-depth pixels because those pixels are
already converted. Applying the operator again would darken them. Alpha and
coverage do not pass through RGB transfer. Moon textures and stars remain
engine-authored display colors, rather than being interpreted as physical radiance.

The installed `sky.fsh` invokes `getSkyColorAt` before underwater/night-vision
effects; `final.fsh` applies user gamma, brightness and contrast later. Those sky effects
currently retain the engine's display-color behavior; their HDR ownership belongs to the
planned VGE-owned sky shader. The solar override bypasses vanilla solar
tint/fog so atmospheric attenuation is not applied twice.

## HDR ordering and validation limits

One shared operator does not mean one fullscreen conversion on the legacy route. Its
display-referred scene and existing OIT blending retain the boundaries above. The
conditional scene-linear route keeps these contributors in floating-point buffers and
selects the operator in final composition, disabling earlier conversions together.
Complete-frame verification of that route is still required. HDR monitor output separately
requires transfer-function and presentation support. The legacy sky bypass remains necessary
because those pixels have already been converted on that route.

No textures, buffers, CPU updates, draw calls or LUT work are added. The shoulder
replaces a vector denominator with two scalar maximum operations and a common
denominator. No GPU timing improvement is claimed. Tests cover intensity ranges,
RGB ratios, alpha, sky bypass and real sky/solar display helpers. In-game
readability, twilight appearance and zenith banding still require user observation.
The SDR dithering implementation and remaining visual checks are documented in
PBR.OutputDithering.md.
