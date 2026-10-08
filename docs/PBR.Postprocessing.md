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

## Texture-array consolidation analysis

Source review: 2026-10-08. This is an allocation and draw-dependency analysis of production
VGE texture owners, not a migration or a new GPU performance measurement. The inventory follows
texture factories, specialized GpuTexture subclasses, framebuffer-owned images and existing
array allocations. Test-only allocations and external engine storage are excluded from owned totals.

**Recommendation:** first consolidate DirectLightingTargets into a three-layer RGBA16F array.
Its producer already writes all three outputs together and its consumer reads them together.
This changes the fully featured PBR composite from 17 sampler inputs to 15. Next consider the
G-buffer's three RGBA16F channels and the two RGBA32F water-transport channels, taking that
same composite to 13 and then 12. These are independent owner-local changes, not a global
screen-texture allocation. The numbers are expected interface reductions, not new linked-program
measurements; rebuild/link and numerical comparison remain implementation acceptance work.

### Inventory conventions

W/H are primary scene dimensions; P/Q are resolved screen-probe grid dimensions; A/B are
material atlas-page dimensions. All listed images are single-sample and have one allocated mip
unless explicitly stated. N means nearest filtering, L means linear; ordinary allocations clamp
at image edges. A typed sampler policy can override texture-object filtering. Sharing an array
saves one sampler only when a consumer can use one compatible sampler policy for the layers;
binding the same array twice with different sampler policies still costs two units.

Screen allocations follow their existing owner's resize/rebuild/reload/world-exit lifetime.
Persistent world/material allocations follow their generation/page/cache lifetimes instead.
Matching dimensions alone does not justify joining those lifetimes. No format conversion,
precision reduction, padding or mandatory allocation of disabled features is assumed below.

### Scene, atmosphere, liquid and postprocessing inventory

