# Owned HDR postprocessing

VGE owns the complete scene postprocessing invocation. OwnedPostprocessHook replaces
ClientPlatformWindows.RenderPostprocessingEffects for a pending HDR scene; it never executes
the original scene pass after an owned failure. OwnedFinalCompositionHook likewise replaces
RenderFinalComposition for that scene with FinalDisplayShaderProgram. The engine's ScreenManager
scheduling, overlay callbacks and presentation blit remain intact. Non-scene menu rendering outside HDR scene ownership keeps
its display-referred engine path, rather than being interpreted as an HDR scene.

Installed 1.22.7 IL confirms: transparent merge, AfterOIT, return from RenderToPrimary,
RenderPostprocessingEffects, stage-8 overlays, RenderFinalComposition, post-final overlays,
and presentation. Evidence is in artifacts/SceneHdrRuntime/installed-postprocess*.il.txt.
The replacement preserves the original exit target (primary), full primary viewport and
standard blending for the following overlay stage.

## Responsibilities and order

Before deferred composition, LightShaftOcclusionRenderer runs at Opaque order 8.75, after
opaque receivers and before direct lighting (9), water transport (10.5), and final PBR composition
(11). It publishes current-frame depth-distance occlusion for aerial in-scattering. Its Before
callback withdraws the previous frame, so forward draws preceding publication see neutral occlusion.

PostprocessPipeline coordinates separate algorithm owners:

1. CameraExposureRenderer meters the completed unexposed scene and publishes temporal EV.
2. BloomRenderer extracts and filters scene-linear HDR bloom.
3. LightShaftRenderer extracts and radially filters exposure-relative HDR light-shaft bloom.
4. RetainedPostprocessRenderer supplies a neutral AO placeholder and prepares owned RGBA16F
   scene/luminance output for antialiasing (or copies scene RGBA when disabled).
5. The owned final shader applies optional edge smoothing, then AO to scene RGB, adds owned glare, applies camera
   exposure and the shared tone curve once, then retains grading, vignettes and dithering.

All scene postprocess draws use typed VGE programs, GLSL 330, PSOs, render passes and one
restoring GraphicsCommandContext boundary. Camera metering and final composition each have
their own restoring boundary before and after the effects. No original engine findbright, blur, light-shaft, SSAO, bilateral or luma
shader is invoked by the replaced HDR pass. The copied SSAO implementation and its kernel
adapter have been removed. SSAO intentionally outputs visibility one until the planned
horizon-integrated algorithm is implemented. The unused bilateral entry point is removed;
filtering remains part of that later task. No temporary AO algorithm or filtering chain is being implemented or qualified.

Final composition binds typed owned scene/luma, bloom, shaft, AO and exposure inputs directly.
It writes only primary color through a private framebuffer borrowing the presentation image;
no sampled texture aliases that output. The engine framebuffer table and overlay/presentation
schedule are unchanged. Final grading, luminance-guided edge smoothing and screen effects are independently authored
in owned shader assets, followed by one final dither. Native controls are read, but the grading,
antialiasing and vignette appearance is not a pixel-equivalent reproduction of engine shaders.
The native FXAA switch selects this edge filter; the filter is not the FXAA algorithm. The engine final executable is not used for HDR scenes.

Publication is atomic across effects and retained operations; missing, stale or failed publication
raises an error. Final consumption is allowed once and the handoff ends scene publication even
on failure. Disabled glare publishes one persistent black image and releases effect storage;
disabled AO skips the placeholder submission and retires its storage. Manual camera exposure is independent.
SceneColorPostprocessBindingHook, PbrFinalDisplayPatches and the old exposure texture-unit scope
are removed. Copied display helpers and obsolete reference-patch fixtures have been removed.

## Original display implementation and deferred effects

No base-game shader implementation is copied into the owned final path. Gamma/brightness/contrast
controls drive an independently authored display transform, followed by luminance-based warm
tinting and procedural damage/frost/glitch treatments. The edge filter uses the owned perceptual
alpha metric and bounded neighboring samples. Bloom retains its independently authored pyramid
until the planned Gaussian refactor. Light shafts now use the independent radial algorithm below. AO is neutral
by explicit user direction, not a fallback to engine rendering.

## Bloom

