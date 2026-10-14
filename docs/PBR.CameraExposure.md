# HDR camera exposure

VGE meters the unexposed HDR scene and adjusts a global camera exposure before the existing
SDR tone curve. This is automatic exposure, not per-frame contrast stretching or local tone
mapping. Lighting, atmosphere, reflections, refraction and scene storage retain their original
radiance. Both LumOn modes use the same camera response.

## Camera model

The design uses log-luminance metering, percentile rejection, bounded exposure, a metering mask and independent
adaptation speeds. VGE uses scene-relative stops, not calibrated photographic EV100: the
scene does not currently declare an absolute photometric camera calibration.

Exposure EV is log2 of the RGB multiplier. One stop doubles radiance at the display operator.
The desired EV is log2(middleGray) minus the trimmed weighted mean of log2(luminance), plus
compensation, clamped to the configured minimum/maximum. Default middle gray is 0.18 **before**
the tone curve; it is not a promised final encoded pixel value. The default EV interval is
[-20, 2]. This covers an 18% diffuse daylight receiver illuminated at 100,000 lux
(about 5,730 nits, requiring about -15 stops for the 0.18 display target), while bounding
night brightening. Manual stops and exposure bounds accept [-48,24]; compensation remains
[-8,8]. These are display multipliers, not EV100. Saved custom limits remain authoritative
when already saved; the defaults apply to new or missing settings.

The histogram contains 64 bins across log2 physical luminance in nits (-16,40). The upper
bound exceeds the maximum decoded half-float payload, 65504 / 7.147944e-6 (about 9.2 billion
nits); the lower bound extends dim receiver sampling without promising scene quantization
precision at that level. A fixed 64 by 36 grid samples
the completed primary scene with nearest texel reads. Rec.709 linear RGB coefficients produce
luminance. Negative RGB is clamped for metering; non-finite samples and luminance outside the
open range are excluded. Each bin stores a quantized weighted count and shifted weighted
log-luminance sum, avoiding bin-center bias. The default center weight is
0.25 + 0.75 * exp(-2 * dot(2*uv-1, 2*uv-1)); uniform weighting is selectable.

The default retained interval is the weighted 10th through 90th percentiles. Partial boundary
bins contribute proportionally using their within-bin mean. This is an approximation within
a bin, not an exact sample sort. Small bright solar/emissive regions and dark tails have
limited influence; large bright/dark regions deliberately affect the camera. An empty valid
histogram retains the previous EV, or uses bounded manual exposure when initializing.

Adaptation moves linearly at at most the selected stops/second while more than one stop
from the target, then eases exponentially within one stop. Defaults are 4 stops/second toward
brighter exposure and 8 toward darker exposure. A constant twelve-stop target change settles
within 0.05 stop in approximately 3.5 seconds when brightening and 1.75 seconds when darkening.
These timings describe a fixed metered target; scene changes can keep moving that target.
Explicitly saved adaptation rates are retained. The transition is integrated
analytically, including a step crossing the one-stop boundary. With a constant target, split
time steps produce the same result within floating-point tolerance. This temporal filter
suppresses pumping; it does not promise
complete immunity to spatial undersampling or genuinely changing illumination.

## Tone curve and display

Final composition adds scene and glare in linear space and applies exp2(EV) once, then samples
the cached scene-linear grading, filmic response and sRGB display transform described in
[PBR.Postprocessing.md](PBR.Postprocessing.md). The native gamma slider is normalized around
its default of 3; extra gamma is also a relative control with neutral value 1. Gamma, brightness
gain and 0.18-pivot power contrast act before tone mapping. The filmic toe and shoulder remain
fixed rather than performing local contrast adaptation. Exposure selects which radiances occupy
that response and is not part of the grading lookup's cache key. Faint positive light may still
fall below final display quantization. Warm tint, vignettes and final dithering follow display encoding.
Legacy compatibility-color conversion retains its separate fixed unit-exposure shoulder;
it does not define the final HDR scene presentation.
The antialiasing luma prepass computes its perceptual alpha with the same exposure while retaining
unexposed RGB. Alpha, depth, glow and other data are not multiplied by exposure. UI and offscreen engine shaders do not bind camera exposure; they remain outside the owned
scene display endpoint.