| Owner and source | Images and layout | Producer → consumers; lifetime and decision |
| --- | --- | --- |
| [DirectLightingTargets](../VanillaGraphicsExpanded/PBR/DirectLightingTargets.cs) | DirectDiffuse, DirectSpecular, Emissive: W×H RGBA16F, L | Direct-lighting MRT → ordinary and pre-overlay PBR composite. Shared lifetime in standalone and LumOn modes. **First recommended group: 3 layers.** Three attachments/outputs remain necessary. |
| [GBufferTextures](../VanillaGraphicsExpanded/GBuffer/GBufferTextures.cs) and [GBufferManager](../VanillaGraphicsExpanded/GBuffer/GBufferManager.cs) | Normal, Material, Environment: W×H RGBA16F, N; PatchId: W×H RGBA32UI, N | Terrain/receiver MRT → direct lighting, composite and LumOn/debug consumers. **Group the three float channels**; retain integer patch identity separately. Preserve primary attachment slots 4, 5, 7 and integer slot 6. Current injection uses Texture2D attachments and must become layer-aware. |
| [Receiver position](../VanillaGraphicsExpanded/GBuffer/GBufferManager.ReceiverPosition.cs) | Optional fallback position: W×H RGBA16F, N | Replaces unavailable engine position storage for receivers. It can physically match the float G-buffer array but is conditional; do not reserve an unconditional fourth layer or change engine attachment ownership. Revisit only after the three-channel migration. |
| [PBRCompositeRenderer](../VanillaGraphicsExpanded/PBR/PBRCompositeRenderer.cs) | compositeColorTex: W×H RGBA16F | Composite output → scene publication. Keep separate from direct/G-buffer/water inputs because the composite samples them while writing this image. No sampler reduction from merging an output with its inputs. |
| [SceneColorParticleTargets](../VanillaGraphicsExpanded/PBR/SceneColor/SceneColorParticleTargets.cs) | Radiance, VisibleRadiance: W×H RGBA16F; ReceiverDepth: W×H R32F; BeforeDepth/AfterDepth: W×H matching primary DepthComponent32 or DepthComponent32F; optional SsaoNormal/SsaoPosition: W×H RGBA16F. N | Particle capture → visibility resolve → later scene lighting. Radiance is read to produce VisibleRadiance: keep separate. Optional normal/position are co-produced and can share a two-layer array, but SSAO handoff/blits need layer-aware destinations and no proven high-pressure consumer benefit. Depth snapshots could share a depth array (two depth-only FBOs, not color MRT); modest resolve binding benefit only, with matching native format and snapshot lifetime. Keep depth-as-color ReceiverDepth separate. |
| [WaterVolumeRenderer](../VanillaGraphicsExpanded/PBR/Liquids/WaterVolumeRenderer.cs) | Optical depth and illumination/source: W×H RGBA32F, N | Signed additive boundary integration MRT → composite. **Recommended two-layer array**, retaining independent outputs, blend state and clears. Not RGBA16F: signed accumulation precision is intentional. |
| [WaterRefractionScene](../VanillaGraphicsExpanded/PBR/Liquids/WaterRefractionScene.cs) | SourceColor: W×H RGBA16F; SourceDepth: W×H R32F; optional reducedColor: ceil(W/2)×ceil(H/2) RGBA16F; reducedDepth: same reduced size RGBA32F. Sampling policies remain per consumer | Pre-overlay capture and final opaque publication have distinct instances; reduction → liquid refraction. Color/depth formats mismatch. Do not merge pre-overlay and final publications: final composite reads the former while writing the latter. compositeColor and final SourceColor are co-produced compatible outputs, but have different publication consumers/lifetimes and give no reduction to the 17-input composite. Keep separate. |
| [AtmosphereTextureSet](../VanillaGraphicsExpanded/PBR/Atmosphere/AtmosphereTextureSet.cs) | Sky: a×2b RGBA16F; aerial Radiance: a×2b×d RGBA16F 3D; Attenuation: a×b×d RGBA16F 3D. L, repeat azimuth/clamp other axes; disabled aerial uses reduced neutral dimensions | Atmosphere publication uploads → sky, direct/forward lighting, composite, liquids and LumOn. **Exclude from a mechanical 2D-array conversion**: 3D distance interpolation and packed angular lobes are part of lookup semantics, and the dimensions differ. Combining them needs a separate LUT-layout/interpolation redesign, not just new bindings. |
| [CameraExposureTargets](../VanillaGraphicsExpanded/PBR/CameraExposure/CameraExposureTargets.cs) | Histogram: 64×1 RG32F; two EV histories: 1×1 R32F, N | Meter → adapt(previous EV) → current EV/final and glare thresholds. Keep histogram and histories separate. Histories match physically but only one is sampled per adaptation draw, so one array adds feedback handling without sampler savings. Auto-exposure owns reset/cut/enable lifetime. |
| [BloomRenderer](../VanillaGraphicsExpanded/PBR/Postprocessing/BloomRenderer.cs) | RGBA16F down/up pyramids; start ceil half-size, clamp each dimension to 1; 3–6 levels; reconstruction outputs match corresponding finer levels | Extract/downsample → upsample/combine → final. Different scales cannot be different layers without padding; same-size down/up images have producer/consumer dependencies. **Do not migrate the outgoing algorithm.** Planned Gaussian filtering should retain separate horizontal/vertical working stores; consider mip chains as a separate layout choice, with explicit safe mip access. |
| [LightShaftRenderer](../VanillaGraphicsExpanded/PBR/Postprocessing/LightShaftRenderer.cs) | Two RGBA16F reduced ping-pong images per early/late instance; early output W×H RGBA16F; late output ceil(W/d)×ceil(H/d) RGBA16F, d=4 or 2 | Depth/radiance extraction → radial passes → early aerial occlusion or late final glare. Keep each ping-pong pair separate by default: each pass samples one while writing the other, and currently uses only one working-image sampler. Grouping them saves allocations, not sampler inputs, and adds feedback requirements. Early/late scratch reuse is a separate lifetime optimization, not array consolidation. |
| [RetainedPostprocessRenderer](../VanillaGraphicsExpanded/PBR/Postprocessing/RetainedPostprocessRenderer.cs), [PostprocessPipeline](../VanillaGraphicsExpanded/PBR/Postprocessing/PostprocessPipeline.cs) | Full-size RGBA16F luma/scene; enabled neutral AO 1×1 RGBA16F; persistent black glare 1×1 RGBA16F | Completed scene → luma → final. AO is a placeholder, black is a disabled-effect identity. Do not allocate full-size layers for neutral images. Planned horizon AO needs its own quality-sized visibility/filter/history/depth resources; do not lock its layout to this placeholder. |

