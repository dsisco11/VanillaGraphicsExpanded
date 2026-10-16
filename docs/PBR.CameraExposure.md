# HDR camera exposure

VGE meters the unexposed HDR scene and adjusts a global camera exposure before the existing
SDR tone curve. The camera owner provides global adaptation; the separate spatial
display operation is described below. Lighting, atmosphere, reflections, refraction and scene storage retain their original
radiance. Both LumOn modes use the same camera response.

## Camera model

The camera uses saturation-based EV100 authoring with fixed lens attenuation q=0.78 and
saturation coefficient 0.78. Thus Lmax=0.78/q=1 cd/m² at camera EV100 zero. For camera
EV100 C and compensation B stops, gain is 2^(-C+B)/Lmax. Higher C darkens; positive B brightens.
This virtual-camera calibration has no aperture, shutter or ISO controls.

The histogram measures physical luminance L in cd/m². The instantaneous scene camera value
is Cscene=log2(L/(MiddleGray*Lmax)); with the default middle gray 0.18, 0.18 nits gives EV100 zero,
1 nit gives about 2.474, and 5730 nits gives about 14.958. This gray-relative scene estimate is
distinct from log2(L/Lmax), the EV100 of a saturation-luminance value. Middle gray is the
linear target before the filmic response, not a promised final encoded pixel value.

GPU history and exposure consumers retain G=log2(gain) in multiplier stops. The automatic
target remains log2(MiddleGray)-log2(L)+Compensation. Compensated gain is clamped to
[-MaxEV100-log2(Lmax),-MinEV100-log2(Lmax)]. Defaults MinEV100=-2, MaxEV100=20 and
ManualEV100=0 preserve the prior [-20,+2] gain interval and unit manual gain. Manual gain is
clamped with the same compensation ordering. Camera values accept [-24,48]; compensation
accepts [-8,8]. Reversed current camera bounds collapse the maximum to the minimum.

Histogram weighting, percentiles, timing, local correction and source calibration are retained.
A higher scene EV100 requests less gain. BrightenRate and DarkenRate describe image brightness,
so increasing camera EV100 uses DarkenRate; decreasing it uses BrightenRate. Neither gain
limits nor histogram log-nit bounds are relabeled as each other.

The histogram contains 64 bins across log2 physical luminance in nits (-24,40) when
local exposure is enabled, or (-16,40) when it is disabled. The upper
bound exceeds the maximum decoded half-float payload, 65504 / 7.147944e-6 (about 9.2 billion
nits); the lower bound extends dim receiver sampling without promising scene quantization
precision at that level. Dense half-resolution metering averages each available 2 by 2
footprint of the completed primary scene before taking its logarithm. Odd-sized edges average
only existing texels. Downscaling is fused into histogram construction without an intermediate
image, and every source texel belongs to exactly one footprint. Rec.709 linear RGB coefficients produce
luminance. Negative RGB is clamped for metering; footprints containing non-finite RGB and luminance outside the
open range are excluded. Each bin stores a quantized weighted count and shifted weighted
log-luminance sum, avoiding bin-center bias. The default center weight is
0.25 + 0.75 * exp(-2 * dot(2*uv-1, 2*uv-1)); uniform weighting is selectable.

Samples distribute their weight between adjacent positions on the 64-point log-luminance
lattice: normalizedLogLuminance * 63 selects the lower/upper positions and their blend.
Integer splitting conserves each sample's original encoded count and actual log sum exactly.
This smooths bin populations without substituting representative bin luminance for the actual
sample. The geometric mean of an untrimmed population remains conserved up to floating-point
reduction rounding; percentile-trimmed results can change as intended when tails straddle bins.

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

