# Water refraction

`Water Refraction` is a persisted ConfigLib graphics option at `WaterRefractionEnabled`. It defaults to false. The existing configuration loader supplies the missing leaf in older settings documents. Each opaque/liquid invocation reads the current setting; disabling withdraws publication, retires snapshot storage and skips ray traversal. LumOn changes do not introduce a separate resource dependency.

## Opaque publication and resource ownership

`PBRCompositeRenderer` remains at Opaque order 11, after direct/indirect lighting and water-boundary capture and before OIT. With refraction enabled, its existing composite draw writes three attachments: the ordinary transported composite, unattenuated scene-linear opaque radiance in RGBA16F, and matching hardware depth in R32F. The extra color/depth outputs come from the same invocation and projection. Radiance is captured before water absorption, in-scattering, atmospheric transport and display conversion. Its alpha marks physical opaque receivers; sky and first-person depth proxies are invalid.

`WaterRefractionScene` owns the two extra textures and the MRT framebuffer, borrowing the existing composite texture. It publishes only after successful composition and display resolve. The next opaque invocation invalidates publication before checking readiness. Resize, attachment replacement, leave-world and disposal retire both images together. Shader reload reuses the existing shader library and writes a new source on the next successful opaque invocation. Liquid rendering never samples its active OIT attachment. The order-11 MRT publication needs no extra copy or fullscreen draw; the pre-overlay capture below adds separate work. The optional MRT adds 12 bytes of storage and nominal output writes per pixel, approximately 23.7 MiB at 1920 × 1080. This is a format-derived lower bound, not a bandwidth or GPU timing measurement.

The installed engine submits local first-person hands and held items inside `EntityPlayerShapeRenderer.DoRender3DOpaque`, called by `SystemRenderEntities` at Opaque order 0.4. Its hand projection and visibility depth therefore precede the order-11 publication. Marking these pixels invalid cannot recover the world color or depth they already replaced. With refraction enabled, the void prefix `WaterRefractionCaptureHook` observes the local `RenderMode.FirstPerson` non-shadow opaque invocation before its projection change. `WaterRefractionCapture` evaluates the existing direct-lighting and composite algorithms into isolated targets, retaining unattenuated world radiance and matching world depth. The original engine method then executes in its original order, with its original arguments. There is no skipped, queued or replayed engine draw.

At the ordinary order-11 composite, only refraction outputs at negative first-person normal markers use that retained pair; other pixels retain final opaque scene coverage. Primary color, depth and first-person shading follow their existing paths. Capture reuses `GlStateCache.BindFramebufferScope` to restore independent read/draw framebuffer bindings. The existing legacy fixed-function scope optionally preserves viewport for fullscreen draws that change it. Each draw uses the existing `GpuProgram.UseScope` to restore shader ownership. The binding scope adopts actual driver bindings without invalidating unrelated cached state. `GpuFramebufferBlitter` owns reusable scratch FBOs borrowing color attachment zero, so original read/draw routing is never modified. The blitter is constructed with its source and destination and configures scratch attachments once. It subscribes to `GpuFramebuffer.AttachmentsChanged`, which fires after attachment updates, completed resize and retirement; notifications mark its setup dirty for the next copy. Ordinary copies perform no attachment queries. The caller retains and disposes the blitter with its target allocation; disposal unsubscribes both targets. `GpuFramebuffer` owns no blit-operation resources; scratch FBOs do not own borrowed textures or renderbuffers. LumOn surface-albedo capture owns its blitter through `LumOnBufferManager`. `GBufferManager.PrimaryFramebuffer` is a persistent non-owning representation of the augmented engine primary FBO; G-buffer setup refreshes it and notifies dependents after the existing window-rebuild callback, including equal-size rebuilds. Depth/stencil blits bind the original targets without changing color routing. Default-framebuffer blits retain the existing default-buffer selection. Blits never change or capture viewport. Fixed-function state uses the existing scope and pipeline descriptions. The direct-lighting and composite renderers select owned or borrowed framebuffer targets through framebuffer APIs rather than issuing raw GL calls. Frame start invalidates previous publication. Resize disposes the final refraction pair, invalidates the borrowed pre-overlay publication, and resizes the composite scratch in place; the next capture rebuilds mismatched isolated storage. Disable and world/disposal boundaries reclaim isolated storage. Composite allocation replacement, failure, world leave and disposal share one owned-resource cleanup path. Immersive bodies, remote players and shadows retain their original behavior and do not trigger this capture.

The early capture adds one direct-lighting draw and one composite draw, three RGBA16F lighting targets and an RGBA16F/R32F radiance/depth pair (36 additional bytes per pixel). The composite scratch is shared with its existing owner. Current-frame LumOn gather has not run at the capture boundary, so captured pixels use direct lighting, emission and the existing standalone environment response rather than stale screen-space GI. Final unmasked pixels still use the selected PBR mode. World geometry drawn later that was occluded by the overlay's depth cannot be recovered from this snapshot; it records actual coverage at the capture boundary. This remains a screen-space limitation, not permission to reorder base-game renderers.

## Optics and traversal

The liquid shader uses its continuous animated water normal and the existing water IOR of 1.333. This is the current water material optical model, not a new configurable IOR property. Air entry uses an eta ratio of 1/1.333; underwater exit uses 1.333. Existing dielectric Fresnel handles total internal reflection. Reflection keeps the existing direct/environment response; scene reflections remain a separate task.

Tracing starts at the displaced interface in view space. Thirty-two quadratically spaced samples cover at most 32 metres, followed by five bisection samples at the first depth crossing. The trace rejects offscreen projections, a two-pixel edge margin, invalid/nonfinite depth, sky, unsupported receiver metadata, foreground samples and adjacent foreground pixels. Foreground eligibility uses reconstructed receiver positions against the oriented local interface plane, rather than comparing receiver depth with surface camera Z. A refracted direction is not rejected solely for pointing toward the camera in view Z. A crossing must finish within 0.15 metres of the represented opaque depth. Exhaustion or unknown coverage retains the existing straight-through OIT response; no missing geometry is declared a hit. Nearest sampling prevents color filtering across foreground boundaries. This conservative screen-space method can miss thin geometry and cannot prove hidden topology or reconstruct offscreen backgrounds. It has no temporal history.

Accepted hits fade toward straight-through transmission near either the interface pixel or receiver's screen boundary, near the 32-metre traversal limit, and as crossing residual approaches its rejection limit. The screen fade begins two pixels from the edge and spans up to 48 additional pixels (eight percent of the smaller dimension, at least two pixels). It replaces the original binary accepted-hit/background-replacement switch. This reduces edge seams without inventing offscreen geometry; missing opaque coverage can still limit refraction as the camera moves.

Accepted above-water paths apply the authored water absorption and constant-source in-scattering over the refracted interface-to-receiver length. Atmospheric transport applies only on the camera-to-interface air segment. Underwater exits apply atmospheric transport on the outgoing air segment and medium transport on the camera-to-interface segment. The existing engine camera classification does not follow animated water contact; that belongs to the waterline task. The local lighting and homogeneous medium approximations are unchanged from [water transport](PBR.WaterMedium.md).