The primary color/depth/glow, native SSAO attachments, terrain color atlases, shadow maps,
OIT engine images and native menu postprocess images remain **external storage**, even where
VGE promotes a format or borrows an attachment. They are not candidates for owner-local array
migration. BorrowedTexture and framebuffer discovery describe existing storage; they do not
make it VGE-owned. See [HDR/display contract](PBR.SharedDisplay.md),
[water refraction](PBR.WaterRefraction.md), [water transport](PBR.WaterMedium.md),
[atmosphere publication](PBR.Atmosphere.Publication.md) and
[aerial lookup](PBR.Atmosphere.AerialPerspective.md).

### LumOn and material inventory

| Owner and source | Images and layout | Producer → consumers; decision |
| --- | --- | --- |
| [LumOnTargets](../VanillaGraphicsExpanded/LumOn/LumOnTargets.cs): anchors | Position/normal: P×Q RGBA16F, N | Anchor MRT → tracing, importance mask, temporal/filter/gather/debug. **Two-layer candidate, 2→1 inputs where both are read.** Keep separate from SH coefficients: SH projection reads anchor position while writing coefficients. |
| LumOnTargets: trace scheduling | ProbeTraceMask: P×Q RG32F, N; ProbePisEnergy: P×Q R32F, N | Importance selection MRT → trace/diagnostics. Different formats; retain. |
| LumOnTargets: directional atlases | Trace/current/history/filtered radiance: 8P×8Q RGBA16F, N; corresponding four metadata images: same size RG32F, N | Trace → temporal with history → spatial filter → gather/projection; current/history swap roles. Same-format groups exist, but temporal/filter draws read and write members of the proposed group. **Defer**, retain ping-pong allocations. Radiance and metadata cannot share one array without changing representation. |
| LumOnTargets: SH9 | Seven coefficient images: P×Q RGBA16F, N | Project filtered atlas through seven MRT outputs → SH9 gather. **Strong seven-layer candidate: 7→1 coefficient samplers, saving six in gather.** Still seven output attachments. Applies to SH9 gather mode; no benefit to the directional-atlas gather branch. Existing owner allocates this family with the screen target set. |
| LumOnTargets: outputs | IndirectHalf: resolved half dimensions RGBA16F; IndirectFull, SurfaceAlbedo: W×H RGBA16F (albedo L, indirect default N); Velocity: W×H RGBA32F N; HZB: W×H R32F, 1+floor(log2(max(W,H))) mips, nearest mip sampling | Gather → upsample → composite; surface capture feeds GI; velocity/depth feed temporal/tracing. Keep separate across dependencies/resolutions. Do not merge IndirectFull into always-present direct lighting: standalone PBR must not allocate or require LumOn resources. HZB retains its hierarchy. |
| [LumOnPmjJitterTexture](../VanillaGraphicsExpanded/LumOn/LumOnPmjJitterTexture.cs) | CycleLength×1 RG16 normalized, N | CPU sequence upload → tracing; cycle/seed lifetime. Unique format/size; retain. |
| [World-probe resources](../VanillaGraphicsExpanded/LumOn/WorldProbes/Gpu/LumOnWorldProbeClipmapGpuResources.cs) | For resolution r, levels l, tile t: radiance r²t×rlt RGBA16F; visibility r²×rl RGBA16F; distance same scalar size RG16F; metadata RG32F; debug RGBA16 normalized. N | Scheduled probe updates/clear → tracing/gather/debug; generation lifetime. Radiance size differs and scalar formats differ. No lossless compatible group in the normal layout; do not pad/promote formats merely to consolidate. |
| [SurfaceAtlasTextures](../VanillaGraphicsExpanded/LumOn/Scene/SurfaceAtlasTextures.cs) | Existing arrays of physical width×height×layers: Depth R16F, Material RGBA8, Indirect/Direct/two Outgoing RGBA16F; N | Capture/relight compute → surface-hit lighting; outgoing publication double-buffered. Four radiance arrays match storage, but updates read published lighting while writing pending/direct/indirect data. **Defer regrouping**; image load/store access and barriers must be audited per dispatch, independently of framebuffer feedback rules. Any layer-bank layout multiplies layer count and must respect MaxArrayTextureLayers and physical-pool budgeting. |
| [Page tables](../VanillaGraphicsExpanded/LumOn/Scene/LumonScenePageTableGpuResources.cs) and [feedback owner](../VanillaGraphicsExpanded/LumOn/Scene/LumonSceneFeedbackUpdateRenderer.cs) | Page table and usage stamp: existing virtual-page-width×height×chunk-slots R32UI arrays, N; generation: chunk-slots×1 R32UI N | CPU residency uploads and compute stamp/compact → trace/feedback. Table/stamp could be separate layer banks; compaction reads both, saving one sampler there. Different clear/update/publication contracts and doubled layer count make this lower priority. Generation is a different shape. |
| [TraceGeometryGpuScene](../VanillaGraphicsExpanded/LumOn/Scene/Geometry/TraceGeometryGpuScene.cs) | Geometry/Legacy: resolution³ R32UI; Light: resolution³ RGBA8; Readiness: slots³ R8UI; Faces: 16384×1 RGBA32UI; Materials: 256×768 RGBA8; Surfaces: 256×256 RGBA32UI; LightColors: 64×1 RGBA16F; BlockLevels/SunLevels: 33×1 R16F. N | CPU scene publication → voxel/surface tracing and relighting. Retain 3D grids/addressing and differently shaped tables. **BlockLevels/SunLevels can become two layers**, saving one sampler in surface-lighting consumers that read both, with only 132 logical bytes total. Low priority, no composite saving. |
| [MaterialAtlasTextureStore](../VanillaGraphicsExpanded/PBR/Materials/MaterialAtlasTextureStore.cs) | Per-page material params and optional normal/depth: A×B RGBA16F, N | Build/upload → terrain/liquid/material consumers. Compatible two-layer group when both enabled; saves one sampler in consumers reading both with matching policy. Page rebuild, optional normal/depth and independently replaced params must become coherent layer publication. Keep engine color atlas external. Grouping pages only helps consumers accessing multiple pages in one draw; current page selection alone does not save a sampler. |
| [Displacement maps](../VanillaGraphicsExpanded/PBR/Materials/MaterialAtlasTextureStore.Displacement.cs), [water-medium maps](../VanillaGraphicsExpanded/PBR/Materials/MaterialAtlasTextureStore.WaterMedium.cs), [neutral relief](../VanillaGraphicsExpanded/PBR/Materials/TerrainReliefBindings.cs) | Per-page indices: A×B R32F N; record tables: count-dependent RGBA32F N; two neutral 1×1 RGBA32F images | CPU table build → tessellation/liquid consumers. Index maps physically match but optional populations and consumer sets differ: defer without a shared consumer benefit. Record dimensions differ; do not pad. Neutral objects are negligible, have different semantic contents and should not drive architecture. |
| [Material tile scratch](../VanillaGraphicsExpanded/PBR/Materials/MaterialAtlasNormalDepthGpuBuilder.TileResources.cs), [multigrid](../VanillaGraphicsExpanded/PBR/Materials/MaterialAtlasNormalDepthGpuBuilder.Multigrid.cs) | Tile-sized L/Base/D0/G1–G4/D/Div/H/Hn/Tmp R32F N; G is RG32F N. Per multigrid level h/hTmp/b/residual R32F N, progressively smaller | Bake derivatives/divergence/solve → normal-depth atlas publication. Compatible sizes within each level, but iterative solver reads/writes scratch members. Offline/build lifetime, no frame-composite benefit; retain ping-pong and exclude from initial migration. |