Bloom applies bounded local correction to its emitting samples before thresholding and filtering.
Final composition locally corrects scene and directional shafts, adds the source-corrected bloom
without destination-local gain, and applies exp2(G) once, then samples
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
requires the current frame and completed point-light publication, validates finite physical
source-envelope metadata, and dispatches `pbr_frame_lighting` once before the first consuming draw.
The retained 32-byte SSBO contains P, 1/P, the source estimate, validity and frame/calibration
identities. The automatic seed reads the previous completed EV buffer; startup, camera cuts
and manual mode use the bounded manual seed. Subsequent metering writes a separate exposure
buffer and leaves the frozen scale unchanged. Initial freezing after metering is rejected.

Storage follows the selected camera multiplier, floored at 7.147944e-6 with finite reciprocal
and conversion arithmetic. It is not forced to the persistent C=1/16 scale or reduced by an
estimated extreme specular peak. A separate bright highlight therefore cannot lower the
encoding scale of faint pixels. Format-specific scene clamps contain exceptional values;
these may clip and lose highlight energy. Source metadata continues to enforce publication
coherence but does not guarantee an unclipped image. Persistent cache/history invalidity
and rejection remain strict. See
[the highlight containment contract](PBR.MicrofacetStorage.Evaluation.md#approved-highlight-policy).

Physical consumers explicitly adopt `IPhotometricFrameBindings`. Sun, sky, block, punctual
and material-emission sources are calibrated. Connected transport, scene storage, metering
and display consumers use the same frozen publication. Metering decodes stored RGB by 1/P;
final display multiplies scene plus glare by exp2(G)/P exactly once. Persistent lighting
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
GPU buffer and dispatch APIs. The histogram dispatch has 1,024 workgroups of 64 lanes. Lanes
visit consecutive half-resolution pixels and continue with a 65,536-sample stride until every
footprint has been processed. Each group initializes 64 shared count/sum pairs, accumulates
up to four unsigned integer atomics per valid footprint, synchronizes, and overwrites its complete
64-bin SSBO slice. No global atomics, per-frame clear or downsample texture are needed.
The reduction uses one 256-lane group. Four lanes independently decode interleaved partials
for each bin, then combine their sums through shared memory across 1,024 slices,
then lane zero performs percentile reduction and the existing analytical adaptation.

The independent strip tiles spread dense input over the GPU instead of retaining only 36
long-running groups. Every group writes all its bins, even when no input reaches that tile;
the reduction derives the partial count from the buffer extent. Scratch payload is fixed at
512 KiB and reused across frames and resize. The previous dense implementation used 18 KiB.
Sampling density, footprint averaging, physical luminance weights and exposure policy are
unchanged; shorter partial populations also retain greater fixed-point precision.

For N half-resolution samples, M = 64 * ceil(N / 65536) bounds the population of each group.
The common integer weight scale is S = min(4096, floor(2^30 / (128*M))). Weights are rounded
at that scale; their absolute error is at most 0.5/S. The encoded sum is
round(encodedWeight * normalizedLogLuminance * 128), where normalizedLogLuminance maps the
selected physical metering interval to (0,1). Count sums are bounded by M*S; encoded log sums
are bounded by 128*M*S <= 2^30, including a uniform image whose samples all hit one bin.
This budget applies to supported scene extents with S >= 1; larger unsupported populations
produce zero weights and the existing empty-meter fallback instead of overflowing.

Adjacent splitting rounds the upper count and assigns the remainder to the lower count;
it similarly rounds the upper log sum and assigns the total-sum remainder to the lower bin.
Each sample therefore retains its total encoded mass and sum, with per-bin count error at
most 1/S from the ideal split, excluding floating-point evaluation error.

Reduction decodes each group to floating point before merging. Its weighted log sum is
encodedSum * rangeSpan / 128 + encodedCount * rangeMinimum. The common scale cancels from
percentiles and means. Per-bin weighted-log sum rounding is at most rangeSpan/(128*S) per
sample before floating-point rounding; this is not a bound on a sparsely populated bin's mean.
Precision therefore depends on input resolution; reference checks
must account for this explicitly. Within-bin weighted means are retained, rather than replacing
samples by bin centres. Percentile boundary populations remain approximate, not an exact sort.

Two owned 32-byte SSBOs retain previous/current log2 gain at offset zero plus optional viewing
metadata. Float offsets are log2 gain, instantaneous scene EV100, metered log2 luminance, validity,
uncapped metering target, metering-target cap-hit flag and two reserved zeros. Existing
scalar exposure consumers continue reading log2 gain at offset zero. Successful dispatch submission and
restoration swap publication; failure never publishes the partially written candidate.
ShaderStorageBarrierBit separates histogram writes from reduction reads and reduction writes
from later fragment reads (including next-frame history reads). The prepared-resource engine
boundary restores program, sampler, UBO and SSBO bindings. Stable frames reuse the same
allocations and programs; there are no CPU luminance/EV readbacks or per-frame GPU allocations.

### Local exposure and the applied camera reference

The enabled-by-default `Postprocessing.LocalExposure.Enabled` operation evaluates the physical
spatial base B after the actually applied global gain E, in log2 multiplier stops. Its display
pivot P is log2(MiddleGray) + Compensation in automatic mode, or log2(MiddleGray) in manual
mode. Relative base contrast is B + E - P. The existing shadow/highlight compression and
local stop limits act on that contrast; scene gain is E + localStops before grading.
Bloom uses this correction at its source samples before filtering, rather than reapplying
the destination scene's local stops to the finished halo. Snapshot combined gain describes
the sampled scene correction, not a universal multiplier for all postprocess contributions.
Automatic compensation shifts both gain and pivot, preserving its authored effect when the
meter is uncapped. Manual gain is authored against fixed middle gray. The final display UBO
carries P in exposure.w; optional local display no longer borrows a second adaptation SSBO.

Global temporal adaptation remains the sole adapting camera process. There is no separately
adapted physical scene estimate or physical-luminance history dependency. At a global cap,
local correction therefore follows the applied exposure rather than a continuing uncapped
scene-average reference. For a fixed physical base and settings, a fixed global exposure
now produces a fixed local correction. The combined local/global gain may still exceed the
global gain limits; local bounds govern the additional display compression.

Automatic metering retains the optional (-24,40) log2-nit domain with local exposure enabled,
or (-16,40) without it. Instantaneous metered luminance, uncapped target and cap state remain
GPU-resident diagnostic metadata. Manual mode performs no histogram/adaptation dispatch and
allocates no camera history, even when local exposure is enabled. Its current-frame spatial
base and explicit camera gain suffice. Invalid spatial guidance produces neutral correction;
missing histogram statistics do not independently disable otherwise valid local display.

The spatial display operation and unchanged grading cache are described in
[PBR.Postprocessing.md](PBR.Postprocessing.md). No source-light or storage calibration changes.

Owned bloom, LightShafts, luma preparation and FinalDisplayShaderProgram consume the published
EV through generated typed read-only storage-block bindings. Manual draws select the captured
manual log-gain and do not read the optional buffer. The graphics baseline remains GLSL 330 with
GL_ARB_shader_storage_buffer_object and GL_ARB_shader_image_load_store explicitly required
for these consumers; it does not
imply GL 3.3 runtime compatibility. GpuSupport owns cached compute/SSBO/SPIR-V capabilities
and stage limits. Camera metering requires 64-lane histogram groups, 256-lane adaptation, 1,024 dispatch groups, 2,560 shared bytes,
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
mode, compensation, manual camera EV100, camera EV100 bounds, middle gray, percentile cutoffs, center weighting,
and brightening/darkening speeds. Automatic mode defaults on. Disable it with ManualEV100 and
compensation both zero to use one display unit per physical nit before the tone curve. Manual mode
submits no metering dispatches and retains no camera history even when local exposure is
enabled; its current spatial base and authored manual gain remain authoritative.

### Saved-setting compatibility

New or missing settings use ManualEV100=0 and MinEV100/MaxEV100=-2/20. The old ManualEV,
MinEV and MaxEV names identify multiplier stops, never camera EV100. Loading negates manual
stops and reverses/negates the validated old gain bounds. Missing legacy bounds use -20/+2;
reversed old bounds first collapse to the old minimum, preserving previous effective output.
An explicitly present new field wins independently of old keys, even if malformed: null,
non-numeric and non-finite new values use that field's current default. Finite snapshot values
are clamped to the supported camera domain. Current reversed bounds collapse to MinEV100.

The ordinary startup loader converts old identities before materializing missing ConfigLib
paths, removes those three old keys, and persists the normalized document. Direct settings
JSON deserialization follows the same rule. Subsequent serialization emits only new keys, so
conversion is idempotent without PhotometricVersion or another marker. Mixed documents retain
explicit current values. Unknown settings, custom effective gain, automatic opt-outs, local
opt-outs, compensation, middle gray, rates and percentiles remain authoritative.

This migration preserves the already-calibrated pipeline's output; it does not promise matching
screenshots from before physical light calibration. The debug snapshot reports applied camera
EV100, gain limits/stops, scene EV100 (NaN when metering is invalid), requested EV100 and cap
state. Applied camera EV100 is the uncompensated equivalent of the final global gain; it can
differ from the authored manual EV100 when compensation is nonzero. Local stops remain separate.

Engine light levels, materials, propagation and world saves remain unchanged. Existing shared
shader mappings supply source-derived torch lux, point candela and 32-nit authored emission;
third-party inputs using those same fields inherit these defaults. Arbitrary shader output still
requires the declared scene contract. No new light configuration or source discovery is added.

The compute implementation reads every scene texel once and classifies ceil(width/2) *
ceil(height/2) averaged samples per metered frame, performing up to four shared integer atomics per
valid sample. It writes and reduces 65,536 group/bin pairs, followed by two scans of 64 bins.
The existing two dispatches, scene sampler, binding restoration and buffer ownership remain.
No intermediate downsample allocation or additional pass is introduced.
Buffer payload is 524,288 histogram bytes plus two 32-byte history buffers (524,352 total), compared
with 520 image bytes previously; allocation granularity, executables and UBOs are excluded.
Fragment consumers read one shared EV float when automatic exposure is enabled.
These are algorithmic storage/work counts, not physical bandwidth or complete-frame GPU timings.

## Validation status

### Calibrated camera authoring

The EV100 migration's Release build and both shader catalogs pass (450 production and 528
fixture variants). The focused run passes 276 tests with zero failures or skips, including
independent saturation-equation/round-trip checks, legacy/current/mixed/malformed JSON,
startup persistence, custom opt-outs, bounds, compensation, temporal partitioning/reset,
snapshot diagnostics, storage scales, glare and both lighting-mode runtime routes.