Bloom starts at half resolution with normalized symmetric box filtering. Extraction uses
peak linear RGB and an exposure-relative soft threshold: the configured threshold is divided
by exp2(EV) to express it in unexposed scene units. The extracted radiance is scaled once by
bloom strength, then reduced through three to six levels. A normalized nine-tap tent filter
upsamples each level and mixes equally with its matching finer level. Constant fields retain
their value rather than accumulating gain with pyramid depth. Narrow and broad components
share the same exposure, with no premature display conversion.

The solar disk contributes through its actual composed radiance. HDR solar outGlow.r is
zero; it no longer drives a second display-derived bloom multiplier. Actual geometry,
liquids, transparent composition, clouds and late AfterOIT contributors are already present
in the metered/bloomed primary image. UI and the later overlay stages are excluded.

Bloom is added after scene SSAO; it is no longer divided by the legacy ambient-bloom mix
or used as an SSAO bypass factor. Its strength/threshold/knee/pyramid controls live in the
existing graphics configuration, and the game's bloom enable remains respected.

## Light shafts

The independently authored light-shaft implementation separates distance occlusion from HDR
shaft bloom. Early extraction reconstructs positive view depth from the current projection and
opaque depth, then smooths visibility over a 128-metre interval. Two or three normalized radial
passes increase their reach toward the projected atmospheric sun. Publication converts visibility
to an occlusion amount: zero is neutral. A full-resolution output gives shared aerial consumers
an unambiguous screen-coordinate mapping. Only aerial in-scattering is multiplied by visibility;
extinction, direct/indirect surface radiance, emission, ordinary bloom and underwater transport
are not darkened by this mask. The physical sky itself is not multiplied by a foreground mask.

After complete scene composition and camera metering, a separate extraction reads HDR scene RGB.
Rec.709 luminance determines the exposure-relative excess above the bloom threshold. One RGB
scale bounds exposed peak radiance before filtering, including the shaft strength. Storage stays
unexposed. The atmospheric sun supplies projection, daylight gating and normalized tint, without
multiplying scene radiance by solar irradiance again. Solar visibility from glow green, sky depth,
an aspect-correct source aperture and a smooth image-edge fade restrict the source. Four balanced
samples cover the reduced pixel footprint. The result is filtered with normalized radial passes
and published separately for additive HDR final composition. There is no unblurred solar source
added by this effect; ordinary bloom and directional shaft bloom are distinct bounded glare lobes,
not replacement solar lighting or compensation for missing bloom. Their configurable strengths
are artistic contributions, not an energy-conserving lens simulation.

The fully featured composite now has 17 active fragment samplers; shader linking requires
that capacity (the validation device exposes 32). GLSL source remains version 330, but this
exceeds the OpenGL 3.3 minimum of 16 fragment samplers. Unsupported linking is an explicit
readiness failure, not a legacy rendering fallback.

Native quality 1 uses quarter-resolution filtering, two passes and at most 16 taps per pass;
quality 2 uses half resolution, three passes and at most 32 taps; quality 3 uses half resolution,
three passes and at most 64 taps. The configured sample limit can further bound those counts.
Filter reaches are 0.12/0.36 or 0.04/0.12/0.36 of the vector toward the sun. UV radial interpolation
is aspect-independent; only circular source support requires aspect correction. Outside-screen
taps contribute zero bloom and clear visibility, preventing clamp streaks or artificial border
occluders. Behind-camera, below-horizon and underwater light publish neutral occlusion/zero bloom;
a 15-percent screen-edge margin fades the projected source continuously.

Occlusion uses opaque depth, so translucent geometry and clouds without depth writes do not
cast occlusion into aerial scattering. Shaft bloom uses the composed scene and visibility mask,
which can carry their attenuation. Offscreen occluders and volumetric shadowing remain outside
this screen-space approximation. Refraction samples the screen mask at its receiving pixel; it
is not a bent-ray volumetric shadow solution. No temporal history is allocated: the current pass
has no validated motion/reprojection contract. This explicit spatial-only path avoids stale
history on camera cuts, teleportation, resizing, reload and world transitions.

## Resources, settings and limits

