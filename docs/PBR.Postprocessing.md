# Owned HDR postprocessing

VGE owns the complete scene postprocessing invocation. OwnedPostprocessHook replaces
ClientPlatformWindows.RenderPostprocessingEffects for a pending HDR scene; it never executes
the original scene pass after an owned failure. The engine's ScreenManager scheduling and
final presentation remain intact. Non-scene menu rendering outside HDR scene ownership keeps
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
4. RetainedPostprocessRenderer preserves SSAO receiver reconstruction and bilateral filtering,
   then prepares the engine-owned luma output for final FXAA (or copies scene RGBA if FXAA is off).
5. The existing final shader applies SSAO to scene RGB, adds owned glare, applies camera
   exposure and the shared tone curve once, then retains grading, vignettes and dithering.

All scene postprocess draws use typed VGE programs, GLSL 330, PSOs, render passes and one
restoring GraphicsCommandContext boundary. Camera metering has its own restoring boundary
before the effects. No original engine findbright, blur, god-ray, SSAO, bilateral or luma
shader is invoked by the replaced HDR pass. The retained SSAO math is derived from the
installed algorithm; its eleven-tap bilateral filter initializes both symmetric endpoints,
including the endpoint the original vertex shader left uninitialized.

The final-composition transpiler routes its existing bloom/god-ray setters to successfully
published owned images on the existing units 2 and 3. It does not replace the engine shader
object or rewrite its framebuffer table. Publication is atomic across effects and retained
operations; missing, stale or failed publication raises an error. Disabled effects publish
one persistent black image and release their effect-only storage. Camera manual mode is
independent and still uses the owned HDR pipeline.

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
images to prevent feedback. Shafts own one half-resolution RGBA16F image. Retained SSAO and
luma outputs are borrowed engine images behind private persistent FBO wrappers; engine
ownership and final consumers remain unchanged. Sampler contracts receive typed textures; external
engine handles are wrapped by BorrowedTexture without allocating or owning native storage.
Named input and output references are retained for each framebuffer publication; render
submissions do not look up texture handles. GpuResourceCollection retires private framebuffers,
attachments and borrowed wrappers while leaving engine storage alive.
The wrapper captures the existing target, dimensions, format and contiguous allocated mip
levels; shader contracts enforce the required sampler target. Screen publication, shader reload and world
exit retire PSOs before images/views. Stable frames allocate no GPU resources. Quality and
size changes recreate only affected storage.

Engine glare allocations remain because the non-scene menu path still consumes them. They
are no longer promoted to HDR or validated as scene requirements. Primary color and luma
remain required floating-point engine storage. Removing menu allocations requires separately
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

Focused GPU/runtime validation is in progress. Real-game halo appearance, shafts through
clouds/transparency, underwater transitions, temporal stability and representative GPU cost
still require user-run visual/performance acceptance. The implementation does not establish
native HDR monitor presentation.