Sixteen actual histogram/adaptation/final cases compare equivalent old/new settings across
automatic/manual exposure, local exposure on/off, 0.0001/5730 cd/m² and storage scales 1/0.125.
Independent camera arithmetic uses tolerances up to 4e-6 stops or relative gain; the integrated
metadata/gain checks allow 0.0003 stops, including histogram quantization. Final lookup output
allows 1.001/255 per channel. These are separate numerical and display-approximation tolerances.
Receipts: artifacts/EV100/build.log, focused.trx, tests.log and test-filter.txt.

No new user-run transition or local/global appearance coverage was obtained for this migration.
Earlier moonlit acceptance remains historical. No game was installed or launched, and no new
performance measurement is claimed; the change retains the existing sampling and pass layout.

### Applied-exposure local contrast

The applied-exposure reference supersedes the former independently adapted scene-average
pivot. A fixed terrain base from the recorded lantern comparison (-14.42059 log2 nits),
middle gray 0.18 and default 0.5 compression now produces combined gains of approximately
9.32259 stops at applied global gain 6.69853, and 9.97333 stops at applied gain 8. The
dark-only capped view therefore no longer loses gain in this controlled same-patch regression.
These are analytical/GPU fixture results using the recorded base, not new live measurements
of the same terrain in both views. Both the final shader and snapshot reuse the corrected helper.

