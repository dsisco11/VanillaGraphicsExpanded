# Integrated PBR liquid rendering

## Ownership and engine boundary

`LiquidRenderer` registers at OIT order 0.369, immediately before the installed engine terrain OIT callback at 0.37. VGE owns `LiquidShaderProgram`, precompiled SPIR-V, frame/draw UBOs and texture bindings. The engine retains mesh creation, pool culling/submission, OIT framebuffer ownership and transparency composition. See [the renderer proposal](PBR.LiquidRenderer.Proposal.md).

`LiquidMeshSourceHook` captures the completed engine renderer and atlas tile metric at construction. The renderer reads current atlas/pool arrays each invocation, rather than retaining arrays across atlas additions. Pool capacity may exceed the atlas count: the engine reserves three extra slots. Only the active atlas prefix must have non-null liquid managers, and only that prefix is submitted. Leave-world and disposal drop borrowed references without deleting engine resources.

`LiquidRenderHooks` inserts a conditional bypass from the engine liquid shader selection through its stop call. Matrix pushes/pops, transparent terrain and meta-block submission remain in place. The original liquid instructions remain available as a vanilla fallback. The previous `PbrLiquidShaderPatches` source injection and liquid atmosphere interception are removed.

## Shader and buffer contracts

The SPIR-V program explicitly reimplements the mesh-pool `IShaderProgram` calls for origin, model-view transform and preview transparency. Every such write publishes a new draw UBO range through the existing uniform ring, so subsequent pools cannot overwrite data used by earlier draws. Mini-dimension restoration follows the engine's existing interface calls. Frame lighting/material-animation inputs are captured once per invocation; integer/vector writes use `CpuUniformBuffer` typed operations.

`LiquidShaderProgram` exposes typed frame, draw and texture inputs; its CPU buffers and generic texture binder are private. Frame setters stage data until `ApplyInputs` publishes it on the active program, while draw setters publish immediately for engine mesh-pool calls. Texture properties choose the target, unit and sampler internally. The renderer supplies borrowed texture IDs without managing GL binding details.

All precompiled stages pass through `ShaderSourceLayout`, which emits explicit locations and resource bindings from their contracts before compilation. Liquid sampler slots and uniform locations are declared individually in `Rendering/Contracts/Liquid.cs`; vertex attributes, varyings and liquid block bindings also appear explicitly in GLSL. Shared includes receive each program's contract bindings during compilation, so their texture units are not tied to liquid rendering. UBO members use std140 offsets rather than standalone uniform locations.

The installed engine exposes only a getter for its SSBO mode. One cached typed field accessor temporarily selects the existing non-SSBO liquid mesh layout and restores the previous mode in a finally block. No per-frame reflection lookup is performed. Program ownership uses `UseScope`; the existing program-stop hook releases contract samplers before engine rendering resumes. The GL cache is invalidated at entry because preceding engine callbacks bind resources directly.

Shared includes own vertex flags and normal decoding, climate/season colormaps, atlas-local animation, perception tint and local fog spheres. Liquid-specific flow rates and still-texture blending remain in the liquid shader. `oit.glsl` accepts straight-alpha color and writes premultiplied weighted color into three depth buckets, along with revealage and glow; `pbr_shadowcoords.glsl` shares cascade coordinates independently of fragment shadow filtering.

### Captured OIT contract

The user-supplied RenderDoc pipeline export `renderdoc-capture-9-30-2026.html`, capture `Vintagestory_2026.09.30_01.48_frame9439.rdc`, event 7331 (`game:chunkliquid`, a subdraw of the reported multidraw), confirms six enabled draw buffers with identity mappings:

| Output | Contents | Source / destination blend factors |
| --- | --- | --- |
| 0 | Three bucket transmission values | `DST_COLOR` / `ZERO` |
| 1 | Overall transmission (`1 - opacity`) | `DST_COLOR` / `ZERO` |
| 2 | Glow | `SRC_ALPHA` / `ONE_MINUS_SRC_ALPHA` |
| 3-5 | Weighted premultiplied color and alpha per bucket | `ONE` / `ONE` |

All blend equations are additive, with the same factors for RGB and alpha. Depth testing is enabled with `LESS`, and depth writes are disabled. The framebuffer history supplied alongside the export shows attachment 0 replaced after initial creation and attachments 3-5 populated from layers 0-2 of one texture array. Reading only the original three-attachment engine setup misses these later changes.

The previous diagnosis of an installation asset/binary mismatch was incorrect. The captured runtime uses bucket OIT, matching the installed GLSL. In particular, writing opacity to attachment 1 would invert transmission under its multiplicative blend; the shader must write `1 - opacity`. The reverted three-target change and its test assumptions are not the supported contract.

Inspection of the installed engine's nested types identifies the missing setup in `SystemRenderOITLayers.BeforeOIT`. At OIT order 0, `OnRenderFrame` rebuilds bucket resources if needed, enables six draw buffers, sets the multiplicative/additive blend factors and clears revealage to one and accumulation to zero. Its `rebuild` method replaces attachment 0 and attaches the array layers at 3-5. `BeforeOIT` also assigns the compositor's bucket sampler units 6 and 7; `SystemRenderOITLayers.AfterOIT` binds their textures at OIT order 1. The ordinary platform framebuffer and merge methods therefore describe only part of the contract.

Bucket setup precedes VGE's order 0.369 callback and the engine terrain callback at 0.37. This capture establishes the vanilla liquid draw state, not the state at the actual VGE draw. The original opaque-water symptom remains unresolved until the framebuffer, draw-buffer mappings and blend state at `pbr_liquid` are checked for intervening changes against this contract.

## Material and optics

