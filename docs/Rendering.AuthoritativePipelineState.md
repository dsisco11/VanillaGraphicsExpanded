# Authoritative pipeline state implementation evidence

The dated sections retain implementation history and the scope of their original receipts. Current
contract dispositions and consolidated acceptance are recorded in [System acceptance](#system-acceptance).
Superseded temporary helpers and custom-clipping experiments described in historical sections are
not part of the current production implementation.

## State cache lifetime

StateCache retains knowledge for the rendering thread and relies on explicit invalidation at
renderer lifecycle and external-mutation boundaries. It does not track context handles or
registration generations, automatically invalidate on context changes, or validate context identity
inside boundary snapshots. Engine scopes must finish on the same live rendering context.

GpuSupport remains the owner of capability capture and its existing registration lifecycle.
Capability-dependent boundary entry still checks readiness through that owner. Native-error handling,
declared mutation coverage, targeted resource retirement and ordered boundary restoration remain in place.
The context-switch/replacement checks described in the historical records below no longer apply to
StateCache; future context recovery is separate work.

The expanded regression selection exposed two existing SurfaceLightingDisplayBoundaryTests failures:
their engine mesh fixture applies an additional fullscreen pipeline beyond the production boundary's
coverage. Both failures reproduced on HEAD with the same shader assets. They are excluded from the
focused cache validation; the fixture has not been changed as part of this lifetime removal.

Final shader-enabled Debug and Release builds passed, and the focused cache/boundary/startup/resource
selection passed 93/93 tests in each configuration with no skips. Receipts: artifacts/
statecache-context-removal-{debug,release}-{build,tests}.log and matching TRX files under
artifacts/TestResults. Baseline failures are recorded in artifacts/statecache-context-baseline-tests.log.
No live-game acceptance or performance measurement was performed.

## Graphics submission design contract

Source review: 2026-10-05. This section resolves the bounded design choices in the
[approved proposal](Rendering.AuthoritativePipelineState.Proposal.md) for the
[implementation plan](Rendering.AuthoritativePipelineState.todo). It specifies complete
submission; retained partial descriptors do not implement these requirements.
The state-cache lifetime policy above governs this design. Historical receipts below remain evidence
only for the implementations and scopes they actually exercised.

### Consumer and mutation coverage

The inventory covers production C# draw sites, descriptor application, shader activation, framebuffer
blend application, raw fixed-function calls, legacy capture and invalidation, plus production shader
point-size outputs. Resource wrappers are submission mechanisms, not additional renderers.
Paths in the tables are repository-relative under `VanillaGraphicsExpanded/`.

| Consumer / source | Draw ownership and current path | Submission owner and compatibility requirements |
| --- | --- | --- |
| [DirectLightingRenderer](../VanillaGraphicsExpanded/PBR/DirectLightingRenderer.cs), [DirectLightingTargets](../VanillaGraphicsExpanded/PBR/DirectLightingTargets.cs) | VGE; prepared GraphicsPipeline, owned EngineFullscreenGeometry and explicit three-output render pass; normal, isolated and shared capture invocation | Complete consumer. Uses the managed target signature and complete restoration set below; see Direct-lighting production submission for current evidence. |
| [PBRCompositeRenderer](../VanillaGraphicsExpanded/PBR/PBRCompositeRenderer.cs), [WaterRefractionCapture](../VanillaGraphicsExpanded/PBR/Liquids/WaterRefractionCapture.cs), [SceneColorParticleCapture](../VanillaGraphicsExpanded/PBR/SceneColor/SceneColorParticleCapture.cs) | VGE fullscreen composition, display, receiver/reduction, particle resolve and SSAO | Prepared complete pipelines, explicit routes and sequential passes; success-only publication and shared capture restoration. |
| [LumOnRenderer](../VanillaGraphicsExpanded/LumOn/LumOnRenderer.cs) | VGE fullscreen draws interleaved with compute | Retained complete realizations and explicit per-target passes; compute barriers and resource ownership remain with existing algorithms. |
| [LiquidRenderer](../VanillaGraphicsExpanded/PBR/Liquids/LiquidRenderer.cs), LiquidDepthRenderer, WaterVolumeRenderer | VGE orchestration, borrowed engine liquid pools | LiquidGraphicsSubmission and EngineLiquidPoolGeometry preserve engine culling/transform staging, validate ordinary indexed layouts/groups, restore UseSsbo and declare depth, six-output OIT and two-output volume policy. |
| [MaterialAtlasNormalDepthGpuBuilder](../VanillaGraphicsExpanded/PBR/Materials/MaterialAtlasNormalDepthGpuBuilder.cs) | VGE procedural fullscreen solver and three public entries | Complete bake boundary, shader-free typed clear, explicit per-rectangle viewport and sampler/readback restoration. |
| [LumOnWorldProbeClipmapGpuUploader](../VanillaGraphicsExpanded/LumOn/WorldProbes/Gpu/LumOnWorldProbeClipmapGpuUploader.cs) | VGE two-pass point resolve | ArrayGraphicsGeometry, two declared complete pipelines and target-specific passes with one-pixel points. |
| [VgeWorldCellBoundsDebugView](../VanillaGraphicsExpanded/DebugView/Views/VgeWorldCellBoundsDebugView.cs), VgeGBufferOverlayDebugView | VGE line and fullscreen debug draws | DebugGraphicsSubmission owns complete descriptions; geometry adapters validate line/fullscreen streams; debug targets publish actual image or window metadata. |
| [LumOnDebugRenderer](../VanillaGraphicsExpanded/LumOn/LumOnDebugRenderer.cs) | VGE OIT, AfterBlit and atlas overlays | Explicit line/point/MRT policies and owned overlay shader; dormant frozen draw removed. |
| [SceneColorParticleDrawScope](../VanillaGraphicsExpanded/PBR/SceneColor/SceneColorParticleDrawScope.cs) | Engine-owned particle submission redirected by VGE; output-zero blend patch and documented engine handoff | Keep compatibility adapter. Preserve engine glow routing, full-scene scissor intent and known source-alpha boundary; particle draw is not made VGE-owned by redirection. VGE resolve draws above do migrate. |
| [GBufferManager](../VanillaGraphicsExpanded/GBuffer/GBufferManager.cs) and [TerrainTessellationDrawHook](../VanillaGraphicsExpanded/HarmonyPatches/TerrainTessellationDrawHook.cs) | Engine-owned terrain/entities/shadow draws, VGE attachments/blend policy, shader and topology interception | Keep explicit engine adapters. Patch count 3 saved/restored by finalizer; ordinary and grouped MeshRef overloads have different effects. Validate installed IL/layout and shared entity shadow path before widening ownership. G-buffer blend policy moves only when its actual draw owner migrates. |
| [GpuVao](../VanillaGraphicsExpanded/Rendering/GpuVao.cs), [GpuEbo](../VanillaGraphicsExpanded/Rendering/GpuEbo.cs), [GpuVertexAttribBinding](../VanillaGraphicsExpanded/Rendering/GpuVertexAttribBinding.cs) | VGE geometry setup/indexed/instanced submission mechanisms | Reuse buffer/VAO ownership. Expose validated metadata to geometry adapters; no second resource hierarchy or assumption that a native VAO name proves layout compatibility. |

Scheduling-only IRenderer implementations (held-light completion, trace geometry/scene/world-probe updates, material artifact queue, uniform-ring begin/end, texture streaming, resource deletion and profiling) do not add graphics draw sites; their shared resource/compute lifetimes remain unchanged. Atmosphere shader replacement and terrain material/scene-slot/displacement hooks participate in engine-owned draws, not additional VGE graphics passes.

All inventoried VGE draw families remain in migration scope. A later adapter prerequisite is not a
permanent deferral or completion claim. The historical **Legacy scope inventory and disposition**
below accounts for the eleven retired capture invocations, including the removed dormant helper.

| Mutation mechanism | Classification and authority boundary |
| --- | --- |
| [EngineStateSwitchingHook](../VanillaGraphicsExpanded/HarmonyPatches/EngineStateSwitchingHook.cs), [EngineStateCallMap](../VanillaGraphicsExpanded/HarmonyPatches/EngineStateCallMap.cs), [EngineStateCalls](../VanillaGraphicsExpanded/Rendering/EngineStateCalls.cs) | Observed/routed only for exact mapped signatures in selected engine assemblies. Depth function/mask, selected capability enables, indexed/global blend factors/masks, viewport, line/point size, patch/provoking state, clear color, pixel store, program/VAO/FBO/renderbuffer/texture/sampler/buffer/image bindings and mapped deletion calls use cache adapters. Unsupported capability enums are forwarded, not proof of complete tracking. |
| [FramebufferBindingHook](../VanillaGraphicsExpanded/HarmonyPatches/FramebufferBindingHook.cs) | Successful setter postfix observes combined read/draw FBO binding without another GL call. Does not establish blend/routing or arbitrary mod coverage. |
| [GpuProgram](../VanillaGraphicsExpanded/Rendering/Shaders/GpuProgram.cs), [OwnedShaderSubmissionHook](../VanillaGraphicsExpanded/HarmonyPatches/OwnedShaderSubmissionHook.cs), terrain material/scene-slot binding hooks | Shader owner and prepared resource publication remain authoritative; native program tracking alone does not restore engine ownership. Texture/UBO inputs may change on every draw with the same executable. |
| [CompleteGraphicsBoundary](../VanillaGraphicsExpanded/Rendering/Integration/CompleteGraphicsBoundary.cs), [EngineBoundaryScope](../VanillaGraphicsExpanded/Rendering/EngineBoundaryScope.cs) | Explicit restoration of declared effects and prepared resource footprints. Resolve unknown incoming fields once at entry; restore shader owner, borrowed bindings and independent read/draw FBOs, then draw state. Complete descriptions supply full PSO restoration coverage; engine compatibility adapters retain their explicitly partial scopes. |
| GpuFramebuffer creation/routing and scratch helpers | Framebuffer blend storage is retired; complete pipeline descriptions own VGE blend policy. Raw draw/read-buffer routing is FBO-local resource state. RenderPass owns routing and restores borrowed engine routing. |
| Engine compatibility scopes, explicit external invalidation, [StateCache.ScissorScope](../VanillaGraphicsExpanded/Rendering/StateCache.ScissorScope.cs) | Retained for bounded engine-owned operations; the legacy fixed-function capture is test-only. Scissor helper preserves enable, not a general dynamic rectangle contract. Complete VGE draws use the full boundary contract. |
| Unmapped engine overloads, other mods and unobserved external stencil/equation/rasterizer/sampling/clip changes | Unknown to current cache unless independently restored. Query required unknown fields at entry, establish complete state, restore and invalidate affected knowledge at declared external boundaries. No untracked mutation inside complete submission. |

Renderbuffer resource operations now use a checked unknown-binding query and suppress known-equal
binds. Managed immediate/deferred retirement and the exact engine DeleteRenderbuffer adapter route
through StateCache, clearing only the retired binding and retaining boundary retirement detection.
Nested scopes and exception cleanup are covered by the render-pass evidence below. Allocation and
renderbuffer binding remain outside pipeline identity. Unobserved raw external changes still require
explicit invalidation; this does not claim coverage of every engine deletion overload.

### Supported state and defaults

These are complete-description construction defaults, not ambient GL defaults or changes to legacy
partial-mask meaning. Capability checks belong to GpuSupport; StateCache does not own limits.
Every supported field is established when unknown and restored if changed at an engine boundary.

| Category | Complete default and supported policy |
| --- | --- |
| Depth | Test off, comparison Less, write off. Support comparison and write independently; static depth range fixed to [0,1]. Clip-depth convention is independently configurable; it does not imply reversed-Z. |
| Stencil | Disabled; front/back Always, reference 0, read/write masks all stencil bits, fail/depth-fail/pass Keep. Support independent front/back comparisons, masks and operations; declared dynamic front/back references when enabled. Validate against attachment stencil bit width. |
| Rasterizer | Cull off with Back selection, CCW front face, Fill front/back, polygon offset fill/line/point off with factor/units 0; depth clamp off, rasterizer discard off, LastVertex provoking convention. Supported alternatives require capability validation and complete application. |
| Output blending | Each output disabled, RGB/alpha Add equations, One/Zero factors, RGBA writes enabled. Support independent enables/equations/factors/masks. Constant factors require declared dynamic blend constant; otherwise canonical zero. Global changes invalidate/update all affected indexed slots, including beyond the routed target count before engine restoration. |
| Sampling | Effective samples 1, multisample on, sample coverage disabled (value 1, invert false), sample mask disabled (all supported words all-ones), alpha-to-coverage and alpha-to-one off; sample shading off (minimum 0). Supported controls are capability-gated; all mask words represented. Target sample count must match exactly. |
| Output interpretation | Framebuffer sRGB off for linear intermediates; explicit static enable for compatible sRGB targets. Color logic operation disabled (Copy), dither disabled. Logic-op rendering rejects initially; disabled must still be established/restored, never inherited. |
| Clipping | Lower-left origin, negative-one-to-one clip depth, depth range [0,1]; all supported user clip-distance enables off. Where clip-control exists, set/restore it; otherwise use the API fixed convention. Custom shader clip distances are unsupported; alternate origin/depth conventions remain authored settings. Current production shader scan found no gl_ClipDistance/gl_CullDistance outputs. |
| Primitive assembly | Triangles, restart and fixed-index restart disabled, restart index 0; patch count 3 when tessellation used. Validate topology/stages/control-point count; explicit restart may be enabled for compatible indexed draws. Tessellation must supply control/evaluation stages; no ambient default tessellation levels. |
| Lines and points | Width and fixed point size 1, static fields. Program point size off by default; explicit enable for shader-sized orb points. World-probe resolve shaders write size 1. Debug selected-orb size 12 uses a distinct static description; varying shader pointSize remains a shader input. No consumer requires dynamic fixed line/point size. |
| Compatibility raster features | All smoothing/test/stipple enables default off; point sprite coordinate origin defaults upper-left. Line and polygon smoothing and point-coordinate origin are core state. Alpha test, point smoothing and line/polygon stipple require compatibility profiles. Alpha defaults Always/reference 0; line repeat/pattern default 1/0xffff; polygon pattern defaults to 128 all-ones bytes. Legacy point-sprite/fixed-function shading modes are excluded from programmable core draws, not accepted as arbitrary complete-pipeline settings. |
| Dynamics | Single viewport and scissor rectangle; scissor enable static. Require viewport every pass/draw and rectangle whenever scissor enabled. Stencil references and blend constant required when declared/used. No viewport arrays initially. Clear area is pass state independent of draw scissor/write masks. |

Active transform feedback and conditional rendering are not permitted across the first-consumer
boundary. Entry must verify inactivity (query once if available and no reliable owner knowledge),
otherwise skip/reject before mutation; binding tracking alone does not prove inactivity. GpuTransformFeedback.Begin/End/Pause/Resume are resource-helper operations outside complete submission and must not enclose these passes. Conditional
rendering needs an explicit inactive engine boundary contract if native status cannot be queried.
These are required checks/contracts, not claims that today's helper implements them. Legacy matrices
and lighting are irrelevant to the linked programmable fullscreen shader. Unsupported exotic raster
modes cannot be admitted merely by leaving them out of the key: disable/restore enumerated compatibility
enables or reject entry if neutralization cannot be validated. Capability-absent state has a canonical
unsupported value and must not generate invalid queries/calls.

Direct lighting explicitly selects the defaults above, dynamic full-target viewport, triangles and
three RGBA16F color slots, no depth/stencil, samples 1. Preserve shader/output mathematics, zero clears
and resource input lifetimes. The production renderer now uses this complete contract; the former
partial LightingPipeline remains only in the test reference used to compare output mathematics.

### Geometry, target and executable contracts

Geometry metadata is immutable structural layout: location, component count, storage scalar type,
float-normalized versus integer/double interpretation, offset, stride, binding slot and divisor.
Buffer identities, instance offsets, index type/range, base vertex and instance count belong to draw
bindings. Reject missing/mismatched active shader attributes and out-of-range draws; no ambient
attribute-constant fallback. Unused supplied attributes are allowed.

The first adapter accepts the owned fullscreen MeshRef produced by UploadMesh: location 0 float3
position and location 1 float2 UV, no color stream, indexed triangles, divisor 0. Sources are
[quad creation](../VanillaGraphicsExpanded/PBR/DirectLightingRenderer.cs) and
[the vertex shader](../VanillaGraphicsExpanded/assets/vanillagraphicsexpanded/shaders/pbr_direct_lighting.vsh).
Record upload metadata at the owning upload boundary and validate the installed engine VAO/index
representation before publishing the adapter; unknown arbitrary MeshRef layouts reject. Existing
installed-engine inspection below establishes ordinary RenderMesh VAO/EBO effects, not every overload.
VGE VAO adapters reuse GpuVertexAttribBinding setup metadata. Pool/grouped/instanced adapters must
establish their index ranges, divisors, draw modes and UseSsbo effects before use; they cannot borrow
the ordinary fullscreen adapter's proof.

Target normalization preserves exact sized internal format (including integer/normalized/sRGB class),
depth/stencil aspects and sample count; normalize nonmultisampled storage's 0 samples to one effective
sample. Never equate RGBA8 with SRGB8_ALPHA8 or RGBA16F with RGBA32F. Route array element i maps fragment
location i to a specific attachment or explicit None. Do not compact holes. Validate scalar class and
output component requirements; explicit discard permits an active output to map to None, while an
accidentally unrouted active output rejects. Attached but unrouted images may be preserved without
being shader outputs. Duplicate writes to one attachment reject. Default framebuffer routes use
explicit front/back buffer tokens, not fabricated color-attachment indices.

Managed metadata comes from [GpuFramebufferAttachment](../VanillaGraphicsExpanded/Rendering/GpuFramebufferAttachment.cs)
and resource format/dimension/sample values. Wrapped engine metadata is captured at framebuffer
rebuild/publication and refreshed with RefreshWrappedFramebuffer/attachment changes, using the existing
[GBufferManager](../VanillaGraphicsExpanded/GBuffer/GBufferManager.cs) boundary. Unsized/unknown formats
require one-time native metadata resolution there, or rejection. Default framebuffer metadata comes
from the actual window surface plus native attachment/sample queries at registration/surface resize,
not assumed RGBA8. GpuFramebuffer.SurfaceMetadata supplies that provider; unpublished default targets still reject, while direct lighting uses managed textures. Check completeness after attachment/storage change and dirty pass entry, not
every unchanged draw. Track resource/attachment revision at the owner lifecycle; same-format resize
refreshes area/dynamics without changing PSO identity. Reject mutation or explicit retirement during an
active pass and require end/rebegin.

Shader realization identity is owner reference plus a monotonically increasing executable revision,
introduced at successful candidate installation in
[GpuProgram.Spirv](../VanillaGraphicsExpanded/Rendering/Shaders/GpuProgram.Spirv.cs).
The lifecycle installs a new GpuProgramInterface and prepared bindings and publishes an explicit
ExecutableRevision only after coherent program/layout/settings publication; failed/superseded
candidates do not advance it. Disposal invalidates use; recycled ProgramId never aliases the old
revision. Retain existing [readiness/activation](../VanillaGraphicsExpanded/Rendering/Shaders/GpuProgram.Preparation.cs),
generated inputs and UBO publication/last-use retirement. Validate vertex/output interfaces once at
preparation through existing contracts/reflection; publish only valid complete objects.

There is no StateCache/PSO context generation requirement in this work, following the user's explicit
removal of context replacement support. Prepared objects, geometry and scopes are confined to the same
live rendering context and renderer lifetime; teardown disposes them before the context ends.
GpuSupport retains registration for capability readiness, including early menu hooks. Its registration
generation is not copied into pipeline keys or cache snapshots. Context switching/loss/replacement
recovery needs separate design and validation later.

### Restoration decisions by boundary

| Boundary | Required restored effects / policy |
| --- | --- |
| Direct-lighting standalone or shared capture | Union of all complete state categories above changed by the pass, plus viewport/scissor/reference/constant, program/engine shader owner, VAO and its EBO association, array buffer, independent read/draw FBO, active texture and touched sampler/texture/UBO/SSBO/image slots. Restore clear helper values if changed. Capture unknown fields, reuse truthful known values. Include global/indexed aliases and prior-shader reactivation effects. No hard-coded engine baseline. |
| Composite/refraction/particle resolve/SSAO preparation | Union all participating pipeline/resource footprints plus allocation, blit/reduction and actual engine SSAO helper effects. GraphicsCommandContext now declares the complete participating pipeline set. Sequential passes share one nonnested interruption. |
| Particle engine redirection | Existing known full-scene scissor and source-alpha output-zero contract, incoming read/draw FBO and borrowed attachment/routing preservation. Verify installed engine entry assumptions when migrating; changed unknown contracts fall back to capture. |
| Liquid depth/surface/volume pools | Shader owner/resources, pool geometry, targets/viewport, indexed MRT blend/masks, depth/cull, UseSsbo and suppression bookkeeping. Capture unknown entry; preserve invalidation until exact pool/engine effects are validated. |
| Raw atlas/resolve/debug entries | Each independent entry declares full pipeline/dynamic changes, target/clear/allocation effects, geometry and shader resources. Capture unknown entry; do not assume a sibling caller's surrounding scope. |
| Engine terrain/tessellation/shared shadows | Partial compatibility boundaries; preserve patch count with finalizer and publish resources through existing owners. Never replace engine depth/blend/geometry policy with fullscreen defaults. |

Known-state restoration requires a documented installed-engine entry invariant (the particle boundary
above); exact-call observation otherwise supplies knowledge, not policy. Cleanup failures invalidate
affected fields and do not publish success. Extend categorized StateCache values/knowledge and boundary
coverage for missing fields; no parallel manager or manual renderer-side restoration. All limits and
extensions remain owned by GpuSupport.

### Deterministic reference and evidence boundaries

Before replacing direct-lighting submission, retain the existing path as a test-only reference and
run both paths in one headless context against separately cleared RGBA16F MRTs. Use the same built
production vertex/fragment shaders and frozen generated inputs: 32x24 and odd 31x19 targets, full
viewport, fixed camera/projection matrices, zNear/zFar, light direction/colors, ambient/point lights,
shadow matrices/maps and options; no clock, jitter or live world dependencies. Freeze texture inputs
per test and publish identical UBO values to both paths.

Reference cases: (1) depth 1 background writes zero to all outputs; (2) depth 0.5 planar diffuse receiver,
nonmetal roughness 0.5, unshadowed constant maps; (3) metallic/roughness extremes and nonzero emissive;
(4) fixed near/far shadow visibility and one point light; (5) negative normal-alpha first-person marker
using explicit G-buffer position; (6) changed inputs on the same pipeline; (7) first-use, repeat, resize
and reload/failure transitions. Compare alpha and all three attachments. Add a separate pre-overlay
capture case proving first-person/held-item markers do not enter borrowed scene inputs, using the
existing boundary fixtures below.

Establish the reference's full neutral baseline explicitly in the fixture (the old partial descriptor
alone cannot defeat hostile state), then exercise the candidate after hostile stencil/depth/blend/masks,
rasterizer/sampling/clip state. Compare each finite component using
`abs(actual-reference) <= 0.001 + 0.002 * abs(reference)`; require exact zero for background, reject
NaN/Inf, report maximum error and mismatched pixels per attachment. Identical draws should normally be
bit-identical on one driver; this half-float tolerance is a ceiling, not permission to hide systematic
differences. Verify restored native state and engine shader ownership on success and exceptions.
Failed preparation must submit no draw and publish no result.

Count native calls and GL queries separately; repeated managed draws must suppress redundant state
calls while publishing changed inputs. Record shader options, sizes, build configuration, driver and
hardware with later receipts. This defines reference methodology; it contains no new executed rendering
baseline, performance measurement or live acceptance. Existing receipts below validate only older
bounded restoration. Fresh reference-versus-migrated results are required before production migration
is complete.

## Configurable raster state contract

All added settings are static pipeline inputs; no new dynamic declaration is needed. Authored inactive
values are validated before canonicalization: disabled alpha parameters become Always/0, disabled line
stipple becomes factor 1/pattern 0xffff, and disabled polygon stipple becomes the all-ones mask. A newly
authored enabled description retains its complete parameters. Boundary snapshots preserve actual
incoming parameters even while their enable is false, without canonicalizing engine state.

Custom shader clip distances are unsupported. Graphics descriptions and prepared realizations
carry no clip-enable mask, and shader packaging does not extract clipping outputs or analyze their
specialization dependencies. VGE graphics-state application explicitly disables all user clip
distances through StateCache. Native masks, knowledge bits, engine adapters and boundary restoration
remain necessary to preserve incoming engine state. Ordinary frustum clipping, clip origin/depth
conventions and depth-clamp policy are unchanged.

ClipControl availability comes from OpenGL 4.5 or ARB_clip_control. Alternate origin/depth conventions
require the caller's projection and reconstruction to agree: zero-to-one projections must emit z in
[0,w], negative-one-to-one projections in [-w,w]. Window depth range stays [0,1]. Upper-left origin changes
viewport Y mapping and the native front-facing area convention; screen-coordinate calculations,
scissor placement, texture reconstruction and authored winding policy must match it. StateCache does
not rewrite matrices, flip winding, or infer reversed-Z. Production conventions remain unchanged.

Polygon stipple uses an immutable 128-byte value: 32 rows bottom-to-top, four bytes per row, most
significant bit first within each byte. Native transfers explicitly use this layout and restore the
borrowed pack/unpack layout, bit order and buffer binding even when an operation fails. This is the
native polygon rasterization mask, not a texture or fragment-shader workaround.

| Added state | Existing owner extended | Application, observation and restoration |
| --- | --- | --- |
| Clip enables and conventions | VGE graphics application fixes clip enables off; RasterizerDesc owns origin/depth conventions; shared GraphicsCapabilities/GpuSupport and RasterizerState/Knowledge retain native engine state | StateCache clip setters and per-distance knowledge; selective boundary queries and exact restore; engine Enable/Disable and ClipControl adapters. |
| Alpha, smoothing, stipple, point origin | RasterizerDesc and categorized raster cache | StateCache setters and ApplyConfigurableRaster; profile-aware queries/restoration including disabled parameters; exact-signature engine adapters. |
| Polygon mask transfer layout | StateCache pixel pack and buffer binding owners, corresponding unpack owner | Canonical upload/readback, exception cleanup, immutable snapshot sharing. Raw engine array uploads retain engine unpack semantics and invalidate only mask knowledge. |
| Unknown native overloads/external mutations | Existing ExecuteExternal and EPipelineState invalidation | ConfigurableRaster invalidation withdraws only added raster knowledge; PixelPack/PixelUnpack and BufferBindings remain independent categories. Unmapped pointer/ref uploads and point-parameter overloads require an explicit external handoff. |

ApplyConfigurableRaster applies only these added settings from a validated complete descriptor. General
complete-pipeline application is now provided by ApplyGraphicsState; production consumer migration
remains pending. The existing partial GlPipelineDesc is not a complete pipeline and cannot bypass
shader activation ownership.

