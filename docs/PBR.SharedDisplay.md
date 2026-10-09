# Shared HDR scene and display conversion

The scene and camera exposure operate in floating-point, scene-linear HDR. SDR describes
only the current final presentation target: the exposed HDR scene is tone-mapped and
sRGB-encoded for display. This is separate from native HDR monitor presentation.

`includes/pbr_color.glsl` owns the display policy for atmospheric sky, the solar
disk, deferred PBR surfaces and forward PBR surfaces. Both LumOn and standalone
lighting supply unexposed scene-linear RGB. The shared curve retains unit calibration
as `VGE_DISPLAY_EXPOSURE`; dynamic camera exposure is applied at final display as
described in [PBR.CameraExposure.md](PBR.CameraExposure.md). There is no sky-only or sun-only exposure scale.
Atmospheric extinction, disk size and reflectance remain lighting inputs.

The operator clamps negative radiance, applies exposure, then divides all channels
by `1 + max(R, G, B)`. Exact linear-to-sRGB encoding follows. One denominator
preserves linear RGB ratios through the highlight shoulder without channel
clipping. The old per-channel shoulders pushed bright colored inputs toward white.
Neutral inputs retain their previous response; near black the mapping approaches
linear. This is a stable chromaticity-preserving SDR operator. Camera exposure adapts
separately; native HDR presentation remains outside its contract.

## Owned scene postprocessing

[PBR.Postprocessing.md](PBR.Postprocessing.md) defines the VGE-owned replacement for the complete
HDR scene postprocess pass: camera exposure, bloom, solar shafts,
luma preparation and final composition. Engine final/glare/luma/SSAO shaders and intermediate
allocations described in historical receipts below are no longer scene dependencies. The engine
overlay scheduling and presentation blit remain unchanged. Horizon-integrated ambient occlusion
runs before deferred composition and attenuates only ambient/indirect light; it is not a final
whole-scene multiplier. Its native settings, spatial filter and lifecycle are documented there.

## Runtime scene handoff

VGE owns the scene color convention: scene rendering is always scene-linear HDR.
SceneColorPipeline prepares floating-point scene/postprocess targets, the final display
program, owned sky, direct lighting, composite and particle separation at Before order 1000.
Missing mandatory dependencies raise rendering errors; they never select a legacy scene
pipeline. Shader registry contents do not enable or disable HDR.

At client startup, before VGE borrows engine attachments, SceneColorPipeline.InitializeStorage
validates the existing table. The engine creates its menu buffers before mod patches load;
when that table is not HDR, VGE calls the engine RebuildFrameBuffers owner and validates
the published replacement. This applies the allocation patch while preserving engine
publication, deletion and dependent-resource notifications. Already valid HDR storage is
retained; rendering does not repeatedly rebuild incompatible targets.
The startup regression exercises the installed rebuild publication/deletion body with real
textures, including normalized pre-mod storage, repeated initialization and suppressed or
incompatible replacement. The scene-color and framebuffer-rebuild suites passed 90/90
tests with no skips; the shader-enabled build passed with no errors. Receipts:
artifacts/SceneHdrRuntime/startup-storage-build.log and startup-storage-tests.trx.

Storage validation imports actual texture formats and dimensions once per publication.
The engine leaves some postprocess dimensions unset (notably BlurVerticalLowRes), so
zero postprocess metadata does not invalidate allocated HDR storage. Positive published
dimensions must match the image; primary scene dimensions remain mandatory. Failures
identify the target and its missing attachment, incompatible format or size mismatch.
The installed-engine attachment regression executes setupAttachment with zero wrapper
dimensions, verifies the native 32x32 image, accepts RGBA16F and rejects RGBA8. The
scene-color suite passed 83/83 tests with no skips; the shader-enabled build had no errors.
Receipts: artifacts/SceneHdrRuntime/allocation-metadata-build.log and
artifacts/SceneHdrRuntime/allocation-metadata-tests.trx.