## Shared receiver and optical evaluation

`liquids/transport.glsl` defines `VgeWaterReceiver`: validity, sampling provenance, unattenuated
linear radiance, view-space position and refracted direction, submerged length in metres, and
confidence. The current sampler reports only unavailable or ray-traced results. Confidence is
independent of validity; later sampling methods must report their own provenance explicitly.
`liquids/refraction.glsl` selects that receiver without evaluating lighting or display conversion.

For accepted above-water rays, scattering compares the incoming photon direction (`-L`) with
`transpose(mat3(modelViewMatrix)) * -refractedDirectionVS`. The trace travels from camera toward
the receiver; outgoing scattered photons travel in reverse. For an underwater exit, outgoing
water photons instead point from the interface toward the camera (`-surfaceVS` transformed to
world space). The refracted segment is air. Solar and point-light scattering share this rule;
isotropic environment lighting is unchanged. Unavailable/disabled refraction retains the
straight-through source direction.

`VgeWaterTransport` evaluates RGB extinction and integrated source radiance independently of
receiver selection. Accepted paths consume the unattenuated snapshot, so opaque water-volume
attenuation is not applied twice. The straight-through path retains its existing `WasComposed`
guard. `VgeWaterCompose` combines linear premultiplied contributions and coverage before the
output adapter. HDR output keeps radiance above one; legacy output applies the shared display
operator and dither once to the resulting straight color. This fixes the previous interpolation
of separately tone-mapped contributions without activating full-scene HDR.

The controlling contracts are the receiver/provenance and composition requirements in
`PBR.WaterRefraction.todo`, the units and photon-direction convention in `PBR.WaterMedium.md`,
and the six-target OIT/engine ownership contract in `PBR.Liquids.md`. Display adaptation follows
`PBR.MaterialColorAndDisplay.md`, `PBR.SharedDisplay.md` and `PBR.OutputDithering.md`.
Focused validation must cover production liquid output and independent transport/direction
references; existing traversal diagnostics retain their original rejection and confidence cases.

Fresh build and focused validation passed 40/40 tests with zero skips in
`artifacts/PbrColor/water-transport-integration-tests.log`. The production liquid cases cover
HDR and legacy output, highlights above one, Snell-selected receivers, linear confidence blending,
TIR, disabled/unavailable refraction, camera rotations and nonzero solar/point-light scattering.
The directional cases independently predict Snell, Henyey-Greenstein and Beer-Lambert results
per RGB channel. A typed precompiled helper fixture checks entry/exit photon directions and
world transforms, colored transport and confidence endpoints. The 12 receiver diagnostic cases
now use a precompiled fixture importing production traversal, preserving the original rejection,
sample-count and confidence checks. No runtime compilation of VGE-owned GLSL is needed.
These receipts establish the scoped water contract, not whole-scene HDR activation or live visuals.
The separate regression run passed 55/55 with zero skips in
`artifacts/PbrColor/water-transport-regressions.log`: liquid transparency/sun/program behavior,
signed water boundaries and capture, RGB water-volume composition in both lighting modes,
refraction publication/overlay handling and resource lifecycle. Both runs used fresh builds.

## OIT composition and limitations

A fully trusted refracted source includes the background, medium transport and Fresnel-weighted interface response. It emits opacity one, causing overall multiplicative OIT revealage to become zero, so the engine compositor does not add the original background again. During fallback transitions, blend premultiplied scene-linear radiance and opacity together: `alpha = mix(fallbackAlpha, 1, confidence)` and `source = mix(fallbackColor * fallbackAlpha, refractedColor, confidence)`. The straight linear color is `source / alpha`; the selected HDR/legacy output adapter runs only after this blend; the original background contributes only `(1-confidence)*(1-fallbackAlpha)`. This keeps the complete result continuous without blending the original background twice. The bucket weights and six output attachments remain unchanged. Failed traces retain the existing scalar straight-through transmission and already-composed opaque water transport. Flow animation, shadows, lava/body lighting, emission, local fog spheres and preview transparency continue through the liquid path.

The snapshot represents opaque receivers only. Accepted paths approximate one water interval; other water boundaries, overlapping liquids and transparent objects are absent from it. Multiple accepted liquid layers retain engine bucket color averaging, rather than ordered optical transport. A transparent object behind water can contribute separately through OIT and is not refracted by this algorithm. Preview transparency deliberately mixes the completed liquid result with the original background. These limitations require live evaluation at overlaps and shorelines; this implementation does not claim complete transparent scene transport or the later scene-linear HDR pipeline.

## Water quality and receiver contract

The following contract governs the work in [PBR.WaterRefraction.todo](PBR.WaterRefraction.todo).
It is a design decision for subsequent implementation, not a description of settings already shipped.
The existing Boolean remains the only implemented water-refraction setting today.

| Persisted property | Values and default | Contract |
| --- | --- | --- |
| `WaterRefractionEnabled` | Existing Boolean, default `false` | Preserve existing saved values. Off is independent of quality. |
| `WaterRefractionQuality` | Integer `3` = ray march x8 (default), `2` = x4, `1` = x2, `0` = UV distortion | Present the UI in highest-to-lowest order, labelled Water Quality. Values are stable identifiers, not loop counts. |
| `WaterRefractionBackgroundScale` | Integer divisor `1` = full size (default), `2` = half width and height | Independent of quality; all four qualities support both resolutions. No automatic resolution reduction when changing quality. |

Missing leaves use these defaults through the existing `ConfigModSystem` load/default-materialization
and `VgeConfig.Sanitize` paths. Preserve the current enable flag when migrating old documents.
Unknown integer quality values reset to `3`; unsupported divisors reset to `1`, rather than silently
turning the feature off. Wrong JSON types follow the existing loader's error/recovery policy; add
focused saved-settings checks when implementing the fields. Runtime changes are adopted as one
frame-consistent settings snapshot; a resolution change invalidates publication before replacing
the pair. A quality-only change does not reallocate the background. Disable withdraws publication,
reclaims refraction-only final and pre-overlay storage, and skips traversal, distortion and reduction.
Shared water-volume, atmosphere and HDR scene resources remain independently owned and active.

The x8/x4/x2 limits count **all ray-position receiver-depth evaluations**, including refinement and
any final revalidation. Cached results can be reused, but the old five refinements and final lookup
cannot be added outside that ceiling. Current tracing can perform 32 coarse evaluations, five
refinements and one final lookup: 38 receiver evaluations, each potentially followed by four
neighbour-depth fetches. Texture taps are not ray steps. Record those taps and bounded UV fallback
work separately; the UV tier performs no iterative ray traversal. The 32-metre current extent is
a baseline, not a mandate to distribute two new samples across that whole distance blindly.

All tiers use shared bilateral receiver filtering: spatial weights combined with valid metadata,
oriented interface-plane eligibility and compatible depth support. Normalize only surviving taps,
and associate colour with the same receiver support as depth. No support means unavailable data.
Do not average distinct surfaces into a fictitious intersection. Filter tolerances and crossing
estimation are selected against the baseline fixtures during their implementation; requiring the
filter does not license removing physical foreground rejection. Projection uses the full view;
sampling offsets and bounds use the background texture dimensions explicitly.