Bloom owns half-resolution and progressively reduced RGBA16F images, plus separate upsample
images to prevent feedback. Light shafts own two reduced-resolution ping-pong images and one bloom output for the late path;
the early path owns two reduced-resolution ping-pong images and one full-resolution occlusion output. The AO placeholder owns one 1x1 RGBA16F target when enabled; there are no bilateral targets.
Luma owns full-resolution RGBA16F storage. Sized formats match the vec4 shader outputs,
removing dependence on unsized engine RGB AO storage. Sampler contracts receive typed textures; external
upstream engine handles are wrapped by BorrowedTexture without allocating or owning native storage.
Named input and output references are retained for each framebuffer publication; render
submissions do not look up texture handles. GpuResourceCollection retires private framebuffers,
attachments and borrowed wrappers while leaving engine storage alive.
The wrapper captures the existing target, dimensions, format and contiguous allocated mip
levels; shader contracts enforce the required sampler target. Screen publication, shader reload and world
exit retire PSOs before images/views. Stable frames allocate no GPU resources. Quality and
size changes recreate only affected storage.

Engine glare allocations remain because the non-scene menu path still consumes them. They
are no longer promoted to HDR or validated as scene requirements. Only primary scene color remains required floating-point engine storage; native luma and SSAO
allocations are neither promoted nor consumed by VGE scene postprocessing. Removing menu allocations requires separately
replacing that display-referred path, not deleting images beneath a remaining consumer.

The base game graphics settings exclusively control bloom, light shafts, SSAO quality and FXAA.
EnginePostprocessInputs reads bloom/fxaa booleans and godRays/ssaoQuality integers through
the client settings API, preserving the engine DoPostProcessingEffects gate.
VGE exposes no duplicate effect switches. The HDR Postprocessing group retains algorithm-specific
bloom strength/threshold/knee/levels and shaft strength/radiance limit/sample count.
Camera exposure controls are described in PBR.CameraExposure.md.

For W by H scene pixels, light-shaft RGBA16F storage costs approximately 8WH + 40WH/d² bytes,
where d is the native quality divisor (4 or 2), before dimension rounding. A bloom
pyramid with both reconstruction chains approaches 16WH/3 bytes (RGBA16F), with exact size
depending on level count and rounded dimensions. Bloom uses four fetches per reduced pixel
and ten per reconstructed pixel. Each shaft radial pass uses one packed fetch per selected tap;
early extraction uses depth, and late extraction uses four scene/depth/visibility/exposure samples.
These are logical storage/work counts, not measured driver allocation or physical bandwidth.

The independent final shader measured median GPU times of 0.0164 ms at 720p, 0.0338 ms at
1080p and 0.0573 ms at 1440p across five warm samples with uniform 1x1 inputs and edge
smoothing enabled. These synthetic timings do not represent a live frame or physical bandwidth.
Receipt: artifacts/SceneHdrRuntime/owned-final-independent.trx.

The independent final implementation builds successfully (zero errors; 102 existing warnings).
Focused validation passed 67 unique checks. The six runtime cases cover both LumOn modes and
native SSAO qualities 0/1/2 without engine final shaders or legacy intermediate outputs.
Receipts: artifacts/SceneHdrRuntime/owned-final-independent.trx and owned-final-runtime.trx;
build: owned-final-independent-build4.log. These checks cover implemented behavior and ownership,
not a temporary AO algorithm. Real-game halo appearance, shafts through
clouds/transparency, underwater transitions, temporal stability and representative GPU cost
still require user-run visual/performance acceptance. The implementation does not establish
native HDR monitor presentation.

Light-shaft algorithm and atmospheric integration validation passed 131 unique focused checks
across light-shafts.trx, light-shafts-corrected.trx and light-shafts-abstractions.trx
under artifacts/SceneHdrRuntime. The final abstraction-cleanup run passed 20/20 checks, including
all six lifecycle cases, with no failures or skips. Its build completed with zero errors and
102 existing warnings (light-shafts-abstractions-build.log). Aerial tests use typed shader inputs;
aerial, shaft and bloom submissions use PostprocessDraw and GraphicsCommandContext, without raw
uniform writes, raster mutation, draw calls or blanket cache invalidation.

The final PSO-path synthetic measurement for four late shaft passes at 320x180 and 16 radial taps,
using uniform 1x1 source textures, was 0.0297/0.0307/0.0307 ms minimum/median/maximum. Its three
RGBA16F targets contain 1,382,400 bytes. This measures neither the early full-resolution occlusion
path nor live-game frame cost or physical bandwidth. Receipt: light-shafts-abstractions.trx.
Live visual and representative performance acceptance remains deferred until the remaining
postprocessing work is finished, per user direction.