Build and focused validation passed 133 distinct correctness cases across the final runs;
12 additional opt-in timing entries were inactive and are not counted as correctness checks.
The controlled manual-plus-local final-draw recheck passed all six lighting/AO combinations,
using an independently calculated uniform-input correction with no camera history allocated.
Coverage includes captured-scene monotonicity, fixed-cap invariance, compensation/middle-gray
policy, manual limits, instantaneous metadata, invalid guidance and actual final grading.
Receipts: artifacts/DenseCameraMetering/display-pivot-final-validation.log and
display-pivot-manual-final-validation.log; the superseded first-run assertions and successful
case accounting are retained in the accompanying validation receipt.

The change retains saved camera/local controls and physical source calibration. Uniform dark
scenes can receive more local lift than before; the earlier moonlit visual acceptance does
not qualify this changed display response. Live appearance remains user-run. Earlier physical
adaptation and old snapshot-field receipts below describe the superseded reference.

### On-demand exposure log snapshot

In the VGE debug viewer, Tools > Log Exposure Snapshot queues one record for the next
completed display frame. The client log entry is prefixed [VGE Exposure Snapshot]. It records
captured camera limits/compensation, requested and applied multiplier stops, target cap state,
instantaneous metered log2 luminance in nits, metering validity, the log-middle-gray pivot,
local controls and centre-pixel local/combined
gain. The centre probe uses the same final-display edge filter and local-exposure functions,
before generated glare and grading. UI pixels are excluded. Gain fields remain multiplier stops;
explicit cameraEV100, minEV100, maxEV100, sceneEV100 and requestedEV100 fields distinguish
the calibrated camera convention. Scene EV100 is unavailable when meteringValid is zero.

