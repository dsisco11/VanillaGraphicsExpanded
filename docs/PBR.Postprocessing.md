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

PostprocessPipeline coordinates separate algorithm owners:

1. CameraExposureRenderer meters the completed unexposed scene and publishes temporal EV.
2. BloomRenderer extracts and filters scene-linear HDR bloom.
3. GodRayRenderer integrates a bounded solar visibility mask.
4. RetainedPostprocessRenderer supplies a neutral AO placeholder and prepares owned RGBA16F
   scene/luminance output for antialiasing (or copies scene RGBA when disabled).
5. The owned final shader applies optional edge smoothing, then AO to scene RGB, adds owned glare, applies camera
   exposure and the shared tone curve once, then retains grading, vignettes and dithering.

All scene postprocess draws use typed VGE programs, GLSL 330, PSOs, render passes and one
restoring GraphicsCommandContext boundary. Camera metering and final composition each have
their own restoring boundary before and after the effects. No original engine findbright, blur, god-ray, SSAO, bilateral or luma
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
alpha metric and bounded neighboring samples. Existing bloom and shaft shaders were independently
authored and remain until their planned Gaussian and radial-occlusion refactors. AO is neutral
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

## Solar shafts

Solar shafts are authored screen-space glare, not atmospheric multiple scattering or Mie
scattering. The effect never samples or radially accumulates scene RGB. It integrates the
composed glow attachment's green visibility channel along a ray toward the atmosphere's
projected sun, using 16, 32 or 64 samples at half resolution. The HDR solar producer writes
visibility one with disk coverage; opaque depth blocks samples and the existing attachment
blend carries cloud/transparency attenuation. This remains an approximation dependent on
contributors honoring the glow/visibility attachment contract.

The source color is the atmosphere owner's attenuated solar lighting scaled by shaft strength
and limited by a configured **linear radiance** ceiling using one RGB-preserving scale.
Normalized exponential sample weights prevent sample-count-dependent intensity. The effect
cannot reproduce the old unbounded HDR radial-sum disk. It adds no central unblurred source
term, does not use an sRGB suppression curve, and does not compensate for missing bloom.

Behind-camera sunlight produces zero. The source fades over a 15% screen-edge margin;
out-of-image taps contribute zero without edge-clamped streaks. A horizon fade and published
solar intensity handle day/night transitions. Underwater views suppress this screen-space
shaft effect; volumetric underwater transport remains separately owned. Visibility and solar
attenuation carry weather effects without feeding generated glare back into exposure.

## Resources, settings and limits

Bloom owns half-resolution and progressively reduced RGBA16F images, plus separate upsample
images to prevent feedback. Shafts own one half-resolution RGBA16F image. The AO placeholder owns one 1x1 RGBA16F target when enabled; there are no bilateral targets.
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

The base game graphics settings exclusively control bloom, god rays, SSAO quality and FXAA.
EnginePostprocessInputs reads bloom/fxaa booleans and godRays/ssaoQuality integers through
the client settings API, preserving the engine DoPostProcessingEffects gate.
VGE exposes no duplicate effect switches. The HDR Postprocessing group retains algorithm-specific
bloom strength/threshold/knee/levels and shaft strength/radiance limit/sample count.
Camera exposure controls are described in PBR.CameraExposure.md.

For W by H scene pixels, half-resolution shaft storage costs approximately 2WH bytes. A bloom
pyramid with both reconstruction chains approaches 16WH/3 bytes (RGBA16F), with exact size
depending on level count and rounded dimensions. Bloom uses four fetches per reduced pixel
and ten per reconstructed pixel; shafts use two texture fetches per selected radial sample.
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