Half-size storage uses `max(1, ceil(width / 2))` by `max(1, ceil(height / 2))`. Resolve pre-overlay
restoration at full resolution before reducing the final publication. The reduction must select
compatible receiver support and preserve its associated radiance/depth/validity, including odd
edge footprints; simple independent colour/depth averages are prohibited. Full resolution remains
the baseline because reduced storage cannot recover subpixel coverage already discarded. Publish
only a complete pair from one frame/projection/settings generation, using the existing owners and
completed resize/reload boundaries. Extra reduction scratch belongs to `WaterRefractionScene`,
not the liquid mesh renderer. Do not introduce a parallel framebuffer/state ownership system.

Receiver outcomes are explicit: valid marched receiver, valid approximate UV receiver, unavailable
coverage, and no transmitted ray due to total internal reflection. Prefer the valid marched result;
when its coverage is genuinely unavailable, attempt bounded UV distortion with the same receiver
eligibility/filtering. Only use undistorted transport when neither selection is usable. An
approximate UV receiver must not be reported as a geometric ray hit. Valid supported distortion
must not be unconditionally faded solely because the interface is near the screen edge, or because
an arbitrary distance fade begins. Any transition blends complete HDR transport contributions,
not a second copy of the original background. Hidden/offscreen geometry and transparent layers
absent from the snapshot remain unavailable; total internal reflection correctly transmits nothing.

Optical evaluation retains IOR 1.333 and material-owned RGB coefficients in inverse metres. For an
above-water camera, the ray into water points away from the surface toward the receiver, so the
outgoing photon direction used in the water phase function is its negative, transformed to world
axes. For an underwater camera, scattering toward the camera uses the water-side interface-to-eye
direction; the outgoing refracted ray beyond the interface lies in air and must not replace it.
Compare that water-side outgoing direction with incoming light propagation (`-L`) consistently.
Keep isotropic environment terms, bounded local lighting and the existing homogeneous-medium
approximation; this contract does not add a volumetric shadow march or change coefficient units.

## HDR producer and consumer contract

The baseline inspection established the pipeline below, before the conditional HDR changes
described under Shared handoff implementation status. Installed-client IL was exported
to `artifacts/PbrColor/water-hdr-engine-il.txt` using Mono.Cecil assembly reading,
without executing the client. `VintagestoryLib.dll` SHA256 is
`E08F22B493B92FEAF0AAEB79D22437EA0F7EFC38AA7F72A04A47F98BC0E40DF0`.
The installed shader sources are under `G:/Vintagestory/assets/game/shaders`; these identify the
inspected installation and are not new source-code dependencies.

| Boundary / owner | Verified baseline behaviour | Required HDR contract |
| --- | --- | --- |
| Opaque material capture, `GBufferManager` and surface patches | Primary attachment zero contains linear albedo for supported geometry, but already-resolved sky colour for sky pixels; engine primary allocation is RGBA8. | Preserve material/radiance distinction and metadata; provide floating-point scene storage and a compatible engine primary handoff. Format replacement alone is insufficient. |
| `WaterRefractionCapture.BeforeOverlay` / `PBRCompositeRenderer.RenderComposite(capture, isolatedLighting)` | Before local first-person projection, evaluates isolated direct plus standalone environment lighting; early return publishes without a display draw. No current-frame LumOn gather exists yet. | Retain engine order and coherent pre-overlay world colour/depth. Do not invent late coverage or reuse stale GI. Keep unattenuated scene-linear capture. |
| `DirectLightingRenderer` at Opaque 9 / LumOn | Direct and indirect signals are unexposed linear; composition selects one environment/GI policy. | Preserve units, publication checks and single application of light. |
| `WaterVolumeRenderer` at Opaque 10.5 | Additive RGBA32F optical-depth/source capture; `WasComposed` prevents repeated liquid bulk transport. | Retain boundary capture and consume matching-generation volume transport once; change publication acknowledgement to the completed HDR scene write, not an obsolete early display resolve. |
| `pbr_composite.fsh` / Opaque 11 | Captures unattenuated RGBA16F radiance and R32F depth before water/aerial transport; ordinary RGBA16F output carries transported geometry plus display-space sky. | Keep pre-transport refraction source separate from transported HDR background. Migrate sky inputs and remove the mixed colour convention. Preserve invalid sky/first-person receiver marking. |
| `pbr_display_resolve.fsh` / `PBRCompositeRenderer` | Converts geometry to SDR primary before OIT; sky bypasses conversion. | Remove this early conversion only with the complete compatible scene handoff; move display conversion after HDR composition and migrate the sky bypass at the same time. |
| `LiquidRenderer` / `pbr_liquid.fsh` / `includes/pbr_liquid.glsl` | OIT 0.369; local display conversion/dither, display-space confidence blending, then local sphere fog and preview alpha; six bucket outputs. | Emit scene-linear surface/background transport and compatible sphere fog; preserve preview alpha and glow semantics, no early display transform. |
| `PbrSurfaceShaderPatches` / `pbr_forward_surface.glsl` | Standard, instanced, animated and transparent terrain evaluate forward light but resolve to display RGB before OIT/AfterOIT. | Migrate every scene forward route sharing HDR targets, including standard-derived first-person items and entityanimated hands. Keep GUI/offscreen routes separate. |
| Engine `SystemRenderOITLayers.BeforeOIT/AfterOIT` | Rebuild installs RGB8 bucket revealage and RGBA16F three-layer accumulation, six outputs and multiplicative/additive blending; AfterOIT binds bucket sources. | Keep alpha/revealage as dimensionless data; all accumulation producers must supply compatible linear premultiplied RGB. Preserve engine attachment ownership and late bucket setup. |
| `ClientPlatformWindows.MergeTransparentRenderPass` / `transparentcompose.fsh` | Bucket unprojection, overall revealage, straight-alpha result blended over Primary using SRC_ALPHA / ONE_MINUS_SRC_ALPHA. | Merge over transported floating-point HDR background, retaining single background contribution. Floating-point accumulation already exists but currently accumulates display RGB. |
| `ClientMain.MainRenderLoop` / AfterOIT | Opaque -> OIT -> merge -> AfterOIT; late entities use Primary. | Keep AfterOIT scene draws inside the HDR interval; preserve their physical/overlay depth contracts. |
| Sky/solar patches and engine celestial/effect draws | `AtmosphereSkyPatches` and solar shader resolve before blending; stars/moon retain authored display colours. | Retain atmosphere radiance through scene blending; explicitly decode/calibrate legacy authored colours at their scene boundary and move display-only perception effects to the final boundary. |
| `RenderPostprocessingEffects` / `RenderFinalComposition` | Bloom extraction reads primary; final scene input is framebuffer index 10, not directly primary. `final.fsh` performs FXAA, bloom/SSAO/godray combinations and display grading/clamps. | Preserve HDR through all pre-display copies/postprocess intermediates, including the scene sent to final composition. Give bloom/SSAO/godrays explicit linear roles; place one tone-map/output conversion before display grading/UI. |