## Scheduling and ownership

### Physical-light storage support

The camera owner also provides `FreezeLightingStorage` for calibrated scene producers. It
requires the current frame and completed point-light publication, admits an outward physical
component bound, and dispatches `pbr_frame_lighting` once before the first consuming draw.
The retained 32-byte SSBO contains P, 1/P, the physical bound, validity and frame/calibration
identities. The automatic seed reads the previous completed EV buffer; startup, camera cuts
and manual mode use the bounded manual seed. Subsequent metering writes a separate exposure
buffer and leaves the frozen scale unchanged. Initial freezing after metering is rejected.

Physical consumers explicitly adopt `IPhotometricFrameBindings`. Sun, sky, block, punctual
and material-emission sources are calibrated. Connected transport, scene storage, metering
and display consumers use the same frozen publication. Metering decodes stored RGB by 1/P;
final display multiplies scene plus glare by exp2(EV)/P exactly once. Persistent lighting
retains its fixed C=1/16 representation independently of the camera.
The capability gate admits the frozen slot and simultaneous
fragment reads of exposure and frame scale. Resize, reload and world retirement withdraw and
dispose both publications through the existing camera owner. Stable submissions reuse storage;
there is no CPU exposure readback. Shared diagnostics identify the active unit convention,
physical bound, GPU-owned P, fixed C and selected compatibility references.

CameraExposureRenderer captures timing and a finite settings snapshot at Before order 1001.
The VGE-owned PostprocessPipeline invokes metering at the RenderPostprocessingEffects
replacement boundary before its owned bloom and solar-shaft passes. Metering samples primary after scene/OIT/late composition,
before generated bloom and light shafts; those effects cannot feed back into exposure. UI is
composed later. Owned bloom uses this same exposure for its threshold while retaining unexposed output
radiance. See PBR.Postprocessing.md for the complete replacement contract.

Two typed GLSL 430 compute programs use the existing compute executable, generated binding,
GPU buffer and dispatch APIs. The histogram dispatch has 36 workgroups of 64 lanes: each lane
fetches and classifies one of the unchanged 2,304 grid samples. Each group initializes 64 shared
count/sum pairs, accumulates with two unsigned integer atomics per valid sample, synchronizes,
and overwrites its own complete 64-bin SSBO slice. No global atomics or per-frame clear are needed.
The reduction dispatch uses one 64-lane group; each lane decodes and sums its bin across the
36 slices, then lane zero performs percentile reduction and the unchanged analytical adaptation.

Weights are rounded to unsigned integers at scale 4,096. The shifted log-luminance sum is
round(encodedWeight * (log2(luminance) + 16) * 256). Decoding divides weight by 4,096
and sum by 1,048,576, then subtracts 16 times decoded weight. Weight rounding error is at most
1/8,192 per sample; sum quantization is at most 1/2,097,152 in decoded units before shader
floating-point rounding. Floating-point products near the largest encoded sum have eight-unit
spacing (about 0.000008 decoded units). Percentile reduction retains within-bin weighted means;
quantization can perturb percentile boundary populations. This is a bounded approximation to
the previous float histogram, not an exact sample sort or calibrated EV100 statistic.
A group contains only 64 samples with weights at most one and shifted levels at most 56:
count <= 262,144 and shifted sum <= 3,758,096,384, both below uint32 capacity.
The complete histogram is decoded to float before merging groups, avoiding integer overflow
across all 2,304 samples. Invalid RGB and luminance outside the open range remain excluded.

Two owned 32-byte SSBOs retain previous/current EV at offset zero plus optional viewing
metadata. Float offsets are EV, adapted log2 luminance, metered log2 luminance, validity,
uncapped metering target, metering-target cap-hit flag and two reserved zeros. Existing
scalar exposure consumers continue reading offset zero. Successful dispatch submission and
restoration swap publication; failure never publishes the partially written candidate.
ShaderStorageBarrierBit separates histogram writes from reduction reads and reduction writes
from later fragment reads (including next-frame history reads). The prepared-resource engine
boundary restores program, sampler, UBO and SSBO bindings. Stable frames reuse the same
allocations and programs; there are no CPU luminance/EV readbacks or per-frame GPU allocations.

