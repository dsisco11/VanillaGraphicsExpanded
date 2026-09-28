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

| Path | Input | Conversion point |
| --- | --- | --- |
| Atmospheric sky | Scene-linear sky LUT radiance | Patched `getSkyColorAt`, before dome blending |
| Sun | Atmosphere-attenuated disk radiance | Solar fragment path, before coverage blending |
| Deferred terrain/entities | Composed lighting and aerial transport | `pbr_display_resolve`, before RGBA8 primary |
| Forward/OIT/held surfaces | Forward lighting and aerial transport | `pbr_forward_surface.glsl`, before existing display-space blending |

Composition and display resolve bypass sky-depth pixels because those pixels are
already converted. Applying the operator again would darken them. Alpha and
coverage do not pass through RGB transfer. Moon textures and stars remain
engine-authored display colors, rather than being interpreted as physical radiance.

The installed `sky.fsh` invokes `getSkyColorAt` before underwater/night-vision
effects; `final.fsh` applies user gamma, brightness and contrast later. Those
effects retain their display roles. The solar override bypasses vanilla solar
tint/fog so atmospheric attenuation is not applied twice.

## HDR ordering and validation limits

One shared operator does not mean one fullscreen conversion today. The primary
RGBA8 attachment and existing OIT blending require the boundaries above. The
scene-linear HDR task must keep sky, celestial coverage, opaque and transparent
composition in floating-point buffers, then move this operator to one final SDR
resolve and remove earlier conversions together. HDR monitor output separately
requires transfer-function and presentation support. Removing the sky bypass now
would double-map existing colors.

No textures, buffers, CPU updates, draw calls or LUT work are added. The shoulder
replaces a vector denominator with two scalar maximum operations and a common
denominator. No GPU timing improvement is claimed. Tests cover intensity ranges,
RGB ratios, alpha, sky bypass and real sky/solar display helpers. In-game
readability, twilight appearance and zenith banding still require user observation.
Dithering remains a separate follow-up if banding persists.