GpuTexture allocation subclasses and direct GL texture-generation sites were checked as a second
coverage pass. GpuBufferView/GpuBufferTexture and GpuTextureView are storage views, not additional
2D image allocations; they do not belong in an image-array packing total. Renderbuffers are not
sampled textures. General-purpose factories are counted through their production owners above.
See [lighting modes](PBR.LightingModes.md), [screen-probe core](LumOn.01-Core-Architecture.md),
[SH/gather](LumOn.06-Gather-Upsample.md), [surface lighting](LumOn.SurfaceCache.LightingContract.md),
[trace geometry](LumOn.TraceSceneGeometryContract.md) and [material ownership](MaterialAtlas.Architecture.md).

### Draw dependencies and sampler savings

| Proposed group | Producer attachment layout → later readers | Expected savings and storage |
| --- | --- | --- |
| Direct lighting | Output locations 0/1/2 attach array layers 0/1/2; input G-buffer remains separate. Composite reads array while writing composite/refraction targets | Composite 17→15; 3 texture objects→1; 24WH bytes unchanged (47.46 MiB at 1920×1080). No extra copy or fetch reduction. |
| G-buffer float channels | Primary slots 4/5/7 attach layers 0/1/2; keep 6 as RGBA32UI. Direct-lighting and composite draws target separate outputs | Another two fewer samplers in composite (15→13) and two in direct lighting where all three are consumed. 24WH bytes unchanged. Raw Texture2D attachment/binding consumers need migration, not ID aliases. |
| Water transport | Output 0/1 attach RGBA32F layers 0/1; composite later reads both | Another one fewer composite sampler (13→12). 32WH bytes unchanged (63.28 MiB at 1080p). Conditional water lifetime remains conditional. |
| SH9 coefficients | Seven output locations attach seven layers; projection reads directional atlas and separate anchors | Six fewer coefficient samplers in SH gather; 56PQ bytes unchanged. Output/draw-buffer count stays seven. No benefit in standalone mode or non-SH gather. |
| Probe anchors | Two anchor outputs attach two layers; trace/PIS/gather later read both | One fewer sampler per consumer using both; 16PQ bytes unchanged. Cannot append SH outputs to this same storage without a feedback strategy. |
| Material params + normal/depth | Upload/build named layers; consumers read only after publication | One fewer sampler where both inputs are active; 16AB bytes unchanged when enabled together. Reserving the optional layer when disabled wastes 8AB bytes per page. |
| Final bloom + shaft outputs | Separate final effect writes into distinct layers, then final composition samples one glare array | Conditional 2→1 glare inputs, final interface 5→4; **not** a fix for the earlier 17-input composite. Current bloom and shaft ceil-half dimensions match at native shaft quality ≥2, including odd sizes. Quality 1 differs. Await Gaussian layout; do not resample solely to force grouping. |

