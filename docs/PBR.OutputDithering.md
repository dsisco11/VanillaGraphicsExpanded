# SDR output dithering

The per-draw boundaries below describe the retained legacy color route. The conditional
scene-linear route disables those calls and retains final encoded-output dithering. Primary
storage is floating point. VGE scene rendering requires HDR through the binding owners described
in [PBR.SharedDisplay.md](PBR.SharedDisplay.md); missing dependencies report errors without a legacy fallback.
Live handoff and perception-effect appearance still require user verification.

`VgeDitherDisplay` in `pbr_color.glsl` adds ordered dither to encoded display RGB,
after shared exposure, tone mapping and sRGB encoding. Its 8x8 Bayer tile visits
all 64 ranks once. The offset is `((rank + 0.5) / 64 - 0.5) / 255`, strictly
bounded below half one RGBA8 code step and exactly zero-mean over a complete tile
before clipping. RGB shares one offset so neutral colors remain neutral. Alpha,
coverage, material buffers, lighting caches and atmospheric LUTs are unaffected.

The pattern is anchored to integer framebuffer pixels, with no frame seed, time
input, extra textures or temporal history. Repeated identical frames have exactly
the same pattern. Clipping at black/white bounds the output; sub-half-step offsets
still quantize exact black and white to their original endpoint codes. Ordered
dither may be spatially visible under magnification; live invisibility is not
claimed from numerical checks.

Deferred geometry dithers once in `pbr_display_resolve` immediately before the
display-referred primary write. The owned sky dithers at the end of its legacy fragment route, after
underwater/night-vision effects. Solar RGB dithers after underwater effects and
before coverage blending. Forward surfaces dither their resolved display RGB
before existing primary/OIT blending. Sky-depth pixels bypass deferred dithering
because their sky/celestial draws have already applied it. Transparency blending
can attenuate the dither, and later vanilla grading may introduce new quantization.

The engine `final.fsh` is also patched through TinyAst to apply `VgeDitherFinalDisplay`
after god-ray/bloom composition, color grading and vignettes. These effects create
gradients after primary-buffer dithering, so they need coverage at their own SDR
output boundary. The final patch changes RGB only. On HDR frames it applies the shared display
conversion before grading; on legacy frames it does not repeat exposure or tone mapping.
It preserves halo intensity, falloff and alpha. Legacy per-draw dithering remains available;
HDR frames preserve floating-point radiance until final conversion.
The user accepted the initial sky result but reported
remaining solar-halo bands; attribution to atmospheric LUT sampling versus later
postprocessing remains unconfirmed without a visual comparison.

The final helper explicitly rounds the dithered RGB to 8-bit codes before output.
This preserves already-quantized colors at every dither rank while distributing
new fractional postprocessing values between adjacent codes. Simply adding noise
again failed the unchanged-color GPU check on the test driver.

The dither amplitude targets SDR RGBA8 output; it must not be carried unchanged
into a future higher-bit-depth HDR presentation path. The conditional scene-linear route
moves display conversion and dithering to the final pass and disables per-draw calls
together. `VgeResolveDisplay` remains a pure transfer
function so lighting computations and numerical references are not contaminated.

## Validation boundary

Floating-point atmospheric LUTs and HDR lighting retain more precision than the
primary SDR output. This implementation addresses that known quantization
boundary; it does not prove that the reported zenith bands originate there.
User observation must distinguish residual LUT interpolation bands from display
quantization after this change. Dither cannot reconstruct missing angular samples.

GPU tests cover actual RGBA8 quantization of sub-code gradients, complete-tile
coverage and brightness, repeated-frame equality, channel neutrality, alpha,
endpoints and the existing sky bypass. Rendering fixtures exercise sky, solar
and both PBR surface modes. No additional buffers, draw calls, CPU updates or
texture samples are required. Production GPU timing and visual acceptance remain
unmeasured.

The float-output test checks every tile rank against an independent reference,
including zero mean and the half-code bound. Actual RGBA8 conversion on the test
driver did not reproduce ideal mathematical rounding at every threshold; disabling
driver dithering did not change that result. The quantized gradient therefore
separately requires adjacent output codes, increasing spatial averages, repeatable
pixels and average error below 1/16 of an 8-bit code step. The driver's internal
conversion precision has not been identified, and no particular internal format
is assumed by the implementation.

Release build and all 72 subagent-run focused cases passed, including sky/solar,
forward/deferred, runtime display boundaries and patch/binding coverage. Receipt:
`artifacts/TestResults/output-dithering-final.trx`. No game was launched.

Final-postprocessing follow-up: Release build and 12/12 subagent-run focused
tests passed, including all 256 existing output codes at every dither rank,
fractional halo-like gradients, alpha/channel preservation and the installed
GLSL 330 final shader with bloom, god rays and FXAA enabled. Receipt:
`artifacts/tests/halo-dither/halo-dither.trx`; log: `artifacts/halo-dither-test.log`.
The reported halo still requires user visual verification.