| Owner | Scene behavior |
| --- | --- |
| Owned sky and solar disk | Unexposed radiance, no local dither |
| Deferred terrain/entities | Linear lighting/transport and primary handoff |
| Forward, OIT, held items and owned liquids | Linear lighting and transport before blending |
| Authored engine effects and engine liquid adapter | Decode authored RGB before blending/integration |
| Cube particles | Isolated radiance/depth capture; compose after material lighting |
| Bloom and luma | Float intermediates; perceptual luma retains linear RGB |
| Light shafts | Owned bounded solar-visibility glare in scene-linear RGB |
| Owned final composition | Original edge smoothing, additive glare, one display conversion, original grading/vignettes and final dither |

Surface shaders use vge_pbrRoute as their single selector: zero retains offscreen/UI
shading, one captures material data, and two emits forward HDR radiance. The atmospheric
solar branch in standard derives its color convention from that same route. These linked
surface programs have no vge_sceneLinear uniform.

Deferred material capture preserves the engine coverage/discard decisions, then writes
alpha 1 for surviving opaque fragments. Material albedo replaces the background; it must
not blend with HDR sky or solar radiance when inherited alpha is slightly below one.
In the sun-through-terrain capture, texture alpha was exactly one but the interpolated
vertex alpha was one float step below one; the RGBA16F blended result retained solar radiance.
Forward/OIT output keeps its authored alpha. Overriding only opaque capture alpha removed
the solar mark while terrain depth and the late solar query were already correct.

PbrDrawRouteHook assigns the surface route on each binding, including nested offscreen
reuse. Programs without that route use the separate scene-color binding for authored
engine effects. Both owners require matching primary/OIT targets in Opaque, OIT or AfterOIT;
offscreen/UI calls remain display-referred. Owned scene programs supply HDR through their
typed inputs. There is no additional global shader-use patch.

Scene postprocessing and final composition are replaced at their existing engine invocation
boundaries by OwnedPostprocessHook and OwnedFinalCompositionHook. SceneColorPipeline requires
the owned final executable; engine final/colorgrade/luma/light-shaft programs are untouched for
non-scene consumers. HasSceneInput tracks pending scene processing, not HDR readiness.
The final handoff consumes that input even on failure and rejects missing effect publication.
All intermediate targets are VGE-owned; only upstream scene inputs and the primary presentation
destination are borrowed. Framebuffer publication/reload retires the owned intermediates and
borrowed references coherently. The original engine postprocess binding transpiler and final
shader patches are no longer registered or present in production. Copied display helpers and
SSAO/filter algorithms were removed; native controls now drive original display code, with
horizon-integrated AO applied to deferred ambient/indirect light before atmospheric transport. No native visual-equivalence claim is made.

Third-party scene contributors must honor the HDR target contract. Merely registering an
unclassified program does not affect VGE ownership, including programs used only for UI.
The registry-wide compatibility gate has been removed. Missing required patched color
bindings produce errors instead of silently writing or interpreting display RGB. Arbitrary
third-party shader output is not automatically converted or guaranteed compatible.

The engine light-shaft radial blur is an authored glare effect calibrated for bounded display
samples. VGE supplies that metric per sample, retains the engine suppression curve, and
decodes the generated contribution before adding it to the HDR scene. The scene and bloom
inputs retain their original radiance. Applying only a perceptual suppression metric to an
unbounded HDR ray sum caused the solar footprint to become a large saturated disk.

The broken-sun capture isolated that defect to the light-shaft pass: replacing only its
fragment shader reduced peak ray RGB from 737 to 0.592 and removed the oversized disk.
The corrected primary is saved at artifacts/BrokenSun/capture-primary-corrected.png.
Focused postprocess/runtime/sun tests passed 96/96 with no skips, including spatial
comparison against the installed legacy glare algorithm and reproduction of the old HDR
accumulator failure. Receipts: artifacts/SceneHdrRuntime/godray-domain-fixed-tests.trx
and artifacts/BrokenSun/godray-corrected-result.json. Capture replay does not replace
user-run visual acceptance. Refraction was subsequently accepted as resolved after controlled
capture replay; the completion note in PBR.BaselineShading.todo records those receipts.