The common scene migration is owned by the **complete-scene HDR task in
[PBR.BaselineShading.todo](PBR.BaselineShading.todo)**, not duplicated inside the water renderer.
Before full-scene HDR activation, that owner must establish and verify:

1. Floating-point scene target handoff through opaque, merge, AfterOIT and pre-display processing,
   with coherent resize/reload and no read/write feedback. Retain engine mesh and framebuffer
   authority, using existing resource APIs and explicit restoration boundaries.
2. Linear output from all scene contributors sharing those targets, including fallback vanilla
   liquids if VGE ownership is unavailable. Installed OIT source families also include
   `particlesquad`, `particlesquad2d`, `clouds`, `cloudvolumetric`, `aurora` and `blockhighlights`,
   in addition to `chunkliquid`, `chunktransparent` and `entityanimated`; standard/instanced
   variants and custom shaders can acquire OIT includes through preprocessing. Opaque
   `particlescube` and authored celestial/effect draws also need explicit routing. Preserve alpha,
   depth, glow and render order; adapt existing effects rather than replacing their algorithms.
   Unknown third-party scene contributors require an explicit compatibility boundary or rejection
   of the HDR handoff, never silently treating their display RGB as radiance.
3. One final scene display boundary after linear composition/effects, before display grading and
   UI. Update `findbright`, blur/intermediate formats, `final` colour operations and FXAA ordering
   consistently. UI/inventory must not pass through scene exposure. Native HDR output and new bloom
   quality algorithms are separate tasks; ordinary SDR output is sufficient here.
   Move SDR dithering to the final encoded output as specified by [PBR.OutputDithering.md](PBR.OutputDithering.md);
   do not retain per-draw dither in scene-linear radiance or apply an 8-bit amplitude to HDR storage.
4. Executed producer/consumer fixtures proving values above one survive the handoff, linear
   transparency matches numerical references, output conversion occurs once, and unsupported or
   failed setup retains an entirely compatible old path rather than mixing old and new routes.

This scene migration is currently **not complete or verified as a whole**. It does not block
water-specific implementation or verification. Shared water optics consume linear receiver data
and produce linear transport results; an output adapter preserves the existing display convention
until full-scene HDR is available. Controlled producer/consumer fixtures can verify the water HDR
branch without replacing the sky or activating HDR throughout the running scene. Sky replacement
and complete-scene activation remain separate parent tasks. Completing the water contract does not
claim their completion or HDR-monitor support. The approved authoritative-pipeline proposal constrains future
state ownership; its unimplemented APIs are not a prerequisite for these source/diagnostic checks.

### Shared handoff implementation status

The engine allocation adapter now selects RGBA16F for primary scene color and bloom blur
intermediates at the original allocation calls. Primary glow, OIT revealage and depth/SSAO data
retain their formats. Luma and god-ray targets were already RGBA16F. Engine framebuffer ownership,
completed-rebuild publication and retirement remain unchanged. The installed allocation sequence
is checked directly, including primary color-before-glow and both SSAO branches.

PBR forward/liquid output and the opaque handoff have explicit linear branches. In that branch,
`VgeSceneOutput` retains nonnegative unexposed radiance, and local sphere-fog colors are decoded
before interpolation. The sun branch also retains radiance; sky HDR support is deferred to the
planned VGE-owned replacement. Legacy OIT adapters decode
straight color before engine premultiplication; volumetric clouds decode the authored color
sample before integration. Postprocessing preserves linear RGB, uses display-derived alpha for
FXAA contrast, and selects one display conversion before final grading. The god-ray glare metric
uses display brightness without clipping its linear RGB. Runtime activation of these branches is deferred to existing binding-owner integration in the parent scene-HDR task. No whole-scene HDR activation
or all-producer coverage is claimed from these branches.

Fresh subagent-run foundation validation passed 33/33 tests with no skips in
`artifacts/PbrColor/scene-color-foundation-tests.log`: installed allocation/Harmony checks,
headless float storage, engine rebuild regressions, actual opaque handoff in both modes,
shared output math and existing display regressions. This receipt predates the subsequent
legacy/postprocess adapters and does not validate their complete integration.

The subsequent fresh shader regression passed 76/76 with no skips in
`artifacts/PbrColor/scene-color-patches-regression.log`. Installed-source and GPU cases cover
legacy OIT decoding before premultiplication, cloud sample decoding before volume integration,
linear RGB with perceptual luma alpha, HDR god-ray suppression, and HDR scene/bloom/god-ray
composition resolved once before grading and final quantization. Existing final/sky/sun/forward
and liquid highlight regressions also pass. The first run exposed an AST mistake: a preprocessor
directive node did not contain the conditional's statement body. The patch now guards the actual
clamp statement, and the final receipt supersedes the failed run and an intermediate whitespace
assertion failure. These counts are separate receipts, not a summed distinct-test total.

| Shared scene-HDR work | Controlling requirements | Current evidence |
| --- | --- | --- |
| Engine scene storage | Parent common-handoff task; HDR contract items 1 and 4; authoritative state proposal's engine ownership boundary | `SceneColorAllocation`/`SceneColorAllocationHook`; original installed IL, Harmony installation, headless storage and rebuild checks. Native window allocation has not been executed by these fixtures. |
| Selectable producer output and fog | HDR contract item 2; `PBR.MaterialColorAndDisplay.md`; `PBR.Liquids.md` six-output and alpha contract | `VgeSceneOutput`, liquid frame/handoff bindings and producer shader patches; helper, handoff and retained legacy-output checks. Conditional frame activation is wired; complete-frame execution remains unverified. |
| Legacy OIT and existing effects | HDR contract items 2 and 3; `PBR.SharedDisplay.md`; `PBR.OutputDithering.md` final-output requirement | `SceneColorLegacyPatches`, `SceneColorPostprocessPatches`, final patch; installed GLSL compilation and independently predicted numerical cases in the 76-test receipt. No new bloom algorithm or HDR monitor output. |
| Ordered opaque particles | HDR contract items 1, 2 and 4; parent common-handoff particle requirement; authoritative state proposal's engine ownership boundary | `SceneColorParticleTargets`, receiver separation shader, draw scope, capture hook and optional opaque handoff layer; focused receipts below. Conditional preparation and activation are wired; complete-frame execution remains outstanding. |

Further installed-source inspection found an additional coupling that activation must resolve:
`SystemRenderParticles.OnRenderFrame3D` enables alpha blending for cube particles at Opaque 0.6,
before direct lighting at 9 and composition at 11. Primary RGB still contains material color at
that point. Merely decoding the particle output or marking the blended pixel as completed radiance
would mix material data with radiance and lose the underlying receiver. Preserve underlying
material/depth and compose particle radiance without reordering or replaying engine submissions.
`SceneColorParticleTargets` now provides the isolated storage for that boundary: particle RGB
blends into owned RGBA16F with accumulated coverage, while depth and glow are borrowed from
primary. Its draw routing leaves underlying material and receiver metadata untouched. Matching
32-bit depth snapshots bracket the original particle invocation; the installed engine requests
`DepthComponent32`, and snapshots retain that exact format. The separation shader restores the
pre-particle material depth where particle visibility survives, or selects a later opaque
receiver and suppresses the particle layer where visibility changed. This comparison uses the
same depth representation without a geometric tolerance. Equal-depth replacement is not
distinguishable from depth alone; the intended engine boundary uses strict `Less` depth testing.
Unknown draws using a different depth convention require rejection by HDR readiness.