### Optional local-exposure adaptation

The disabled-by-default `Postprocessing.LocalExposure.Enabled` candidate independently
adapts physical log luminance. A cap on camera EV cannot become the viewing estimate.
The histogram's optional open domain is (-24,40) log2 nits; shifted-sum quantization uses
128 instead of 256 units. With 64 samples, 4096 weight units and a 64-stop span, a
workgroup sum is bounded by 2,147,483,648, within uint32. Default metering retains the
existing (-16,40) domain and quantization. Samples at/below the lower endpoint remain
excluded, not a fabricated luminance floor.

Enabling the candidate meters even in manual camera mode. Applied EV remains the saved
bounded manual value; physical luminance history is still estimated and adapted. The
uncapped target and cap-hit flag describe what the meter requests relative to the configured
exposure interval, even when automatic gain is disabled. They are GPU-resident diagnostic
fields, not CPU readbacks. Empty populations publish invalid viewing metadata and neutral
local correction. Cuts, reload, resize, world leave and enable changes invalidate history.
Viewing adaptation uses the existing rates with the luminance/exposure direction reversed.
These are practical temporal filters, not measured physiological rod/cone timings.

The spatial display operation and unchanged grading cache are described in
[PBR.Postprocessing.md](PBR.Postprocessing.md). No source-light or storage calibration changes.

Owned bloom, LightShafts, luma preparation and FinalDisplayShaderProgram consume the published
EV through generated typed read-only storage-block bindings. Manual draws select the captured
manual EV and do not read the optional buffer. The graphics baseline remains GLSL 330 with
GL_ARB_shader_storage_buffer_object and GL_ARB_shader_image_load_store explicitly required
for these consumers; it does not
imply GL 3.3 runtime compatibility. GpuSupport owns cached compute/SSBO/SPIR-V capabilities
and stage limits. Camera metering requires 64-lane groups, 36 dispatch groups, 512 shared bytes,
three compute storage blocks and one fragment storage block. Missing capabilities or shaders
raise rendering errors; no legacy raster fallback or reserved legacy texture unit is used.

Startup and resource retirement initialize directly to the current measured target. Settings
changes, dimensions/world changes, camera mode changes, a player-position displacement over
32 blocks in one frame, yaw jumps over 135 degrees, pitch jumps over 90 degrees, and invalid
or greater-than-one-second frame intervals reset adaptation. Position/orientation checks are
camera-cut heuristics based on the engine player/camera mode; continuous normal motion keeps
history. A failed initial submission retains reset until successful publication. Screen
resource publication, shader reload and world exit retire compute programs, buffers and scene borrows coherently.

## Controls and cost

The Camera Exposure section in the existing graphics configuration persists automatic/manual
mode, compensation, manual EV, EV bounds, middle gray, percentile cutoffs, center weighting,
and brightening/darkening speeds. Automatic mode defaults on. Disable it with manual EV and
compensation both zero to use one display unit per physical nit before the tone curve. Manual mode
submits no metering dispatches and retires histogram/history storage when local exposure
is disabled. With the optional candidate enabled, physical viewing estimation remains
active while the saved manual gain is authoritative.

### Saved-setting compatibility

New or missing camera settings use exposure limits -20/+2 and manual exposure zero. Existing
saved camera values remain unchanged, subject to the finite snapshot bounds; no version marker
or automatic stop offset is applied. A leftover marker from an intermediate configuration is
ignored as an unknown field. Compensation, middle gray, adaptation, percentiles and dimensionless
glare controls retain their meanings. Updating defaults does not rewrite saved custom settings.

Existing exposure values may need user adjustment: the formerly uncalibrated solar/block/source
mixture had no universal scale, so the same stops do not promise the same old screenshots.
Engine light levels, materials, propagation and world saves remain unchanged. Existing shared
shader mappings supply source-derived torch lux, point candela and 32-nit authored emission;
third-party inputs using those same fields inherit these defaults. Arbitrary shader output still
requires the declared scene contract. No new light configuration or source discovery is added.