For comparison, let each view settle, aim at unlit terrain, and click once with the bright
lantern-lit area excluded and once with it included. Retain both timestamped entries and check
probe luminance as well as exposure, since moving the camera can select a different surface.
History/validity fields distinguish unavailable metering from a measured zero. Manual mode
without local exposure has no metering history. The action performs one small synchronous GPU
readback and may briefly stall that requested frame; there is no continuous logging, readback,
probe allocation or compute dispatch when the button has not been clicked.

Before the pivot correction, headless validation passed 11 snapshot cases and 48 related exposure/local-display cases.
The final incremental build rechecked all 11 snapshot cases successfully, including fixed
global gain with changing local lift, manual mode, invalid metadata, physical storage scales,
edge-filtered guidance and invariant log formatting. Receipts:
artifacts/DenseCameraMetering/snapshot-validation.log and snapshot-final-validation.log.
The menu action itself remains unverified in a live game; no game was launched or installed.

### Metering qualification

Adjacent-bin interpolation (2026-10-14) passed 89 correctness checks, plus nine inactive
opt-in timing entries in the focused run. Six separately executed full-resolution timing
cases passed. Independent double-precision references cover fractional populations, odd
footprints, physical storage scales and weighted moments. A near-endpoint/interior sweep
checks nonnegative adjacent populations, conserved mass/log moment and continuous changes
within integer-quantization tolerances. Existing adaptation, snapshot and lifecycle checks pass.
Receipt: artifacts/DenseCameraMetering/interpolated-focused-validation.log.

RTX 4090 synthetic component medians after interpolation were:

| Scene resolution | Uniform pair GPU (ms) | Varied weighted pair GPU (ms) |
| --- | ---: | ---: |
| 1280 x 720 | 0.0512 | 0.0512 |
| 1920 x 1080 | 0.0584 | 0.0573 |
| 3840 x 2160 | 0.0911 | 0.0891 |

The matched earlier single-bin fixture measured 0.0512/0.0563/0.0860 ms for uniform input.
These sequential device measurements do not establish exclusive GPU occupancy, whole-frame
performance or live visual acceptance. They qualify the extra histogram updates on this GPU.

Before adjacent-bin interpolation, parallel dense metering (2026-10-14) retained
half-resolution coverage with 1,024 partials and a 256-lane cooperative merge.
On the same RTX 4090 synthetic uniform-input fixture,
eight warm samples produced the following histogram/adaptation pair GPU medians:

