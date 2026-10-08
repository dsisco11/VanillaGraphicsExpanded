# HDR camera exposure

VGE meters the unexposed HDR scene and adjusts a global camera exposure before the existing
SDR tone curve. This is automatic exposure, not per-frame contrast stretching or local tone
mapping. Lighting, atmosphere, reflections, refraction and scene storage retain their original
radiance. Both LumOn modes use the same camera response.

## Camera model

The design follows established histogram auto-exposure practice described in
[Epic's auto exposure documentation](https://dev.epicgames.com/documentation/unreal-engine/auto-exposure-in-unreal-engine):
log-luminance metering, percentile rejection, bounded exposure, a metering mask and independent
adaptation speeds. VGE uses scene-relative stops, not calibrated photographic EV100: the
scene does not currently declare an absolute photometric camera calibration.

Exposure EV is log2 of the RGB multiplier. One stop doubles radiance at the display operator.
The desired EV is log2(middleGray) minus the trimmed weighted mean of log2(luminance), plus
compensation, clamped to the configured minimum/maximum. Default middle gray is 0.18 **before**
the tone curve; it is not a promised final encoded pixel value.

The histogram contains 64 bins across log2 luminance (-12,16). A fixed 64 by 36 grid samples
the completed primary scene with nearest texel reads. Rec.709 linear RGB coefficients produce
luminance. Negative RGB is clamped for metering; non-finite samples and luminance outside the
open range are excluded. Each bin stores weighted sample count and the actual weighted
log-luminance sum, avoiding bin-center bias. The default center weight is
0.25 + 0.75 * exp(-2 * dot(2*uv-1, 2*uv-1)); uniform weighting is selectable.

The default retained interval is the weighted 10th through 90th percentiles. Partial boundary
bins contribute proportionally using their within-bin mean. This is an approximation within
a bin, not an exact sample sort. Small bright solar/emissive regions and dark tails have
limited influence; large bright/dark regions deliberately affect the camera. An empty valid
histogram retains the previous EV, or uses bounded manual exposure when initializing.

Adaptation moves linearly at at most the selected stops/second while more than one stop
from the target, then eases exponentially within one stop. The transition is integrated
analytically, including a step crossing the one-stop boundary. With a constant target, split
time steps produce the same result within floating-point tolerance. Defaults brighten at
1 stop/s and darken at 3 stops/s. This temporal filter suppresses pumping; it does not promise
complete immunity to spatial undersampling or genuinely changing illumination.

## Tone curve and display

The existing stable RGB-ratio-preserving shoulder remains c / (1 + max(c)). Near zero its
slope approaches one, so there is no added shadow crush or moving black point. Its midtone
slope decreases smoothly and highlights approach the SDR limit without per-channel clipping.
Exposure changes which radiances occupy that fixed response; the curve itself does not
change over time. Local contrast adaptation would need a separate demonstrated visual need.

Final composition adds scene and glare in linear space, applies exp2(EV) once, then resolves
the shared shoulder and sRGB transfer before independently authored grading, vignettes and final dithering.
The antialiasing luma prepass computes its perceptual alpha with the same exposure while retaining
unexposed RGB. Alpha, depth, glow and other data are not multiplied by exposure. UI and offscreen engine shaders do not bind camera exposure; they remain outside the owned
scene display endpoint.

## Scheduling and ownership

CameraExposureRenderer captures timing and a finite settings snapshot at Before order 1001.
The VGE-owned PostprocessPipeline invokes metering at the RenderPostprocessingEffects
replacement boundary before its owned bloom and solar-shaft passes. Metering samples primary after scene/OIT/late composition,
before generated bloom and light shafts; those effects cannot feed back into exposure. UI is
composed later. Owned bloom uses this same exposure for its threshold while retaining unexposed output
radiance. See PBR.Postprocessing.md for the complete replacement contract.

Two typed GLSL 330 graphics programs use procedural fullscreen triangles and existing PSOs,
render passes and restoring GraphicsCommandContext boundaries. The histogram is a 64x1 RG32F
image. Adaptation writes one of two 1x1 R32F images while sampling the other. Successful
submission swaps publication; failure never exposes partially written history. There are no
compute shaders, atomics, CPU luminance readbacks or per-frame GPU allocations.

The owned FinalDisplayShaderProgram binds exposure through its typed sampler contract. Its
restoring graphics boundary manages texture/sampler state; no engine shader-use hook or reserved
legacy texture unit is required. The final shader samples history directly; the CPU never reads EV.
Automatic shader/boundary failures are reported instead of selecting a legacy display route.

Startup and resource retirement initialize directly to the current measured target. Settings
changes, dimensions/world changes, camera mode changes, a player-position displacement over
32 blocks in one frame, yaw jumps over 135 degrees, pitch jumps over 90 degrees, and invalid
or greater-than-one-second frame intervals reset adaptation. Position/orientation checks are
camera-cut heuristics based on the engine player/camera mode; continuous normal motion keeps
history. A failed initial submission retains reset until successful publication. Screen
resource publication, shader reload and world exit retire pipelines and images coherently.

## Controls and cost

The Camera Exposure section in the existing graphics configuration persists automatic/manual
mode, compensation, manual EV, EV bounds, middle gray, percentile cutoffs, center weighting,
and brightening/darkening speeds. Automatic mode defaults on. Disable it with manual EV and
compensation both zero to retain the previous unit-exposure camera response. Manual mode
submits no metering passes and retires histogram/history storage.

The portable implementation performs 64 * 64 * 36 = 147,456 scene texel fetches per automatic
frame, plus at most 128 histogram fetches and one history fetch. Its fixed sample grid makes
metering work independent of display resolution. It intentionally avoids newer compute/atomic
requirements. Image payload is 512 histogram bytes plus 8 history bytes, excluding driver
allocation granularity, FBO/VAO objects, pipelines and UBO bookkeeping. Final and owned antialiasing luma
add one cached 1x1 fetch per fragment. These are algorithmic counts, not measured physical
memory bandwidth or complete-frame GPU time.

## Validation status

Focused numerical/GPU validation is being run against actual compiled shaders. Live visual
acceptance remains required for bright exterior/dark interior transitions, stationary views,
sun entry/exit, emissives, underwater views, day/night changes, and varied frame rates.
GPU measurements from an offline fixture cannot establish complete-game performance or
cross-vendor behavior. Native HDR monitor presentation is outside this SDR implementation.