Bloom blur spacing is measured in destination pixels using derivatives of the center UV.
The installed engine supplies full-window frameSize to every blur pass, including its
half- and quarter-resolution targets; retaining those offsets leaves a tight solar halo.
VGE retains the Gaussian weights and derives all 17 coordinates from the center, including
the final coordinate the installed vertex shader leaves unset. Using destination spacing
keeps horizontal and vertical spread equal when a pass also downsamples its input.
The no-sun-bloom capture confirmed that extraction and final bloom composition were active;
the change widens their footprint rather than increasing bloom intensity.
Capture replay retained a nearly circular halo (18x17 quarter-resolution texels at
10% of peak, versus 13x13 previously). The shader-enabled build and 98/98 focused tests
passed, including rectangular half/quarter targets checked against all 17 Gaussian taps
and HDR energy conservation. Receipts: artifacts/SceneHdrRuntime/bloom-destination-tests.trx
and artifacts/NoSunBloom/blur-destination-ab.json. User-run appearance remains to be confirmed.

## Display and validation limits

The SDR operator remains unchanged; dynamic camera exposure now precedes it at final display.
See PBR.CameraExposure.md for metering, history, controls and validation. Alpha, revealage, depth, glow
and SSAO metadata are not color-transfer inputs. Sky spatial perception effects stay at
their existing location; authored underwater/night-vision tints are decoded on the linear
route. Native HDR monitor presentation remains separate work.

The runtime GPU fixture prepares the actual producer owners, renders the owned sky above
one, preserves it through direct/composite handoff, and draws the installed final shader
against an independent single-conversion reference. It also reads actual bound uniforms
across scene/offscreen reuse, checks missing-final errors without changing scene output,
ignores unrelated shader registrations, and checks final/reload/world input lifetime. Installed engine methods accept the Harmony binding patch.
Separate shader/storage/particle tests cover blends, effects and metadata. These fixtures
do not execute a complete native game frame or establish live appearance, compatibility
with every mod, or GPU cost. The HDR migration was later marked complete by user direction
with the deferrals recorded in PBR.BaselineShading.todo; camera exposure has its own acceptance.

Mandatory-HDR validation passed **337/337 tests with no skips**, including runtime ownership
and failure cases, installed Harmony integration, scene storage/particle paths, sky/forward/
final shaders, lighting-mode lifecycle, water capture, transparency and refraction. The Debug
build passed with zero errors. Receipts: artifacts/SceneHdrRuntime/mandatory-hdr-checked-build.log,
mandatory-hdr-owner.trx and mandatory-hdr-regression.trx. The three-case owner run is included
in the 337 cases. This replaces the earlier fallback-specific receipt; eight obsolete registry
classification cases were removed and five direct-renderer cases added. No game was launched.

Unified surface routing validation passed **341/341 tests with no skips**, including the
surface route transitions, linked installed shaders without a duplicate scene-color uniform,
forward HDR radiance and solar rendering. The focused 90-case run is included in that total.
The shader-enabled Debug build passed with zero errors and existing warnings. Receipts:
artifacts/SceneHdrRuntime/surface-route-build.log, surface-route-targeted.trx and
surface-route-regression.trx. These checks do not establish in-game appearance.

The display operator itself adds no textures, buffers, draw calls or LUT work. The shoulder
replaces a vector denominator with two scalar maximum operations and a common
denominator. No GPU timing improvement is claimed. Tests cover intensity ranges,
RGB ratios, alpha, sky bypass and real sky/solar display helpers. In-game
readability, twilight appearance and zenith banding still require user observation.
The SDR dithering implementation and remaining visual checks are documented in
PBR.OutputDithering.md.