| Scene resolution | Previous 36 groups (ms) | Selected parallel pair (ms) |
| --- | ---: | ---: |
| 1280 x 720 | 0.1065 | 0.0512 |
| 1920 x 1080 | 0.2202 | 0.0563 |
| 3840 x 2160 | 0.8397 | 0.0860 |

Varied, centre-weighted inputs measured 0.0512/0.0568/0.0860 ms at those resolutions.
Small 64x36 and 320x180 sources measured 0.0461 ms. These are synthetic component costs,
not exclusive GPU occupancy or whole-game frame costs. The 512 KiB retained scratch trades
additional storage for greater parallelism. A tested intermediate with 1,024 partials and
the former serial merge regressed 720p; that intermediate is not the selected implementation.
The shader-enabled build and all 86 selected cases passed: 77 correctness checks plus nine
executed opt-in timing cases. Coverage includes populated-to-invalid input replacement,
capability limits, odd edges, upper-range accumulation and scene/local-exposure lifecycle.
The selected merge, capability admission and correctness/timing receipt are recorded under
artifacts/DenseCameraMetering/parallel-merge-final-validation.log.

Initial dense downscaled metering qualification (2026-10-14): the shader-enabled build completed,
and 77 correctness checks passed, with three opt-in timing entries inactive in that run.
Coverage includes a one-pixel lamp
between former grid locations, independently averaged odd footprints, invalid footprint
rejection, centre weighting, resolution-dependent weight precision, upper-range accumulation,
physical storage scales, adaptation, local exposure and scene lifecycle routing in both lighting
modes. The manual-retirement fixture now explicitly disables local exposure when asserting
that no metering consumer remains. Receipt: artifacts/DenseCameraMetering/final-focused-validation.log.

The initial 36-group dense implementation passed three opt-in timing/correctness cases on
NVIDIA RTX 4090. Eight warm samples per
resolution used a uniform RGBA16F input concentrating every sample in one bin, stressing
shared atomic contention. Source upload, readback and compilation are outside timing.

| Scene resolution | Half-resolution samples | Pair GPU (ms) | Submission CPU (ms) |
| --- | ---: | ---: | ---: |
| 1280 x 720 | 230,400 | 0.1065 | 0.0772 |
| 1920 x 1080 | 518,400 | 0.2202 | 0.0751 |
| 3840 x 2160 | 2,073,600 | 0.8397 | 0.1188 |

These are synthetic histogram/adaptation component costs, not complete-frame performance,
exclusive GPU occupancy or live appearance acceptance. These are the baseline for the
subsequent workgroup-distribution optimization, not its selected cost. Timing receipt:
artifacts/DenseCameraMetering/performance-validation.log. No game launch or installation
was performed. The historical sparse-grid measurements below are not current dense costs.

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

Before dense sampling, the shader-enabled build and 109/109 focused numerical/GPU cases passed with no failures or
skips. Existing compiler/analyzer warnings remain. Receipt: artifacts/exposure-final-validation.log
and artifacts/CameraExposure/exposure-final.trx. An independent 21-case GPU recheck passed
without skips: artifacts/exposure-independent-validation.log and
artifacts/CameraExposure/exposure-independent.trx. Coverage includes independent uniform/weighted
CPU histogram statistics, all 64 bins against the retained test-only raster reference, invalid
samples, upper-range local-bin overflow bounds, percentile trimming, temporal frame partitioning,
manual/reset/empty history, buffer publication/disposal and binding restoration. Runtime cases
exercise standalone and LumOn modes, settings changes, resize, shader reload and world retirement;
bloom, shafts, luma and final read the typed buffer while retaining exactly-once display exposure.

Historical sparse-grid measurements on NVIDIA GeForce RTX 4090 used the same 2,304 grid samples,
weighted policy and scene inputs for both former algorithms. They do not measure the current
dense sampler. Eight warm alternating samples report
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