Counts refer to replacing distinct sampler uniforms with one sampler2DArray, not an array of
sampler2D uniforms. Renumber all remaining units densely: removing sampler uniforms but leaving
a binding at unit 16 does not establish a usable 16-unit fragment interface. Different shader
variants may optimize optional samplers away; count active linked inputs and validate unit indices
for standalone/LumOn, pre-overlay and all quality variants after implementation.

Safe recommended graph: terrain writes G-buffer array → direct lighting reads it and writes
lighting array → composite reads both plus water array and writes separate color/refraction
images. The active draw framebuffer must not expose sampled storage in an unsafe feedback
configuration. An attachment retained by an inactive FBO is harmless. Preserve all clear values,
output slots, indexed blending, integer output types and existing pass ordering.

A shared allocation does not remove layers' logical texel storage, samples, shader fetches or
MRT output bandwidth. It reduces object/binding count at consumers. CPU/GPU timing benefits are
unmeasured. First recommended groups need no staging copies: allocate arrays at the owning
rebuild boundary and render into their layers directly. Copying old textures into an array every
frame would defeat much of the benefit. Atomic replacement can transiently retain old+new storage
until the existing disposal boundary, as with other resource rebuilds.

### Existing abstraction support and actual gaps

- [Texture3D](../VanillaGraphicsExpanded/Rendering/Texture3D.cs) and
  [DynamicTexture3D](../VanillaGraphicsExpanded/Rendering/DynamicTexture3D.cs) already accept
  TextureTarget.Texture2DArray. Reuse those GpuTexture owners; no new borrowed wrapper,
  handle lookup, texture manager or catch-all postprocess resource class is needed.
- [GpuFramebufferAttachment.FromTexture](../VanillaGraphicsExpanded/Rendering/GpuFramebufferAttachment.cs)
  accepts mip and layer and calls FramebufferTextureLayer. GpuFramebuffer.Create accepts these
  attachments. Build one borrowed attachment per layer, owned backing array once through
  GpuResourceCollection. Existing MRT helpers taking DynamicTexture2D[] are convenience overloads,
  not a reason to add another framebuffer system. Non-layered attachments allow normal vertex/
  fragment shaders to write multiple layers through MRT; no geometry shader is required.
- ShaderTextureTarget.Texture2DArray, typed GpuTexture properties, generated sampler binding,
  state-cache targets, uploads and array-image bindings already exist in LumOn. Change the
  consumer contract, GLSL sampler type and layer coordinate together. Use explicit sampler
  policy and preserve texelFetch/filtering behavior. Existing properties typed DynamicTexture2D
  cannot describe an array and need their owning API adjusted.