The opaque handoff shader has an explicitly enabled optional particle layer. It combines
`background * (1 - coverage) + premultipliedParticleRGB` on the linear route after opaque air/water
transport. Decoded legacy particle color already includes its original fog effects and must not
receive that transport again. The retained material background remains the refraction receiver;
this does not add refracted particle layers or ordered transparent transport. Capture resets and
primary attachment notifications invalidate publication; owned-image disposal preserves borrowed
engine images. Depth copies preserve independent framebuffer bindings and scissor state, and
storage clearing leaves indexed blend/write masks unchanged. Scissor preservation is owned by
`StateCache.PreserveScissorState`: the caller must establish known state through the cache before
capture. The copy applies its scissor-disabled pipeline and restores cached state without driver
queries or invalidation. Unknown state is rejected before copying rather than guessed.
The cache-owned scope and particle regressions pass 14/14 focused tests with no skips after a
fresh build (`artifacts/PbrColor/scene-color-particles-cache-scope.log`), including nested and
exceptional restoration, unknown-state rejection, and indexed-state preservation.

Fresh subagent-run validation passed 27/27 with no skips in
`artifacts/PbrColor/scene-color-particles-final.log`, including particle capture/separation,
installed cube-shader output, linear/legacy handoff and existing display regressions. Cases
cover HDR ordered blending, zero/partial/full coverage, later foreground replacement,
zero-alpha depth writers, empty draws/reuse, indexed state restoration, unsupported-depth
rejection, equal-size publication invalidation and borrowed-image survival after retirement.
Copied depth matches actual source readback exactly; nominal fragment depth uses a small
storage-quantization tolerance for the installed fixed-point format. A fresh build succeeded;
the shader catalog contains 170 stages / 408 variants and the final receipt verifies all
408 binaries current. The initial run caught use of the color-texture factory for depth
snapshots; those now use `DepthTexture`. Fixture corrections cover OpenTK enum availability,
engine lighting defaults, depth quantization and deferred disposal. This final receipt
supersedes those failed intermediate runs; it is not added to prior overlapping test counts.

`SceneColorParticleCaptureHook` now brackets the installed `Render(int, float)` call, which follows
standard alpha-blend setup in `OnRenderFrame3D`. Its void prefix never suppresses the original
submission. The draw scope establishes the known full-scene particle boundary through pipeline
descriptions, redirects only its color destination and restores independent framebuffer bindings
and the engine's standard output-zero blend factors. A finalizer withdraws failed captures and
restores routing even when the original method throws. Model, stage, framebuffer and current
shader checks exclude OIT/offscreen submissions; the cube shader's color-convention input is
selected at each recognized submission to prevent stale fallback/offscreen values.

The capture owner is registered at Opaque 8.5, ahead of direct lighting at 9. It resolves the
current particle layer and material depth before direct lighting, LumOn, water-volume integration
and composition consume that depth. Engine visibility depth remains unchanged for OIT visibility
and interface clipping. The opaque handoff receives the separated particle layer after material
transport. Before-stage invalidation withdraws the previous frame, and resize/world/disposal
boundaries retire owned snapshots. `PrepareFrame` preflights resources and the patched cube shader;
runtime activation is deferred. Future scene-HDR binding-owner integration must prepare it after
the Before 8.5 reset and reject an incomplete handoff. Complete-frame execution remains outstanding.

Shader compatibility now uses the existing executable-capability registry:
`SceneColorConvention` is declared only after the relevant fragment patch succeeds and is
published only after successful engine compilation. It describes the selectable color branch,
not complete HDR readiness. Particle preparation requires that capability on the current
executable as well as its binding uniform; a similarly named uniform alone is insufficient.
The frame coordinator must additionally validate target formats, all contributors, perception
effects and prepared consumers before choosing HDR. This change does not enable a partial frame.
Focused verification passed 8/8 tests with zero skips in
`artifacts/PbrColor/scene-color-capability-tests.log`: executable identity/failure/reload semantics,
final-patch capability declaration and particle preparation in both SSAO modes. The publication
fixture explicitly supplies a capability on its test program; this is not an installed-engine
compilation or complete-frame HDR receipt. The particle rasterization, integration and publication
test files still contain runtime GLSL compilation/raw GL setup and need migration to the prescribed
precompiled test path. Their existing raster coverage has not been removed to hide that gap.

The runtime shader registry now explicitly registers `SceneColorParticleShaderProgram`; packaged
shader compilation alone did not make `GpuShaderPrograms.Get` return it. A registered-owner
lifecycle fixture exposed this omission before activation. The corrected callback/publication
integration passed 27/27 tests with no skips in
`artifacts/PbrColor/scene-color-particle-integration-final.log`. This includes actual registered
shader resolution and Before/world/resize/disable/disposal boundaries, not a live game frame.

Source review also identified required SSAO follow-through: the installed cube shader writes
engine normal/position outputs 2/3. When those engine attachments exist, particle capture now
stores both outputs in separate RGBA16F images. The underlying engine position remains intact
for deferred lighting, including physical first-person receivers. After ordinary opaque
composition, `SceneColorParticleSsaoShaderProgram` restores only pixels where particle depth
still owns visibility; later foreground and untouched pixels retain their existing metadata.
Zero-alpha particles retain their original depth/metadata behavior. The restoration destination
borrows only engine normal/position, avoiding feedback with sampled visibility depth. Both
registered particle programs must be ready before selecting the HDR frame. This additional
SSAO path passed a fresh 31/31 focused GPU/integration tests with zero skips in
`artifacts/PbrColor/scene-color-particle-ssao-tests.log`; shader generation covered 172 stages
and 410 variants. Cases cover selective restoration, zero-alpha depth writes, later foreground,
untouched pixels, borrowed-image lifetime, installed cube SSAO outputs and registered owner
lifecycle with SSAO enabled/disabled. The lifecycle fixture invokes restoration after receiver
resolve; the placement inside `PBRCompositeRenderer` was source-reviewed, not executed by that
fixture. This receipt overlaps the earlier suites and must not be added to their counts.
The SSAO component test was subsequently rewritten to follow `VanillaGraphicsExpanded.Tests/agents.md`:
precompiled production shaders, typed GPU abstractions, shared setup and separate behavioral/lifetime
cases. Its fresh receipt is 7/7 passed with zero skips in
`artifacts/PbrColor/scene-color-particle-ssao-standards.log`. These cases upload independently specified
particle submission results and execute the real depth copies, receiver resolve and metadata restore;
they do not rasterize particles or verify engine scheduling. The earlier synthetic rasterization case
is no longer part of this test file.