The RGBA material atlas supplies roughness, metallic, emission and transmission. Positive transmission selects water-like optics only for non-emissive, non-lava, non-full-alpha liquids. Other liquids retain their material/emissive response rather than inheriting water optics indiscriminately.

Water uses IOR 1.333, dielectric Fresnel including underwater total internal reflection, GGX direct reflection, geometric surface normals, atmosphere solar/environment inputs, shadow cascades and view-space dynamic lights. One opaque-depth sample supplies bounded background thickness for artistic albedo-derived exponential extinction and local in-scattering. Sky depth uses a finite fallback; foreground intersections clamp to zero. This is not a measured per-fluid medium model.

Transmission remains straight-through weighted OIT. There is no screen-space refraction or scene reflection. Scalar revealage and display-space engine composition remain approximations; their improvements have separate tasks.

## Deliberate interim behavior

The owned color pass uses undisplaced liquid mesh geometry. It does not preserve vanilla wave displacement, murkiness discard or water fog, and it does not sample the engine liquid-depth texture. It still animates material textures and applies authored colormaps. Local fog spheres and perception tint are independent compatibility effects.

Above-water aerial perspective uses VGE atmosphere resources. This pass adds no camera-to-interface underwater fog while the water-volume task is pending. Other engine scene consumers still use their existing underwater behavior.

The separate engine pass using `chunkliquiddepth.vsh` is still active and can disagree with the undisplaced visible surface. Replacing it with a VGE-owned pass and sharing a modern displacement model with color rendering is explicitly tracked in PBR.BaselineShading.todo; this work does not claim color/depth surface parity yet.

## Failure and lifecycle

Preparation and complete atlas/atmosphere availability are checked before taking liquid submission ownership. Unavailable inputs leave vanilla rendering enabled. A runtime exception logs its stack and disables the owned renderer for the world session. If some owned submission may already have occurred, vanilla is suppressed for that invocation only to avoid double accumulation; subsequent invocations use vanilla. Leaving the world resets the failure state. Arbitrary GL/context failures are not guaranteed recoverable.

The renderer allocates no screen target and samples current primary/shadow framebuffer references each invocation, including after resize. Shader reload follows the existing program library. Engine liquid depth and OIT resources remain engine-owned.

## Validation and acceptance

Focused tests cover production SPIR-V linking, GPU readback of consecutive pool-origin UBO ranges, retained previous allocations, transform/transparency restoration, installed engine bypass boundaries in either atlas-patch order, and real Harmony hook installation. Existing material/surface, atmosphere and uniform-ring suites remain part of validation.

The earlier combined focused run passed 142/142 tests with no skips, including fresh SPIR-V compilation. This is headless validation, not live-game visual acceptance.

The captured bucket-contract regression run passed 18/18 liquid tests after a fresh shader build. `LiquidTransparencyTests` renders the production program into six independent float targets with the captured blend factors. It checks opacity 0, 0.1, 0.5 and 1, actual transmitting-water optics, and differently tinted overlapping layers in both draw orders. It models the installed three-bucket compositor on the CPU for a known-background resolve; it does not execute the engine compositor or test texture-array attachment wiring. `LiquidSunHighlightTests` also allocates all six outputs while retaining its bucket-0 highlight readback. These checks validate shader outputs and blending, not live callback state or the original opacity symptom.

User-run acceptance remains required for water/lava and other liquids, mini-dimensions, previews, reload/resize, world changes, day/night and both PBR modes. Inspect the documented interim surface/depth mismatch and missing replacement underwater medium explicitly. GPU timing has not been measured. The parent liquid task remains open.

## Sun highlight at the screen edge

Investigation against installed Vintage Story 1.22.7 found a screen-visibility dependency in the vanilla liquid path. `SystemRenderSunMoon.OnRenderFrame3DPost` queries samples passed around the sun quad and sets `targetSunSpec = clamp(samples / 1500, 0, 1)`. `OnRenderFrame3D` smooths that value into `DefaultShaderUniforms.SunSpecularIntensity`; `ChunkRenderer.RenderOIT` supplies it to `chunkliquid.fsh`, where it multiplies the water specular term. A clipped or occluded sun can therefore suppress vanilla water highlights.

The VGE liquid shader does not consume this value. Its solar irradiance comes from `AtmosphereModSystem`, independently of the sun quad. `LiquidSunHighlightTests.FixedReceiverRetainsSunlightAcrossViewportEdge` renders the actual precompiled liquid program with fixed world-space sunlight and a fixed water receiver. Camera rotation moves sun NDC Y from 0.992354 to 1.012532; recovered receiver RGB changes from 0.787089 to 0.787002 (ratio 0.999889). The test passes with shadows disabled and zero aerial contribution. It isolates the shader's view transform and direct highlight, not live engine shadow contents, renderer selection, or final post-processing.

The user confirmed in RenderDoc that the affected draw used vanilla `chunkliquid`. The ownership handoff rejected the installed engine layout: `ChunkRenderer` allocates each pool array with `textureIds.Length + 3` capacity, while VGE required equal lengths. That readiness check returned before submission and before setting the Harmony suppression flag. `LiquidMeshSource.TryGetAtlasPools` now checks coverage and non-null entries only for the active atlas prefix; unused trailing capacity does not trigger fallback. The suppression transpiler remains unchanged. User-run RenderDoc verification of `pbr_liquid` ownership and off-screen highlights remains required after rebuilding/restarting.

The ownership regression executes the installed `ChunkRenderer` constructor under `LiquidMeshSourceHook`: one atlas produces four slots, with three null spare entries. It also exercises actual runtime atlas growth and rejects missing active entries or insufficient capacity. The focused source/hook/shader/sun-highlight run passed 9/9 tests after the readiness fix.