Native feature classification follows [Khronos glEnable](https://raw.githubusercontent.com/KhronosGroup/OpenGL-Refpages/main/gl4/glEnable.xml).
Original validation, before the compiler-derived output contract amendment below: delegated
shader-enabled Debug and Release validation on 2026-10-06 passed 124/124 affected
runtime tests in each configuration, with zero failures/skips. Separate generator suites passed
177/177 each. Native fixtures used NVIDIA RTX 4090 / driver 591.86: OpenGL 4.3 core and OpenGL 4.6
compatibility. Both expose clip control; capability-absent rejection is synthetic evidence only.

The original six ConfigurableRasterizerDescriptionTests covered explicit defaults, every added field's active
identity, copied pattern ownership/equality, inactive canonicalization, enum/range/mask/profile
rejection and the then-authored clip-output declarations. Eight ConfigurableRasterizerGpuTests cases cover
native state agreement, individual clip bits, A-to-B-to-A transitions, redundant-call suppression,
selective invalidation, hostile disabled parameters and boundary restoration on success/exceptions.
Real 32-by-32 fixture draws verify clipping, alpha rejection, line/polygon stipple, upper-left Y
orientation and projected depth matching the selected clip convention. Pack and unpack callbacks
also fail after temporary transfer setup, proving native buffer/layout restoration on that path.
Engine LSB-first bitmap upload and an actual DynamicTexture2D upload exercise cache coherence.
The original generator tests checked authored clip masks; those tests were removed with the
superseded metadata. Existing boundary/cache/description/texture regressions are included in the runtime selection.

Second source review corrected core polygon-smoothing availability, explicit neutral snapshots for
unavailable fields, checked pixel-buffer binding failure, and pixel-store observation in existing
texture/readback owners. No upload algorithm or resource ownership changed. Independent audit found
no source gaps; linked-executable verification and general complete application retain their existing
preparation/application prerequisites. No live-game appearance or performance result is claimed.

Receipts: artifacts/configurable-raster-{debug,release}-tests.log and
artifacts/configurable-raster-generator-release.log. Earlier compilation attempts exposed a missing
OpenTK GetPName member (resolved using the named All enum) and two stale legacy-coverage assertions
(now list the partial descriptor's actual fields). An initial Release shader-cache replacement access
error cleared on retry. These earlier attempts are not substituted for the final passing receipts.

Commands used NUGET_PACKAGES=C:/Users/Sisco/.nuget/packages with default shader compilation enabled:

~~~text
dotnet test VanillaGraphicsExpanded.Tests/VanillaGraphicsExpanded.Tests.csproj -c Debug --no-restore --filter '<selection>' -v quiet --logger 'console;verbosity=detailed'
dotnet test VanillaGraphicsExpanded.Tests/VanillaGraphicsExpanded.Tests.csproj -c Release --no-restore --filter '<selection>' -v quiet --logger 'console;verbosity=detailed'
selection: FullyQualifiedName~ConfigurableRasterizer|FullyQualifiedName~GraphicsPipelineDescriptionTests|FullyQualifiedName~ShaderPipelineIdentityTests|FullyQualifiedName~EngineBoundary|FullyQualifiedName~PipelineStateCoverageTests|FullyQualifiedName~CategorizedStateCacheTests|FullyQualifiedName~GlStateCacheInvalidationTests|FullyQualifiedName~EngineState|FullyQualifiedName~PixelPack|FullyQualifiedName~GpuTextureLifetimeTests|FullyQualifiedName~DynamicTextureReadPixelsTests|FullyQualifiedName~DepthStencilTextureTests
dotnet test ShaderContractGenerator.Tests/ShaderContractGenerator.Tests.csproj --no-build --no-restore --verbosity minimal
dotnet test ShaderContractGenerator.Tests/ShaderContractGenerator.Tests.csproj --configuration Release --no-restore --verbosity minimal
~~~

The generator Debug run reused its successful focused build. Final builds emitted existing
compiler/analyzer warnings and NU1900 vulnerability-feed access warnings; exact warning totals were
not collected. Both final runtime invocations rebuilt successfully before executing the tests.

### Compiler-derived output contract amendment

Historical correction (superseded by the custom-clipping removal above): removes authored clip-output metadata from shader attributes/contracts,
generator emission and shader identity. Descriptor validation now checks only the native clip mask
and implementation limit; it does not claim to verify executable compatibility. The corresponding
unit test verifies that enabled and disabled masks share shader identity while retaining distinct
pipeline identity. Native raster tests continue to use fixture shaders that actually write clip outputs.
The obsolete generator declaration tests were removed.

This metadata-removal correction did not itself implement compiled-output reflection or preparation
tests; those are now covered by Prepared graphics realizations below. Pixel-transfer, texture upload,
rasterizer transitions and engine-boundary restoration were unchanged by the correction.

Fresh delegated validation after this correction passed 124/124 affected runtime tests and 173/173
shader-generator tests in both Debug and Release, with zero failures/skips. Runtime commands used the
same selection above, default shader compilation enabled, and --no-restore; generator commands used
-c Debug/Release --no-restore -v quiet. Logs: artifacts/configurable-raster-{debug,release}-tests.log
(now the correction receipts) and artifacts/configurable-raster-generator-{debug,release}-tests.log.
Builds reported zero compiler errors; existing compiler/analyzer and NU1900 feed-access warnings remain.
Source review and git diff --check passed. No compiled-output verification or live-game result is claimed.

## Immutable graphics descriptions

Complete descriptions now live in `Rendering/Pipeline/Descriptions`. The construction boundary is
[GraphicsPipelineDesc](../VanillaGraphicsExpanded/Rendering/Pipeline/Descriptions/GraphicsPipelineDesc.cs):
required shader/layout/target identities, an explicit dynamic declaration and shared capabilities,
plus optional state values resolved against the design defaults above. The result has only read-only
properties. Existing mutable categorized StateCache storage continues to record native knowledge;
it is not reused as immutable configuration storage.

`DepthStencilDesc`, `StencilFaceDesc`, `RasterizerDesc`, `ColorBlendDesc`, `SamplingDesc`,
`PrimitiveAssemblyDesc` and `OutputDesc` own separate state families. `PipelineValues<T>` copies
sequences and compares elements structurally; every element used by descriptions is itself immutable.
Record value equality composes these fields, while complete descriptor equality and hashing exclude
labels and capability objects. No native object name, framebuffer dimensions, executable revision or
context registration is stored in this reusable key. Future prepared realizations supply executable
revision and renderer-lifetime checks separately.

[ShaderPipelineIdentity](../VanillaGraphicsExpanded/Rendering/Pipeline/Descriptions/ShaderPipelineIdentity.cs)
retains the existing immutable ShaderStageSelection objects from ShaderLoadPlan in canonical stage order.
ShaderStageSelectionComparer in the shader-contract layer compares effective paths, entry points,
structural variants, typed specialization bits, fixed defines and existing resource/interface declarations.
GpuBindingContract supplies matching structural hashing and equivalence without allocating resource-entry
copies. Independently created equivalent selections compare equal regardless of map insertion order.
The whole-program identity caches its hash, but exact equality remains authoritative; it includes the
asset domain and does not introduce GUIDs or an interning registry. Unused option declarations remain
outside effective pipeline identity, while existing reload SameInputs semantics remain unchanged.
Native executable/interface validation remains preparation work; a description is not a prepared or
submittable object.

Shader-selection reuse validation: shader-enabled Debug and Release builds succeeded, and each focused
identity/load-plan/settings/ownership selection passed 31/31 tests with zero skips. Five added cases
cover retained selection references, insertion-order-independent equality/hashing, changed effective
inputs, unused declaration domains and caller mutation isolation. Broader contract selections each
passed 113/116 with three catalog/baseline mismatches: GeneratedCatalogContainsEveryPackagedProgram
(138 expected, 147 actual), ShaderBindingMigrationTests and ShaderMigrationBaselineTests. Source review
locates those assertions in catalog declarations and saved baselines outside the changed identity path;
an older checkout was not executed to establish a reproduced baseline. No baseline files were altered.
Receipts: artifacts/shader-identity-{debug,release}.log and
artifacts/shader-identity-focused-{debug,release}.log.

[VertexLayoutDesc](../VanillaGraphicsExpanded/Rendering/Pipeline/Descriptions/VertexLayoutDesc.cs)
validates and sorts copied attributes by location, rejects duplicate locations/unsupported packing,
and requires shared binding strides/divisors to agree. Instance buffers and index ranges remain draw
arguments. [RenderTargetSignature](../VanillaGraphicsExpanded/Rendering/Pipeline/Descriptions/RenderTargetSignature.cs)
retains sparse output slots and explicit discard policy, exact sized formats, depth/stencil aspects
and normalized sample count. Its bounded sized-color whitelist is in TargetFormatPolicy; unknown,
unsized and compressed formats reject. Actual attachment metadata/routing and linked shader output
compatibility still belong to preparation/pass setup.

Validation runs unconditionally before canonicalization, including invalid inactive enum/float values.
Enabled scissor/stencil/constant blending requires the corresponding declared dynamics; viewport is
always explicit. Depth/stencil enables require matching aspects; integer blending, dual-source factors,
unsupported topology/stage combinations, device-limit violations and unavailable optional features
reject. Polygon mode applies equally to both faces. Clip and compatibility settings follow the
configurable raster contract above; logic operations remain fixed neutral policy.

Inactive culling, stencil, blending, bias, restart, patch size and sampling values resolve to explicit
canonical values. Enabled configurations retain their full authored behavior; constructing another
enabled description validates and retains its supplied settings. Color masks remain significant with
blending disabled. Sample masks have an explicit all-ones suffix: `SamplingDesc.GetMaskWord(index)`
resolves every native word, so a future state applier must visit all supported words, including omitted
ones. Trailing all-ones words do not distinguish identities. This avoids device-dependent defaults and
never permits stale native mask words to survive application.

[GpuSupport.Graphics](../VanillaGraphicsExpanded/Rendering/GpuSupport.Graphics.cs) extends the existing
capability capture with vertex binding/stride/offset limits, sample-mask words, line/point ranges and
optional graphics features alongside its existing resource limits. GpuSupport owns one immutable
`GraphicsCapabilities` instance, exposed through `GpuSupport.Graphics`; all cached context characteristics,
extension flags, graphics limits and resource/compute limits reside in this immutable record. Existing
properties forward to that same storage, including immutable compute-axis arrays. Initialization builds
the complete value privately and publishes it only after all query groups succeed; initialization status,
registration bookkeeping and diagnostic counters remain on GpuSupport. Ordinary reads neither copy the
record nor query GL. GraphicsPipelineDesc uses that shared instance by default; tests may supply
synthetic capabilities of the same type. There is no pipeline-specific capability cache or snapshot
operation. The complete-description baseline is OpenGL 3.3, with optional features checked individually.
Capabilities do not participate in equality or add StateCache context tracking.

Shared coverage also includes UBO and texture-buffer offset alignment, texture-buffer size, label
length and immutable program-binary format lists. Buffer/texture wrappers, shader preparation,
parallel linking and executable caching consume these shared values rather than issuing independent
capability queries. Core-promoted debug, direct-state-access, multi-bind, texture-buffer-range and
clear-texture features accept the corresponding API version as well as extension advertisement.

Target-dependent format support resides in GpuSupport.GetInternalFormatCapabilities, keyed by image
target and sized internal format. It caches immutable support, framebuffer-renderability and sample-count
results on demand; successful capability reinitialization clears that cache. Failed queries are not
cached, and warm reads do not consume pending native errors. This optional query API requires OpenGL
4.3 or ARB_internalformat_query2 and does not impose that requirement on ordinary pipeline descriptions.

Legacy `GlPipelineDesc` is explicitly documented as a partial compatibility override. Its stable mask
numbering and consumers remain unchanged; there is no implicit conversion or shared base/interface
allowing it to masquerade as GraphicsPipelineDesc. Full state application, pass setup and production
consumer migration remain pending. These changes do not establish rendering or performance improvements.

Validation on 2026-10-05: shader-enabled Debug and Release test builds succeeded; the focused selection
passed **39/39 in each configuration, zero skips**. It covers copied/read-only storage, structural
identity and operators, field-sensitive state, inactive canonicalization and re-enable behavior,
shader specialization/layout identity, sparse exact targets, explicit dynamics, invalid/capability
rejection, stable legacy masks, and native GpuSupport limit capture. Second source review found no
remaining discrepancies; completion audit is recorded in the implementation plan.

Shared capability ownership validation on 2026-10-06 passed **41/41 in both Debug and Release,
zero skips**, with shader compilation enabled. Additional integration checks verify that snapshots
copy shared cached values without recapture or native queries, and that default descriptor validation
uses shared native limits. Receipts: `artifacts/shared-support-{debug,release}.log` and
`artifacts/TestResults/shared-support-{debug,release}.trx`.

The subsequent immutable ownership correction passed **41/41 in both Debug and Release, zero skips**,
with shader compilation enabled. Repeated reads now assert reference identity, preserving the pending
native-error and no-recapture checks. Receipts: `artifacts/immutable-support-{debug,release}.log`.

Complete capability consolidation passed **44/44 in both Debug and Release, zero skips**, with shader
compilation enabled. Coverage includes startup behavior, native context/resource/extension values,
immutable compute collections and retained values across explicit reinitialization. The previously
omitted compute shared-memory query now populates its limit using the named OpenTK enum.
Receipts: `artifacts/all-capabilities-{debug,release}.log`.

Expanded shared capability coverage and consumer migration passed **176/176 in both Debug and Release,
zero skips**, with shader compilation enabled. Seven added cases check native global limits/features,
program-binary formats, target-specific format support and sample counts, cache key isolation,
warm error preservation, cache reset and rejection of repeated invalid queries. The selection also
covers startup, UBO submission, shader linking, program-binary caching and texture/buffer consumers.
Receipts: `artifacts/capability-consumers-{debug,release}.log`.

Delegated command (substitute Debug/debug or Release/release):

```powershell
$env:NUGET_PACKAGES = 'C:\Users\Sisco\.nuget\packages'
dotnet test VanillaGraphicsExpanded.Tests/VanillaGraphicsExpanded.Tests.csproj -c <Configuration> --no-restore --filter 'FullyQualifiedName~GraphicsPipelineDescriptionTests|FullyQualifiedName~PipelineStateCoverageTests|FullyQualifiedName~GpuSupportLimitsTests|FullyQualifiedName~GlPipelineDescValidationTests|FullyQualifiedName~GlPipelineStateMaskTests|FullyQualifiedName~GlPipelineDescDebugTests' --logger 'trx;LogFileName=pso-descriptions-<configuration>.trx' --results-directory artifacts/TestResults
```

Receipts: `artifacts/pso-descriptions-{debug,release}-tests.log` and
`artifacts/TestResults/pso-descriptions-{debug,release}.trx`. The initial sandbox profile selected an
unavailable shader compiler package path; using the installed owner package cache resolved it. Release
also encountered a transient shader-cache WriteAtomic access failure before C# tests; the unchanged
retry succeeded (`artifacts/pso-descriptions-release-initial.log` retains the failed attempt).
Offline NU1900 vulnerability-audit warnings and existing analyzer warnings remain. A redundant static
type-pattern test warning was removed afterward without changing test behavior.

The native stride check was corrected to honor its introduction in OpenGL 4.4, instead of relying on
a 4.3 driver's acceptance of the query; see [Khronos OpenGL 4.4, Appendix G.1](https://registry.khronos.org/OpenGL/specs/gl/glspec44.core.pdf).
No production draw was migrated, no new rendered-output comparison or performance measurement was
made, and Vintage Story was not launched. Native capability checks do not establish live rendering acceptance.

## Engine-boundary restoration

The implementation/evidence below records the earlier restoration foundation. Current consumer ownership and retired helper names are reconciled in **Legacy scope inventory and disposition** and **Remaining consumer submission**; historical receipts do not describe the current production helper set.

Inventory and implementation contracts established on 2026-10-05. Categorized cache storage
and declared boundary entry/restoration are implemented. Refraction and independent fullscreen
callbacks are integrated with headless correction evidence. Caller reconciliation and user live
acceptance after the startup correction are complete. Earlier pending statements below describe
the evidence available at those implementation checkpoints; Consolidated acceptance status records
the final disposition. This section records implementation against
the retired restoration plan, under
its retired approved proposal and the
[parent sequencing exception](Rendering.AuthoritativePipelineState.todo).
The retired documents are historical records, not controlling dependencies for current design. Paths below are relative to the repository root. References to existing source names locate
the compatibility implementation; proposed names describe responsibilities to implement.
Second source review and independent audit-stage-completion review passed on 2026-10-05;
the audit confirmed complete inventory/contracts with no unresolved findings. The second review
corrected the caller count and distinguished explicit framebuffer blend application from Bind.

### Entry points and interruption

`HarmonyPatches/WaterRefractionCaptureHook.cs` prefixes
`EntityPlayerShapeRenderer.DoRender3DOpaque(float,bool)` and delegates to
`PBR/Liquids/WaterRefractionCapture.BeforeOverlay`. Eligibility is enabled refraction,
Opaque stage, non-shadow, local player, FirstPerson mode, and no previous attempt this frame.
The Before callback clears attempt/publication; dry geometry and nonpositive dimensions exit
before GPU work. Capture sets attempted and withdraws publication before checking these guards.

Installed assemblies inspected read-only with Mono.Cecil on 2026-10-05:
`G:/Vintagestory/VintagestoryLib.dll` and `G:/Vintagestory/Mods/VSEssentials.dll`,
assembly version 1.22.7.0. `SystemRenderEntities.OnRenderOpaque3D` calls disable-cull at
IL_001c, toggle-blend at IL_002e and enable-depth at IL_003e before invoking
`DoRender3DOpaque` at IL_007a. Player rendering sets hand projection at IL_00c2,
then invokes held-item and batched entity rendering. It does not replay all fullscreen state
on return from the prefix. These calls establish incoming state before interruption, not
a post-capture restoration contract. Preserve actual incoming values, including depth writes;
do not infer them from defaults. Later shader/texture/geometry setup is a separate contract.

The ordinary `DirectLightingRenderer.OnRenderFrame` (Opaque order 9) and
`PBRCompositeRenderer.OnRenderFrame` (Opaque order 11) are independent engine entry points.
They each need their own boundary. Their shared internal draw implementations run inside the
capture adapter's single boundary when invoked by capture. An internal draw must require an
active compatible boundary, rather than silently opening a nested one.

### Complete operation and effect matrix

Classification: **observed** means an exact-signature engine adapter or cache backend tracks
the mutation; **preserved** means the supported adapter explicitly restores it; **reestablished**
means a verified helper or resumed engine operation establishes it before consumption;
**excluded** means the supported operation cannot use that path without another contract.
Observation alone does not imply preservation.

| Operation/source | Effects and incoming-state disposition |
| --- | --- |
| Capture entry / `WaterRefractionCapture.Capture` | Existing outer fixed scope, framebuffer scope, blanket invalidation and duplicate CapturePipeline. Replace with one boundary before allocation. Pipeline disables depth test, blending, cull, scissor, enables all color channels and disables depth writes. Preserve each affected field. |
| `DirectLightingTargets`, `DirectLightingBufferManager.EnsureBuffers` | First use, resize, partial allocation and disposal can bind textures/FBOs and retire resource names. Existing collection and binding scopes own resources; outer independent read/draw scope covers legacy combined-FBO restoration. No viewport mutation until target binding. Preserve binding associations, not deleted names. |
| `DirectLightingRenderer.RenderLighting` | Missing mesh/dimensions/primary returns before scope; failed buffers or shader readiness returns after scope. Matrix/input preparation does not change projection ownership. LightingPipeline has the same six intents as capture. BindWithViewport changes viewport; Clear changes clear color and writes the owned MRT. Shader UseScope and RenderMesh follow; success returns true. Exceptions unwind all scopes. |
| `PBRCompositeRenderer.RenderComposite` capture branch | Withdraws publication; checks stage/mesh/dimensions/primary and isolated lighting. Owns FBO scope before preparation. No ordinary scratch/display target or current-frame GI. Shader readiness can fail. Pipeline and BeginCapture precede target bind; allocation failure is caught and null target exits without a receiver draw. Writes coherent color/depth, publishes only after completed draw, then returns. |
| Ordinary composite target preparation | `PrepareTargets` creates/resizes owned scratch and borrowed primary resolve FBO. Withdraw borrowers before resizing, refresh borrowed source identity. Allocation and shader failures can occur before the current legacy fixed scope; the new adapter must cover the whole operation. |
| Ordinary composite draw and cleanup | Same CompositePipeline; BeginFrame may create publication/reduction targets; shader inputs and UseScope, then primary resolve and display UseScope. `SceneColorParticleCapture.RestoreSsao` binds its owned target, sets viewport and the same six pipeline intents, then draws. `PublishRefractionScene` either publishes directly or runs reduction with CompositePipeline, viewport, shader activation and RenderMesh; catches optional publication failure. Include all these helpers in the callback boundary. |
| `Rendering/GpuFramebuffer.cs` Bind / BindWithViewport / Clear | Binding tracked by StateCache; Bind refreshes dirty attachments but does not apply blend policy. Explicit ApplyAttachmentBlendState (`GpuFramebuffer.Blending.cs`) changes indexed state and needs declared effects if invoked. Viewport currently raw: migrate to declared cache-backed dynamic application. Global ColorMask affects all draw outputs, not only index zero. Clear(r,g,b,a) mutates clear-color helper state: preserve through StateCache clear-operation storage, outside static PSO identity. Clear masks/scissor established by declared draw/clear state. |
| `GpuFramebuffer.Creation.cs` routing/attachments | DrawBuffers/ReadBuffer are FBO-local; creation changes owned FBO routing, not incoming engine FBO routing. Preserve read/draw bindings with existing scope. Mutation of routing on a borrowed engine FBO is excluded from this boundary. No framebuffer blend-policy reapplication is inferred from rebinding. |
| Texture creation / retirement | Existing texture scopes preserve allocation bindings/active unit. Deletion uses existing targeted lifecycle invalidation and deferred retirement. Do not fold bindings into fixed-function structs. |
| `Rendering/Shaders/GpuProgram.cs` preparation, UseScope, disposal | Retain readiness, failed-activation cleanup, prepared input publication and engine CurrentShaderProgram ownership. Existing UseScope stops/restores the previous owner (or compute/raw program fallback). Preserve it, including exceptions; restoration of prior owner may bind its resource inputs. Restore borrowed binding scope after shader cleanup, then pipeline snapshot last. |
| Installed `ClientPlatformWindows.RenderMesh(MeshRef)` | IL binds VAO and element buffer, draws, unbinds element buffer and VAO. No fixed-function or viewport mutation. Exact-signature mappings observe these calls. Engine rendering reestablishes its geometry before drawing; adapter additionally preserves incoming VAO and generic array-buffer binding for strict handoff, without rewriting VAO-owned EBO associations. No SSBO multi-draw overload is used by the fullscreen quad. |
| Shader resources / resumed engine inputs | ShaderProgramBase.Use binds program, default uniforms and selected textures; BindTexture2D mutates active unit/texture/sampler. VGE prepared input publication also changes resource slots. Preserve the active unit and touched texture/sampler/UBO/SSBO/image slots through existing cache binding APIs around the entire operation, after preparing executable resource footprints and before activation. Resolve/query missing incoming slots at entry. Prior owner activation runs first on cleanup. Do not assume every slot is rebound by held-item code or create another binding cache. |
| Unchanged state / unsupported helpers | Blend equations, cull mode/winding, scissor rectangle, depth range, stencil, sampling, clipping and patch/provoking state are not changed by these fullscreen descriptors/helpers. Leave untouched; new helper effects require expanded coverage before mutation. Arbitrary mod/raw GL, external shader callbacks with undeclared effects, context migration mid-operation and engine-FBO routing changes are excluded; fail entry or suspend authority, never assert warm-cache authority over them. |

Resource preservation footprint is the union of prepared shader assignments (including previous
owner restoration), allocation scopes and documented quad helper bindings. Preparation that can
mutate these bindings must itself use existing scoped APIs; otherwise its effects must be included
before it executes. A missing/unbounded foreign shader footprint causes optional capture to skip,
not an assumption that shader reactivation restores everything. Ordinary callbacks surface the
unsupported contract. This is an adapter declaration and existing binding-owner composition,
not a new resource hierarchy or resource cache.

### Legacy scope inventory and disposition

All eleven previously retained production capture invocations have been replaced or removed.
The historical subset implementation now exists only as `LegacyFixedFunctionReference` in GPU
fixtures, including the negative control that reproduces indexed blend corruption. Production
`StateCache.CaptureLegacyFixedFunctionState` and its duplicate snapshot are retired.

| Consumer / historical entry | Current owner and disposition |
| --- | --- |
| `VgeWorldCellBoundsDebugView` | ArrayGraphicsGeometry publishes line streams; DebugGraphicsSubmission owns depth/no-depth variants and line width. |
| `VgeGBufferOverlayDebugView` | Owned DebugTextureShaderProgram and EngineFullscreenGeometry replace engine blit activation. |
| `LumOnDebugRenderer` OIT | Explicit line/point layouts and complete per-mode descriptions; orbs declare six indexed output policies, depth writing and shader point size. |
| `LumOnDebugRenderer` AfterBlit | Complete fullscreen pipeline and published actual window-surface metadata; no renderer-side viewport/scissor copies. |
| `RenderWorldProbeClipmapBoundsFrozen` | Dormant method and unused frozen capture removed entirely. |
| `LumOnDebugRenderer` normal-depth atlas | Same owned texture shader and complete debug submission as other overlays. |
| `LumOnWorldProbeClipmapGpuUploader.UploadCpu` | One boundary declares both point resolve pipelines and explicit radiance/metadata passes. |
| `MaterialAtlasNormalDepthGpuBuilder` BakePerTexture / ClearAtlasPage / BakePerRect | Independent complete boundaries; shader-free clear pass, bounded procedural triangle, explicit solver targets and typed load operations. |
| `WaterVolumeRenderer` | LiquidGraphicsSubmission owns additive two-output policy, viewport and UseSsbo restoration; engine pool managers retain culling/transforms. |

Related composite/capture coordination also uses complete pipelines for direct lighting, composition,
receiver/reduction, SSAO and display, declaring all participating realizations before shared entry.

`FullscreenBoundary` is retired from production. Its partial-description behavior survives only
in `ReferenceFullscreenBoundary` for the independent direct-lighting comparison renderer.
`GpuFramebuffer` no longer stores or applies blend policy. Complete descriptions own migrated
blend state; GBufferManager and redirected engine particle draws retain their explicit partial
compatibility policies because their actual draw owner remains the engine.

See **Remaining consumer submission** for implementation traceability and verification status.

### State representation and source layout decisions

Value types go individually in `Rendering/Pipeline/State/`, namespace
`VanillaGraphicsExpanded.Rendering.Pipeline.State`. StateCache knowledge, query, transition and
snapshot partials remain in `Rendering/`. Engine boundary declarations/adapters go in
`Rendering/Integration/`; context identity provider is separate from adapter policy.
Existing partial descriptor masks retain their stable numbering. No complete submission or
pipeline registry is introduced by the bounded restoration work.

| Existing authoritative field (all fixed-function partials) | Concrete value category / policy |
| --- | --- |
| depthTestEnabled, depthFunc, depthWriteMask (`StateCache.cs`) | DepthState: enable, comparison, write; static. |
| blendEnabled, blendFunc, blendEnabledIndexed, blendFuncIndexed (`StateCache.cs`) | BlendState: effective per-output enables and independent RGB/alpha factors; static. Global values become derived uniform assertions, never independently restored state. |
| colorMask (`StateCache.cs`) | BlendState: per-output color write masks; static. Add indexed tracking/query/application because global ColorMask overwrites every supported output. |
| cullFaceEnabled, scissorTestEnabled, lineWidth, pointSize (`StateCache.cs`) | RasterizerState: cull/scissor enables and static line width/point size. No present consumer needs a dynamic declaration for the latter two. |
| provokingVertex (`StateCache.Patches.cs`) | RasterizerState: static provoking convention, following parent rasterizer contract. |
| patchVertices (`StateCache.Patches.cs`) | PrimitiveAssemblyState: static control-point count. |
| New viewport | DynamicDrawState: one integer rectangle, supplied by target/pass dimensions; excluded from pipeline identity. Scissor rectangle is dynamic when a later consumer requires it. |

Corresponding cache knowledge is separate field flags and per-output flags; a known comparison
does not imply known depth enable/write. Blend uniform assertions require every supported index
known and equal. Arrays belong to the cache; snapshot/descriptor publication deep-copies them.
Invalidation clears only knowledge, not snapshots or native state. Existing legacy snapshot fields
and scissor restore descriptor are copies, not additional authoritative cache fields; migrate them
to category values/coverage when their scopes migrate. Capability limits, counters, PixelPackState,
program/VAO/FBO/texture/sampler/buffer/image/feedback bindings and retirement associations remain
with their existing owners. Clear-color helper state gets a focused resource-operation value/known
flag in StateCache, not a static graphics category. No unsupported stencil/sampling state is added.

### Boundary API, coverage and failures

Chosen contract: immutable `EngineBoundaryDeclaration` composed from
`PipelineStateCoverage.From(desc)`, dynamic declarations and named helper effects;
`StateCache.TryBeginEngineBoundary(declaration, out scope)` resolves required incoming values
before drawing-state mutations. `EngineBoundaryScope` is a sealed, exactly-once disposable owner
of an internal `PipelineStateSnapshot` (category values, explicit coverage, context token).
It is not a PSO, has no pipeline key and cannot alias mutable indexed cache payloads.
Public VGE draw code uses PSO Apply or declared dynamic application. StateCache is the sole
native query/transition/restoration owner. No renderer stores a parallel list of pipeline values.

Effective coverage is the union of participating descriptor intents and helper effects, closed
over aliases: global blend enable/factors and color masks expand to every MaxDrawBuffers index,
not the temporary target's attachments. FBO-stored blend-policy effects are included when present.
Ordinary composite additionally unions ResolvePipeline/reduction declarations. The capture's
first required set is depth enable/write, cull/scissor enable, per-output blend enables and masks,
viewport and clear color; factors are included whenever helper/cleanup declarations can modify
them. Unchanged depth comparison, equations and static widths do not require preservation just
because the old helper captured them. Every managed mutation checks its affected set against
active coverage before issuing GL, including clear helpers and engine adapters during cleanup.
An unsupported managed mutation fails before native execution. Entry query failure returns no
scope/no draw; it may update truthful knowledge but cannot change native state or publish capture.

Non-nesting is enforced per active context/cache. A second Begin fails before mutation. Dispose
marks the scope consumed before cleanup so repeated disposal cannot replay restoration. Cleanup
attempts all independent owners even if one fails: shader ownership, resource binding scopes,
independent read/draw FBO bindings, final pipeline/dynamic/helper restoration. Aggregate cleanup
failures with the operation error; do not let a later error erase an earlier restoration failure.
Native transition failure makes affected knowledge unknown and throws `EngineBoundaryRestoreException`.
The optional BeforeOverlay catch may swallow operation failure only after successful cleanup;
it must propagate restoration/context failure (including inside aggregates). Publication is withdrawn
on either failure. Ordinary callback restoration failures also propagate. No catch may label a
failed restore safe continuation. Unknown current cache values are restored without draw-time
queries; warm equal values suppress native calls. Saved snapshots survive selective invalidation.

Viewport observation adds the exact `GL.Viewport(int,int,int,int)` signature used by installed
`ClientPlatformWindows.GlViewport`; rectangle convenience overloads delegate to the same cache
owner in VGE. Unsupported float/indexed viewport overloads are excluded until inventoried;
if discovered in supported engine targets, require an exact adapter or explicit invalidation
before relying on cached viewport. Indexed color masks require `GL.ColorMask(int,bool,bool,bool,bool)`
and matching observation where engine IL uses it. ClearColor's four-float signature also routes
through cache for warm helper-state knowledge. Queries occur at boundaries only for missing values.

### Context identity and recovery

Use OpenTK's GLFW current-window pointer (`GLFW.GetCurrentContext()`) plus the reference identity
of the engine's `GameWindowNative` owner and a monotonically allocated registration generation.
Installed `ClientPlatformWindows.window` owns GameWindowNative (OpenTK GameWindow); its constructor
uses NativeWindow.Context. Installed OpenTK 4.9.4 exposes GetCurrentContext, MakeContextCurrent
and DestroyWindow. Do not use `GlExtensions.TryGetContextKey`: vendor/version/renderer strings
are device characteristics and can coincide across replacement contexts.

Before the first routed engine state call, register the actual owner/current handle through the
engine adapter's focused provider. Hooks installed in StartPre can run during menu rendering before
StartClientSide; renderer initialization alone is too late. Later initialization reuses the registration;
headless fixtures register their own NativeWindow owner. Retire registration on owner disposal,
and allocate a new generation when initialization supplies a different owner/context. At boundary
entry and exit require the registered live owner and current handle to match; missing registration,
zero handle, changed handle or disposed owner rejects entry. A context switch makes cache knowledge
unknown; returning does not revive old knowledge. GpuSupport refreshes immutable capabilities when its
registered context identity changes, without resetting diagnostic totals. Mismatch at exit consumes the snapshot without
issuing GL into the replacement context, invalidates old knowledge and propagates restore failure.
Resources must follow existing shutdown/reinitialization ownership; this mechanism never deletes
old-context names in a new context or attempts automatic resource recreation. Same-owner context
recreation is unsupported unless explicit retirement/re-registration advances generation first.
World leave, resize and shader reload are not context replacement and must not advance generation.
This conservative policy defines recovery without claiming arbitrary context-switch support.

### Deterministic reference cases and evidence boundaries

For subsequent unit/headless verification use engine-like depth enable/write and mixed output
blending (0–2 enabled; metadata outputs disabled), nonuniform masks/factors and hostile viewport.
Record actual native state before/after, cache agreement, boundary queries/native call counts,
and compare lighting/composite images with identical inputs on the compatibility reference and
migrated paths. Fix camera matrices, textures, options and dimensions. Compare floating outputs
with format-appropriate tolerances and exact publication/depth/marker decisions; retain clean
world pair and follow capture with the real negative-marker first-person draw. Exercise cold,
warm, partially unknown, invalidated, first-use, repeat, odd-size resize, reload, disabled/dry,
failed allocation/readiness/activation, thrown draw, cleanup failure, nesting, double Dispose and
context mismatch/re-registration. Test ordinary composite with both SSAO restoration and reduction
enabled; isolated capture must not run ordinary display/GI paths. Prove no draw after failed entry,
no aliasing snapshot arrays, truthful unknown state after failures and unaffected binding retirement.

Existing supplied RenderDoc investigation is defect evidence only: events 5989 versus 6244/6343
show metadata blending changed; normal alpha 5 covers 81,320 held-item pixels. Numeric outputs
remain in `artifacts/refraction-cutoff/numeric.txt`; capture source is
`C:/Users/Sisco/Desktop/refraction-cutoff.rdc`. Fresh read-only installed IL inspection confirms
the supported entry/helper signatures. No build or graphics test is needed to establish this
inventory; no runtime code changed. All later builds/tests must be delegated. Live fix acceptance
and performance measurements remain pending; never launch the game. UV cutoff remains separate.

### Proposal traceability

| Restoration proposal section | Plan work / inventory evidence |
| --- | --- |
| Problem and Architectural contract | Historical capture establishes the defect; FullscreenBoundary and actual capture/callback marker tests establish automated correction, not live appearance. |
| Categorized state representation / Source organization | Complete field table; categorized values and separate knowledge partials; CategorizedStateCacheTests and PipelineStateCoverageTests. Resource owners remain separate. |
| Proposed boundary mechanism (entry/application/global-indexed) | BoundaryEntry, PipelineStateSnapshot and BoundaryRestoration; EngineBoundaryEntryTests and EngineBoundaryRestorationTests cover resolved values, alias closure, invalidation, context and failures. |
| Dynamic state, bindings, shader ownership | StateCache.Dynamic, EngineBoundaryScope and BoundaryBindings; EngineBoundaryBindingTests, GpuProgramUseScopeTests and real callback/viewport/FBO regressions. |
| Authority, invalidation, lifetime | RenderContextRegistry, boundary context validation, ExecuteExternal and targeted resource retirement; context, external-mutation, deletion and unaffected-binding regressions. |
| Refraction integration and compatibility | One capture boundary and independent lighting/composite adapters; actual marker/publication regressions; all eleven historical invocations now have the dispositions recorded above. |
| Relationship to approved PSO work | Parent exception governs this bounded work; future command context reuses cache mechanism, complete pipeline adoption remains parent work. |
| Verification and acceptance | Baseline 213 passing tests plus 97 fresh affected tests after the startup correction; delegated builds and measured fixture counters below. User live confirmation received on 2026-10-05; no new RenderDoc inspection claimed. |

The approved parent architecture's ownership, static/dynamic policy, engine restoration and context
generation requirements govern these decisions. Complete descriptors, target signatures, preparation
and full submission are future parent-plan obligations, not inventory completion criteria here.

### Categorized storage and cache-backed coverage

The fixed-function foundation now lives in `Rendering/Pipeline/State/`: `DepthState`,
`BlendState` (one draw output), `RasterizerState`, `PrimitiveAssemblyState` and
`DynamicDrawState`. `StateCache.FixedFunctionStorage.cs` owns their values and separate
field/index knowledge, represented by byte-backed `[Flags]` enums. Knowledge checks use .NET
`HasFlag`; setting and clearing use enum bitwise operators, preserving unrelated flags. The former nullable fixed-function fields are removed, including the
patch/provoking fields and the scissor preservation reader. Bindings, VAO element associations,
resource retirement and shader ownership retain their existing implementations.

`StateCache.Blending.cs` uses the current context's `MaxDrawBuffers`, independently of bound
framebuffer routing. Global operations establish every output; indexed operations update only
that output. A global operation is suppressed only when every affected output is known and equal.
Enables, factors and masks have independent validity. `CopyBlendValues` copies an array of value
structs, so subsequent live cache mutation cannot alter the copy. It does not implicitly resolve
unknown fields or provide a usable boundary snapshot; explicit snapshot coverage remains required.

`StateCache.Dynamic.cs` applies integer viewports outside static pipeline identity, normalizing
sizes to cached implementation limits. `GpuFramebuffer.BindWithViewport`, existing VGE viewport
calls and legacy viewport cleanup use this owner. Routing the remaining known VGE callers is
necessary to prevent the newly cached state from becoming stale; it does not migrate their
restoration contracts. `StateCache.ClearOperations.cs` tracks the native clear-color value as
resource-operation state. Existing VGE clear-color calls and exact engine signatures now use it.
`EngineStateCalls` adds the integer viewport, indexed mask and four-float clear-color mappings;
the existing startup discovery performs observation without a second native call or recursive hook.

`RenderContextRegistry` combines the current GLFW pointer, weak owner identity and monotonically
allocated registration generation. `EngineRenderContext` registers the actual
`ScreenManager.Platform.window` at renderer initialization after checking its pointer is current.
`RenderContextLifetimeHook` retires that owner before `NativeWindow.Dispose(bool)`; owner liveness
is checked as an additional fallback. World leave, resize and shader reload do not retire it.
The existing headless fixture owns a raw GLFW window rather than an OpenTK NativeWindow wrapper,
so it registers the fixture as that window's owner and explicitly retires before destruction.
This preserves the same pointer/owner/generation contract without replacing the established fixture.

Context changes withdraw mutable knowledge. GpuSupport owns draw-buffer, viewport, patch, binding-slot
and buffer-alignment limits and refreshes them when its registered context generation changes. A
temporary detach and return to the same live context can reuse immutable capabilities; mutable state
remains unknown. Missing registration also prevents reuse of authoritative knowledge; future
boundary entry must reject it. Resource names are neither deleted nor recreated by this mechanism.
Scalar setters reject invalid enum/size inputs before native mutation and publish knowledge only
after the native call returns. Native restoration failure handling is supplied by the new scoped
mechanism below, not by the retained legacy scopes.

The installed 1.22.7 engine and OpenTK 4.9.4 metadata were checked again for the public platform/window
fields and the protected `NativeWindow.Dispose(bool)` signature. Automated evidence does not launch
Vintage Story or establish live refraction correctness. Declared boundary entry is recorded below;
restoration is documented below and production refraction adapter integration remains pending.

| Restoration plan task group | Controlling source | Implementation and verification |
| --- | --- | --- |
| Complete categorized storage, separate knowledge, copies | Restoration proposal / Categorized state representation; inventory / State representation and source layout decisions | Category value files, StateCache storage and scalar/patch/scissor partials; categorized native-state and invalidation tests. |
| Global/indexed aliases and supported output extent | Restoration proposal / Global and indexed state | StateCache.Blending; native mixed enables/factors/masks, highest supported index, independent copies, suppression and MRT rendered output tests. |
| Viewport and clear helper coverage | Restoration proposal / Dynamic state, bindings, and shader ownership; inventory effect matrix | StateCache dynamic/clear owners, engine mappings, framebuffer and existing caller routing; native viewport/clear values, clamping, repeated calls and selective validity tests. |
| Context knowledge and capability lifetime | Parent architecture / Engine integration and cache authority; inventory / Context identity and recovery | Context registry, engine initialization/retirement and fixture lifecycle; actual context switch/replacement, missing/dead registration, re-registration and capability refresh tests. |
| Unaffected resource ownership and transition behavior | Restoration proposal / Authority, invalidation, and lifetime; parent architecture / resource retirement | Existing engine-state, resource-deletion, VAO/buffer, framebuffer, unbind and scissor regressions retained. |

Delegated Release validation passed on 2026-10-05: 59 tests, zero failures or skips; build
completed with zero errors and 101 warnings. The run includes the water-refraction
compatibility regression and successful installation/removal of the native-window disposal hook.
Shader build receipts remained enabled. Commands used the installed package cache via
`NUGET_PACKAGES=C:/Users/Sisco/.nuget/packages`:

- `dotnet build VanillaGraphicsExpanded.Tests/VanillaGraphicsExpanded.Tests.csproj -c Release --no-restore -v quiet`
- `dotnet test VanillaGraphicsExpanded.Tests/VanillaGraphicsExpanded.Tests.csproj -c Release --no-build --no-restore --filter 'FullyQualifiedName~CategorizedStateCacheTests|FullyQualifiedName~GlStateCacheInvalidationTests|FullyQualifiedName~EngineState|FullyQualifiedName~GpuFramebufferBlendStateIntegrationTests|FullyQualifiedName~FramebufferBindingStateTests|FullyQualifiedName~StateCacheResourceDeletionTests|FullyQualifiedName~GlStateCacheUnbindIntegrationTests|FullyQualifiedName~ScissorStateScopeTests|FullyQualifiedName~WaterRefractionCaptureStateTests' --logger 'trx;LogFileName=phase2-state-validation.trx'`

Receipts: `artifacts/phase2-build-validation.log` and
`VanillaGraphicsExpanded.Tests/TestResults/phase2-state-validation.trx`.
Tests measure zero additional native calls for repeated known scalar/global/indexed/dynamic/helper
operations, and reapplication only for selectively invalidated scalar fields. Capability resolution
is repeated after a context switch. These are operation-count checks, not CPU/GPU speedup claims.

The second source review retained Debug PSO labels, migrated the old scissor reader, narrowed legacy
invalidation to its existing footprint, routed remaining viewport/clear mutations, and added invalid
input checks. The separate audit-stage-completion pass reconciled storage, transitions, context
lifetime, exact engine signatures, copy independence and resource regression evidence against the
restoration proposal and parent architecture. Actual boundary capture/cleanup guarantees belong to
the subsequent snapshot/restoration work; the plan now states that dependency explicitly for clear
color. No scope has been removed and the original mixed-state refraction restoration defect is not
claimed fixed by this foundation.

Delegated Debug compilation also passed (zero errors, 106 warnings), including the retained PSO
debug-group branch: `dotnet build VanillaGraphicsExpanded.Tests/VanillaGraphicsExpanded.Tests.csproj
-c Debug --no-restore -v quiet`, with the same package-cache environment. Receipt:
`artifacts/phase2-debug-build-validation.log`. Final source review and the independent contract audit
found no remaining foundational coverage issues. The later entry work is recorded below;
production consumer migration and live acceptance remain open.

### Declared boundary entry and resolved snapshots

`Rendering/Pipeline/PipelineStateCoverage.cs` derives field coverage from both descriptor intent
masks and copies indexed output identifiers. Declarations union that coverage with explicit dynamic
and helper effects in `Rendering/Integration/EngineBoundaryDeclaration.cs`. They retain no pipeline
values, executable identity or resource ownership. Global blend enable/factors and write-mask effects
cover all supported draw outputs. Indexed declarations are validated against the context limit,
independently of framebuffer attachments. Category flag enums identify covered fields here; their
values describe the contract, not a copy of the cache's current validity flags.

`StateCache.BoundaryEntry.cs` resolves missing covered state before publishing a token. It requires
a live registered context and checks the handle/generation again after resolution. Nested entry,
including reentrant entry during resolution, is rejected. Missing registration, unsupported output
indices and query failures return no scope, retain a diagnostic exception and issue no drawing-state
transition. Earlier successful queries may leave truthful partial knowledge; no default is substituted
for a failed read. Native error status is checked around cold reads and may be consumed on rejected
entry; this does not change native drawing state.

`StateCache.BoundaryQueries.cs` owns those reads. Depth, rasterizer, assembly, viewport, clear color,
and each output's enable/factors/mask are independently resolved only when required and unknown.
Factor knowledge is published after all four component queries succeed. Viewport and patch limits
are resolved at entry when needed, preventing first-use capability queries in managed draws.
`PipelineStateSnapshot` copies category values, retains explicit immutable coverage and the context
token, and privately clones indexed storage. Its output accessor returns a value copy. Uncovered
fields are not captured values even when they share a category struct with covered fields.

`StateCache.BoundaryValidation.cs` enforces coverage before setters issue native operations or
suppress identical known requests. `Apply` checks the whole descriptor before its first transition;
an undeclared later field cannot leave earlier pipeline fields changed. Scalar/global/indexed backend
setters, dynamic application, clear-color helpers and the engine adapters use the same guard.
Unsupported capability/patch forwarding is rejected while a boundary is active. Legacy capture and
unknown patch/provoking getters cannot introduce nested capture or draw-time state queries.
Resource-binding owners remain separate; this work does not claim a complete borrowed-binding handoff.

The initial `EngineBoundaryScope` entry token is now extended by the disposable restoration mechanism
documented below. `ReleaseEngineBoundary` remains a cache-owner primitive which only releases active
registration; entry-only tests use it, while restoration owners call it after cleanup. Production
renderers had not migrated when entry validation was recorded. The subsequent integration and
headless refraction correction are documented below; unrelated legacy consumers remain in place.

| Restoration plan task group | Controlling source | Implementation and evidence |
| --- | --- | --- |
| Descriptor/dynamic/helper coverage and global aliases | Restoration proposal / Boundary declaration and entry; Global and indexed state; inventory / Boundary API, coverage and failures | PipelineStateCoverage, EngineBoundaryDeclaration; PipelineStateCoverageTests verify both intent masks, all descriptor fields, alias containment and copied declaration payloads. |
| Complete resolved incoming snapshot, selective queries and no defaults | Restoration proposal / Categorized state representation; Boundary declaration and entry | BoundaryEntry/BoundaryQueries and PipelineStateSnapshot; EngineBoundaryEntryTests verify cold/warm/partial depth entry, all categories, mixed indexed values, query-error rejection and native-state agreement. |
| Independent indexed ownership and explicit coverage | Restoration proposal / Categorized state representation; parent architecture / Complete descriptions and partial overrides | Private cloned snapshot payload; tests mutate live arrays through global setters, invalidate knowledge and verify retained mixed values; narrow coverage leaves unrelated depth fields unknown. |
| Context-bound entry and nesting | Restoration proposal / Authority, invalidation, and lifetime; inventory / Context identity and recovery | Registered-context checks and active/resolving guards; tests cover missing current context, absent registration, replacement generation and nested entry. |
| Validate every supported drawing-state mutation before native work | Restoration proposal / Architectural contract; Authority, invalidation, and lifetime; parent architecture / Engine integration and cache authority | BoundaryValidation and shared setter guards; tests reject whole PSOs, global aliases, unlisted outputs, unsupported capabilities and identical known out-of-contract requests without native transitions. |

Operation counts distinguish state/capability value reads from the native error-status checks around
each cold read. With output capability already cached, complete cold depth entry performs three value
reads, warm re-entry performs zero, and invalidating depth then reestablishing only comparison leaves
two reads. Cold enable/factors/mask capture performs six value reads per supported output. Entry issues
zero drawing-state transitions. Covered viewport/patch application adds no capability or state reads.
These are deterministic operation-count assertions, not CPU/GPU performance measurements.

Delegated validation passed on 2026-10-05: Release build completed with zero errors and 101 warnings;
76 focused tests passed with zero failures or skips. This includes the 17 new coverage/entry cases
and the existing categorized state, invalidation, engine mapping, framebuffer/blending, resource
retirement, unbinding, scissor and refraction compatibility regressions. The real failed-query case
checks rejection of a GL query's default return when it also reports a native error; the separate
entry-failure case proves no token or drawing-state transition is published.

Commands used `NUGET_PACKAGES=C:/Users/Sisco/.nuget/packages`, with shader receipts enabled:

- `dotnet build VanillaGraphicsExpanded.Tests/VanillaGraphicsExpanded.Tests.csproj -c Release --no-restore -v quiet`
- `dotnet test VanillaGraphicsExpanded.Tests/VanillaGraphicsExpanded.Tests.csproj -c Release --no-build --no-restore --filter 'FullyQualifiedName~EngineBoundaryEntryTests|FullyQualifiedName~PipelineStateCoverageTests|FullyQualifiedName~CategorizedStateCacheTests|FullyQualifiedName~GlStateCacheInvalidationTests|FullyQualifiedName~EngineState|FullyQualifiedName~GpuFramebufferBlendStateIntegrationTests|FullyQualifiedName~FramebufferBindingStateTests|FullyQualifiedName~StateCacheResourceDeletionTests|FullyQualifiedName~GlStateCacheUnbindIntegrationTests|FullyQualifiedName~ScissorStateScopeTests|FullyQualifiedName~WaterRefractionCaptureStateTests' --logger 'trx;LogFileName=phase3-state-validation.trx'`
- `dotnet build VanillaGraphicsExpanded.Tests/VanillaGraphicsExpanded.Tests.csproj -c Debug --no-restore -v quiet`

Receipts: `artifacts/phase3-build-validation.log`, `artifacts/phase3-test-validation.log`, and
`VanillaGraphicsExpanded.Tests/TestResults/phase3-state-validation.trx`. The initial Debug build
encountered a shader-cache atomic-write access failure, recorded in
`artifacts/phase3-debug-build-validation.log`. A retry passed with zero errors and 106 warnings,
without source or shader-setting changes: `artifacts/phase3-debug-build-retry.log`.

The second source review checked all native mutation sites, separated validation from entry
orchestration, and added explicit registration/replacement and real native query-failure evidence.
The independent audit-stage-completion pass reconsulted the restoration contract, parent authority
rules and inventory decisions, then reconciled each entry requirement against source and final
receipts. No required entry/snapshot finding remains. Restoration evidence follows below; production
consumer adapters and live acceptance remain pending. No game was launched.

### Scoped restoration, borrowed bindings and failure guarantees

`EngineBoundaryScope` now implements exactly-once disposal. Its `Run` method retains the operation
exception, consumes the scope before cleanup, and attempts registered shader scopes, borrowed bindings,
independent framebuffer scopes, then fixed-function/dynamic/helper restoration in that order. Cleanup
within each owner group is reversed. Operation code cannot dispose the boundary early or nest `Run`.
Adapters register existing `GpuProgram.UseScope()` owners with `AddCleanup`; registration transfers
cleanup responsibility to the boundary, not ownership of the underlying shader or resources.

`StateCache.BoundaryRestoration.cs` restores only snapshot coverage through existing setters. It
compares valid current knowledge, reapplies unknown values without queries, and restores mixed enables,
factors and masks by output index without a trailing global overwrite. It never uses blanket
invalidation to force restoration. Native errors and thrown transitions leave the affected field
unknown and are collected while independent fields continue. Context identity/generation is checked
before operation and cleanup owners. A mismatch consumes the boundary, discards obsolete knowledge
through the existing context owner, and issues no old-context cleanup into the replacement context.

`EngineBoundaryRestoreException` distinguishes an unsafe handoff from an ordinary operation failure.
`Run` aggregates both when cleanup fails; it rethrows the original operation exception when cleanup
succeeds. The existing shader `UseScope` owner now also retains failed activation and failed rollback
together. `ShaderOwnershipRestoreException` identifies that failure even when it occurs before a
shader scope can be registered. `IsRestorationFailure` recognizes both kinds inside aggregates.
Binding APIs retain compatibility behavior outside boundaries but no longer swallow native exceptions
under active entry/restoration authority. Failed shader cleanup withdraws program knowledge.

`Rendering/Integration/EngineBoundaryResources.cs` derives active texture/sampler, image and indexed
buffer footprints from `GpuPreparedBindings`, unions participating/helper effects and copies payloads.
`EngineBoundaryExecution.TryRun` includes the incoming prepared VGE shader footprint. It rejects
foreign engine owners, unowned raw/compute programs and incoming owners requiring preparation before
optional work; those paths need an independently verified adapter before they can be supported.
Shader preparation and footprint declaration precede entry. Arbitrary callbacks or shader reloads
which expand the declared footprint mid-operation are not supported.

`StateCache.BoundaryBindings.cs` resolves missing slots in the existing binding cache. It captures all
image-view parameters and indexed buffer offsets/sizes, preserves generic buffer aliases after indexed
restoration, restores the active texture unit, incoming VAO and generic array buffer, and reuses separate
read/draw `FramebufferScope` owners. VAO-owned element-buffer associations are not rewritten. Texture
queries may temporarily select another unit; entry restores that selector in a checked finally block.
A selector-restoration failure is surfaced as a failed handoff, never reported as unchanged optional
entry. Unsupported slot limits fail before cache-array allocation or native selection, using the
GpuSupport capability snapshot rather than a separate StateCache limit cache. Unknown incoming
buffer bindings preserve the queried effective range; resizing borrowed storage during the interruption
is outside the supported resource contract.

Borrowed snapshots hold copied binding values, not a second live cache or resource ownership. Existing
tracked deletion paths record retired names for the active boundary. Cleanup rejects those names even
if a numeric name could subsequently be reused. Resource deletion, deferred retirement and unrelated
binding knowledge retain their existing owners. No renderer performs native capture or manual restore.
`ExecuteExternal` requires managed boundaries to have ended and invalidates only its declared affected
categories in a finally block, including when external work throws.

| Restoration contract item | Controlling source | Implementation and verification |
| --- | --- | --- |
| Effective-set restoration, mixed aliases, invalidation and no-op suppression | Restoration proposal / Application and restoration; Global and indexed state | BoundaryRestoration; native mixed output and all-category restoration tests, with and without invalidated knowledge. |
| Ordered, exactly-once cleanup and retained operation errors | Restoration proposal / Dynamic state, bindings, and shader ownership; inventory / Boundary API, coverage and failures | EngineBoundaryScope, cleanup ordering, real UseScope ownership, draw/setup failure, multiple-owner failure and repeat-disposal tests. |
| Declared prepared footprint, bindings and retirement | Inventory / Complete operation and effect matrix; parent architecture / resource ownership and engine integration | EngineBoundaryResources, BoundaryBindings and existing binding/deletion owners; native texture/sampler/image/UBO/SSBO, active unit, geometry, independent FBO and retired-name tests. |
| Context mismatch and truthful failure state | Restoration proposal / Authority, invalidation, and lifetime; inventory / Context identity and recovery | Per-owner context checks, checked transitions and targeted unknown flags; context replacement, native error and real shader rollback failure tests. |
| Explicit unknown/external boundaries | Restoration proposal / Authority, invalidation, and lifetime; parent architecture / Submission contract | ExecuteExternal and conservative foreign-owner rejection; active-boundary rejection, targeted finally invalidation and no-operation tests. |

Delegated validation passed on 2026-10-05: Release build had zero errors and 107 warnings; Debug had
zero errors and 106 warnings. All 105 focused cases passed, with zero failures or skips. The suite
includes entry, coverage, restoration/binding tests and existing cache, engine mapping, resource
retirement, framebuffer, shader ownership, generated resource/image binding and refraction regressions.
Shader build receipts stayed enabled and no game was launched. Commands used
`NUGET_PACKAGES=C:/Users/Sisco/.nuget/packages`:

- `dotnet build VanillaGraphicsExpanded.Tests/VanillaGraphicsExpanded.Tests.csproj -c Release --no-restore -v quiet`
- `dotnet test VanillaGraphicsExpanded.Tests/VanillaGraphicsExpanded.Tests.csproj -c Release --no-build --no-restore --filter 'FullyQualifiedName~EngineBoundary|FullyQualifiedName~PipelineStateCoverageTests|FullyQualifiedName~CategorizedStateCacheTests|FullyQualifiedName~GlStateCacheInvalidationTests|FullyQualifiedName~EngineState|FullyQualifiedName~GpuFramebufferBlendStateIntegrationTests|FullyQualifiedName~FramebufferBindingStateTests|FullyQualifiedName~StateCacheResourceDeletionTests|FullyQualifiedName~GlStateCacheUnbindIntegrationTests|FullyQualifiedName~ScissorStateScopeTests|FullyQualifiedName~WaterRefractionCaptureStateTests|FullyQualifiedName~GpuProgramUseScopeTests|FullyQualifiedName~GeneratedResourceBindingTests|FullyQualifiedName~GpuImageUnitBindingIntegrationTests' --logger 'trx;LogFileName=phase4-state-validation.trx'`
- `dotnet build VanillaGraphicsExpanded.Tests/VanillaGraphicsExpanded.Tests.csproj -c Debug --no-restore -v quiet`

Receipts: `artifacts/phase4-build-validation.log`, `artifacts/phase4-test-validation.log`,
`artifacts/phase4-debug-build-validation.log`, and
`VanillaGraphicsExpanded.Tests/TestResults/phase4-state-validation.trx`.

Measured counters distinguish state/capability value reads, state transitions, resource binds and
error-status polling. Cold depth entry uses three value reads; partially known entry uses two;
unchanged warm entry/restoration uses zero value reads and zero fixed-function transitions, with six
native error-status checks for its three restored fields. Warm borrowed-slot restoration adds no
texture/image/indexed-buffer binds in the tested footprint. These counts do not establish CPU/GPU
speedups or live appearance. The second source review addressed failed shader activation/rollback
exception preservation and late context invalidation. The independent completion audit reconciled
all restoration tasks against the linked proposal, parent architecture, inventory and final receipts;
no required mechanism finding remains. Production integration is recorded below; live acceptance
remains pending.

### Shared capability ownership

StateCache retains mutable GPU state only. All implementation limits it consumes come from
GpuSupport: output counts, viewport dimensions, patch size, texture/image/indexed-buffer slot counts
and shader-storage buffer range alignment. GpuSupport.EnsureCurrentContext uses the registered
handle/generation for warm reads without native string or limit polling. Replacement generations
refresh the shared snapshot; state invalidation does not. Capability capture counts belong to
GpuSupport.CaptureCount, while StateCache.BoundaryQueries counts mutable state reads.

Image limits use GL_MAX_IMAGE_UNITS and GL_MAX_COMBINED_IMAGE_UNIFORMS rather than texture-unit
limits. GpuSupportLimitsTests checks the shared values against native queries and verifies warm
reuse without consuming pending native errors. CategorizedStateCacheTests verifies same-lifetime
reuse and capability refresh after explicit context retirement/re-registration.

### Refraction and fullscreen callback integration

FullscreenBoundary composes descriptor-derived coverage with viewport/clear helper effects and
prepared shader footprints, including texture unit zero used by allocation helpers. The capture
adapter resolves one boundary before target allocation and runs direct lighting and pre-overlay
composition inside it. The shared draw methods require the active scope. Their independently
registered callbacks establish separate boundaries; ordinary composite includes display resolve,
SSAO restoration and receiver reduction. Shader readiness is established before footprint capture;
the shared draw methods do not compile variants inside the boundary.

The duplicate CapturePipeline and the three legacy snapshots have been removed from these entry
points. Capture and composite no longer invalidate all cache knowledge: supported native changes
are observed by the existing owners and engine adapters, while boundary snapshots restore the
actual mixed indexed state. Other legacy consumers retain their existing contracts for their own
migration review. Resource allocation, resize, borrowing and disposal remain with existing target
owners. Standalone preparation and standalone SSAO callers retain their distinct binding scopes.

EngineBoundaryScope.Activate registers the existing UseScope owner for ordered cleanup after all
sequential passes. It does not introduce another shader activation implementation. This preserves
both operation and cleanup exceptions before borrowed slots/framebuffers and drawing state are
restored. Capture and ordinary composite withdraw publication on an escaping error; optional
allocation/reduction and BeforeOverlay catches cannot swallow classified restoration failures.
Entry rejection skips optional capture without drawing; ordinary callbacks surface unsupported
entry contracts. Shared work and outer callbacks reject nested boundaries.

Incoming active foreign engine owners and unowned raw/compute programs remain rejected because
this adapter cannot establish their resource reactivation footprint. Headless fixtures model engine
Stop observation with targeted program-cache invalidation; they do not force an extra native unbind
to satisfy boundary entry.

| Integration task group | Controlling source | Implementation and evidence |
| --- | --- | --- |
| One capture boundary and independent callbacks | Restoration proposal / Refraction integration and compatibility; inventory / Entry points and Complete operation and effect matrix | FullscreenBoundary, shared lighting/composite methods and capture adapter; actual callback/capture GPU regressions. |
| Preparation, helpers, shader cleanup and coverage | Restoration proposal / Boundary declaration and entry; Dynamic state, bindings, and shader ownership; parent proposal / Submission contract | Prepared resource unions, descriptor-derived coverage, deferred UseScope ownership, SSAO and reduction composition; native viewport and independent framebuffer checks. |
| Publication, failure and resource lifetime | Restoration proposal / Application and restoration; Authority, invalidation, and lifetime; inventory / Boundary API, coverage and failures | Capture/composite withdrawal and restoration-failure filters; existing scene/target owners; rejection, draw failure, resize, reload, toggle and teardown regressions. |
| Mixed-index corruption and coherent world publication | Restoration proposal / Problem and evidence; Verification and acceptance; inventory / Deterministic reference cases | Legacy negative control and real MRT marker rasterization followed by actual capture/ordinary callbacks; negative normal-alpha and clean world color/depth assertions. |

Final delegated validation passed on 2026-10-05. Release build: zero errors and 107 warnings.
Final Debug incremental build: zero errors and zero warnings (the preceding full Debug build
reported 106 warnings). TRX inspection confirms 213 distinct tests passed, with no failures or skips:
38 boundary/capture/SSAO cases, 71 cache/resource/shader cases and 104 relevant rendering cases.
The marker regression first reproduces nonnegative alpha with the legacy scope, then verifies -1
after the actual capture and restores the coherent world pair through ordinary composition. Cases
also cover first use, repeated frames, actual 3x3 resize, reload, disabled/dry capture, failed entry,
failed draws and retiring the incoming shader during cleanup. SSAO restoration and receiver
reduction execute together through the real ordinary composite callback.

Commands used NUGET_PACKAGES=C:/Users/Sisco/.nuget/packages, with shader receipts enabled:

- dotnet build VanillaGraphicsExpanded.Tests/VanillaGraphicsExpanded.Tests.csproj -c Release --no-restore -v quiet
- dotnet build VanillaGraphicsExpanded.Tests/VanillaGraphicsExpanded.Tests.csproj -c Debug --no-restore -v quiet
- dotnet test VanillaGraphicsExpanded.Tests/VanillaGraphicsExpanded.Tests.csproj -c Release --no-build --no-restore --filter '<selection below>' --logger 'trx;LogFileName=phase5-<group>-validation.trx'

Selections (separate runs, with distinct test IDs):

~~~text
boundary: FullyQualifiedName~EngineBoundary|FullyQualifiedName~WaterRefractionCaptureStateTests|FullyQualifiedName~SceneColorParticlePublicationTests
cache: FullyQualifiedName~PipelineStateCoverageTests|FullyQualifiedName~CategorizedStateCacheTests|FullyQualifiedName~GlStateCacheInvalidationTests|FullyQualifiedName~EngineState|FullyQualifiedName~GpuFramebufferBlendStateIntegrationTests|FullyQualifiedName~FramebufferBindingStateTests|FullyQualifiedName~StateCacheResourceDeletionTests|FullyQualifiedName~GlStateCacheUnbindIntegrationTests|FullyQualifiedName~ScissorStateScopeTests|FullyQualifiedName~GpuProgramUseScopeTests|FullyQualifiedName~GeneratedResourceBindingTests|FullyQualifiedName~GpuImageUnitBindingIntegrationTests|FullyQualifiedName~GpuSupportLimitsTests
render: FullyQualifiedName~WaterRefractionLifecycleTests|FullyQualifiedName~WaterRefractionOverlayCompositionTests|FullyQualifiedName~WaterRefractionReductionTests|FullyQualifiedName~WaterRefractionResolutionTests|FullyQualifiedName~PbrCompositeHdrTests|FullyQualifiedName~PbrDirectLighting|FullyQualifiedName~DirectLightingBufferOwnershipTests|FullyQualifiedName~SceneColorParticle&FullyQualifiedName!~SceneColorParticlePublicationTests
~~~

Receipts: artifacts/phase5-build-validation.log, artifacts/phase5-debug-build-validation.log,
artifacts/phase5-{boundary,cache,render}-tests.log and
VanillaGraphicsExpanded.Tests/TestResults/phase5-{boundary,cache,render}-validation.trx.
Earlier commands that tested stale binaries after a failed build were withdrawn; cancelled broader
runs are not completion evidence. Final builds succeeded before their corresponding test runs.

The second source review corrected helper-scope validation, kept disabled-option retirement inside
the callback boundary and verified that real GlPipelineDesc application remains with every draw.
The independent completion audit reconsulted the selected contract, proposal, parent submission and
ownership rules, entry/helper inventory and final source/receipts. All required integration items
are satisfied. Legacy-caller reconciliation and broader PSO adoption retain their own plan gates.
Live appearance, first-person source cleanliness in a new capture and the separate screen-bottom
UV fallback remain outside the automated acceptance claim. No game is launched by this work.

### Legacy reconciliation and future submission ownership

The 2026-10-05 reconciliation was documentation-only; its historical callback tables above describe
the bounded implementation at that time. Direct lighting now uses GraphicsCommandContext to
coordinate the existing boundary mechanism around complete passes, preserving engine-aware shader cleanup, borrowed bindings/framebuffers,
then StateCache drawing-state restoration. Unknown external mutations require an explicit handoff
and affected-category invalidation; engine hooks do not prove arbitrary raw GL coverage.

Direct lighting has removed its partial descriptor and local target/clear/draw setup. Shared capture
declares its prepared direct-lighting pipeline together with the existing composite shader footprints
through TryRunShared. Its direct-lighting pass ends before the bounded composite callback starts.
The earlier composite/capture adoption prerequisite required retaining partial draws and FullscreenBoundary unions until
complete submission covers the same operation. Keep any still-used adapter until its final caller migrates. Partial descriptors remain
restricted to declared compatibility operations outside complete submission or at explicit boundaries.
Viewport remains dynamic; framebuffer blend policy moves with complete consumer pipelines, with
one policy owner throughout. No renderer restore PSO or second cache is introduced.

| Reconciliation task | Controlling source | Evidence |
| --- | --- | --- |
| Caller dispositions and helper retirement | Restoration proposal / Refraction integration and compatibility; original caller inventory | The current inventory accounts for every migrated invocation and the removed dormant helper; historical reference behavior remains test-only. |
| State ownership, cleanup, unknown mutations and category separation | Restoration proposal / Dynamic state, bindings, and shader ownership; Authority, invalidation, and lifetime; Source organization | StateCache category/knowledge partials, BoundaryValidation, EngineBoundaryScope and FullscreenBoundary source review; existing boundary/cache/callback regressions above. |
| Future adoption and bounded scope | Parent proposal / Submission contract, Engine integration and cache authority, Adoption strategy | Parent submission, direct-lighting and remaining-consumer tasks explicitly own reuse and compatibility removal; proposal and project index agree. |

Second source review reconfirmed the complete fixed-function category inventory: depth, effective
per-output blend/masks, rasterizer, primitive assembly and dynamic viewport. Knowledge stays separate
from reusable values; clear-operation state has its separate owner. Binding caches, capabilities,
context identity and resource lifetimes remain outside fixed-function value structs. The review
corrected the frozen/live bounds inventory discrepancy and checked retained entry mutations and
caller context. No remaining scope has evidence supporting isolated deletion or fullscreen substitution.

No runtime files changed, so the 213 passing tests and Debug/Release receipts above remain applicable.
No new build/test run or live acceptance is claimed for this reconciliation. The completion audit
reconsulted the restoration contract and linked proposal/parent adoption requirements; retained
consumers are explicitly allowed here and remain incomplete under the parent plan. Supplied live
validation and the broader parent completion gates remain outstanding.

### Consolidated acceptance status

Automated acceptance evidence was reconciled on 2026-10-05 against the restoration proposal's
Verification and acceptance items 1–8 and the section mapping above. The delegated receipt review
confirmed that commit 421e64c9's runtime implementation is unchanged by the documentation-only
reconciliation in 79394b44. The three retained TRX receipts contain 213 distinct passing test IDs,
zero failures and zero not-executed tests. Their runs followed the successful Release build.
Release reported zero errors/107 warnings; the final Debug incremental build reported zero errors/
zero warnings. No rerun was needed or claimed. Commands, filters and receipt paths remain recorded
under Refraction and fullscreen callback integration.

Coverage includes mixed global/indexed state and masks, partial knowledge, snapshot independence,
context replacement, unsupported mutation rejection, exactly-once cleanup, setup/draw/shader failure,
actual marker rasterization and publication, independent callbacks, resize/reload, disabled capture,
engine mappings, shader ownership, resource retirement and unaffected borrowed bindings. This is
bounded unit/headless evidence, not an engine-wide authority or installed-build claim.

| Measured fixture operation | Value queries | Native transitions / other calls | Evidence |
| --- | --- | --- | --- |
| Cold depth entry, unchanged restoration | 3 | 0 fixed-function transitions | EngineBoundaryRestorationTests |
| Depth entry with comparison already known | 2 | Queries resolve only missing enable/write values | EngineBoundaryEntryTests, EngineBoundaryRestorationTests |
| Unchanged warm depth entry/restoration | 0 | 0 fixed-function transitions; 6 native error-status checks during restoration | EngineBoundaryRestorationTests |
| Cold enable/factors/write-mask coverage | 6 per supported draw output | Entry emits no fixed-function mutation | EngineBoundaryEntryTests |
| Unchanged warm borrowed-slot footprint | 0 additional | 0 additional texture/resource-slot binds | EngineBoundaryBindingTests |

These are passing counter assertions for named fixtures, not timings or whole-frame totals.
Capability reads have their separate GpuSupport counter. Error polling is not a state-value query
and is not hidden by the zero-query claim. CPU/GPU speedup, representative frame cost and full
submission/preparation measurements have not been established; broader measurements remain in
the parent plan.

The user supplied a startup crash at 11:49:47 PDT on 2026-10-05 in
Vintage Story 1.22.7: the engine's LoadFrameBuffer viewport call reached GpuSupport before context
registration. After the startup correction below, the user confirmed on 2026-10-05: "okay, its all
working". This supplies live acceptance of the corrected build in the current rendering task.
No new RenderDoc capture was supplied: indexed state, negative markers and clean world publication
retain the native/headless evidence recorded above rather than a claimed live texture inspection.
No remaining bottom-cutoff problem was separately reported in this confirmation; its UV-fallback
cause was not isolated and no UV-fallback fix is claimed. Vintage Story was not launched by the agent.

Second review and completion audit reconciled the source/test evidence, proposal section mapping,
caller dispositions and parent prerequisites. Automated consolidation is satisfied. The live-evidence
task and final acceptance gate are now satisfied by the supplied confirmation together with the
automated evidence. This completes the bounded restoration plan, not the parent PSO migration.

### Early engine context registration correction

StartPre installs engine state hooks before StartClientSide registers the render context. Menu
rendering can call LoadFrameBuffer between these events. Previously its routed Viewport command
requested shared viewport limits with no registered context and threw before rendering.

EngineStateCalls now obtains its cache through one engine-adapter accessor. If current registration
is absent, it registers the actual ScreenManager platform window through EngineRenderContext before
using the cache. The provider still verifies the current native handle and live window owner. Existing
registrations are reused; StartClientSide registration stays idempotent and disposal retains the
existing retirement hook. GpuSupport remains the capability owner, and direct boundary entry still
rejects unregistered contexts. No device-string identity, fabricated owner or unchecked fallback is added.

EngineStartupContextTests reproduces the reported exception through the former direct ApplyDynamic
path, then invokes the real engine Viewport adapter against an unregistered native fixture context
and an engine window owner. It checks native viewport values, idempotent registration, warm capability
and transition suppression, retirement/new generation, wrong-window rejection and existing context
operation with no engine platform. Test-owned wrappers borrow the fixture context and cannot destroy it.

The second source review checked every engine adapter's cache access, provider handle/liveness guards,
disposal retirement, unchanged direct boundary rejection and test cleanup. The correction adds no
native drawing-state restoration outside StateCache. The earlier 213-test receipts remain historical
baseline evidence; fresh affected validation is recorded below. Subsequent user confirmation supplies
successful live acceptance as recorded above.

Fresh delegated validation passed: Debug test-project build, zero errors/101 warnings; Release
production build with shader compilation enabled, zero errors/6 warnings. The three new TRX files
contain 97 distinct passing test IDs, zero failures/skips: 84 startup/engine/cache/boundary/publication
cases, four capture cases and nine shader ownership/generated-resource/image-binding cases.
Receipts: artifacts/startup-context-debug-build.log, artifacts/startup-context-release-build.log,
artifacts/startup-context-{focused,refraction,resources}.log and
TestResults/startup-context-{focused,refraction,resources}.trx. The final Debug build includes the
completed test cleanup changes; preliminary receipts are not substituted for these results.

Commands used NUGET_PACKAGES=C:/Users/Sisco/.nuget/packages with no shader-build overrides:

~~~text
dotnet build VanillaGraphicsExpanded.Tests/VanillaGraphicsExpanded.Tests.csproj -c Debug --no-restore
dotnet build VanillaGraphicsExpanded/VanillaGraphicsExpanded.csproj -c Release --no-restore
dotnet test VanillaGraphicsExpanded.Tests/VanillaGraphicsExpanded.Tests.csproj -c Debug --no-build --no-restore --filter '<selection>' --logger 'trx;LogFileName=startup-context-<group>.trx' --results-directory TestResults

focused: FullyQualifiedName~EngineStartupContextTests|FullyQualifiedName~EngineState|FullyQualifiedName~EngineBoundary|FullyQualifiedName~Categorized|FullyQualifiedName~GpuSupport|FullyQualifiedName~StateCacheResourceDeletion|FullyQualifiedName~ShaderScope|FullyQualifiedName~ShaderUniformState|FullyQualifiedName~GlStateCacheInvalidation|FullyQualifiedName~SceneColorParticlePublication
refraction: FullyQualifiedName~WaterRefractionCaptureStateTests
resources: FullyQualifiedName~GpuProgramUseScopeTests|FullyQualifiedName~GeneratedResourceBindingTests|FullyQualifiedName~GpuImageUnitBindingIntegrationTests
~~~

## Native error-checking correction (2026-10-06)

Ordinary cached raster transitions validate managed inputs and capability support, suppress known
unchanged values, and issue the necessary native commands without unconditional error polling.
They do not promise immediate detection of every driver error. Explicit external mutation and
resource-lifetime invalidation remain necessary for truthful cache authority.

Detailed raster-transition checks are opt-in through GlDebug.CheckStateTransitions, disabled by
default in both Debug and Release. A changed transition checks before and after its native command
when enabled, withholding affected knowledge on failure. Boundary restoration owns its checks
instead of repeating this diagnostic pair inside each setter. Known-equal transitions consume no
native errors even when diagnostics are enabled.

Checked restoration retains independent field-level attempts and targeted invalidation. Known-equal
fields require no native work or per-field error polls; changed or unknown fields are checked before
and after restoration. A pre-existing error rejects that checked attempt rather than being attributed
to its command; the affected field becomes unknown and independent cleanup continues. A native error
reported after a grouped command invalidates the entire affected group, not a guessed individual
component. Cleanup failure remains an unsafe handoff and propagates with the original operation error.

Pixel pack/unpack layouts validate their inputs before mutation and use optional transition diagnostics.
Changed layouts and scope restoration do not poll errors by default; checked boundary restoration owns
its checks. Pixel-transfer buffer changes remain checked safety operations before native code interprets
a managed pointer using those bindings. Known-equal bindings/layouts do not poll. Polygon-stipple transfer reuses those checked owners and
checks the transfer itself; restoration does not repeat checks already owned by a checked transfer.
Cold state reads retain native error checks so a failed query cannot publish a default as known state.
Unavoidable safety checks and optional diagnostics are accounted separately from state-value reads
and native mutations. Reduced call counts alone do not establish CPU or GPU timing improvements.

This policy supersedes the historical per-field polling counts recorded earlier and the earlier blanket
promise of immediate native-error detection on ordinary transitions. Existing completion receipts
remain historical evidence for their tested revisions. Current verification is recorded below.

Traceability for this correction:

| Requirement and controlling source | Implementation and verification |
| --- | --- |
| Proposal / Native error-checking policy; plan / Correct native error polling and checked-operation ownership | GlDebug.CheckStateTransitions; RasterEnables/RasterParameters; StateTransitionDiagnosticsTests distinguishes normal calls, optional diagnostics and inherited errors. |
| Proposal / Engine integration and cache authority; retained Boundary API, coverage and failures | BoundaryRestoration, BoundaryRasterizer and BoundaryBindings skip known-equal fields, independently check changed fields and preserve targeted invalidation; boundary restoration/entry suites retain failure and lifecycle cases. |
| Proposal / Native error-checking policy; retained configurable raster transfer ownership | PixelPack/PixelUnpack no-op suppression; PixelTransferBindings and PolygonStipple retain checked safety setup/transfer/cleanup; pixel layout and configurable raster GPU suites verify native state and exception restoration. |
| Proposal / Validation and acceptance | Delegated shader-enabled Debug/Release tests; operation counters distinguish native mutations, value reads and error polling. No live-game or timing acceptance is inferred. |

Delegated shader-enabled verification passed on 2026-10-06: Debug 74/74 and Release 74/74,
zero failures or skips in both final runs. Commands used NUGET_PACKAGES=C:\Users\Sisco\.nuget\packages:

~~~powershell
dotnet test VanillaGraphicsExpanded.Tests/VanillaGraphicsExpanded.Tests.csproj -c Debug --no-restore --filter 'FullyQualifiedName~StateTransitionDiagnosticsTests|FullyQualifiedName~ConfigurableRasterizer|FullyQualifiedName~EngineBoundary|FullyQualifiedName~PixelPackStateTests|FullyQualifiedName~PixelUnpackStateTests|FullyQualifiedName~DepthStencilTextureTests|FullyQualifiedName~PipelineStateCoverageTests' -v quiet
~~~

Repeat with -c Release. Receipts: artifacts/polling-debug-tests.log and
artifacts/polling-release-tests.log. Shader compilation remained enabled. An initial shader-cache
atomic replacement failed transiently; a later test run exposed one test still assuming unconditional
diagnostics. The corrected explicit opt-in test is included in both final passing runs. Existing
compiler/analyzer and NU1900 feed-access warnings remain.

| Verified operation | Native mutations | State-value reads | Error-status checks |
| --- | ---: | ---: | ---: |
| Two ordinary raster enable changes plus point-origin change, with repeated identical requests | 3 | 0 | 0 |
| Known identical pack/unpack layouts | 0 | 0 | 0 |
| Diagnostic retry of unknown raster field | 1 | 0 | 2 |
| Changed raster restoration with diagnostics enabled | 1 | 0 | 2 |
| Unchanged warm depth and borrowed binding restoration | 0 | 0 | 0 |

Second source review verified checked-owner nesting, alias invalidation, retired-resource rejection,
independent cleanup and native query/transfer safety. These counts establish suppressed calls, not
measured frame-time or GPU savings. Live game validation was not performed.

Independent audit-stage-completion review passed after the second review and both final test receipts;
no unresolved implementation, contract or verification gaps remain for this correction.

## Complete state application and engine boundary (2026-10-06)

Complete descriptor application remains below prepared executable submission: StateCache applies
validated fixed-function intent and explicit GraphicsDynamicState values. Shader preparation,
render-pass compatibility, geometry validation and production consumer migration retain their later
implementation dependencies. Existing partial GlPipelineDesc behavior remains available.

The cache reuses its categorized values and knowledge for depth, rasterizer, primitive assembly,
dynamic state and indexed blending, adding stencil, sampling and output interpretation ownership.
PipelineStateSnapshot retains copies of these same value-only category structs and their separate knowledge
masks. Indexed sample-mask values are copied once into private snapshot storage; callers can enumerate
values without accessing the mutable dictionary. Missing incoming fields are queried directly into
the live category and become known only after their complete native query group succeeds. Restoration
compares saved category values and knowledge with the live cache, avoiding a second flattened state
representation and preserving independent failure invalidation.

Complete application establishes disabled parameters as well as enables, all supported sample-mask
words and native output slots, depth range, restart policy and explicit output encoding. Shared
GpuSupport capabilities gate native operations; no cache-owned limits or context replacement model
is introduced. Global blend equations update indexed aliases; independent native equations/factors
are used only where supported. Ordinary unchanged application avoids native state calls and queries.
Optional transition diagnostics and checked restoration retain the native error policy above.

CompleteGraphicsBoundary borrows the existing EngineBoundaryExecution shader/resource lifetimes.
It requires an explicit inactive conditional-rendering contract and verifies transform feedback is
inactive before changing rendering state. Complete coverage captures missing values, including
inactive parameters, and restores independent changed fields on success and exceptions. It rejects
unknown shader ownership through the existing adapter. This is the adapter foundation for the
first fullscreen consumer; it does not replace the production renderer or assert authority over
arbitrary engine/mod calls. Unobserved operations still require ExecuteExternal or explicit targeted
invalidation before subsequent managed use. Existing retirement and VAO-owned EBO rules remain intact.

Exact native signatures in EngineStateCalls now route additional stencil, raster, sample, depth-range,
blend-equation and dynamic commands through cache owners; EngineStateCallMap validates signatures and
patches engine callers only. This observes the routed calls without recursively patching cache GL calls.
Unsupported overloads and external callbacks are outside that observation contract.

FixedFunctionCalls, BoundaryQueries and BoundaryErrorChecks retain separate scalar counters.
DrawSubmissions counts commands issued by owned GpuVao/GpuEbo draw helpers, with no history allocation;
it is not an engine-wide draw counter. Native engine RenderMesh paths remain outside that count.

| Plan requirement -> controlling contract | Implementation and required evidence |
| --- | --- |
| Complete transitions -> Proposal / Static and dynamic state coverage; Supported state and defaults | ApplyGraphicsState, explicit dynamics and categorized setters; hostile native state, A-to-B-to-A, disabled parameters and unchanged-call tests. |
| Alias and lifetime -> Proposal / Engine integration and cache authority; Restoration decisions by boundary | Global/indexed blend coherence, existing targeted resource retirement and VAO tracking; alias, invalidation and retirement regression suites. |
| First-consumer boundary -> Supported state and defaults; Boundary API, coverage and failures | CompleteGraphicsBoundary, complete coverage/resolved snapshots and independent restoration; excluded-operation guards, callback exceptions, shader/resource-owner regressions. |
| Instrumentation and verification -> Proposal / Validation and acceptance; Native error-checking policy | Separate bounded counters; actual three-target fullscreen draw and pixel checks, native state comparisons, delegated shader-enabled Debug/Release receipts. |


The installed-driver tests use a three-color-target 8-by-8 fullscreen fixture with explicit state,
verify every output pixel, and exercise success/exception restoration. This is state-application proof,
not the later frozen production-lighting shader comparison. Native probe evidence on the test driver
showed negative stencil references stored as zero and 257 retained as 257 even on an eight-bit stencil
target; the cache normalizes only the negative native input, while managed complete draw values obey
the supported target's reference range.

The actual driver exposes independent blending and transform-feedback activity queries. The absent
independent-blending global fallback and unavailable activity-query rejection are source-reviewed;
these runs do not establish native coverage on hardware lacking those features. One remaining raw
scissor restoration in LumOnDebugRenderer is followed by its existing explicit InvalidateAll boundary;
its renderer migration remains outside this adapter's authority.

Verification commands use NUGET_PACKAGES=C:\Users\Sisco\.nuget\packages, with shader compilation enabled:

~~~powershell
dotnet test VanillaGraphicsExpanded.Tests/VanillaGraphicsExpanded.Tests.csproj -c Debug --no-restore --filter 'FullyQualifiedName~CompleteGraphics|FullyQualifiedName~ConfigurableRasterizer|FullyQualifiedName~GraphicsPipelineDescriptionTests|FullyQualifiedName~PipelineStateCoverageTests|FullyQualifiedName~EngineBoundary|FullyQualifiedName~EngineStateSwitchingTests|FullyQualifiedName~EngineStateInventoryTests|FullyQualifiedName~StateCacheResourceDeletionTests|FullyQualifiedName~GlStateCacheInvalidationTests|FullyQualifiedName~GpuVaoIntegrationTests|FullyQualifiedName~GpuResourceManagerDeletionQueueIntegrationTests|FullyQualifiedName~StateTransitionDiagnosticsTests|FullyQualifiedName~PixelUnpackStateTests|FullyQualifiedName~PixelPackStateTests' --logger 'console;verbosity=normal'
~~~

Repeat with -c Release. Receipts: artifacts/complete-graphics-debug-tests.log and
artifacts/complete-graphics-release-tests.log. The selection includes twelve new complete-state/draw
cases and existing descriptor, engine-boundary, shader-ownership, retirement/name-reuse, VAO/EBO,
pixel-transfer and diagnostic regressions. Initial runs exposed the core polygon-mode query shape,
an unhandled installed-engine CullFace(TriangleFace) signature and obsolete coverage/count/sentinel
expectations. Those were corrected before final validation. A Release attempt hit a transient
ShaderVariantCache atomic file-replacement failure and was retried unchanged.

Second source review covered complete preflight-before-mutation, disabled-state values, global/indexed
aliases, categorized invalidation, viewport updates preserving other dynamics, snapshot detachment,
independent restoration failures and engine adapter signatures. No production renderer migration,
live-game appearance validation or frame-time improvement is claimed.

Final delegated results on 2026-10-06: Debug **171/171 passed** and Release **171/171 passed**,
zero failures or skips, shader compilation enabled. Existing compiler/analyzer and NU1900 warnings
remain. Repeated identical complete application adds zero fixed-function calls, zero state reads and
zero error-status checks with diagnostics disabled. The fullscreen fixture records one owned draw
per successful callback and checks exact output pixels across all three attachments.

Independent audit-stage-completion reviewed the linked contracts, implementation, source-layout
corrections and both final receipts after the second review. It found no unresolved requirements or
evidence gaps for complete state application and the first-consumer boundary foundation.

Categorized snapshot refactor verification (2026-10-06): shader-enabled Debug and Release each passed
85/85 focused tests, zero failures/skips. The two added warm/cold snapshot tests mutate and invalidate
live categories and indexed sample masks, verify saved values/knowledge remain unchanged, and verify
native restoration. Existing query-failure and exceptional-cleanup regressions also passed.
Receipts: artifacts/categorized-snapshot-debug-tests.log and artifacts/categorized-snapshot-release-tests.log.
Command (repeat with -c Release; NUGET_PACKAGES=C:\Users\Sisco\.nuget\packages):

~~~powershell
dotnet test VanillaGraphicsExpanded.Tests/VanillaGraphicsExpanded.Tests.csproj -c Debug --no-restore --filter 'FullyQualifiedName~CompleteGraphics|FullyQualifiedName~EngineBoundary|FullyQualifiedName~StateTransitionDiagnostics|FullyQualifiedName~PixelPack|FullyQualifiedName~PixelUnpack|FullyQualifiedName~ConfigurableRasterizer|FullyQualifiedName~PipelineStateCoverage' -v quiet
~~~


## Unified category storage (2026-10-06)

The mutable cache owns one value struct and one independent knowledge mask per category:
DepthState/DepthStateKnowledge, RasterizerState/RasterizerStateKnowledge,
PrimitiveAssemblyState/PrimitiveAssemblyStateKnowledge, DynamicDrawState/DynamicDrawStateKnowledge,
SamplingState/SamplingStateKnowledge, StencilState/StencilStateKnowledge and
OutputState/OutputStateKnowledge. Indexed BlendState values retain per-output BlendStateKnowledge.
All value structs live in Rendering/Pipeline/State and its matching namespace. Enables are ordinary
category values with category-specific knowledge bits. There is no second complete/supplemental
state representation. Sample-mask dictionary membership remains independent per-word validity;
scalar SamplingStateKnowledge.All does not imply that every sample-mask word is known.

PipelineStateSnapshot copies category values and separate coverage-filtered masks. Blend values and
knowledge arrays and indexed sample-mask storage are privately copied. A partial snapshot cannot
restore warm fields outside its declaration. Complete-only fields still require explicit complete
boundary authority; expanding a knowledge enum does not grant new partial-boundary authority.
Legacy partial descriptor mapping remains unchanged. Full coverage resolves inactive parameters.

StateCache.FixedFunctionStorage owns live category storage. DepthRange, RasterModes,
RasterCapabilities, DrawDynamics, PrimitiveRestart/RestartEnables, Sampling/SamplingEnables,
Stencil/StencilEnable, Output/OutputEnables and BlendEquations partials own their focused transitions.
GraphicsEnables adapts native capability identifiers to these owners; CompleteApplication remains
whole-pipeline orchestration. BoundaryCategoryQueries and BoundaryCategoryRestoration operate on
these same values and masks. Invalidation clears only the existing public groups: viewport leaves
scissor/constant knowledge intact; patch count leaves restart knowledge intact; cull enable leaves
cull mode intact. Diagnostic policy, managed validation, external invalidation and resource lifetimes
are unchanged. Capability data remains owned by GpuSupport.

Traceability and inventory dispositions:

| Contract/source | Implementation or reviewed no-change disposition | Evidence |
| --- | --- | --- |
| Proposal / Architectural responsibilities, Complete descriptions and partial overrides; retained State representation and source layout decisions | Unified value structs, category masks, FixedFunctionStorage and focused transition owners; all retired storage symbols removed from runtime source | Category mapping and source sweep; native state and invalidation regressions |
| Proposal / Engine integration and cache authority; retained Boundary API, coverage and failures | Coverage, snapshot, entry/query/restoration and exact engine capability routing use unified owners | Detached warm/cold snapshots, exception restoration, engine routing and coverage tests |
| Proposal / Native error-checking policy | Existing optional setter diagnostics and checked boundary/transfer owners retained | StateTransitionDiagnosticsTests and pixel layout/readback tests |
| Graphics submission design contract / Supported state and defaults, Restoration decisions by boundary | CompleteApplication establishes the same authored values; GraphicsDynamicState and immutable descriptions are unchanged | Hostile-state, A-to-B-to-A, indexed alias and actual fixture-draw tests |
| Plan inventory / remaining category consumers | Depth, Rasterizer, RasterEnables, RasterParameters, PipelineRasterizer, PrimitiveAssembly, Patches, Dynamic, Blending, ScissorScope, ClearOperations and Legacy retain existing per-field writes and do not overwrite the expanded structs | Source review and focused category/legacy regressions |
| Plan inventory / remaining boundary and resource consumers | BoundaryValidation keeps complete-only authority; BoundaryRasterizer, BoundaryBindings, BoundaryActivity, EngineBoundaryScope, EngineBoundaryDeclaration/Execution and FullscreenBoundary/CompleteGraphicsBoundary require no ownership changes. PixelPack/Unpack, PixelTransferBindings, PolygonStipple and resource deletion remain with existing owners | Source review; boundary, transfer, retirement and diagnostic suites |
| Plan inventory / authored interfaces and index | EngineStateCalls and EngineStateCallMap signatures remain intact; EPipelineState numbering and groups unchanged; project.todo already links the governing plan and requires no index edit | Exact-signature engine tests, coverage tests and source review |

Historical receipts above retain their original scope and counts. Delegated shader-enabled verification
passed on 2026-10-06: Debug **161/161** and Release **161/161**, zero failures or skips. Both runs
include UnifiedDynamicKnowledgePreservesUnrelatedFields and
CompleteOnlyFieldsRequireExplicitBoundaryAuthority, plus detached warm/cold snapshot checks.
Receipts: artifacts/unified-categories-debug-tests.log and artifacts/unified-categories-release-tests.log.
Existing compiler/analyzer warnings remain; no build errors. No production migration, live-game
acceptance or performance measurement is implied by this structural correction.

With NUGET_PACKAGES=C:\Users\Sisco\.nuget\packages, run (repeat with -c Release):

~~~powershell
dotnet test VanillaGraphicsExpanded.Tests/VanillaGraphicsExpanded.Tests.csproj -c Debug --no-restore --filter 'FullyQualifiedName~CompleteGraphics|FullyQualifiedName~CategorizedStateCacheTests|FullyQualifiedName~ConfigurableRasterizer|FullyQualifiedName~GraphicsPipelineDescriptionTests|FullyQualifiedName~PipelineStateCoverageTests|FullyQualifiedName~EngineBoundary|FullyQualifiedName~EngineStateSwitching|FullyQualifiedName~EngineStateInventoryTests|FullyQualifiedName~StateCacheResourceDeletionTests|FullyQualifiedName~GlStateCacheInvalidationTests|FullyQualifiedName~StateTransitionDiagnosticsTests|FullyQualifiedName~PixelUnpackStateTests|FullyQualifiedName~PixelPackStateTests|FullyQualifiedName~DepthStencilTextureTests|FullyQualifiedName~ScissorStateScopeTests|FullyQualifiedName~GpuFramebufferBlendStateIntegrationTests|FullyQualifiedName~GlPipelineStateMaskTests' --logger 'console;verbosity=normal'
~~~

Second source review checked all 17 parameter groups and 18 enable mappings, enum All masks,
coverage normalization, knowledge publication after native query groups, independent restore failure
invalidation, and per-field assignments preserving other category values. Repository-wide source
search found no retired complete/supplemental storage symbols. The inventory dispositions above
include unchanged consumers; existing test names describing complete graphics behavior are retained.


Independent audit-stage-completion consulted the governing documents, full affected-item inventory,
implementation and both final receipts after the second review. It passed with no unresolved
implementation, contract or evidence findings for the unified category correction.


## Packed boolean category values (2026-10-06)

Category structs now retain boolean values in a private booleanValues field of their existing
category knowledge enum type. The type supplies stable bit identities; this field contains values,
not knowledge. Cache and snapshot knowledge remain separate masks. Public named boolean properties
use readonly Enum.HasFlag getters and typed bitwise set/clear operations, with no boolean backing
fields. False defaults, object initializers and value-copy semantics are preserved. Setters cannot
introduce non-boolean parameter bits into the private value field. No transition, query, restoration,
engine-routing, invalidation or native error policy changes are required at callers.

Exhaustive source inventory and dispositions:

| Type | Boolean audit and selected representation |
| --- | --- |
| DepthState | TestEnabled and WriteEnabled share the existing byte DepthStateKnowledge bits. Comparison and DepthRange remain scalar/tuple values. |
| BlendState | Enabled uses its existing byte BlendStateKnowledge bit. Nested GlColorMask already packs R/G/B/A into one byte; preserve it and its equality. Factors and equations are not booleans. |
| RasterizerState | CullEnabled, ScissorEnabled, AlphaTest, PointSmooth, LineSmooth, PolygonSmooth, LineStipple, PolygonStipple, DepthClamp, RasterizerDiscard, PolygonOffsetFill/Line/Point and ProgramPointSize use their existing uint RasterizerStateKnowledge bits. ClipDistances is already a per-distance uint bitset. Preserve all other parameters and the immutable stipple-pattern reference. |
| PrimitiveAssemblyState | PrimitiveRestart and PrimitiveRestartFixedIndex use their existing byte knowledge-enum bits. PatchVertices and RestartIndex remain scalar. |
| SamplingState | Multisample, SampleCoverageEnabled, SampleMask, SampleAlphaToCoverage, SampleAlphaToOne and SampleShading use the matching SamplingStateKnowledge bits. SampleCoverage.Invert stays in its value/invert tuple: the existing SampleCoverage knowledge bit describes a whole query group, not the invert value. Reusing it would conflate parameter validity with a component value. No new enum or fake knowledge bit is introduced for one unmatched boolean. |
| StencilState | TestEnabled uses the existing StencilStateKnowledge bit. Front/back function/reference/read masks, write masks and operation tuples contain no boolean values. |
| OutputState | FramebufferSrgb, Dither and ColorLogicOp use existing OutputStateKnowledge bits; LogicOperation remains separate. |
| DynamicDrawState | No booleans: viewport/scissor coordinates and blend constant only. No change. |
| GraphicsDynamicState | Authored reference record, not a cache value struct. Nullable declarations remain separate from native knowledge; no boolean packing applies. |
| PixelPackState / PixelUnpackState | SwapBytes and LsbFirst lack an existing matching flags enum. Keep positional readonly-record semantics, with-expressions and equality. Replacing two bytes with one would not reduce these int-aligned layouts; a new transfer enum and record rewrite have no demonstrated storage benefit here. |
| LumOnCameraState | Coordinates/dimension only, no booleans. No change. |
| LumOnWorldProbeScheduler.LevelState | No scalar boolean fields; dirtyAfterInFlight and disableAfterInFlight are per-probe arrays. Importance flags have unrelated importance semantics. Array/lifecycle redesign is outside graphics state packing; preserve scheduler ownership. |
| Generated shader-owner ...State structs | RuntimeSubmissionEmitter emits retained non-UBO shader input fields, not graphics category state. No matching category value enum; preserve generated input/revision/publication ownership. No generator input changes required. |

SamplingStateKnowledge, StencilStateKnowledge and OutputStateKnowledge use byte underlying storage;
all defined bits, including non-boolean validity bits, fit (highest bits 7, 6 and 3 respectively).
Existing numeric assignments and All masks are unchanged. Depth, blend, primitive assembly and dynamic
knowledge already use bytes; RasterizerStateKnowledge remains uint because its highest bit is 26.
No serialized/native ABI layout consumer of these internal category structs was found.

The reference sweep includes category transitions, queries, snapshot copies, boundary restoration,
full and partial application, engine signatures, invalidation, coverage masks and reflective tests.
Named accessors preserve these consumers without changing native-call or error-checking behavior.
The earlier unified-category inventory remains the owning-file map. No resource lifetime or generated
shader ownership changes are required. project.todo continues to point to the governing plan.

Traceability: plan audit/storage tasks -> Proposal / Categorized cache storage -> the inventory and
value properties above; consumer tasks -> Unified category storage and Proposal / Engine integration
and cache authority -> unchanged category consumers and snapshot/coverage regressions; verification
-> Proposal / Native error-checking policy -> existing diagnostics/transfer suites plus
StateBooleanValueTests (independent set/clear pairs, false defaults, copies, grouped parameter and
equality preservation) and StateValueLayoutTests (Unsafe.SizeOf, including reference-containing state).

Measured managed x64 layouts using Unsafe.SizeOf<T>:

| Type | Before (bytes) | After (bytes) |
| --- | ---: | ---: |
| DepthState | 32 | 24 |
| BlendState | 32 | 32 |
| RasterizerState (contains a reference) | 88 | 80 |
| PrimitiveAssemblyState | 12 | 12 |
| DynamicDrawState | 48 | 48 |
| SamplingState | 24 | 16 |
| StencilState | 80 | 80 |
| OutputState | 8 | 8 |
| PixelPackState | 20 | 20 |
| PixelUnpackState | 28 | 28 |
| LumOnCameraState | 56 | 56 |
| GlColorMask | 1 | 1 |

Sampling's flag field follows its parameters to avoid leading alignment padding; merely replacing
its booleans while placing the flag first retained the original 24-byte size. The final 16-byte layout
is measured, not inferred. Some structs retain their sizes despite smaller boolean payloads because
of alignment. Sampling/Stencil/Output knowledge enums shrink from 4 to 1 byte; the other widths remain
Depth/Blend/PrimitiveAssembly/Dynamic 1 byte and Rasterizer 4 bytes. Only field representation and
these internal enum widths changed; bit numbers, masks and consumer APIs remain intact.

Baseline receipt: artifacts/boolean-layout-before.log, one measurement case passed. The first attempt
hit a transient shader-build infrastructure failure; the unchanged retry progressed and the measurement
fixture's xUnit namespace was corrected before collecting the successful baseline. Baseline was
collected before modifying production layouts. Earlier implementation receipts are historical. Size measurements are managed x64 layouts, not marshaling sizes or evidence
of faster transitions, frame-time improvement or GPU savings. Live game acceptance remains user-run.


Final delegated shader-enabled Debug and Release verification each passed **170/170**, with zero
failures or skips. Both final logs report the corrected 16-byte SamplingState layout. Receipts:
artifacts/boolean-packing-debug-tests.log and artifacts/boolean-packing-release-tests.log.
The selection includes all 161 prior category/coverage/engine/native-state regression cases, seven
independent packed-value cases, grouped sampling/equality preservation, and the managed-size fixture.
Existing compiler/analyzer warnings remain; no build errors in the final runs.

With NUGET_PACKAGES=C:\Users\Sisco\.nuget\packages, run (repeat with -c Release):

~~~powershell
dotnet test VanillaGraphicsExpanded.Tests/VanillaGraphicsExpanded.Tests.csproj -c Debug --no-restore --filter 'FullyQualifiedName~CompleteGraphics|FullyQualifiedName~CategorizedStateCacheTests|FullyQualifiedName~ConfigurableRasterizer|FullyQualifiedName~GraphicsPipelineDescriptionTests|FullyQualifiedName~PipelineStateCoverageTests|FullyQualifiedName~EngineBoundary|FullyQualifiedName~EngineStateSwitching|FullyQualifiedName~EngineStateInventoryTests|FullyQualifiedName~StateCacheResourceDeletionTests|FullyQualifiedName~GlStateCacheInvalidationTests|FullyQualifiedName~StateTransitionDiagnosticsTests|FullyQualifiedName~PixelUnpackStateTests|FullyQualifiedName~PixelPackStateTests|FullyQualifiedName~DepthStencilTextureTests|FullyQualifiedName~ScissorStateScopeTests|FullyQualifiedName~GpuFramebufferBlendStateIntegrationTests|FullyQualifiedName~GlPipelineStateMaskTests|FullyQualifiedName~StateValueLayoutTests|FullyQualifiedName~StateBooleanValueTests' --logger 'console;verbosity=detailed'
~~~

The second source review verified all 29 direct boolean mappings, valid enum widths, false defaults,
independent set/clear behavior, tuple preservation and the absence of raw-layout/native ABI consumers.
Native transitions and knowledge owners remain unchanged; existing native regressions establish
known-false suppression, invalidation, detached snapshots, partial authority and exception restoration.

Independent audit-stage-completion reviewed the exhaustive inventory, linked contracts, implementation,
final layouts and both final test receipts after the second review. It passed with no unresolved
requirements or evidence gaps for the packed boolean category correction.


## Prepared graphics realizations (2026-10-06)

GraphicsPipeline is an explicitly constructed managed realization, not a native resource or an
interning registry. It retains its immutable GraphicsPipelineDesc, borrowed GpuProgram and existing
GpuPreparedBindings, and the successful executable revision. GraphicsPipelineLifetime is disposed
by the owning renderer at teardown; it invalidates all dependent realizations without acquiring
shader ownership. Construction and validation remain on the rendering thread in the same live
context. No context replacement machinery or native ResourceId is introduced.

GpuProgramInterface owns retained GraphicsExecutableInterface metadata alongside prepared resources.
The existing candidate installation publishes the structural shader identity and increments
ExecutableRevision only after installing coherent layout/settings. Failed or superseded candidates
do not advance the revision. Requested reloads, pending incompatible options, owner disposal and
successful replacement all reject use of an old realization. Replacement preparation calls the
existing readiness path and repeats compatibility validation; it never changes engine activation,
generated input submission, UBO ownership or deferred resource retirement.

Preparation validates the current capability owner, description, actual shader selection, vertex
locations/components/scalar interpretation, fragment locations/scalar classes/component coverage,
geometry input topology. Matrix columns and array elements occupy explicit
vertex locations; supplied unused attributes remain allowed. Divisors are immutable geometry-layout
requirements, not reflected shader properties: shared binding rates are checked by VertexLayoutDesc,
and future draw adapters must match the complete layout. Sparse output slots retain explicit discard
policy. ValidateTargets compares exact normalized formats/aspects/samples, excluding resource IDs
and dimensions. Actual target metadata, storage-specific support/completeness and attachment lifetimes
remain render-pass responsibilities; this realization owns no framebuffer.

Historical clipping implementation, removed after the custom-clipping cancellation:
CompiledClipDistance reads only built-in output declarations from the exact captured SPIR-V bytes
already owned by PreparedProgramBinary, including the driver-binary-cache path. It chooses geometry,
then tessellation evaluation, then vertex as the final producer. BuiltIn decorations, member types,
and the selected entry-point interface identify outputs without debug names. Reachable function writes
through output access chains exclude implicit unused block members; an unused default
gl_ClipDistance[1] declaration is not evidence of an active output. Selected specialization
values and supported integer arithmetic resolve array extents; unsupported/unverifiable forms reject
enabled clipping. This bounded inspection adds no resource reflection system or authored clip mask.
It establishes declaration coverage, not proof that every control-flow path writes every enabled
output; shader authors retain that obligation.

Linked interface queries and their error checks occur only during candidate preparation. Repeated
realization validation uses retained metadata and revision/lifetime checks without native queries,
state transitions or error polling. Shader resource publication continues through existing generated
inputs when the later submission layer activates an owner, even for an unchanged pipeline.

| Task -> controlling source | Implementation and verification |
| --- | --- |
| Preparation -> Proposal / Prepared pipelines, identity, and lifetime; Geometry, target and executable contracts above | GraphicsPipeline composes readiness and GpuProgramInterface, GraphicsInterfaceValidation checks retained numeric interfaces, existing description/capability validation is reused. Production shader and numeric interface fixtures cover mismatches and reuse. |
| Historical compiled clipping -> superseded by custom-clipping cancellation below | The earlier parser, clipping fixtures and authored/prepared masks have been removed. Native engine clip state remains tracked and restored. |
| Identity/lifetime -> Proposal / Categorized cache storage, Prepared pipelines, identity, and lifetime; same-live-context contract above | Description identity is unchanged; no category values/knowledge enter keys. ExecutableRevision, GraphicsPipelineLifetime and disposal/reload fixtures cover invalidation and failed publication. |
| Resource/layout ownership -> Proposal / Architectural responsibilities and Submission contract | Existing GpuPreparedBindings remains the resource validator; compute uses the unchanged optional-metadata constructor path. Existing shader input/accessor regressions protect generated publication and failed candidate behavior. |

Explicit objects need no global retention cache. Future interning must use structural equality after
hash comparison and bound retention/eviction so obsolete executable generations are not held forever.
Production consumer migration and submission remain separate work. No live-game or performance
improvement is established by this implementation.

Interface rules were checked against the [OpenGL shading language specification](https://registry.khronos.org/OpenGL/specs/gl/GLSLangSpec.4.60.html)
and the [SPIR-V core grammar](https://raw.githubusercontent.com/KhronosGroup/SPIRV-Headers/main/include/spirv/unified1/spirv.core.grammar.json).
Reusable test shader sources live under VanillaGraphicsExpanded.Tests/Fixtures/Shaders/assets.
The test build invokes SpirvBuild with the shared ShaderBuildTool tests registry and publishes
artifacts/spirv-tests/<Configuration>/vanillagraphicsexpanded/shaders. The former clipping fixture family
has been removed. There is no separate
fixture compilation script or checked-in binary payload. Debug and Release use the same catalog
with optimization, configuration-specific debug information, variant caching and verified receipts.

The normal project reference still builds the current production shaders. Production compilation
excludes declarations in the existing tests/ program namespace. The test build
combines the generated declarations with its fixed and numerical fixtures and overlays their source assets
on production inputs in its intermediate directory. This complete test output includes current
production variants and one coherent digest manifest, avoiding overlapping manifests when a fixture
reuses a production stage. The normal source coverage validator rejects undeclared entry points.
Cache entry reads and writes are synchronized by content key. Different variants remain parallel,
but aliases cannot replace a cached file while another worker reads that same entry on Windows.
The focused concurrency regression reproduces the former access-denied failure independently of
shader compilation.
GpuShaderContracts retains the shared declaration scope so fixtures reuse the exact production stage
instances. Build selection separates their assets without changing stage ownership; publishing the production
asset catalog does not publish their test binaries or source files.

Fixture inventory and disposition:

| Family | Compilation owner |
| --- | --- |
| Existing water, uniform/storage, prepared-binding, framebuffer, layout, global-definition, scene-color, material, world-probe and direct-lighting test declarations | Shared tests registry; the 24 test source files formerly under production assets now live in test assets. Production stage reuse retains original identities. |
| Executable-interface fixtures | Shared tests registry; compiler-generated binaries and matching digest/interface manifest. Clipping-only fixtures are removed. |
| Complete-state, configurable rasterizer, first-person marker and deferred program deletion | Shared tests registry; no per-test driver GLSL compilation. |
| Aerial lookup, solar segment/disk, temporal debug reprojection, terrain displacement, eye-relative shading and relief kernels | Shared tests registry; authored wrappers import production kernels and carry fixed numeric interface locations. |
| Ordinary particle capture/integration draws and sampler handoff | Shared tests registry. |
| Installed shader coverage/depth/surface, terrain capture/tessellation/displacement raster, detail workload, sky patches, display dither, capture interception and scene-color patches | Runtime GLSL retained: installed sources and production transformations are the test inputs. Companion stages stay GLSL so native linking exercises that path. |
| Forward-surface scenario source transformations and installed solar bloom extraction | Runtime GLSL retained for constructed scenario sources and engine-stage compatibility. |
| Chunk-slot/relief engine binding and particle publication | Runtime GLSL retained for engine uniform discovery/publication and binding integration. |
| ShaderBuildTool.Tests temporary catalogs | Existing compiler pipeline retained; deliberately isolated temporary sources and outputs are mutated by build/cache/failure tests. |
| Source-only parser, layout, generator, import, patch and source-map fixtures | Remain source inputs; these tests do not require shader binaries. |


Validation: shader-enabled Debug and Release each passed 79/79 focused tests, zero failures/skips.
Receipts: artifacts/prepared-pipeline-debug-tests.log and artifacts/prepared-pipeline-release-tests.log.
Both commands rebuilt with the normal shader producer enabled; no skipped-shader or no-build option.
The headless fixture uses the installed driver; these receipts establish preparation/lifetime behavior,
not migrated production rendering, live-game acceptance or a performance improvement.

Commands used NUGET_PACKAGES=C:/Users/Sisco/.nuget/packages:

~~~text
dotnet test VanillaGraphicsExpanded.Tests/VanillaGraphicsExpanded.Tests.csproj -c Debug --no-restore --filter '<selection>' --logger 'console;verbosity=normal'
dotnet test VanillaGraphicsExpanded.Tests/VanillaGraphicsExpanded.Tests.csproj -c Release --no-restore --filter '<selection>' --logger 'console;verbosity=normal'
selection: FullyQualifiedName~PreparedGraphicsPipelineTests|FullyQualifiedName~GraphicsPipelineDescriptionTests|FullyQualifiedName~ShaderPipelineIdentityTests|FullyQualifiedName~ShaderInputSubmissionTests|FullyQualifiedName~CompiledClipDistanceTests|FullyQualifiedName~CompiledGraphicsClippingTests|FullyQualifiedName~ShaderInterfaceCompatibilityTests|FullyQualifiedName~ProductionShaderAccessorGpuTests
~~~

Earlier iterations exposed the implicit unused ClipDistance block member and two fixture assumptions:
block-form specialization was emitted with a fixed extent, and the composite fixture needed all three
output slots. The reader now excludes unused built-ins, specialization fixtures use real standalone
output declarations preserving compiler expression IDs, and the target fixture matches the executable.
Final receipts above include these corrections. Unsupported specialization expressions fail closed
when clipping is enabled. The reachable/unreachable function regression uses explicit synthetic entry
wrappers around compiler-produced code; the compiler inlines the separate GLSL helper fixture.

Second source review covered the proposal/contract mapping, structural identity, shared interface
installation, lifetime/reload rejection, topology and numeric interfaces, cold inspection and warm
query-free validation. Independent completion audit found no unresolved source or coverage defects;
final commands and receipts were independently checked. Git diff --check passed. Existing build
warnings remain; no new runtime acceptance or timing claim is made.


### Fixture build ownership correction

Clip-distance fixtures now use the SpirvBuild target and ShaderBuildTool test-only registry.
The test project embeds freshly generated artifacts after the shared receipt validation; source-tree
binaries and the independent compilation script were removed. Fixture builds use normal optimization
and configuration-specific debug information. The redundant separate debug-binary test row was
removed; both configurations now exercise their own generated binaries, with 78 focused cases each.
IDE design-time builds do not invoke the fixture producer.

Delegated shader-enabled Debug and Release each passed 78/78 cases with no failures/skips, using
the preparation selection above. Fresh fixture generation succeeded in both configurations.
Incremental validation reported 10 cache hits and zero compiler invocations. Removing generated
present.spv caused the receipt check to detect the missing output and restore an identical binary
from the existing cache (verified SHA256), again without compiler invocation.
Receipts: artifacts/clip-fixture-pipeline-fresh-build.log and
artifacts/clip-fixture-pipeline-{debug,release,missing-output}.log.
The shared fixture target is invoked automatically by the ordinary dotnet test commands above.
Earlier attempts encountered sandbox compiler-write denial and an MSBuild resource-name projection
error; the final validated target uses explicit source-item metadata for unique embedded names.
Source review and git diff --check passed. No runtime graphics behavior or performance claim changed.

### Shared test shader build validation (2026-10-06)

The consolidated test catalog contains 172 programs, 480 combinations and 486 stage binaries;
the production output contains 414 binaries and excludes the tests/ namespace. Both configurations
rebuilt through SpirvBuild with default parallel compilation. Source coverage, digest verification,
stage specialization, graphics linking and migrated numerical fixtures exercise the shared output.
Runtime compilation remains only where source processing or engine GLSL integration is under test.

The shared-uniform fixtures retain their original locations and single uploads. Removing only
OpName from otherwise identical Debug modules reproduced the installed driver's duplicate-location
failure. Retaining compiler metadata allowed Release linking, but that workaround was rejected.
Release debug stripping is restored. The affected fixture linking failures remain unresolved
until the standalone numeric uniform migration is scoped and implemented.
See [shader compiler policy](ShaderAuthoring.md) for the evidence and asset-size tradeoff.

Historical receipts with the temporary metadata-retention policy: Debug passed 1510/1510;
Release passed 1509/1510, with no skips. These do not validate the restored stripped Release policy.
The historical Release failure was RuntimeFailurePreservesInstalledGeneration's wrong-stage injection:
specialization leaves InvalidValue pending, and the next executable-interface preparation rejects it.
It also failed before the metadata correction; the rejected-shader error cleanup correction is recorded
under Packaged shader interfaces below. All migrated shared-uniform cases and graphics
inventory linking cases pass. Logs: artifacts/shared-fixtures-focused-{debug,release}.log.
The earlier broader run was not green: it also exposed snapshot drift, compute-binding assertions,
boundary/lifetime failures and the unchanged SurfaceLightingParams CPU/shader size mismatch.
These focused receipts do not establish full-suite acceptance.

ShaderBuildTool.Tests passed 50/50 in each configuration, including the cache concurrency regression
and compiler invocation policy (artifacts/shared-fixtures-tool-{debug,release}.log). An unchanged
build performed zero compiler invocations for both catalogs. Removing generated eye-relative.fsh.spv
triggered receipt invalidation and restored byte-identical output using 486 cache hits and zero
compiler invocations (artifacts/shared-fixtures-{incremental,recovery}.log). Both configuration
manifests contain every expected output. No in-game visual or performance acceptance is claimed.

Release metadata-retention rollback: the compiler and invocation regression again select `-g`
only for Debug, with `-O` in both configurations. The earlier passing shared-uniform Release
results are historical, not current acceptance. At that rollback, UBO migration still awaited
an agreed inventory and scope; the completed migration and fresh evidence are recorded below.

## Standalone numeric input migration inventory

Status: completed and independently audited on 2026-10-06 under the approved engine GLSL
compatibility exceptions below. The original source inventory is reconciled with the final block
map, exact retained engine interfaces, CPU owners and runtime-test dispositions. Broader-suite
baseline failures and live/performance limitations remain explicit in the verification record.

Controlling lifetime sources: Rendering.UniformBufferLifetime.md and its archived proposal/checklist
at revision `62bcc898^` (removed by `62bcc898`, "docs: remove completed documents").
Reuse CpuUniformBuffer packing/revisions, UniformPublication, existing transient/persistent allocators,
shader-owned versus borrowed disposal, and prepared numeric binding slots. No new allocation system.

| Source | Original standalone numeric declarations (disposition below) |
| --- | --- |
| VanillaGraphicsExpanded/assets/vanillagraphicsexpanded/shaders/includes/atmosphere_sun_fragment.glsl | `int vge_atmosphereSunDraw`; `int vge_sceneLinear`; `vec4 vge_atmosphereSun`; `vec4 vge_atmosphereDisk` |
| VanillaGraphicsExpanded/assets/vanillagraphicsexpanded/shaders/includes/atmosphere_sun_vertex.glsl | `int vge_atmosphereSunDraw`; `vec4 vge_atmosphereSun`; `vec4 vge_atmosphereDisk` |
| VanillaGraphicsExpanded/assets/vanillagraphicsexpanded/shaders/includes/pbr_forward_surface.glsl | `int vge_sceneLinear`; `vec3 pointLights[DYNLIGHTS]`; `vec3 pointLightColors[DYNLIGHTS]`; `int pointLightQuantity` |
| VanillaGraphicsExpanded/assets/vanillagraphicsexpanded/shaders/includes/pbr_shadowmaps.glsl | Commented interface examples only: shadow matrices, ranges and intensity. Production `pbr_direct_lighting.fsh` already supplies these through `pbr_direct_lighting_params_ubo.glsl`; no standalone declarations here. |
| VanillaGraphicsExpanded/assets/vanillagraphicsexpanded/shaders/includes/tessellation/terrain.tesh | `int vge_displacementReactive` |
| VanillaGraphicsExpanded/assets/vanillagraphicsexpanded/shaders/includes/tessellation/terrain_displacement.glsl | `mat4 mvpMatrix`; `mat4 modelViewMatrix`; `mat4 projectionMatrix`; `float vge_tessellationFocalPixels`; `int vge_displacementEnabled`; `vec4 vge_tessellationPixels`; `vec2 vge_tessellationDistance` |
| VanillaGraphicsExpanded/assets/vanillagraphicsexpanded/shaders/includes/vge_terrain_normal.glsl | `int vge_twoSidedTerrain` |
| VanillaGraphicsExpanded/assets/vanillagraphicsexpanded/shaders/includes/vge_view.glsl | `mat4 modelViewMatrix` |
| VanillaGraphicsExpanded/assets/vanillagraphicsexpanded/shaders/pbr_display_resolve.fsh | `int particleLayerEnabled`; `int sceneLinear` |
| VanillaGraphicsExpanded.Tests/Fixtures/Shaders/assets/vanillagraphicsexpanded/shaders/tests/aerial-lookup.fsh | `vec3 displacement`; `float visibility` |
| VanillaGraphicsExpanded.Tests/Fixtures/Shaders/assets/vanillagraphicsexpanded/shaders/tests/displacement-height.fsh | `vec3 sampleInput` |
| VanillaGraphicsExpanded.Tests/Fixtures/Shaders/assets/vanillagraphicsexpanded/shaders/tests/displacement-metric.fsh | `float distance` |
| VanillaGraphicsExpanded.Tests/Fixtures/Shaders/assets/vanillagraphicsexpanded/shaders/tests/eye-relative.fsh | `vec3 surface`; `int outputMode` |
| VanillaGraphicsExpanded.Tests/Fixtures/Shaders/assets/vanillagraphicsexpanded/shaders/tests/eye-relative.vsh | `mat4 modelViewMatrix` |
| VanillaGraphicsExpanded.Tests/Fixtures/Shaders/assets/vanillagraphicsexpanded/shaders/tests/particle-draw.fsh | `vec4 colour`; `float depthValue` |
| VanillaGraphicsExpanded.Tests/Fixtures/Shaders/assets/vanillagraphicsexpanded/shaders/tests/rasterizer.vsh | `int lineMode`; `int zeroDepth` |
| VanillaGraphicsExpanded.Tests/Fixtures/Shaders/assets/vanillagraphicsexpanded/shaders/tests/relief-off.fsh | `vec2 metric` |
| VanillaGraphicsExpanded.Tests/Fixtures/Shaders/assets/vanillagraphicsexpanded/shaders/tests/relief-on.fsh | `vec2 metric` |
| VanillaGraphicsExpanded.Tests/Fixtures/Shaders/assets/vanillagraphicsexpanded/shaders/tests/sun-raster.vsh | `vec3 camera` |
| VanillaGraphicsExpanded.Tests/Fixtures/Shaders/assets/vanillagraphicsexpanded/shaders/tests/sun-segment.fsh | `float elevation` |
| VanillaGraphicsExpanded.Tests/Fixtures/Shaders/assets/vanillagraphicsexpanded/shaders/tests/temporal-debug.csh | `mat4 prevViewProjMatrix`; `int probeSpacing`; `int debugMode`; `vec2 probeGridSize`; `float depthRejectThreshold`; `float normalRejectThreshold`; `float temporalAlpha` |
| VanillaGraphicsExpanded.Tests/Fixtures/Shaders/assets/vanillagraphicsexpanded/shaders/tests/uniform_state.csh | `float scalar`; `float values[2]`; `vec3 vector`; `mat4 transform` |
| VanillaGraphicsExpanded.Tests/Fixtures/Shaders/assets/vanillagraphicsexpanded/shaders/tests/water_pixel_normal_refraction.fsh | `vec3 surfaceVS`; `vec3 normalVS`; `vec3 baseNormalVS`; `mat4 projectionMatrix`; `mat4 inverseProjectionMatrix`; `vec2 frameSize`; `int underwater` |
| VanillaGraphicsExpanded.Tests/Fixtures/Shaders/assets/vanillagraphicsexpanded/shaders/tests/water_receiver_filter.fsh | `vec2 sampleUv`; `vec3 surfaceVS`; `vec3 normalVS`; `mat4 inverseProjection`; `vec2 frameSize` |
| VanillaGraphicsExpanded.Tests/Fixtures/Shaders/assets/vanillagraphicsexpanded/shaders/tests/water_refraction_diagnostics.fsh | `int diagnosticScenario`; `vec2 frameSize`; `vec3 customSurface`; `vec3 customNormal`; `int diagnosticBudget`; `int diagnosticSelect`; `int diagnosticQuality`; `int diagnosticUnderwater`; `mat4 projectionMatrix`; `mat4 inverseProjectionMatrix` |
| VanillaGraphicsExpanded.Tests/Fixtures/Shaders/assets/vanillagraphicsexpanded/shaders/tests/water_uv_refraction.fsh | `vec3 surfaceVS`; `vec3 normalVS`; `mat4 projectionMatrix`; `mat4 inverseProjectionMatrix`; `vec2 frameSize`; `int underwater` |

Additional production source-injection and publication owners identified:

| Family | Sources and CPU publication | Implemented ownership and compatibility disposition |
| --- | --- | --- |
| Atmosphere | AtmosphereSkyPatches; atmosphere_sun_vertex/fragment includes; PbrSurfaceShaderPatches/PbrTerrainColorPatches; AtmosphereShaderBindingHook; AtmosphereProgramBindings | Retain the approved engine GLSL interface: lighting snapshots are frame/view data, solar selection is reset on each relevant engine Use including startup. No UBO epoch or HasUniform behavior change. Binary solar fixtures use their shared SunInputs block. |
| Scene-color routing | PbrFinalDisplayPatches; SceneColorLegacyPatches/SceneColorPostprocessPatches; SceneColorParticleCapture; PbrDrawRouteHook | Retain approved engine GLSL routing values and nested capture/restoration semantics per engine program. Owned display resolve has its separate Routing block. |
| Terrain subdivision | TerrainDisplacementPatches; TerrainTessellationStages; TerrainDisplacementRuntime | Retain approved engine GLSL view/frame subdivision and per-pool reactive values, including shadow variants and engine matrices. Binary displacement fixtures use DisplacementInputs. |
| Terrain normals | vge_terrain_normal.glsl; TerrainSurfaceNormals | Retain approved engine GLSL per-pool two-sidedness. Owned eye/relief fixtures carry the equivalent integer in their fixture blocks. |
| Display resolve | IPBRDisplayResolveShaderProgramBindings; PBRDisplayResolveShaderProgram; pbr_display_resolve.fsh | One shader-owned 16-byte block for the two integer routing values; keep typed setters, zero defaults, dirty suppression and SingleFrame publication. Use slot 28, distinct from inherited include blocks. |
| Surface lighting | SurfaceLightingParamsUbo; lumon_surface_lighting.glsl; atmosphere snapshot publication | Lookup consumers now publish 144 bytes; producer-only atmosphere fields remain neutral. SurfaceLightingDispatch remains the owner of the real atmosphere values, as detailed below. |
| Generated fixture values | IUniformStateComputeBindings and water UV/pixel-normal/receiver/diagnostic bindings | Typed per-fixture blocks with reviewed std140 offsets; retain independent array snapshots, mutation guards and numerical behavior. |
| Fixed/numerical fixtures | TestShaderPrograms; BuiltShaderFixture; TerrainShaderTestFixture; numerical/state/particle tests | Share one UBO publication across stages for sun and eye matrices. Use existing publication helpers; preserve runtime compilation where source processing is the actual subject. |

Approved boundary: untouched base-game matrices, shadow transforms/ranges and dynamic-light arrays
remain engine-owned inputs on engine GLSL paths. VGE-owned SPIR-V fixtures importing those kernels
need explicit fixture UBO inputs instead. Sampler/image locations, storage/atomic bindings, structural
defines and specialization constants retain their existing distinct contracts. The block map and compatibility decision below define the implemented disposition.
Completion still requires the final caller sweep and validation.

### Approved engine publication boundary

`GpuUniformRingModSystem` registers the existing publication epoch at the world's `Before`/`Done`
render stages. `UniformPublication` requires an open epoch even for `MultiFrame` storage: retaining
storage does not remove last-use accounting. `AtmosphereShaderBindingHook` also deliberately resets
solar selection before a lighting snapshot exists, and `AtmosphereProgramBindings` initializes linked
engine interfaces before their first draw. Moving those values to a UBO must preserve that behavior.
Skipping the write when no ring is active would leave context-global bindings stale; it is not an adapter.

Approved scope (2026-10-06): retain the engine GLSL numeric inputs as compatibility exceptions.
This covers the atmosphere, scene-color routing, terrain subdivision and terrain-normal engine
adapters inventoried above, including their VGE-added inputs. Preserve their current activation,
name discovery and reload behavior. The engine-frame publication boundary stays unchanged.
Every VGE-owned SPIR-V input and reusable binary fixture still migrates, including fixture copies
of imported engine inputs. Runtime tests of those engine interfaces retain the matching compatibility
declarations; test-owned numeric controls use blocks. Do not add a second allocator or bypass
last-use tracking. This approval resolves the compatibility disposition, not the completion gate.

### Exact retained engine numeric interfaces

All members below are single values unless an array extent is shown. They retain engine GLSL
program-local storage and the engine's linked-interface/reload lifecycle. Fixture copies use
the block layouts below.

| Members and types | Declaration source | CPU publication and frequency |
| --- | --- | --- |
| `vec3 vge_atmosphereEnvironment` | PbrTerrainColorPatches and PbrSurfaceShaderPatches | AtmosphereShaderBindingHook.Postfix publishes Lighting.Environment on each relevant engine Use when a snapshot exists. |
| `vec3 vge_atmosphereSolar`; `vec3 vge_atmosphereSunDirection`; `vec3 vge_atmosphereAerialParams` | PbrSurfaceShaderPatches; AtmosphereSkyPatches also declares SunDirection | Same Postfix publishes snapshot Solar, Sun, and altitude/horizon/camera-underwater respectively on engine Use. |
| `float vge_atmosphereLutHorizon` | AtmosphereSkyPatches | Same Postfix publishes snapshot horizon on sky activation. |
| `int vge_atmosphereSunDraw`; `vec4 vge_atmosphereSun`; `vec4 vge_atmosphereDisk` | atmosphere_sun_vertex/fragment.glsl | Same Postfix resets SunDraw every Use, including startup; only solar draws write Sun direction/horizon and Disk radiance/angular radius. |
| `int vge_pbrRoute` | PbrSurfaceShaderPatches | PbrDrawRouteHook.Postfix derives the route from the current engine render stage/framebuffer at Use. |
| `int vge_sceneLinear` | PbrFinalDisplayPatches; SceneColorLegacyPatches; SceneColorPostprocessPatches; pbr_forward_surface.glsl; atmosphere_sun_fragment.glsl | SceneColorParticleCapture enables it for the borrowed particle program and resets it on scope disposal. Other engine programs retain existing zero/default routing unless their existing caller selects it. |
| `int vge_displacementEnabled` | TerrainDisplacementPatches and terrain_displacement.glsl | TerrainDisplacementRuntime.Bind updates changed pool eligibility before adaptive grouped draws; resets publication knowledge on program replacement. |
| `int vge_displacementReactive` | TerrainDisplacementPatches and terrain.tesh | Same Bind publishes frame/atlas change state at first relevant use each frame; shadow program excluded. |
| `vec2 vge_tessellationDistance`; `vec4 vge_tessellationPixels`; `float vge_tessellationFocalPixels` | TerrainDisplacementPatches and terrain_displacement.glsl | Same Bind publishes fade distances, framebuffer size/target edge pixels/maximum level and focal scale once per program per frame. |
| `int vge_twoSidedTerrain` | vge_terrain_normal.glsl | TerrainSurfaceNormals.Bind publishes per-pool two-sidedness before the grouped draw. |
| `mat4 modelViewMatrix`; `mat4 viewMatrix` | PbrSurfaceShaderPatches selects the engine matrix; vge_view.glsl and terrain_displacement.glsl reuse modelViewMatrix | Existing engine matrix publication owns values and draw frequency; no replacement VGE upload owner. |
| `mat4 mvpMatrix`; `mat4 projectionMatrix` | terrain_displacement.glsl | Existing engine view/draw matrix publication, retained with adaptive terrain's engine interface. |
| `float shadowRangeFar`; `float shadowRangeNear`; `mat4 toShadowMapSpaceMatrixFar`; `mat4 toShadowMapSpaceMatrixNear` | TerrainTessellationStages | Existing engine shadow/view publication; injected stages reuse its data ownership. |
| `vec3 pointLights[DYNLIGHTS]`; `vec3 pointLightColors[DYNLIGHTS]`; `int pointLightQuantity` | pbr_forward_surface.glsl | Existing engine light arrays/count; extent follows the engine's DYNLIGHTS compilation setting and values follow its draw publication. |

AtmosphereProgramBindings.Register resolves active allowlisted members after linking and removes
old metadata before recompilation. Its immediate initialization concerns aerial sampler units,
not numeric values. Atmosphere sky/aerial textures, terrain relief/material textures and chunk-slot
samplers are opaque resources and retain their binding contracts.

### Block design for the binary-owned inputs

Use explicit `std140` blocks and the existing generated `UniformBlock` resource submission. Typed
scalar/vector/matrix setters write a shader-owned `CpuUniformBuffer`; array setters copy into owned
packed storage. Register ownership and mutation guards through the existing shader owner. Default
bytes are zero; unchanged writes retain revisions; changed inputs publish immutable versions at the
next activation. Use `SingleFrame` initially, with no new lifetime policy or content cache. Borrowed
fixture blocks remain fixture-owned and are disposed after submission has retired through the existing
publication mechanism. Shared stages use one identical declaration and one logical publication.

The following layouts give member byte offsets. Migrated program-owned inputs use the dedicated
`GpuBindingRegistry.Ubo.ShaderInputs` slot 28, separate from imported frame/world-probe/material/terrain
blocks at 12/13/15/27. Reusing a slot across different programs is valid, but every
activation must establish that program's range through StateCache. Sampler and image slots are separate.

| Block owner | Member offsets | Bytes |
| --- | --- | ---: |
| PBRDisplayResolveShaderProgram | sceneLinear 0; particleLayerEnabled 4 | 16 |
| UniformStateComputeShader | scalar 0; values[2] 16 (stride 16); vector 48; transform 64 | 128 |
| WaterUvRefraction fixture | surfaceVS 0; normalVS 16; projectionMatrix 32; inverseProjectionMatrix 96; frameSize 160; underwater 168 | 176 |
| WaterPixelNormalRefraction fixture | surfaceVS 0; normalVS 16; baseNormalVS 32; projectionMatrix 48; inverseProjectionMatrix 112; frameSize 176; underwater 184 | 192 |
| WaterReceiverFilter fixture | sampleUv 0; surfaceVS 16; normalVS 32; inverseProjection 48; frameSize 112 | 128 |
| WaterRefractionDiagnostic fixture | diagnosticScenario 0; frameSize 8; customSurface 16; customNormal 32; diagnosticBudget 44; diagnosticSelect 48; diagnosticQuality 52; diagnosticUnderwater 56; projectionMatrix 64; inverseProjectionMatrix 128 | 192 |
| temporal-debug fixture | prevViewProjMatrix 0; probeSpacing 64; debugMode 68; probeGridSize 72; depthRejectThreshold 80; normalRejectThreshold 84; temporalAlpha 88 | 96 |
| eye-relative fixture, shared vertex/fragment declaration | modelViewMatrix 0; surface 64; outputMode 76; imported vge_twoSidedTerrain 80 | 96 |
| sun-raster fixture, shared vertex/fragment declaration | vge_atmosphereSunDraw 0; vge_sceneLinear 4; vge_atmosphereSun 16; vge_atmosphereDisk 32; camera 48 | 64 |
| aerial-lookup fixture | displacement 0; visibility 12 | 16 |
| particle-draw fixture | colour 0; depthValue 16 | 32 |
| rasterizer fixture | lineMode 0; zeroDepth 4 | 16 |
| sun-segment fixture | elevation 0 | 16 |
| relief fixtures | imported modelViewMatrix 0; imported vge_twoSidedTerrain 64; metric 72 | 80 |
| displacement fixtures | mvpMatrix 0; modelViewMatrix 64; projectionMatrix 128; vge_tessellationFocalPixels 192; vge_displacementEnabled 196; vge_tessellationPixels 208; vge_tessellationDistance 224; sampleInput 240; distance 252 | 256 |

Matrices use the existing CPU matrix convention and 16-byte column stride; matrix readback must check
non-symmetric values. Integer and boolean shader controls use 32-bit components. A vec3 followed by a
scalar can share its fourth component; an array element instead retains std140's 16-byte stride.
These layouts are design inputs, not linked-layout verification receipts.

The normal/depth import closes over `vge_view.glsl` and `vge_terrain_normal.glsl`; both eye-relative
and relief fixture declarations therefore include their matrix/two-sided inputs even if a particular
selection optimizes them away. Displacement fixtures share a superset layout for shadow and visible
projection variants. Unused members remain zero; removal by optimization is distinct from missing
required active input publication.

Direct native-call scan: the migrated consumers no longer use `ShaderUniformUpload`, which has been removed. `GpuProgramLayout` and the terrain material/chunk-slot hooks also
call `Uniform1`, but those calls assign sampler units; retain them. `UniformBlockBinding` calls
establish resource interfaces, not numeric value uploads. Engine `program.Uniform` calls need their
separate compatibility disposition and cannot be accounted for by scanning `GL.Uniform` alone.

`RuntimeSubmissionEmitter` already distinguishes `CpuUniformBuffer` resources and validates all
resources before publication. Reuse that path rather than generating ordinary numeric GL uploads for
the new blocks. The obsolete `UniformValueReader` and `ShaderUniformPublication` paths have been removed.
Typed setters now write the owned buffers; arrays retain copied values and mutation guards remain
on the owning shader. The generator rejects numeric `UniformLocation` runtime properties, and
prepared owned executables reject active standalone numeric uniforms. Sampler/image location
metadata remains supported. Engine GLSL compatibility uses its existing separate interface.

### Runtime source-test dispositions

The raw C# declaration scan additionally finds the following runtime source families. Compilation
remains runtime when source transformation or engine linking is the test subject; that fact alone
does not exempt test-authored numeric inputs from block migration.

| Test source | Numeric declarations / disposition |
| --- | --- |
| AtmosphereEngineBindingTests; AtmosphereSkyLookupTests | sampleDirection; retain engine patch/link exercise, move test-owned direction to a block; injected atmospheric inputs retain the approved engine GLSL interface |
| AtmosphereSunBloomTests | attenuation; test-owned block, preserve the tested source processing |
| PbrForwardSurfaceNumericalTests | surfaceView; fixture-owned matrix block, distinguish imported engine values |
| PbrFinalDisplayDitherTests; SceneColorParticlePublicationTests | vge_sceneLinear; retain the approved production engine routing interface |
| PbrTerrainCaptureGpuTests | normal-input, foliage-transmission and liquid-interface numerical fixtures now build through SpirvBuild; only the actual transformed-engine-main test compiles at runtime |
| TerrainChunkSlotBindingTests | position/normal; fixture-owned block, preserve engine chunk-slot binding behavior |
| TerrainDisplacementRasterTests | injected engine shadow matrices; retain engine contract test and reconcile other imported values |
| ShaderPatchRecoveryTests | deliberately mismatched vec3/vec4 mismatch and invalid failedUniform source; retain standalone declarations because interface/source failure is the assertion |
| UniformExtractorTests; ShaderSourceLayoutTests; DerivedGlobalConstantsTests; ShaderPatchingTests; PbrTerrainColorPatchesTests | parser, layout, compatibility-source and rejection strings; not runtime VGE numeric publication, retain input examples and update expected patched declarations where applicable |
| RenderTestBase | comments on compatibility upload helpers; not shader declarations; audit helper callers before removal |

### Surface-lighting ownership finding and baseline

The 144-byte producer is `SurfaceLightingDispatch`: it already writes environment, solar and sun
at offsets 96, 112 and 128 from `AtmosphereModSystem.Lighting`, with defined fallback values. Its
compute shader consumes these fields during direct-light production. `SurfaceLightingParamsUbo`
instead belongs to the three lookup consumers (`SurfaceLightingQueryBatch`, `WorldProbeTraceBatch`,
and `LumOnScreenProbeAtlasTraceShaderProgram`), which sample previously produced radiance through
`sampleSurfaceLighting`; they do not compute direct illumination from these atmosphere members.
Its fix must supply the full shared block ABI with explicit neutral producer-only fields, preserve
the lookup-domain values, and leave atmosphere publication with the producer. Copying live atmosphere
into every lookup would introduce an unrelated dependency and would not change sampled radiance.

Fresh normal shader-enabled baseline (2026-10-06): Debug passed 6/7 and stripped Release passed 1/7.
Both configurations rebuilt 414 production and 486 shared stage binaries. Debug's failure is
`LumOnNearFieldFunctionalTests.Parallax_ChangesDirectionalLookup` rejecting the 96-byte block.
Release additionally fails two solar shared-uniform links (locations 12/16) and three eye-relative
links (location 16). No workarounds or skipped variants were applied. Logs:
`artifacts/ubo-migration-baseline-debug.log` and `artifacts/ubo-migration-baseline-release.log`.
These are baseline failures, not migration acceptance. The lookup buffer now reserves the full
144-byte ABI, retaining zero producer-only fields. Fresh focused verification passed 18/18 in
Debug and 18/18 in stripped Release, with no skips: the formerly failing parallax case, world-probe
transport and surface-lighting consumer lookup coverage. Logs: `artifacts/surface-lighting-abi-debug.log`
(4 cases), `artifacts/surface-lighting-lookup-debug.log` (14 cases), and
`artifacts/surface-lighting-abi-release.log` (18 cases). Source review confirms only lookup storage
size changed; the producer's lighting values and ownership remain intact. Standalone numeric-input
migration acceptance is established by the final combined evidence below, not this correction alone.

Implemented migration: display resolve owns a 16-byte routing block;
the four water fixture owners retain typed accessors backed by owned blocks; eye-relative and solar
raster fixtures use one shared declaration and one publication across vertex and fragment stages.
Engine include guards permit fixture-owned declarations while leaving the default engine interface
unchanged. An emitted-source defect exposed by imported blocks (`}#line`) is corrected at directive
insertion, with source-mapping regression coverage; shader compilation remains exclusively in SpirvBuild.

### Current migration verification

The consolidated migrated binary-fixture batch passed 241 tests in Debug and 241 in stripped
Release, with one existing source-map skip in each configuration. Both configurations rebuilt
414 production and 486 shared test shader variants. Logs are `artifacts/ubo-consolidated-debug.log`
and `artifacts/ubo-consolidated-release.log`. These receipts predate removal of the obsolete numeric
submission code and migration of runtime test-owned controls; final receipts below supersede them.

Runtime engine-source tests use `FixtureUniformInputs` to publish their own `TestInputs` block
through existing `CpuUniformBuffer` publication at slot 28. Engine-owned and approved VGE-added
engine uniforms retain their compatibility API. `StandaloneNumericInputTests` deliberately links
a numeric uniform to verify rejection by owned executable preparation; it is an intentional
negative-test declaration, not an alternative submission path.

The final fixture sweep also moves the numerical normal/transmission/liquid tests from
`PbrTerrainCaptureGpuTests` into shared binary fixtures. `normal-input` shares a 16-byte block
between vertex and fragment stages: transmission 0, two-sided 4, smooth-normal 8, back-facing 12.
`foliage-transmission` uses 48 bytes: visibility 0, light direction 16, view direction 32.
`liquid-interface` uses 16 bytes: cosine 0 and boolean underwater 4. All bind at slot 28, retain
zero defaults and use one fixture-owned single-frame publication with per-case writes.
The original transformed-engine-main assertion remains runtime GLSL.

Range publication continues to validate size and slot through `GpuUniformBuffer.BindPublicationRange`
and `GpuSupport`, using the existing ring alignment. Native shader compilation/linking checks
per-stage and combined active block limits for the actual compiled variants. No secondary limits
cache or allocation system is introduced.

Binding snapshot reconciliation updates only display-resolve, uniform-state and the four migrated
water fragment contracts: numeric locations are replaced by their named block at slot 28, while
opaque resources and output metadata retain their contracts. The display vertex fingerprint remains
unchanged. Missing water fragment rows are added for their reviewed block contracts. Unrelated
pre-existing composite/catalog migration snapshot drift is not silently rebaselined.

Measurement interpretation: ShaderBindingOperationTests counts ring bytes/allocations and native
resource binds over eight repeated submissions after warm-up. Its zero numeric GL uploads follows
from removal of that submission path; it is not a driver timing measurement. ShaderUniformStateTests
checks one 128-byte publication shared across A/A/B/A/replacement activations and verifies GPU output.
These counters concern GPU storage/publication, not all managed allocations (the array assignment
workload still creates caller arrays). No CPU/GPU frame-time improvement or in-game visual acceptance
is inferred from them.

The corrected expanded Debug run (`artifacts/ubo-final-debug.log`) reports 1,815 passes,
three pre-existing snapshot failures and one existing source-map skip. All 491 packaged binary
specializations, declared graphics combinations, compute replacement/disposal cases and source
registry coverage pass. The registry correction places shared declarations under `includes/tests`;
the compute inventory now releases program state through StateCache rather than bypassing it.
The remaining snapshot failures concern the older composite/catalog baseline, not a waived UBO ABI
assertion. Corrected stripped Release inventory (`artifacts/ubo-final-inventory-release.log`)
passes 1,464 cases with the one pre-existing wrong-stage recovery failure and no skips.
All compiled selections specialize, every declared graphics combination links, and all compute
replacement/disposal cases pass. The broader migrated numerical Release batch also passed its
changed cases (`artifacts/ubo-final-release.log`); that earlier run exposed the subsequently fixed
include classification and compute test cache bypass.

Final supporting receipts (2026-10-06):

| Verification | Debug | Stripped Release | Evidence |
| --- | ---: | ---: | --- |
| Contract generator | 173 passed | 173 passed | artifacts/ubo-generator-{debug,release}.log |
| Shader build tool | 50 passed | 50 passed | artifacts/ubo-buildtool-{debug,release}.log |
| Lifetime, sharing, ring, publication, production accessors and binding contracts | 63 passed | 63 passed | artifacts/ubo-lifetime-{debug,release}.log |
| Operation counters and publication reuse | Covered in expanded tests | 6 passed with detailed counters | artifacts/ubo-operations-release.log |

Both configurations compile 414 production and 491 shared binaries through the normal producer.
Release retains optimization and strips debug names. Its incremental build reports zero compiler
invocations; deleting the copied eye-relative fragment binary and rebuilding restores identical
bytes through the existing cache, with 491 cache hits and zero compiler invocations. Receipts:
`artifacts/ubo-incremental-release.log` and `artifacts/ubo-recovery-release.log`. The binary digest
inventory verifies packaged bytes against the manifest.

Eight unchanged submissions measured zero additional ring copies, allocations, native range binds,
reflection or name-resolution work. The shared A/A/B/A/replacement case uses one 128-byte publication.
The unchanged mixed-lifetime workload on both upload backends reports 128 logical publications;
stable retained allocation count is 1 versus transient reference 8, and copied bytes are 16 versus
128, with no blocking waits in this fixture. These are repeatable operation counts, not timing claims.

Remaining broader-suite failures are retained and not skipped: three catalog/composite snapshot
mismatches and the Release wrong-stage recovery case leaving InvalidValue before recovery preparation.
`artifacts/shared-fixtures-debug.log` and `artifacts/shared-fixtures-release.log` establish them before
this migration. The independent audit classified the unchanged wrong-stage recovery behavior as
separate from numeric UBO publication; installed-generation preservation and the other reload/failure
cases pass. The existing source-map skip also remains visible. No game was launched.

Second source review checked CPU/GLSL layouts, ownership/disposal, mutation guards, failure-before-
publication, sampler metadata, runtime compatibility and direct native-call cleanup. The independent
completion audit accepted the implementation and final receipts after resolving the exact engine
exception inventory. No migration finding remains. The known unrelated failures above remain
visible; the audit does not characterize the entire repository suite as passing.

## Packaged shader interfaces (2026-10-06)

Custom shader clip distances are unsupported. Graphics descriptions and prepared realizations
carry no clip-enable mask, and shader packaging does not extract clipping outputs or analyze their
specialization dependencies. VGE graphics-state application explicitly disables all user clip
distances through StateCache. Native masks, knowledge bits, engine adapters and boundary restoration
remain necessary to preserve incoming engine state. Ordinary frustum clipping, clip origin/depth
conventions and depth-clamp policy are unchanged.

The digest manifest associates exact binary hashes with immutable stage declarations, entry point,
structural key, build configuration and extractor identity. SPIRV-Cross supplies numeric input/output
shapes, locations and execution modes. Linked activity and driver-assigned resource addresses remain
with their existing runtime owners. Schema version controls runtime compatibility; the nonempty extractor
identity records provenance, not a demand for the identical compiler version. Build receipts fingerprint
the managed extractor and nested native DLL, SO and DYLIB libraries, so tool changes invalidate incremental reuse.

Supported declarations are location-decorated 32-bit signed/unsigned integers and 32/64-bit floating
scalars/vectors, floating matrices and literal-sized arrays. Interface blocks and unresolved
specialization-dependent array extents reject explicitly. Linked vertex inputs and fragment outputs
are checked against these declarations before candidate publication, allowing linker elimination.
Metadata does not assign resource addresses or replace linked topology/resource validation.

Execution modes retain three operand slots with unused slots zeroed. Operand-bearing support is
limited to LocalSize, LocalSizeId, Invocations and OutputVertices, whose operands the compiler API
exposes. Standard operand-free OpenGL geometry/tessellation, origin, depth and transform-feedback
modes are preserved. Other modes reject explicitly instead of publishing fabricated zero operands.
LocalSizeId operands are tagged as binary-local IDs, never resolved workgroup dimensions.

Ordinary numeric specializations remain unrestricted. The clipping-specific finite-domain tables,
SPIRV-Tools optimizer dependency, activity reflection, runtime parser and clipping shader fixtures
have been removed. No native dependency-analysis integration is planned.

Compilation and extraction finish before a staged binary/metadata directory replaces the previous
complete generation. Failed compilation retains the previous generation and invalidates the attempted
build receipt. Runtime validates captured binary association even when driver caching is disabled.

### Interface ownership and verification mapping

| Plan task -> controlling source | Implementation and acceptance coverage |
| --- | --- |
| Remove custom clipping -> Proposal / Static and dynamic state coverage | RasterizerDesc and GraphicsPipeline carry no custom clip mask. StateCache disables distances and ConfigurableRasterizerGpuTests verifies warm suppression plus successful/failed engine restoration. |
| Portable contract -> Proposal / Prepared pipelines, identity, and lifetime | PackagedShaderInterface holds immutable declarations; ShaderInterfaceReflection uses compiler APIs, rejects unsupported shapes/modes, and ShaderInterfaceReflectionTests exercises real optimized numeric declarations. PackagedInterfaceValidationTests covers identity, association and malformed data. |
| Build/publication -> same proposal section; ShaderAuthoring / build and package rules | ShaderVariantBuild extracts on compilation and variant-cache hits, stages complete generations and replaces the publication directory. ShaderParallelBuildTests verifies failed-generation preservation; ShaderIncrementalBuildTests verifies missing-output recovery and catalog removal with matching metadata. |
| Preparation/lifetime -> Proposal / Prepared pipelines, identity, and lifetime; Prepared graphics realizations above | PreparedProgramBinary captures and verifies exact bytes before either driver-cache path. GpuProgram validates linked declarations before installation; DriverProgramCacheTests exercises malformed metadata with caching enabled/bypassed and retains the prior executable, bindings and revision. Existing pipeline/lifecycle/batch tests cover borrowed lifetimes, reload and superseded candidates. |
| Linked activity and submission -> Proposal / Architectural responsibilities and Submission contract | GraphicsInterfaceValidation retains target/discard/topology checks. GpuPreparedBindings owns resource activity; generated UBO inputs and compute submission remain unchanged. PreparedGraphicsPipelineTests checks 100 retained validations add no reflection queries. |

Delegated verification results and measurement limits follow.

Release packaging was verified using the existing Cake command from an isolated source copy:
`dotnet run --project CakeBuild.csproj --configuration Release -- --target Package --configuration Release`
with working directory `artifacts/interface-packaging/workspace/CakeBuild`. The copy included all source
assets, including ignored debug include files; an initial incomplete copy failed and was corrected before
the successful run. Existing release output was untouched. The ZIP contains exactly 414 SPIR-V binaries
and one matching schema-2 Release manifest: every length/hash matches, with no missing/stale variants,
build receipt or private cache content. Production output copies all 414 pairs exactly; shared test outputs
copy all 481 pairs exactly in both configurations.

The resulting ZIP is 5,544,063 bytes; uncompressed SPIR-V payloads total 5,206,928 bytes and the
manifest is 297,187 bytes (22,282 compressed). The package shader invocation measured 2,259.1 ms
accumulated extraction elapsed time across workers and 50,813.7 ms build wall time, with 18 cache hits
and 396 compiler invocations. The complete Cake task took 74.288 s. A prior unchanged production
receipt check performed no compilation: 54.0 ms receipt checking, 1,385.4 ms total invocation.
These are local build observations, not CPU-only timings or evidence of improved frame/GPU time.
Receipts: `artifacts/interface-packaging/release-package.log`, `package-results.json`, `copy-results.txt`.

The actual CompilerFingerprint method was also exercised in the isolated tool output with temporary
nested `.dll`, `.so`, `.so.1` and `.dylib` files: addition and same-length content changes each changed
the fingerprint, and removal restored the original fingerprint. No real dependency was modified.
This verifies dependency-byte invalidation on this Windows host, not native loading on other platforms.
The broader hash filter was validated after packaging; it changes build invalidation only, not ZIP payloads.
Receipt: `artifacts/interface-packaging/fingerprint-results.log`.

Debug runtime verification initially passed 559/560 cases. Its sole failure was the relocated compute
fixture copying only its renamed binary; it now copies the matching sibling manifest as required by
the direct-file loader. The focused rerun passed 5/5 (the corrected case plus four digest-index cache
cases), giving 564 distinct passing Debug cases without repeating unaffected tests. Existing native
specialization/link/binding failure tests now substitute a paired manifest deliberately, so association
validation does not accidentally replace the failure path they are intended to exercise.

The Debug preparation fixture measured 0.026 ms for managed pipeline construction after shader
readiness. That executable retained four graphics-interface queries and 30 resource-reflection queries
from preparation; 100 repeated validations took 3.761 ms and added zero reflection queries. Separate
driver-cache fixtures measured complete graphics load paths at 19.766 ms bypassed, 13.293 ms cache
population, and 10.906/1.882 ms for subsequent cache hits. These single-process observations have
different preparation boundaries and are not an isolated measurement of metadata-validation cost.
Receipts: `artifacts/interface-packaging/main-debug.log`, `main-debug-recheck.log`.

The final tool suites passed 74/74 in Debug and Release, including a compiler-produced LocalSizeId
case using the pinned compiler's Vulkan 1.3 output solely as a reflection test. This does not introduce
a runtime backend or another compilation path. The final Release shader build covered 414 production
and 481 shared variants; extraction accumulated 2,493.7/2,810.6 ms respectively, with manifests of
297,187/335,596 bytes. Its managed preparation fixture measured 4.030 ms and 2.401 ms for 100 retained
validations, again retaining four graphics and 30 resource queries with zero additional reflection queries.
These small preparation measurements include process/JIT variation and are not controlled comparisons.

The broad Release run passed 563/564; its remaining wrong-stage recovery failure reproduced the
earlier baseline above. GpuShaderModule now consumes pending native error flags only after a rejected
specialization, appending them to the original diagnostic. A subsequent valid candidate is therefore
not rejected by the previous failure's error flag. Successful loading and StateCache transitions gain
no error polls. RuntimeFailurePreservesInstalledGeneration also asserts a clean error state before
attempting recovery. Focused confirmation passed 28/28 in both Debug and Release. Combined with
the still-applicable broad receipts, all 564 distinct targeted runtime cases have passing evidence
in both configurations; the tool suites pass 74/74 each. No cases were skipped or weakened.
Receipts: `artifacts/interface-packaging/failure-cleanup-debug.log`, `failure-cleanup-release.log`.

Reproduction uses `dotnet test <project> -c <Debug|Release> --no-restore` with
`NUGET_PACKAGES=C:\Users\Sisco\.nuget\packages`. The tool project is
`ShaderBuildTool.Tests/ShaderBuildTool.Tests.csproj` (unfiltered). The runtime project is
`VanillaGraphicsExpanded.Tests/VanillaGraphicsExpanded.Tests.csproj`, with detailed console logging
and this filter (Debug initially omitted ShaderDigestIndexCacheTests, then ran those four cases separately):

```text
FullyQualifiedName~PreparedGraphicsPipelineTests|FullyQualifiedName~GraphicsInterface|FullyQualifiedName~ShaderInterface|FullyQualifiedName~SpirvGraphicsLifecycleTests|FullyQualifiedName~GpuComputePipelineSpirvIntegrationTests|FullyQualifiedName~ComputeInputSubmissionTests|FullyQualifiedName~GpuProgramSamplerRetirementTests|FullyQualifiedName~ShaderInputSubmissionTests|FullyQualifiedName~UniformLifetime|FullyQualifiedName~ShaderUniformStateTests|FullyQualifiedName~DriverProgramCacheTests|FullyQualifiedName~ProgramBinaryStoreTests|FullyQualifiedName~ShaderLinkBatchTests|FullyQualifiedName~ConfigurableRasterizer|FullyQualifiedName~ShaderDigestIndexCacheTests
```

The failure-cleanup rerun selected RuntimeFailurePreservesInstalledGeneration, DriverProgramCacheTests,
ShaderLinkBatchTests and GpuComputePipelineSpirvIntegrationTests through FullyQualifiedName filters.
Normal shader orchestration stayed enabled. The package metadata/copy receipt predates this
failure-only runtime cleanup; that correction changes neither shader payloads nor packaging/copy rules.
Second source review and independent audit-stage-completion passed on 2026-10-06 with no unresolved required findings.
No live-game visual acceptance or frame-time improvement is claimed.

## Render passes and target lifetime (2026-10-06)

`Rendering/Pipeline/Passes/RenderPassDesc` freezes borrowed target references, sparse output routing,
typed color clears, independent depth/stencil intentions and an optional pixel render area.
`RenderPass.Begin` requires an existing complete `EngineBoundaryScope`; it cannot create a nested
pass or a second restoration cache. The boundary owns drawing-state restoration. The pass restores
its target's draw routing and the incoming independent read/draw framebuffer bindings, and registers
cleanup with the boundary so exceptional exits cannot leave borrowed resources locked.

Target signatures retain exact formats, effective samples and actual attached depth/stencil aspects.
A packed image attached only to depth does not imply a stencil attachment. Dimensions and resource
names remain outside pipeline identity. All images must have equal dimensions/sample counts; separate
depth and stencil images are rejected by the existing single-format target contract. Full-target area
and viewport resolve on each begin, so a retained description follows a same-format resize. Explicit
areas are checked against the refreshed dimensions. Pipeline validation checks exact compatibility
without a native query or reflection.

Managed image metadata comes from existing attachment/resource owners. Texture and renderbuffer
storage changes notify those owners, advancing framebuffer attachment revisions. Completeness is
checked on the first use and after image/routing changes. Renderbuffer allocation publishes the actual
native sample count, including driver rounding. An active pass retains non-owning references
to the framebuffer, images and managed backing resources; their resize/replacement/retirement APIs
reject until the pass ends. Pass disposal never disposes those resources. External raw mutation is
prohibited during the pass and remains the external owner's responsibility at publication boundaries.

`GpuFramebuffer.PublishRenderPassMetadata` discovers wrapped images at an explicit publication
boundary, retaining exact image selections without acquiring ownership. Failed or missing publication
cannot satisfy strict entry. `GBufferManager` resolves it during primary framebuffer refresh, including
equal-size rebuilds, before publishing one completed change notification. Default surfaces require GpuFramebuffer.SurfaceMetadata publication; they are never guessed to be ordinary RGBA8 attachments.

Clears establish writable masks, explicit scissor area and neutral clear interpretation using existing
StateCache categories. Float, signed and unsigned color values use the corresponding native typed clear;
depth and stencil clear separately to preserve unrequested packed aspects. Preceding draw masks,
rasterizer discard and scissor cannot suppress a requested clear. Numeric clear values do not mutate
global clear-color/depth/stencil helper values. Discard is permission to leave contents undefined,
implemented conservatively by retaining contents rather than invalidating unrelated aspects or pixels.
No resolve, blit, readback, barrier or shader activation is implied.

The integer-texture fixture exposed incomplete signed/unsigned mappings in `TextureFormatHelper`;
the existing allocation helper now supplies integer external formats and matching scalar types for
the sized integer families already accepted by target descriptions. Renderbuffer binding queries,
known-equal binds, exact engine deletion routing and managed immediate/deferred retirement now share
StateCache ownership as required for strict renderbuffer target operations.

| Contract and controlling source | Implementation and behavioral evidence |
| --- | --- |
| Proposal / Target signatures and render passes; design contract / Geometry, target and executable contracts | RenderPassTargets, RenderTargetSignature, framebuffer publication/revisions; sparse routing, packed aspects, wrapped refresh, resize, replacement, format/sample and retirement tests |
| Proposal / Categorized cache storage, Native error-checking policy; design contract / Restoration decisions by boundary | RenderPassOperations and existing EngineBoundaryScope/StateCache owners; hostile masks/scissor, typed clears, independent aspect and area preservation, routing/binding restoration |
| Proposal / Prepared pipelines, identity, and lifetime; plan / Render passes and target lifetime | Borrow guards, GraphicsPipeline.ValidateTargets and exception cleanup; same pipeline across IDs/dimensions, active mutation rejection, native incomplete setup and omitted-dispose cleanup |

Second source review and the independent audit-stage-completion pass concluded with no unresolved
required finding on 2026-10-06. Delegated tests passed 180/180 cases in both Debug and Release;
after the final routing-query guard, the affected 23/23 cases passed again in both configurations.
Receipts: `artifacts/render-pass-broad-debug-metadata.log`, `render-pass-broad-release-metadata.log`,
`render-pass-query-debug.log` and `render-pass-query-release.log`.

An initial integer clear fixture exposed the format-helper mapping defect described above. An initial
broader run also exposed resource-manager shutdown ordering: owning-attachment fixtures cannot allocate
after that suite closes their disposal context. The new fixtures now borrow independently owned storage.
The existing `SceneColorParticleTests.CapturePreservesCallerFramebufferAndIndexedState` retains that
order sensitivity and is excluded from the broad selection; it passed 1/1 independently in each
configuration (`artifacts/render-pass-isolated-debug.log`, `render-pass-isolated-release.log`). Those
receipts remain applicable: subsequent changes affect renderbuffer allocation and the new pass's query
guard, while that fixture uses texture-backed legacy capture. This is not a claim of a single green
181-case process. The publication regression additionally caught duplicate primary-FBO notifications;
atomic metadata publication restored the existing single-notification contract before the final runs.

Commands used `NUGET_PACKAGES=C:\Users\Sisco\.nuget\packages` and normal shader orchestration:

```powershell
dotnet test VanillaGraphicsExpanded.Tests/VanillaGraphicsExpanded.Tests.csproj -c Debug --no-restore --filter '(FullyQualifiedName~RenderPass|FullyQualifiedName~RenderbufferBindingLifetime|FullyQualifiedName~Framebuffer|FullyQualifiedName~EngineBoundary|FullyQualifiedName~EngineStateSwitching|FullyQualifiedName~PipelineDescription|FullyQualifiedName~ConfigurableRasterizerDescription|FullyQualifiedName~PipelineStateCoverage|FullyQualifiedName~PreparedGraphicsPipeline|FullyQualifiedName~CompleteGraphics|FullyQualifiedName~TextureFormatHelper|FullyQualifiedName~LumOnBufferOwnership)&FullyQualifiedName!~SceneColorParticleTests.CapturePreservesCallerFramebufferAndIndexedState' --logger 'console;verbosity=minimal'
dotnet test VanillaGraphicsExpanded.Tests/VanillaGraphicsExpanded.Tests.csproj -c Debug --no-restore --filter 'FullyQualifiedName~RenderPass|FullyQualifiedName~RenderbufferBindingLifetime' --logger 'console;verbosity=minimal'
```

Both commands were repeated with `-c Release`. `git diff --check` passed. No game launch, live visual
acceptance, packaging result or CPU/GPU performance improvement is claimed by this work.

## Immediate graphics submission

`GraphicsCommandContext.TryRun` declares prepared pipelines before entering the existing
`CompleteGraphicsBoundary`. Their complete state coverage and prepared resource footprints are
resolved once at entry. The callback can execute sequential nonnested `BeginPass` / `EndPass`
operations. Select a declared pipeline, assign its existing generated shader inputs, supply
`GraphicsDynamicState` (including an explicit viewport), and call `Draw`. `PassViewport` provides
the current target area's viewport without introducing a second dynamic representation.

Before native submission the context validates target compatibility and lifetime, executable revision,
geometry layout/topology/ranges, dynamics and current shader resources. State is applied through
`StateCache.ApplyGraphicsState`. Activation uses `GpuProgram.UseScope`, retained until shader change
or pass end. Every subsequent draw calls the existing `Use` publication path even for the same
pipeline, so edited resources and typed UBO versions cannot be skipped. Existing input mutation,
recursion and UBO epoch rules remain in force; the documented engine GLSL exceptions are unchanged.
The context rejects recursive draws or context edits during submission and rejects use after its
callback ends. No new shader-input binding or reflection system is involved.

Shader activation scopes are consumed once before framebuffer cleanup. Forgotten pass ends and
exceptions use the existing ordered `EngineBoundaryScope` cleanup: shader ownership, borrowed
bindings, framebuffer scopes, then category snapshots. The boundary additionally preserves the
incoming VAO's element-buffer association, including when it is the engine mesh drawn inside the
boundary. Resources remain externally owned unless explicitly created by a geometry adapter.

`ManagedGraphicsGeometry` owns immutable VAO/VBO/EBO uploads and configures them through
`GpuVertexAttribBinding`. It validates requested index subranges against retained CPU index values,
base vertex and per-binding vertex/instance capacity. Its initial index storage is unsigned 32-bit;
unused attributes are permitted in the pipeline layout, while mismatched layouts reject. Setup uses
existing binding scopes, and native draw submission uses cached VAO/EBO selection. Private owned
storage prevents silent metadata drift; changing geometry requires a replacement upload.

`EngineFullscreenGeometry.Upload` owns the existing engine upload/delete lifecycle for position/UV
indexed triangles. It rejects unknown mesh representations and checks the installed native attribute
layout, offsets, buffers and byte extents at publication. Ordinary `RenderMesh` binds the mesh VAO
and unsigned-int element buffer, draws, then unbinds that EBO and VAO. The adapter reports those exact
effects to StateCache; failure forgets the uncertain selection and only that mesh's EBO association.
It never adopts grouped, pooled or instanced engine helper semantics. Draw-time validation checks
retirement and retained engine metadata without querying the driver.

Installed-engine IL inspection confirms location 0 float3 positions and location 1 float2 UV when
normals are absent, with legacy `VertexAttribPointer` stride zero and offset zero. Publication
normalizes that tightly packed representation to the 12-byte/8-byte structural layout; additional
normal/color/flag/custom streams reject. The native helper fixture uses that exact representation
and calls the installed ordinary `ClientPlatformWindows.RenderMesh`, rather than simulating its
draw commands. Engine upload itself remains mocked in the headless fixture; its layout and buffer
allocation contract are backed by the inspected installed implementation.
The installed `QuadMeshUtil.GetCustomQuadModelData(-1,-1,0,2,2)` also matches the first consumer's
authored contract after clearing `Rgba`: four vertices, six indices, twelve position floats, eight
UV floats, no normal/flag/custom streams, and no instanced streams.

Raw GL calls and arbitrary engine rendering are prohibited inside the callback. Deliberate external
operations run after `TryRun` exits through `GraphicsCommandContext.ExecuteExternal`, which uses
the existing StateCache owner to invalidate the declared categories even if the operation throws.
A subsequent `TryRun` resolves truthful entry state and reestablishes complete draw intent. Ending
only a pass does not end the enclosing engine boundary and does not authorize external operations.

| Contract and controlling source | Implementation and verification |
| --- | --- |
| Proposal / Submission contract; design contract / Geometry, target and executable contracts | GraphicsCommandContext, existing RenderPass/GraphicsPipeline validation, owned geometry adapters; deterministic HZB pixels and rejected draw contracts |
| Proposal / Prepared pipelines, identity, and lifetime; numeric input migration inventory | Existing GpuProgram activation, generated typed UBO publication and prepared resource validation; changed texture/mip on one pipeline and prior-owner restoration |
| Proposal / Native error-checking policy; design contract / Restoration decisions by boundary | Existing category transitions and EngineBoundaryScope, VAO-owned EBO restoration and observed engine helper effects; warm counters, external operations and exception cleanup |

Delegated validation passed 165 distinct selected cases in each configuration: Debug 115/115 broad
plus 65/65 focused, and Release 159/159 broad plus 10/10 focused, with overlapping cases counted once.
After narrowing upload invalidation to its actual VAO/array/element footprint, the four affected
engine-geometry cases passed again in both configurations. Receipts are
`artifacts/submission-debug-broad-final.log`, `submission-debug-additions.log`,
`submission-release-broad.log`, `submission-release-additions.log`,
`submission-debug-upload-final.log` and `submission-release-upload-final.log`.
The installed upload/draw inspection is retained in `artifacts/submission-engine-il.txt`.

Commands used `NUGET_PACKAGES=C:\Users\Sisco\.nuget\packages` and normal production/shared-test
shader orchestration (414 production and 481 combined variants when rebuilt). All used
`dotnet test VanillaGraphicsExpanded.Tests/VanillaGraphicsExpanded.Tests.csproj -c Debug --no-restore
--filter '<selection>' --logger 'console;verbosity=normal'`, repeated with `-c Release` as described:

- The broad selection ORs `FullyQualifiedName~` matches for GraphicsCommandContextTests,
  EngineGraphicsGeometryTests, RenderPass, EngineBoundary, EngineStateSwitching,
  PreparedGraphicsPipeline, CompleteGraphics, ShaderInputSubmission and ShaderOwnership.
  Release additionally includes GpuVaoIntegrationTests.
- The Debug focused selection covers GraphicsCommandContextTests, EngineGraphicsGeometryTests,
  GpuVaoIntegrationTests and GpuProgramUseScopeTests. Release focused covers
  EngineGraphicsGeometryTests and GpuProgramUseScopeTests.
- Final upload checks select only `FullyQualifiedName~EngineGraphicsGeometryTests`.

Initial failed engine fixtures were corrected to bind a VAO before EBO upload and to represent the
installed legacy stride-zero attribute setup. Warm managed draws add neither fixed-function calls
nor boundary state queries; executable/resource reflection counters stay unchanged. Changed inputs
produce the expected HZB pixel value, and rejected contracts emit no draw. These are behavioral and
counter checks, not frame-time measurements. No production consumer migration, game launch, live
visual acceptance or CPU/GPU performance improvement is claimed by this infrastructure.

Second source review and the independent audit-stage-completion pass reconciled the submission,
geometry, input-publication and restoration requirements with these receipts. The final affected-path
rechecks and `git diff --check` passed; no required submission-contract findings remain open.

## Direct-lighting production submission

`DirectLightingRenderer` now retains a prepared `GraphicsPipeline` for the current shader owner and
successful executable revision. Its complete description selects the existing position/UV triangle
layout, three linear RGBA16F outputs and a dynamic viewport. The pass declares all three zero clears
and derives its viewport from the actual target. Normal manager-owned outputs and isolated capture
outputs use the same submission. Same-format resize preserves pipeline identity; successful shader
reload replaces the realization. Renderer disposal retires its realization, lifetime and owned engine
geometry without taking ownership of shader programs or borrowed output images.

The renderer assigns the same generated shader inputs through `DirectLightingRenderer.Inputs.cs`.
`GraphicsCommandContext.Draw` retains engine-aware activation and current resource/UBO publication
on every draw, including repeated pipeline selections. It replaces the former renderer-side partial
descriptor, framebuffer bind, clear and mesh submission. No additional live-state cache, restore PSO,
packed-value manipulation or context-generation tracking is introduced.

Pre-overlay capture uses `TryRunShared`: complete direct-lighting state and the existing composite
program resource footprints share one engine interruption. The direct-lighting pass ends before the
existing bounded composite callback runs. Shader cleanup precedes borrowed binding/framebuffer and
categorized drawing-state restoration. Standalone composite and its partial descriptor remain with
their current owners until their own consumer migration. Conditional rendering inactivity is the
ordinary opaque/pre-overlay callback invariant; transform-feedback inactivity is checked at entry.

| Work and controlling source | Implementation and evidence |
| --- | --- |
| Production adoption; proposal / Submission contract; design / Deterministic reference and evidence boundaries | DirectLightingRenderer and Inputs, DirectLightingSubmissionTests and the frozen DirectLightingReferenceRenderer compare all three radiance outputs using identical built shaders and inputs. |
| Lifetime and readiness; proposal / Prepared pipelines, identity, and lifetime; design / Geometry, target and executable contracts | Renderer-owned realization and EngineFullscreenGeometry, existing target owners and revision checks; normal/isolated output, resize, repeated inputs, failed preparation and reload cases. |
| State authority and capture; proposal / Engine integration and cache authority; design / Restoration decisions by boundary | GraphicsCommandContext.TryRunShared, existing CompleteGraphicsBoundary and EngineBoundaryScope; hostile-state/exception checks plus capture, particle-publication and shader-ownership regressions. |

The reference fixture preserves the previous submission path and explicitly establishes a complete
neutral baseline before running its partial descriptor. The candidate is also exercised after hostile
native state with explicit cache invalidation. Engine mesh uploads use native VAO/VBO/EBO fixtures;
the comparison invokes the installed engine's ordinary RenderMesh helper. Surface-lighting fixture
draws preserve caller-owned state and install the existing production shader-stop observation hook,
rather than allowing the mock draw to impose its own pipeline or hide an unobserved shader stop.

Comparison inputs are frozen at 32x24 and 31x19, with a later 17x13 target resize. They cover exact-zero
background, planar diffuse, metallic and roughness extremes, emission, independently effective near
and far shadows with a point light, explicit first-person receiver position, and changed inputs.
Shadow cases additionally prove that disabling their intensity changes radiance. All three RGBA16F
attachments, including alpha, are checked for finite values and against the documented tolerance.
Failed binary preparation emits no draw; successful reload refreshes the realization. Hostile-state
and draw-exception checks verify native enables/masks, viewport, independent framebuffer bindings
and engine shader ownership. Existing capture fixtures verify pre-overlay source separation.

Validation uses NVIDIA GeForce RTX 4090, OpenGL 4.3.0 NVIDIA 591.86. The built direct-lighting program
has one nonstructural variant with no specialization overrides; the generated DropShadowIntensity
input selects shadow participation. Debug observations for 32x24 / 31x19 respectively were
30.824 / 0.047 ms to obtain the prepared realization and 0.892 / 0.381 ms for one repeated whole
standalone submission. Release observations for those same sizes were 0.080 / 23.526 ms for
realization and 0.607 / 0.429 ms for repeated submission; Release executed the odd-size case first.
These are individual fixture/JIT-inclusive CPU observations, not averaged
benchmarks or a before/after speedup claim. The shader had already been prepared by the constructor;
the realization timing is not shader-compilation cost.

Each measured repeat recorded four fixed-function transitions, one counted boundary state query,
and zero additional executable-interface reflection queries. The standalone operation includes a
clear, pass entry and engine restoration, so it is not the same workload as two draws within one
already active pass. Source-accounted native checks outside BoundaryQueries are eight draw-buffer
routing reads per pass and eight GL.IsTexture validations for active borrowed integer samplers per
submission. Native error polls are separate and excluded from those state-read counts. This migration
does not claim a query-free production callback; stable managed-draw suppression remains covered by
GraphicsCommandContextTests. GPU timing, in-game appearance and broader engine/other-mod coverage
remain outside these headless receipts. No game was launched.

Delegated commands used `NUGET_PACKAGES=C:\Users\Sisco\.nuget\packages` and
`dotnet test VanillaGraphicsExpanded.Tests/VanillaGraphicsExpanded.Tests.csproj -c Debug --no-restore
--filter '<selection>' --logger 'console;verbosity=detailed'`, repeated for Release. Production and
shared fixture shaders use normal project orchestration, including release debug stripping.
The broad selection ORs `FullyQualifiedName~` matches for DirectLightingSubmissionTests,
PbrDirectLighting, WaterRefractionCaptureStateTests, SceneColorParticle, SurfaceLightingPbr,
DirectLightingBufferOwnershipTests, GraphicsCommandContextTests and EngineGraphicsGeometryTests.
Debug separately selects SurfaceLightingDisplayBoundaryTests with `--no-build`; Release includes
that family in its broad selection. Receipts are `phase7-regression-debug.log`,
`phase7-display-debug.log` and `phase7-regression-release.log` in the repository root.

Final validation passed 100 distinct selected cases per configuration: Debug 98/98 broad plus 2/2
display-boundary cases, and Release 100/100, with no failures or skips. Every compared component in
all three radiance attachments had maximum error zero. After strengthening changed-input verification
to run the candidate before the reference on their shared shader owner, and asserting unchanged
pipeline identity, the two comparison cases passed again in both configurations. Those final builds
and rechecks are recorded in `phase7-final-debug.log` and `phase7-final-release.log`; they also include
the final fixture cleanup. Preparation failures, reload and exception recovery remain covered there.

Earlier regression failures exposed fixture behavior: the mock draw imposed an extra partial pipeline,
then hid missing shader-stop observation by unbinding the program itself. The fixture now uses
geometry-only submission and the production stop hook. The initial shadow setup also did not produce
occlusion; fixed comparison coordinates and independent cascade controls now make its effect explicit.
Only the corrected passing receipts support completion.

The second source review and independent audit-stage-completion pass reconciled the complete selected
contract, proposal, design/reference methodology, current sources and final receipts. No required
adoption, lifetime, restoration or changed-input findings remain open. `git diff --check` passed.
The remaining consumers and final system-wide measurements retain their separate planning gates.

## Remaining consumer submission

The adoption contract is the proposal's **Submission contract**, **Engine integration and cache
authority**, and **Adoption strategy**, together with this document's consumer inventory,
complete-state defaults, target/pass lifetime and geometry contracts. These requirements apply to
all migrated families; the legacy inventory above tracks every retired capture entry individually.

| Requirement group | Implementation and retained responsibility | Verification |
| --- | --- | --- |
| Composite/capture and success-only publication | PBRCompositeRenderer.Pipelines, WaterRefractionCapture and SceneColorParticleCapture.Pipelines declare preparation before one complete interruption; reduction and SSAO use explicit passes. Engine particle rendering remains in SceneColorParticleDrawScope. | WaterRefractionCaptureStateTests; SceneColorParticlePublicationTests; SceneColorParticleIntegrationTests, including engine Stop handoff and foreign-owner rejection. |
| Atlas allocation, independent clear and bounded solver draws | MaterialAtlasNormalDepthGpuBuilder.Submission, Passes, Statistics, TileResources and Multigrid retain the existing solve and allocation ownership. R32F/RG32F shader outputs now declare their stored scalar/vector width. Scratch publication uses binding-preserving attachment APIs before entry. | MaterialAtlasBakeSubmissionTests exercises all three actual entries, nonflat solved heights, untouched exterior pixels, failure and restoration. |
| CPU world-probe two-pass resolve | LumOnWorldProbeClipmapGpuUploader.Pipelines declares actual radiance/metadata formats and immutable point layouts; ArrayGraphicsGeometry borrows streaming buffers. | WorldProbeCpuSubmissionTests; ArrayGraphicsGeometryTests; existing transport integration. |
| LumOn fullscreen and clear-only history | LumOnRenderer.Submission retains one realization per pass name/executable/target signature. HZB uses actual mip areas. ClearHistory declares every target as a clear-only pass. Compute and explicit barriers retain their existing owners. | LumOnTemporalRendererTests; LumOnHistorySubmissionTests; compute/shared-binding suites. |
| Debug windows, images and primitives | DebugRenderTarget retains actual window formats and borrowed image metadata per target until rebuild/resize, so alternating OIT/window draws reuse their publications. DebugGraphicsSubmission owns bounded per-mode pipeline realizations. Array/fullscreen adapters own geometry. Orbs explicitly declare six output blend policies and point-size/depth behavior. | LumOnDebugRendererFunctionalTests; DebugGraphicsSubmissionTests; SurfaceRenderPassTests. |
| Liquid depth/surface/volume | LiquidGraphicsSubmission prepares ordinary pool metadata before entry and routes the existing manager's per-pool hook through GraphicsCommandContext.Draw. Engine culling, mini-dimension origin/model-view staging and atlas selection remain in their original order. EngineLiquidPoolGeometry validates native stream layout/capacity once and current allocation groups on each submission. | Liquid adapter tests and existing liquid shader/transport regressions; installed engine pool source/IL establishes the ordinary indexed path and UseSsbo behavior. |
| Single state/blend owner and compatibility cleanup | Production legacy capture and temporary FullscreenBoundary are removed; reference implementations live only in test fixtures. GpuFramebuffer blend storage is removed. Complete pipelines own VGE draws. G-buffer, particle and terrain/tessellation hooks remain explicit engine-owned compatibility paths. | Indexed-blend pipeline tests, historical negative control, direct-lighting comparison and engine-boundary regressions. |

Clear-only command contexts have no executable but still resolve complete drawing-state coverage;
they cannot draw without a declared pipeline. Window surfaces are distinct from image attachments:
metadata includes actual color encoding/channel widths, samples and depth/stencil aspects. Unknown
window formats reject. The original window draw selector is restored with DrawBuffer because
selectors such as Back are not accepted by DrawBuffers. The headless window fixture draws to its
actual 1x1 surface and verifies metadata resize/revision handling; it does not resize an operating-system
window. Real window resize remains part of user-run live acceptance.

The pool adapter supports the installed 64-bit ordinary indexed liquid representation. It does not
interpret the engine's shared SSBO index stream. UseSsbo is temporarily false only while engine
managers stage and submit the declared ordinary pools, and is restored on failure. Borrowed VAO
and element associations are restored through StateCache scopes. Engine pool allocation/culling
provenance supplies CPU-side range validation; there is no per-draw GPU index readback. Preparation
and reusable geometry remain separate from the command context's thin submission composition.
Installed engine source and IL identify the final OIT storage as RGB8, R16F, RGBA8 and three
RGBA16F accumulation targets. The shared OIT shader declares three bin-transmission components
and one total-transmission component to match those first two attachments. Glow retains separate
source-alpha blending, while revealage multiplies and accumulation adds. LiquidProductionPipelineTests
prepares the actual production surface executable against those formats, the depth executable against
Depth24-only storage and the volume executable against two RGBA32F attachments;
LiquidBlendPolicyTests independently checks the stored arithmetic. Liquid depth is Depth24-only,
uses Less comparison and explicitly discards its dummy fragment color output.

The shader-enabled Debug and Release builds and combined consumer/shared-state suites passed
310/310 tests in each configuration with zero failures or skips. Receipts are
`artifacts/phase8-final-current-debug.log` and `artifacts/phase8-final-current-release.log`;
the exact selection is retained in `artifacts/phase8-final-filter.txt` and each log's command header.
The commands use the test project's normal production and shared fixture shader orchestration.
After the final debug-target metadata retention correction, the affected selection passed 7/7 in both
configurations with no failures or skips (`artifacts/phase8-target-retention-debug.log` and
`artifacts/phase8-target-retention-release.log`). These focused reruns cover the final source; the
broader receipts remain applicable to unchanged consumers and shared submission mechanisms.
Headless tests do not establish in-game appearance or performance. User-run visual acceptance and
comparable workload measurements remain outstanding under the acceptance plan.

The broader water regression run exposed four stale lowest-quality receiver expectations. An isolated
build of unchanged commit `c6329702049c8881c6bf2b564826a02da8de8db4` reproduced the same four
failures (89/93 passed, zero skipped; `artifacts/phase8-water-baseline-debug.log`). The source archive
was supplemented only with ignored debug GLSL includes required by its existing shader build.
The corrected test independently projects the fixture's mesh/geometric-normal difference onto the
receiver plane for pixel-normal offset; traced qualities retain the Snell intersection oracle.
Fresnel, scattering, display-transfer checks and tolerances are unchanged. No refraction-model code
was changed for that test correction.

Second source review and the independent completion audit against the proposal, consumer inventory,
legacy-entry dispositions and adoption requirements found no remaining required issue on 2026-10-06.
All eleven original production legacy-capture invocations are absent, and the temporary framebuffer
blend owner and fullscreen coordination adapter are retired. Engine-owned compatibility paths retain
their explicit restoration contracts. Project binding and capture entries are reconciled with this
coverage; unobserved external mutations and live acceptance are not claimed complete.

## System acceptance

The governing sources are the approved proposal (including its configurable raster-state, numeric
input, custom-clipping removal and context-lifetime amendments), the implementation plan's execution
contract and acceptance requirements, and the design/consumer contracts above. The baseline for this
acceptance work is commit `1d998a97`, containing the completed consumer migration. Existing receipts
are reused only for unchanged behavior; new measurements and affected verification are recorded below.

### Proposal-to-implementation coverage

Each row maps the named proposal sections to their current owning source and behavioral evidence.
Source paths below Rendering are under `VanillaGraphicsExpanded/Rendering/`; test names locate the
existing unit or GPU suites. Detailed commands and limits remain with the linked historical receipts
and the consolidated verification below.

| Proposal sections / required contract | Current implementation and evidence |
| --- | --- |
| Intent; Current implementation and gaps; Pipeline model | Pipeline/Descriptions/GraphicsPipelineDesc, Pipeline/GraphicsPipeline, Pipeline/GraphicsCommandContext and StateCache separate draw policy from cached transitions. DirectLightingSubmissionTests compares all three outputs to the retained reference; the remaining-consumer table accounts for every VGE draw family. The historical gaps are not current implementation claims. |
| Naming and namespaces; Architectural responsibilities | Responsibility-named types live in Pipeline/Descriptions, Pipeline/State and Pipeline/Passes; geometry, interface validation and lifetime each have focused owners. Existing GpuProgram, GpuFramebuffer and buffer hierarchies remain resource owners. GraphicsCommandContext composes them without owning allocation algorithms or another binding system. |
| Scope and exclusions | Engine-owned particle, G-buffer, terrain/tessellation and shared-shadow paths retain explicit partial adapters. Compute uses its existing pipeline and resource owners. One live rendering context is assumed; no context generations, render graph, alternate backend, interning or asynchronous preparation are introduced. Consumer and mutation coverage records unobserved external paths. |
| Categorized cache storage | Pipeline/State category values and independent category knowledge masks are copied by PipelineStateSnapshot. StateBooleanValueTests, StateValueLayoutTests, CategorizedStateCacheTests, GlStateCacheInvalidationTests and PipelineStateCoverageTests cover packed-value independence, known false versus unknown, detached snapshots and selective/partial authority. Managed sizes are reported separately below. |
| Complete descriptions and partial overrides | GraphicsPipelineDesc resolves defaults and validates in all configurations; GlPipelineDesc remains explicitly partial compatibility intent. PipelineCanonicalization preserves structural identity; StateCache.ApplyGraphicsState establishes supported complete values. GraphicsPipelineDescriptionTests, CompleteGraphicsStateTests and GraphicsCommandContextTests verify independence from ambient state and rejection of undeclared changes. |
| Static and dynamic state coverage | Description category records, GraphicsDynamicState, StateCache category transitions and complete restoration own supported raster/depth/stencil/blend/sampling/output/assembly state. ConfigurableRasterizerDescriptionTests and ConfigurableRasterizerGpuTests cover the approved compatibility-state additions. Custom shader clipping is unsupported; user clip enables are explicitly disabled for VGE draws and restored for engine handoff. |
| Target signatures and render passes | RenderTargetSignature, RenderPassDesc, RenderPassTargets, RenderPassOperations and GpuFramebuffer attachment/surface publication retain exact formats, sparse/discard routes, typed clears, render areas and borrowed lifetime. RenderPassClearTests/RenderPassLifetimeTests, SurfaceRenderPassTests and native liquid-target tests cover incompatible targets, clear independence, retirement, replacement and resize. |
| Prepared pipelines, identity, and lifetime | ShaderPipelineIdentity, GraphicsPipelineLifetime, GraphicsPipeline and GraphicsInterfaceValidation use immutable descriptions plus owner/executable revision. Existing prepared bindings and generated std140 inputs remain authoritative. GraphicsPipelineDescriptionTests, ShaderPipelineIdentityTests and PreparedGraphicsPipelineTests cover equality, mutation resistance, reload/disposal and validation without repeated reflection. |
| Compiler packaging and numeric-input amendments within preparation | ShaderBuildTool/Spirv/ShaderInterfaceReflection and ShaderVariantBuild package immutable declarations with exact binary association; PreparedProgramBinary and GpuProgram.Spirv validate candidates before publication. PackagedInterfaceValidationTests, ShaderInterfaceCompatibilityTests and existing build/driver-cache receipts cover rejection and atomic generations. The Standalone numeric input migration inventory and ShaderAuthoring.md define the approved engine GLSL exceptions; no runtime instruction parser or release debug-name dependency remains. |
| Submission contract | GraphicsCommandContext and geometry adapters validate pass, targets, executable, ranges, dynamics and resources before drawing. GpuProgram activation publishes changed inputs on every draw. GraphicsCommandContextTests, EngineGraphicsGeometryTests, ArrayGraphicsGeometryTests and LiquidGraphicsSubmissionTests cover input changes, failure-before-draw, recursive/external mutation and UseSsbo restoration. |
| Native error-checking policy | GlDebug.CheckStateTransitions is opt-in; category transitions and pixel-store scopes suppress unchanged work. Boundary cleanup retains checked, independent restoration and targeted invalidation. StateTransitionDiagnosticsTests, PixelPackStateTests, PixelUnpackStateTests, ConfigurableRasterizerGpuTests and boundary tests distinguish ordinary setters, cold queries, transfer safety and checked restoration. Counters are bounded instrumentation, not a complete driver trace. |
| Engine integration and cache authority | CompleteGraphicsBoundary, EngineBoundaryScope, EngineStateCalls and exact-signature observation compose prepared footprints with prior shader-owner restoration. EngineBoundaryEntryTests/EngineBoundaryBindingTests, EngineStateSwitchingTests and capture/publication tests prove covered handoffs. Unknown mutations still require explicit invalidation; no whole-engine or arbitrary-mod authority is claimed. |
| Interpretation of existing project TODOs | project.todo links concrete targets, prepared shader dependencies and scope retirement to the current coverage matrix. Resource binding remains separate from PSO identity. Required engine compatibility scopes remain; all eleven inventoried legacy fixed-function capture calls and framebuffer-owned blend policy are retired. |
| Adoption strategy | Direct lighting was followed by composition/capture, liquids, atlas baking, CPU probe resolve, LumOn fullscreen and debug draws. The Remaining consumer submission and Legacy scope inventory tables map each entry to output, lifetime, routing and restoration tests. Historical reference implementations exist only in test fixtures. |
| Validation and acceptance | Valid prior consumer receipts are consolidated with current state-contract tests and comparable-workload measurements below. Correctness, CPU timing, GPU timing, managed layout and live evidence are reported separately. There is no required speedup threshold. |
| Decisions to resolve during implementation design | Graphics submission design contract records supported defaults, geometry adapters, exact target/discard policy, window metadata, executable revision, restoration coverage and dynamic declarations. Current consumer evidence exercises these decisions; historical staged helpers do not supersede the final contracts. |

### Consolidated automated verification

Delegated verification on 2026-10-06 used the installed package cache, normal shader-enabled test
builds and the hidden native context described below. No production source changed during acceptance;
the only executable changes are the opt-in measurement fixture and an optional neutral-baseline
parameter whose default preserves the comparison fixture's existing behavior.

| Evidence set | Debug | Release | Receipt / applicability |
| --- | ---: | ---: | --- |
| Consumer/shared-state regression selection | 310 passed | 310 passed | `artifacts/phase8-final-current-{debug,release}.log`; unchanged production behavior, exact filter in `phase8-final-filter.txt` |
| Final debug-target metadata correction | 7 passed | 7 passed | `artifacts/phase8-target-retention-{debug,release}.log`; final target-cache source |
| Fresh state/identity/boundary/pass/submission selection | 205 passed | 207 passed | `artifacts/phase9-acceptance-{debug,release}.log`; Release additionally includes the two direct-lighting submission cases |
| Fresh shared comparison-fixture follow-up | 2 passed | Included above | `artifacts/phase9-direct-lighting-debug.log`; final optional-baseline source |
| Explicit matched workload and realization retention | 1 passed | 1 passed | `artifacts/lighting-measurement-{Debug,Release}.log` and JSON/TRX below |

Each listed successful run had zero failures and zero skips. Counts are separate receipts with
overlapping coverage, not one combined or deduplicated suite. The original compiler/bootstrap attempt
failed before tests because the sandbox's default package directory lacked the pinned shader tool.
The same Debug command passed after selecting NUGET_PACKAGES=C:/Users/Sisco/.nuget/packages;
the failure and retry remain in its log. Existing compiler/analyzer warnings remain (including nullable,
obsolete API, platform and xUnit warnings); successful builds have no errors. No full-repository test
suite or new distributable package run is claimed.

The fresh regression selection retains packed values, actual layouts, known-false/unknown distinctions,
detached snapshots, partial coverage, selective invalidation, native error policy, resource retirement,
engine switches, prepared identity/bindings and target/pass lifetime. The exact initial and expanded
filters are `artifacts/phase9-acceptance-filter.txt` and `artifacts/phase9-acceptance-release-filter.txt`.
TRX files are in `artifacts/TestResults/` with corresponding log basenames. Commands from the repository
root were:

```powershell
$env:NUGET_PACKAGES = 'C:/Users/Sisco/.nuget/packages'
$selection = Get-Content artifacts/phase9-acceptance-filter.txt -Raw
dotnet test VanillaGraphicsExpanded.Tests/VanillaGraphicsExpanded.Tests.csproj -c Debug --no-restore --filter $selection.Trim() --logger 'trx;LogFileName=phase9-acceptance-debug.trx' --results-directory artifacts/TestResults
# After the final shader-enabled measurement builds:
dotnet test VanillaGraphicsExpanded.Tests/VanillaGraphicsExpanded.Tests.csproj -c Debug --no-build --no-restore --filter 'FullyQualifiedName~DirectLightingSubmissionTests' --logger 'trx;LogFileName=phase9-direct-lighting-debug.trx' --results-directory artifacts/TestResults
$selection = Get-Content artifacts/phase9-acceptance-release-filter.txt -Raw
dotnet test VanillaGraphicsExpanded.Tests/VanillaGraphicsExpanded.Tests.csproj -c Release --no-build --no-restore --filter $selection.Trim() --logger 'trx;LogFileName=phase9-acceptance-release.trx' --results-directory artifacts/TestResults
```

Release and the Debug follow-up reuse the final measurement builds; they do not bypass shader
orchestration for stale binaries. Earlier compiler-packaging and generated-input receipts remain
applicable to those unchanged owners, with their original counts and limits recorded in the sections
above. The opt-in measurement test's ordinary-run skip policy is explicit below; it was enabled for
both reported measurement runs. No required selected regression remains failing or skipped.

### Matched renderer measurement method

DirectLightingMeasurementTests is an opt-in reproducible experiment using the retained partial
reference and current complete direct-lighting renderer in the same headless context. Both use the
same production shaders, frozen shadow/point-light inputs, three RGBA16F outputs and equivalent
clears. All finite output components are compared before and after measurement against the existing
0.001 + 0.002 * abs(reference) ceiling. The shared fixture's optional neutral setup defaults to its
original correctness-test behavior; measurement establishes neutral state outside each CPU batch.

For each of 32x24 and 640x360, warm both paths with 16 draws, then run six ABBA blocks, each containing
reference/current/current/reference batches of 16 invocations. This yields 12 batches per path and
size. CPU batches exclude input setup, neutral baseline and explicit completion waits; separate GPU
elapsed-query batches include the complete submitted work. These are standalone renderer invocations,
each entering a boundary and pass, rather than repeated draws within an already active pass. Reported
per-invocation values divide each batch by 16; medians and min/max describe those batch averages.

The environment is Windows 10.0.26200 x64, .NET 10.0.12, Intel Core i9-12900K (24 logical processors),
NVIDIA RTX 4090, driver 591.86 and a hidden OpenGL 4.3 context. Transition diagnostics are disabled;
native debug output and the production GPU profiler are enabled in both runs. Synchronous debug
output is enabled in Debug and disabled in Release.
CPU timings include assertions, engine-service mocks, boundary handling and driver backpressure.
GPU intervals can include CPU submission gaps, clears and restoration. Debug/Release are separate
processes with uncontrolled JIT, driver caches, scheduling and GPU clocks, not paired compiler benchmarks.

The fixture is skipped in ordinary unfiltered runs unless VGE_RUN_LIGHTING_MEASUREMENTS=1. Explicit
measurement commands enable it and retain JSON, console and TRX receipts:

```powershell
$env:NUGET_PACKAGES = 'C:/Users/Sisco/.nuget/packages'
$env:VGE_RUN_LIGHTING_MEASUREMENTS = '1'
$env:VGE_LIGHTING_REPORT = "$PWD/artifacts/lighting-measurement-Debug.json"
dotnet test VanillaGraphicsExpanded.Tests/VanillaGraphicsExpanded.Tests.csproj -c Debug --no-restore --filter 'FullyQualifiedName~DirectLightingMeasurementTests' --logger 'trx;LogFileName=lighting-measurement-Debug.trx' --results-directory artifacts/acceptance-measurements -v minimal *> artifacts/lighting-measurement-Debug.log
```

Repeat with Release in the configuration and output names. Normal production/shared-fixture shader
orchestration remains enabled. JSON and console logs are `artifacts/lighting-measurement-{Debug,Release}.{json,log}`;
TRX files are in `artifacts/acceptance-measurements/`. These measurements do not establish production
frame cost, retained heap bytes or a GPU throughput improvement.

### Measured results

Both explicit measurement runs passed 1/1 with zero failures or skips. CPU and GPU columns below are
microseconds per renderer invocation: median [minimum, maximum] of the 12 batch averages. GPU intervals
are separately timed batches, not the GPU portion of the adjacent CPU samples.

| Build / target | Reference CPU | Complete CPU | Reference GPU interval | Complete GPU interval |
| --- | ---: | ---: | ---: | ---: |
| Debug / 32x24 | 215.2 [190.3, 322.3] | 343.7 [305.7, 375.2] | 10.6 [6.7, 538.4] | 18.2 [6.9, 128.3] |
| Debug / 640x360 | 202.7 [162.8, 306.0] | 327.6 [288.6, 430.0] | 48.6 [12.0, 603.6] | 45.9 [12.0, 364.9] |
| Release / 32x24 | 187.8 [166.5, 318.2] | 334.8 [287.4, 962.7] | 57.0 [7.1, 559.6] | 57.3 [7.2, 667.4] |
| Release / 640x360 | 165.8 [151.7, 248.6] | 309.0 [266.9, 464.2] | 53.1 [12.0, 256.8] | 32.5 [12.0, 250.9] |

The complete path costs more CPU time in this fixture; these results establish no submission speedup.
It establishes/restores a broader contract than the partial reference. The noisy GPU intervals do not
support a GPU performance conclusion. Per-thread allocation volume per invocation was 47,888 reference
versus 88,368 complete bytes in Debug, and 47,144 versus 88,360 in Release, at both sizes. These volumes
include mock invocation/argument objects and are not production allocation or retained-cache sizes.

Every 16-invocation batch recorded 0 reference versus 64 complete instrumented fixed-state calls,
0 versus 16 BoundaryQueries, and zero additional graphics-interface reflection on both paths.
Thus the complete path recorded four fixed-state commands and one counted boundary query per invocation.
The four commands include required clear/draw-state transitions; the counter excludes both paths'
native clear commands. Source accounting additionally identifies eight routing and two framebuffer
binding GetInteger calls per complete pass, absent from the partial reference, and eight active-sampler
IsTexture validations per invocation on both paths. Those are excluded from the bounded counters;
bindings, helper operations, cold helper queries and error polls are not comprehensively counted.
Pass-boundary capture remains an optimization opportunity requiring explicit routing/restoration
ownership, not evidence that unchanged category setters fail to suppress redundant calls.

First renderer realization with an already prepared shader measured 23.601 ms Debug / 18.330 ms Release
for the first size in each fresh process, and 0.164 / 0.103 ms for the later size. These individual
observations include first-use/JIT effects and exclude fixture setup and shader compilation; they are
not stable per-pipeline averages. Six executable invalidation/repreparation samples per configuration
measured 42.970–47.150 ms Debug and 43.383–69.473 ms Release. The application driver-binary cache was
bypassed; driver-internal caches were uncontrolled. These samples include executable reprepare plus
realization and exclude the invalidation call itself.

Each target-size run retained the same realization through 1,000 repeated preparation calls with no
additional measured draw-time interface reflection. Three invalidations replaced and explicitly retired
the old realization; teardown retired the current one. DirectLightingRenderer retains one pipeline
field rather than an accumulating interning table. This proves the exercised identity/lifetime behavior,
not GC collectability, byte retention or bounded growth of unrelated caches. Global interning remains
outside the implementation scope.

### State-cost and storage interpretation

StateTransitionDiagnosticsTests verifies that ordinary changed raster and pixel-layout setters do
not poll errors with diagnostics disabled, and unchanged setters preserve pending native errors.
EngineBoundaryRestorationTests verifies that an unchanged warm partial boundary emits zero counted
state calls, queries or error checks. Changed checked restoration deliberately retains its safety
checks: StateTransitionDiagnosticsTests requires one changed native command and two error polls,
without nested transition diagnostics. Cold queries and transfer-buffer safety checks are separate
required work. Suppressing redundant setters does not remove those ownership obligations.

GraphicsCommandContextTests verifies repeated draws within an active pass add no counted fixed-state
calls, boundary reads or interface reflection, while changed generated inputs still reach the draw.
That is narrower than a complete one-draw renderer invocation. RenderPass setup captures framebuffer
bindings and framebuffer-local routing, and cleanup restores them. The matched renderer measurements
include that setup on each invocation; these raw queries are outside BoundaryQueries. Sampler native
object validation, helper operations, error polling and timer instrumentation are also outside the
bounded state counters. Neither a zero counter nor a smaller count establishes zero driver calls.

Fresh x64 StateValueLayoutTests results agree with the earlier measured storage table: DepthState
24 bytes, BlendState 32, RasterizerState 80, PrimitiveAssemblyState 12, DynamicDrawState 48,
SamplingState 16, StencilState 80 and OutputState 8. PixelPackState is 20, PixelUnpackState 28,
LumOnCameraState 56 and GlColorMask 1. Category knowledge enums use one byte except Rasterizer's
four bytes. The recorded pre-packing baseline was DepthState 32, RasterizerState 88 and SamplingState
24; the other category struct sizes are unchanged. These are Unsafe.SizeOf managed layouts, including
RasterizerState's reference field, not native ABI sizes or evidence of CPU/GPU speedup.

### User-run live acceptance

Status: **pending**. On 2026-10-06 the user confirmed that the consumer-migration build has not yet
been tested in-game and requested that live acceptance remain pending. No game was launched to
obtain acceptance evidence. The following cases are ready for the user's installed build; record
build identity, enabled options, driver, outcome and any capture/event IDs alongside each result.

| Case | Actions and expected behavior | Evidence status |
| --- | --- | --- |
| Water and first-person handoff | With visible water, move the held item across the screen and change camera pitch; test each quality and receiver scale. Confirm no held-item silhouette enters the pre-overlay receiver, no state-induced cutoff returns and liquid depth/surface/volume agree. Existing screen-space missing-information limitations remain separate optical behavior. | Pending user run |
| Composition and particles | Compare PBR with LumOn disabled/enabled, including particles in front of geometry and water. Confirm emission, SSAO, glow and ordinary postprocessing survive capture and engine handoff. | Pending user run |
| Debug paths | Exercise G-buffer overlay, world-cell bounds, probe bounds/points/orbs, ray lines and atlas overlays; enable OIT and window overlays together. Disable them again and confirm ordinary rendering/state returns. | Pending user run |
| Window and target lifecycle | Resize the window repeatedly, including odd dimensions, change applicable receiver settings, leave/reenter a world and revisit debug views. Confirm full target coverage, refreshed metadata and absence of stale images/native-name reuse errors. | Pending user run |
| Shader lifecycle | Use the existing shader reload mechanism while migrated passes are visible; exercise a controlled unavailable/failed shader if practical, then recover. Confirm no partially published frame, stale realization or broken engine shader owner. | Pending user run |
| RenderDoc handoff | Capture a representative frame and inspect actual liquid RGB8/R16F/RGBA8/RGBA16F routes, depth-only output discard, per-slot blends, viewports, shader/resources and the first engine draw after each VGE interruption. Record event IDs; compare equivalent camera/settings when judging performance. | Pending user capture |

Headless surface tests draw to the actual small native surface and test publication revisions; they
do not substitute for operating-system window resize. Synthetic GPU timings above describe the
chosen workload on one driver, not production frame cost, visual quality or arbitrary-mod compatibility.

### Completion review and remaining scope

The second source/evidence review and independent audit-stage-completion pass on 2026-10-06 checked
the plan's inherited proposal, design and shader-authoring contracts against the mapping, current owners,
actual TRX results and measurement JSON. No required implementation or automated-evidence item remains
unresolved. The audit distinguishes permitted pass-boundary reads from unchanged managed-draw checks,
and the explicit pending live status from automated acceptance. Final whitespace validation passed.

The implementation and automated acceptance are complete within the declared VGE-owned draw scope.
User live rendering/RenderDoc evidence remains pending. Unknown engine/mod mutations, context
replacement, global interning/eviction, asynchronous preparation and additional backends remain outside
that scope. Measured complete-boundary CPU cost and uncounted pass/helper queries are documented
optimization opportunities, not a claimed speedup. Existing project issues concerning normal maps,
tessellation and screen-space water optics are not closed by this acceptance record.