`PBRCompositeRenderer.PrepareFrame` now exposes composition preparation before scene submission:
it prepares separate scratch/primary destinations and both the pre-overlay fallback and selected
ordinary composition shader variants, plus display handoff. The normal draw reuses the same
preparation methods. Preparation does not draw, publish refraction or select HDR; lighting,
particles, compatible scene producers and final processing remain the frame owner's other gates.
Registered-owner preparation and related regressions passed 11/11 tests with zero skips in
`artifacts/PbrColor/scene-color-compositor-preflight-tests.log`. Preparation selects both lighting
modes without draws/publication or primary-color changes, preserves independent framebuffer
bindings, and rejects missing/invalid primary metadata. Subsequent ordinary composition, borrowed
target replacement, pre-overlay capture state and color handoff checks also pass. This proves the
compositor preparation boundary, not complete-frame execution.

Installed-engine inspection identifies `ShaderRegistry.shaderPrograms` as the shared array for
file and memory registrations, including third-party programs. `Before` callbacks precede the
engine shadow/opaque/OIT stages. The outer screen renderer calls scene rendering, postprocessing,
AfterPostProcessing, final composition, AfterFinalComposition and the final blit in that order.
The frame decision must therefore cover the engine postprocess/final methods explicitly rather
than infer their role from `CurrentRenderStage` alone. A registry inventory also does not establish
that arbitrary third-party Before callbacks are safe scene contributors.

`SceneColorShaderInventory` now reads that engine registry instead of maintaining a separate
registration list. Scene contributors require current linked color-convention or material-capture
capabilities. Known auxiliary passes require linked engine classes using engine file assets;
unknown programs, third-party subclasses and memory programs cannot claim that exemption merely
by copying a pass name. VGE-owned programs still require preparation by their consuming owners.
An empty registry does not itself establish frame readiness; the coordinator must separately
require the actual scene and final consumers. Future scene-HDR activation must use this classification
as one of its preparation gates.

The installed registry always includes `woittest` and `colorgrade`. Field-use inspection identifies
`woittest` as an optional framebuffer-debug OIT producer, so its patch decodes straight authored
RGB at `drawPixel` entry before the unchanged alpha/weight calculation. The alternate `colorgrade`
endpoint resolves linear input before grading and dithers after output only when its color flag
is explicitly enabled. Neither pass receives an unconditional auxiliary exemption. Ordinary terrain
fragment patches now declare `SceneMaterialCapture` after successful source mutation.
Fresh build and 19/19 focused checks passed with zero skips in
`artifacts/PbrColor/scene-color-shader-inventory-final.log`: inventory classification, executable
capability lifetime and installed-source patch placement. These are CPU/source checks, not GPU
execution of the two new engine shader branches or complete-frame HDR evidence.
The particle path allocates a nominal 28 bytes per pixel of owned scratch storage, plus 16 bytes
when SSAO metadata is retained. It adds two depth copies, one receiver-separation draw and an
optional SSAO-restoration draw. These are format/pass counts, not measured GPU costs.

Runtime scene-HDR activation is deferred to the parent full-scene task. The experimental
`SceneColorFrame` coordinator, global shader-use hook, postprocess/final hooks and their dedicated
tests were removed. There is no new callback on every engine shader activation and no Before-stage
HDR preflight. The owned liquid and opaque handoff explicitly select legacy output; the water HDR
shader branch remains independently tested. Shader reload no longer invokes the removed coordinator.

Future activation must integrate with existing surface, atmosphere and owned-program binding
owners, with explicit boundaries for uncovered engine consumers. It must handle nested offscreen/UI
uses, postprocess/final ordering, failed preparation and lifecycle resets without stale convention
values. Retained target validation, inventory and shader output helpers are groundwork, not an active
frame controller. Earlier coordinator-specific receipts are historical and do not validate a current
activation path. Sky HDR patches remain removed; the owned-sky replacement is a separate task.
After this deferral, a fresh build and 55/55 focused tests passed with zero skips in
`artifacts/PbrColor/water-deferred-coordinator-regressions.log`: the 40 water checks, lighting-mode
lifecycle, opaque handoff and final-output regressions. Water transport completion is unchanged.

The opaque handoff tests now use the existing fullscreen framework for pipeline and framebuffer
setup rather than issuing redundant raw GL state calls. All nine existing numerical cases passed
after that cleanup in `artifacts/PbrColor/scene-color-handoff-framework-tests.log`, with no skips.
`SceneColorOutputTests` has also been migrated to a typed offline-built fixture importing the
production color helper; its original HDR, negative-input, alpha and dither assertions are retained.
Fresh build and the migrated output plus nine handoff cases passed 10/10 with no skips in
`artifacts/PbrColor/scene-color-output-handoff-precompiled-tests.log`. The helper now executes
from production-importing SPIR-V through the shared GPU program/fullscreen owners, with no
runtime GLSL compilation or test-local shader lifetime management.

Vanilla engine shader fixtures use GLSL compilation, as explicitly clarified by the user.
`InstalledShaderFixture` reuses the existing installed-source patch helper and
`TerrainShaderTestFixture`, then imports reflected driver locations into the matching engine
shader wrapper. The executable has one owner. No separate exporter, numeric interface contract,
SPIR-V manifest or extra test-build project is required for vanilla shaders.
Reachability review of `particlesquad2d` found that its `oitPass <= 0` direct assignment bypasses
the OIT decoding wrapper, but the installed engine library constructs and renders that owner
only from `GuiCompositeMainMenuLeft`; its pass field retains zero. This is a GUI route, where
the frame binding must remain legacy, not a demonstrated scene-HDR omission. No production
decode was added to that branch. GUI isolation still requires a rendered check.
No complete-frame HDR or live appearance claim follows from these focused checks. The common
handoff still requires complete execution evidence, review and audit.

Decals register at AfterOIT 0.5 and do not share this early material-buffer problem. These are
installed IL observations, not live visual results. Complete execution coverage of readiness,
unknown-contributor rejection and failure/lifecycle handling, plus sky perception effects, remains required.

Current missing-scene coverage, scalar OIT revealage and multiple-liquid bucket averaging are
separate limitations. Moving RGB into HDR fixes colour-space composition, not ordered refraction
through arbitrary transparent layers. Preserve the documented single represented opaque receiver
contract until a separate transport extension is designed and verified.

The contract inventory uses the following controlling sources. These references describe the
requirements consulted, not new claims of runtime verification:

| Work item | Consulted document and requirement | Evidence / verification boundary |
| --- | --- | --- |
| Receiver baseline and sampling decisions | [PBR.BaselineShading.todo](PBR.BaselineShading.todo), refraction item; current Optics and traversal section above | Exact production traversal include, liquid consumer, snapshot/capture owners and focused diagnostic/production GPU fixtures. No live camera-defect reproduction claimed. |
| Medium and liquid compatibility | [PBR.WaterMedium.md](PBR.WaterMedium.md), Evaluation and ownership; [PBR.Liquids.md](PBR.Liquids.md), Captured OIT contract; [PBR.LiquidRenderer.Proposal.md](PBR.LiquidRenderer.Proposal.md), Compatibility and lifecycle | Preserve SI units, RGB transport, one submission owner, six bucket outputs and engine-owned meshes/targets. Shared receiver/transport evaluation and linear confidence composition are implemented and covered by the focused water receipt above. |
| Settings and resource decisions | Parent refraction enable/disable contract and current `ConfigModSystem`, `VgeConfig`, `WaterRefractionScene` / `WaterRefractionCapture` | Stable settings/defaults and publication policy specified above; new settings runtime tests belong to implementation, not this design receipt. |
| HDR dependency and colour boundaries | [PBR.MaterialColorAndDisplay.md](PBR.MaterialColorAndDisplay.md), Lighting and display; [PBR.EntityAndLateCoverage.md](PBR.EntityAndLateCoverage.md), Transparency and display boundary; [PBR.SharedDisplay.md](PBR.SharedDisplay.md), HDR ordering; [PBR.OutputDithering.md](PBR.OutputDithering.md), final-output migration | Source/installed IL inventory defines the full-scene activation contract; water-specific implementation is independently testable and does not claim complete-scene HDR acceptance. |
| Lighting and engine authority | [PBR.LightingModes.md](PBR.LightingModes.md), LumOn composition and lifecycle; [Rendering.AuthoritativePipelineState.todo](Rendering.AuthoritativePipelineState.todo) and its approved proposal, Scope boundaries / Engine integration | Retain mode-generation ownership, engine shader activation and framebuffer restoration. Diagnostic hooks compile away normally; no new submission architecture is introduced. |

## Validation and remaining acceptance

### Receiver baseline diagnostics

`WaterRefractionDiagnosticTests` executes a precompiled fixture importing the actual
`includes/liquids/refraction.glsl` with opt-in event/sample macros through the existing headless
shader framework. Ordinary shader variants
expand these hooks to nothing; traversal equations and rejection thresholds remain
unchanged. Three diagnostic outputs record hit/reason/evaluation count/confidence, last receiver
UV and positive view depth, and submerged length/refracted Z/fallback mixing weight/returned Z.
On rejection, the sample is the last attempted lookup, not a selected valid receiver; a projection
failure before any lookup leaves it zero. The fallback weight is `1-confidence` (one on failure),
not the engine's final revealage or a measurement of visible distortion strength.

The following controlled 128-pixel GPU cases are reproduced in
`artifacts/PbrColor/water-refraction-contract-final.log`:

| Case | Observed result | Implication for subsequent work |
| --- | --- | --- |
| Flat / tilted normal | Accepted in 23 receiver evaluations, confidence 1; UV x changes from 0.503125 to 0.571335. | Established successful baseline; retain supported geometric distortion. |
| Receiver 1 cm behind a flat interface | Interface-plane rejection on the first evaluation (reason 4). | The fixed 2 cm exclusion rejects this physically behind-water sample; shallow-path eligibility needs improvement. |
| One adjacent foreground texel | Entire trace rejected on first evaluation (reason 5), despite valid central depth. | Bilateral support can retain valid taps, provided foreground colour never leaks into them. |
| Edge sweep at columns 1, 3, 6, 12, 32, 64 | Column 1 fails projection before a receiver read. Columns 3 and 6 are accepted but confidence is 0.058087 and 0.409624; columns 12 onward have confidence 1. | Distinguishes hard margin rejection from avoidable attenuation of a valid receiver. At column 3 the receiver UV x is already 0.138667: the interface-edge fade suppresses otherwise supported distortion. |
| Receiver depth 30 m, interface depth 2 m | Accepted length 28.009766 m, 36 evaluations, confidence 0.498169. | Range fade halves a valid contribution without a missing receiver. |
| Positive view-Z ray | Accepted with direction Z +0.115695, 13 evaluations, confidence 1; UV (0.804779, 0.5), sampled positive depth 1.822360 m, returned ray Z -1.850749 m. | Confirms the helper can accept a ray moving toward the camera; this uses a synthetic wide projection and does not establish ordinary in-game camera coverage. |
| Depth step from 20 m to 3 m | Residual rejection (reason 7), 13 evaluations, approximately 0.434958 m mismatch. | Necessary discontinuity guard; broad threshold relaxation would admit unsupported crossings. |
| Grazing normal | Projection rejection (reason 1) after 12 receiver evaluations. | Demonstrated screen-coverage limitation; no hidden receiver is asserted. |
| Sky / invalid receiver metadata | Rejection (reason 2) on first evaluation. | Keep invalid coverage distinct from physical receivers. |
| Receiver beyond traversal range | Exhaustion (reason 9) after 32 evaluations. | No hit is proven; a bounded approximate fallback must retain its own validity rules. |

The diagnostic codes also distinguish homogeneous reconstruction failure (3), total internal
reflection (6) and nonfinite radiance (8); their mere instrumentation is not proof that every
branch was exercised by these new cases. Existing production-SPIR-V `WaterRefractionTests` were
rerun alongside them, including flat/tilted surfaces, underwater exit/TIR, invalid/nonfinite depth,
bottom-edge composition and fixed-world pitches 15/45/75 degrees at headings 0/90 degrees.
Those tests predict the floor texel and composition independently; they do not launch the client.

The fresh focused Debug build/test invocation passed **44/44**, zero skips: 12 diagnostic cases,
20 production refraction cases, nine overlay-composition cases, two lifecycle cases and one
capture-state case. The final fresh build and expanded water/liquid/composite regression passed
**114/114**, zero failures/skips, in 5.09 seconds. These counts overlap and must not be added.
Receipts: `artifacts/PbrColor/water-refraction-contract-final.log` and
`artifacts/PbrColor/water-refraction-contract-regression.log`. The initial shader build compiled
and verified 168 stages / 406 variants; the final incremental receipt verified all 406 binaries
current. Existing compiler/analyzer warnings remain. An initial diagnostic assertion incorrectly
treated receiver depth 30 m as a 30 m water path; the actual interface starts at 2 m. Correcting its
expected 28 m path gives the independently expected half-strength range fade. No production
threshold was changed to obtain a pass. Earlier failed diagnostic logs are superseded by these
fresh receipts, not counted as successful validation. Sandbox restore/compiler-path failures
were resolved through approved escalation before the successful fresh build/test runs.

Run through a build/test subagent with the existing NuGet package location:

```powershell
$env:NUGET_PACKAGES = 'C:/Users/Sisco/.nuget/packages'
dotnet test VanillaGraphicsExpanded.Tests/VanillaGraphicsExpanded.Tests.csproj -c Debug --no-restore --filter 'FullyQualifiedName~WaterRefraction|FullyQualifiedName~WaterMedium|FullyQualifiedName~WaterBoundary|FullyQualifiedName~WaterVolume|FullyQualifiedName~Liquid|FullyQualifiedName~PbrComposite' --logger 'console;verbosity=detailed'
```

Source/contract review separates these executed baseline observations from the planned fixes.
Second review and an independent completion audit found the baseline investigation and design
contract fully supported by the source, linked documents and executed receipts.
No settings UI, reduced-step algorithm, bilateral filter, downsampling or whole-scene HDR activation is claimed
implemented by these diagnostics. Live appearance and production GPU cost remain unmeasured.

### Earlier implementation receipts