The compute implementation fetches 2,304 scene samples per automatic frame, compared with
147,456 in the retired raster implementation at the same sample grid. It performs at most
4,608 shared integer atomic additions, 2,304 workgroup-pair writes and 2,304 pair reads,
followed by two serial scans of 64 reduced bins. Two dispatches replace two draws; three
exposure FBOs and the procedural VAO are removed. One scene sampler remains in metering,
while reduction has no samplers; bloom/luma/shafts/final no longer sample exposure textures.
Buffer payload is 18,432 histogram bytes plus eight history bytes (18,440 total), compared
with 520 image bytes previously; allocation granularity, executables and UBOs are excluded.
Fragment consumers read one shared EV float when automatic exposure is enabled.
These are algorithmic storage/work counts, not physical bandwidth or complete-frame GPU timings.

## Validation status

The earlier physical-camera qualification passed 197 focused cases with no failures or skips,
including 28 new migration, physical-metering scale, actual half-float storage, exceptional-output,
shaft scale and final display checks. Both current shader catalogs and the Release build pass.
Receipts: artifacts/TestResults/photometric-phase5-superseding.trx and
artifacts/photometric-phase5-final-build.log. Earlier receipts below qualify the retained camera
algorithm; their former scene-relative histogram bounds are superseded by the physical range above.
The nine automatic-migration cases are superseded by the user-approved removal of that mechanism;
focused settings/default checks qualify the retained compatibility behavior (42/42 passing, no
skips, in artifacts/TestResults/exposure-defaults-no-migration.trx; Release build passes). The unchanged shader
checks retain their evidence. These are headless correctness checks. Integrated package and user-run adaptation/appearance
acceptance remain in PBR.PhotometricLighting.todo.

The shader-enabled build and 109/109 focused numerical/GPU cases passed with no failures or
skips. Existing compiler/analyzer warnings remain. Receipt: artifacts/exposure-final-validation.log
and artifacts/CameraExposure/exposure-final.trx. An independent 21-case GPU recheck passed
without skips: artifacts/exposure-independent-validation.log and
artifacts/CameraExposure/exposure-independent.trx. Coverage includes independent uniform/weighted
CPU histogram statistics, all 64 bins against the retained test-only raster reference, invalid
samples, upper-range local-bin overflow bounds, percentile trimming, temporal frame partitioning,
manual/reset/empty history, buffer publication/disposal and binding restoration. Runtime cases
exercise standalone and LumOn modes, settings changes, resize, shader reload and world retirement;
bloom, shafts, luma and final read the typed buffer while retaining exactly-once display exposure.

Matched synthetic measurements on NVIDIA GeForce RTX 4090 use the same 2,304 grid samples,
weighted policy and scene inputs for both algorithms. Eight warm alternating samples report
medians; source upload and readback are outside timing. Combined GPU timings include their
production restoration boundaries. CPU submission timings exclude query-result waiting.

| HDR source | Raster pair GPU (ms) | Compute pair GPU (ms) | Raster submission CPU (ms) | Compute submission CPU (ms) | Compute histogram GPU (ms) | Compute adaptation GPU (ms) |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 1280 x 720 | 0.6892 | 0.0164 | 0.6478 | 0.1567 | 0.0061 | 0.0133 |
| 1920 x 1080 | 0.6922 | 0.0164 | 0.5054 | 0.1533 | 0.0061 | 0.0133 |
| 3840 x 2160 | 0.6912 | 0.0174 | 0.6943 | 0.2318 | 0.0061 | 0.0143 |

Per-stage timings use independent queries/submissions and do not add up to the paired timing;
query boundaries and batching differ. The matched fixture reports zero measured EV difference
and passes all 64 count/log-sum comparisons within quantization tolerances. These measurements
are not complete-game frame costs, cross-vendor results or physical bandwidth claims.

Live visual acceptance remains deferred for bright exterior/dark interior transitions, stationary
views, sun entry/exit, emissives, underwater views, day/night changes and varied frame rates.
No game was launched. Native HDR monitor presentation is outside this SDR implementation.