- PSOs/render passes already express output formats, attachment slots and draw routing. Rebuild
  their target associations using existing lifecycle notifications. Selected layer attachments
  explicitly reject independent Resize: resize/recreate the backing array once and republish
  every layer attachment/FBO together. Do not call the old per-texture framebuffer Resize path
  on a collection of layers. Retire submissions before attachments before storage.
- [GpuTexture](../VanillaGraphicsExpanded/Rendering/GpuTexture.cs) currently allocates with
  TexImage2D/TexImage3D. [GpuTextureView](../VanillaGraphicsExpanded/Rendering/GpuTextureView.cs)
  exists but requires immutable storage. It derives from GpuResource, not GpuTexture;
  ShaderPreparedSubmission's typed sampler overloads accept GpuTexture, so views are not a
  drop-in typed sampler input. A view-based migration would need immutable allocation/lifetime
  support and a deliberate typed view-binding contract. Neither is needed for the first groups.
- GpuSupport already owns maximum texture size, array layers, color attachments, draw buffers
  and sampler capabilities. Validate through that owner. No TextureBarrier call was found in
  current rendering sources; do not quietly assume feedback synchronization is implemented.

Array storage/layer attachment and sampler2DArray are available within the GLSL 330/OpenGL 3.3
feature set, independently of VGE's other runtime requirements. Array layers share dimensions,
format and mip layout; they do not filter into adjacent layers. See the
[Khronos array specification](https://registry.khronos.org/OpenGL/extensions/EXT/EXT_texture_array.txt).
Texture views require immutable storage and OpenGL 4.3/ARB_texture_view; see
[ARB_texture_view](https://registry.khronos.org/OpenGL/extensions/ARB/ARB_texture_view.txt).

For ping-pong candidates, default to separate allocations. A restricted input view excluding
output layers can isolate subresources, but needs the storage/binding work above. Alternatively,
OpenGL 4.5/ARB_texture_barrier permits disjoint texel reads/writes and defines TextureBarrier
between dependent draws in feedback arrangements. It is not safe to infer that different array
layers alone suffice under older rules, nor does a generic MemoryBarrier replace TextureBarrier
for framebuffer feedback. Ordinary rendering to one object followed by sampling it while drawing
to another needs no new explicit barrier. Compute image writes retain their own access/barrier
requirements. See [ARB_texture_barrier](https://registry.khronos.org/OpenGL/extensions/ARB/ARB_texture_barrier.txt).

### Recommended migration order and acceptance

1. DirectLightingTargets, both composite variants and diagnostic readers. Use three layers,
   preserve three MRT outputs, compact sampler units, and compare all output channels in both
   standalone and LumOn modes. This alone addresses the reported 17-sampler composite.
2. Water optical/source pair and G-buffer float channels as independent owner-local changes.
   Water has the smaller interface change; G-buffer has broader raw engine attachment/debug
   consumers. Their combined endpoint is 12 composite inputs; neither is required before the
   first change can ship. Keep engine inputs and publication ownership intact.
3. SH9 coefficients, then anchors as separate arrays. Verify SH and directional gather choices,
   LumOn disabled/startup/switching, unchanged probe grids and per-output values.
4. Material pair only after measuring relevant consumer pressure; preserve optional normal/depth,
   page replacements and bake uploads. Small light-level tables are an optional later cleanup.
5. Revisit final glare grouping during Gaussian bloom design and AO grouping during horizon-AO
   design. Preserve native enable/quality independence and odd-resolution rounding. Do not expand
   quarter-resolution shafts, tiny neutral textures or AO histories merely to match bloom storage.
   Keep temporal/filter working sets separate unless a measured benefit justifies views/barriers.

Implementation validation should use existing typed program/GPU abstractions to check layer
routing, exact channel preservation, no feedback, active sampler counts/indices, settings changes,
odd dimensions, resize, reload, disposal and publication. Do not replace distinct per-layer
semantics with shared clears. Benchmark matched scenes before claiming faster rendering;
allocation/binding savings above are analytical. No build or GPU run is needed for this
source-only analysis, and no runtime texture migration is included in this completed subtask.