Constructor-configured blitting and attachment notifications passed a fresh build and 236/236 regressions. After tightening ordinary primary-load notification suppression, a fresh final build and 34/34 affected tests passed. Functional checks prove that silent external attachment changes are not queried during ordinary copies, wrapper refresh updates the retained copy, repeated setup/load/unload emits no refresh, and an equal-size window-rebuild callback notifies once. Resize completion/no-op suppression, retirement rejection and subscription removal are also covered. Receipts: `artifacts/PbrColor/event-blitter-build.log`, `artifacts/PbrColor/event-blitter-regressions.log` and matching TRX, plus `artifacts/PbrColor/event-blitter-final-refresh.log` and matching TRX. Counts overlap; working-tree whitespace checks and source review passed.

Blit extraction into `GpuFramebufferBlitter` passed a fresh build and 232/232 regressions with zero skips. Coverage includes managed/raw target overloads, original routing and viewport preservation, unchanged-attachment reuse, texture replacement, depth/combined copies, renderbuffers, reset/disposal nonownership, and the production LumOn surface-albedo capture/recreation path. Independent source review confirmed caller-owned lifetime and reset before target disposal. Receipts: `artifacts/PbrColor/framebuffer-blitter-build.log`, `artifacts/PbrColor/framebuffer-blitter-regressions.log` and matching TRX. Counts overlap earlier suites.

Scratch-FBO conversion passed a fresh build and 223/223 regressions with zero skips. Tests verify binding-only exceptional restoration, copied color/depth values, unchanged original MRT routing and viewport, refreshed replacement attachments, borrowed texture/renderbuffer lifetime, and capture integration. Receipts: `artifacts/PbrColor/scratch-blit-regressions.log` and matching TRX. The working-tree whitespace check passed. These tests include the earlier suites; live rendering and GPU cost remain unmeasured.

Before scratch-FBO conversion, the framebuffer scope and API reuse changes passed a fresh build and 189/189 focused regressions with zero skips. Coverage includes actual stale-cache independent read/draw bindings, complete MRT routing and read selectors, viewport restoration on exceptional exits, borrowed framebuffer dimensions/ownership, engine and foreign program restoration through `UseScope`, bidirectional blit routing, real pre-overlay capture draws, resize invalidation, world-leave deletion and subsequent recreation. Receipts: `artifacts/PbrColor/render-boundary-regressions.log` and matching TRX. The working-tree whitespace check passed; live rendering and GPU cost remain unmeasured.

Rendering reuses `GpuFramebuffer.BindWithViewport` and `Clear`; `Wrap` accepts optional viewport dimensions without taking ownership. The additional framebuffer-state scope has been removed. Binding restoration uses the existing `FramebufferScope`, with combined scopes preserving distinct read and draw targets. The primary resolve attaches the engine color texture once to a reusable non-owning target, leaving engine MRT routing untouched. Ordinary frames only bind it; source framebuffer/attachment replacement, resize, and world/disposal boundaries retire the target. The additional framebuffer rendering and shader suspension helpers have been removed. When no engine shader owner is registered, `GpuProgram.UseScope` reads the previous program through `GlStateCache.GetCurrentProgram()` under the existing cache contract. Synchronization with untracked engine program changes remains deferred. Direct lighting and composition retain pipeline descriptions and scoped restoration. The earlier abstraction regression receipt (`artifacts/PbrColor/render-abstraction-regressions.log`, 179/179) predates this API cleanup; fresh validation is recorded below. Test counts overlap and are not additive.

The pre-overlay correction preserves engine submission order and passed a fresh build plus 110/110 related regressions with zero skips. Eight production-composite variants cover available/missing capture, marked/unmarked pixels and both lighting modes; only the refraction pair is restored and visible first-person lighting remains unchanged. The runtime integration fixture executes two real shader draws, checks publication and unchanged primary color/depth, restores an active engine shader owner and independent read/draw framebuffer bindings despite a stale managed cache, and verifies viewport, blending, depth mask, frame invalidation and disable retirement. An independent capture/installed first-person shader regression run passed 84/84; these suites overlap. Evidence: `artifacts/PbrColor/held-refraction-engine-il.txt`, `artifacts/PbrColor/held-refraction-regressions.log` and matching TRX, and `artifacts/PbrColor/water-refraction-capture-regressions.log` and matching TRX. Independent source review and `git diff --check` passed.

The combined run exposed an older numerical fixture's uninitialized aerial texture: seven color comparisons passed alone but failed after capture allocations. Explicit zero initialization changed the exact capture/refraction pair to 21/21 passing and the final combined run to 110/110. Expected optics and tolerances were unchanged. This was a reproduced headless fixture issue; it does not establish live visual acceptance.

Focused subagent validation passed 109/109 liquid, water-medium/boundary, refraction, composite and configuration regression tests with no skips. A subsequent configuration-persistence run passed 23/23 after adding actual saved-option loader restoration assertions. These suites overlap; their counts must not be summed as distinct tests. The production shader catalog verified 168 stages / 406 variants; the final incremental build recompiled 13 variants after the final parameter comments. Build succeeded with existing compiler/analyzer warnings and no errors. Independent source review found no confirmed new implementation defect; `git diff --check` passed.

Tests exercise opaque MRT publication in both shader lighting modes, flat/tilted entry, underwater exit and total internal reflection, foreground/adjacent shoreline/sky/nonfinite/offscreen/exhaustion fallback, an independently predicted Snell-gradient texel and display value, persisted-option restoration and repeated snapshot toggle/resize/attachment replacement. Lifecycle tests vary the lighting configuration but do not execute live engine mode switching. Receipts: `artifacts/PbrColor/water-refraction-regression.log`, `artifacts/PbrColor/water-refraction-tests/water-refraction-regression.trx` and `artifacts/PbrColor/water-refraction-config.log`.

During fixture development, `ReadPixelsRegion` unbound the render target; a later draw initially wrote elsewhere and assertions read stale pixels. Rebinding before each draw resolved the reproduced fixture failure. Temporary diagnostic shader edits were removed before the passing regression run.

The screen-transition correction adds production GPU checks for continuous normalized bottom-row revealage, camera/world transform consistency, and a fixed horizontal water/floor scene viewed at pitches of 15, 45 and 75 degrees and headings of 0 and 90 degrees. The fixed scene's refracted floor texel, Fresnel response and premultiplied display contribution are predicted independently on the CPU. These cases use 128-pixel targets; a 16-pixel target's steep floor depth quantization exceeded the conservative crossing tolerance at 15 degrees. This remains a nearest-depth screen-space approximation. Positive view-Z accepted rays are not covered by these tests, and no live camera-facing defect reproduction is claimed. The shader build succeeded; final focused validation passed 27/27 and related water/liquid/display regressions passed 98/98, with no skips. Receipts: `artifacts/PbrColor/water-refraction-transitions-fixed-world.log` and `artifacts/PbrColor/water-refraction-transitions-regressions.log`, with matching TRX files.

User-run visual acceptance remains outstanding for shorelines, grazing views, entering/exiting water, overlapping transparent geometry, waves/flow, resize/reload and LumOn switching. No game is launched for this work. Snapshot output bandwidth, traversal cost and full production GPU frame/pass time remain unmeasured; the parent checklist stays open until its acceptance requirements are resolved.
