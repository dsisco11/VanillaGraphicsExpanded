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

Water uses IOR 1.333, dielectric Fresnel including underwater total internal reflection, GGX direct reflection, geometric surface normals, atmosphere solar/environment inputs, shadow cascades and view-space dynamic lights. Explicit material-owned absorption/scattering replaces albedo-derived extinction and tint-based scattering; see [water medium units, measured reference and transport](PBR.WaterMedium.md). An oriented boundary capture supplies RGB transport to opaque composition. After successful composition, OIT supplies the interface response without repeating bulk extinction. The bounded opaque-depth proxy remains a fallback when capture is unavailable; finite sky paths are supported when captured boundaries establish an exit; transparent-receiver coverage remains unresolved.

Transmission remains straight-through weighted OIT. There is no screen-space refraction or scene reflection. Scalar revealage and display-space engine composition remain approximations; their improvements have separate tasks.

## Wave displacement and liquid-depth ownership

Visible water and liquid depth now evaluate the same four-band Gerstner function from one frame snapshot. The wavelengths are 3, 5, 8 and 13 metres, with component amplitudes 0.012, 0.018, 0.024 and 0.016 metres. Each block is one cubic metre, so engine vertex coordinates are metres. Phase speeds use the gravity-wave dispersion relation with 9.80665 m/s² gravity and a fixed 3 m reference depth. CPU phase reduction in double precision keeps waves world-anchored as the camera moves. Wind intensity, the engine's weak-wave flag and its oceanity bits scale displacement; lava and non-upward faces remain undisplaced. Other non-lava liquids currently share this bounded wave response because the engine's packed liquid flags do not identify their material type. Lateral Gerstner motion fades near shore flags to reduce cracks. Wave heights and steepness are bounded; this is a GPU geometric wave model, not a fluid simulation.

The shared wave evaluation also differentiates its parametric surface to produce a smooth upward normal. Water optics evaluate that normal per fragment from the original surface coordinates and interpolated wave weights, avoiding linear normal interpolation over the coarse mesh triangles. Side faces, lava and other opaque liquid responses retain their existing geometric-normal path. Flag or mesh discontinuities at shore boundaries may still require local handling after in-game inspection.

Liquid shadow-cascade coordinates and nonlinear coverage are evaluated per fragment from the displaced surface position. Interpolating cascade coverage from mesh vertices can imprint triangle boundaries onto the sun reflection even when the water normal is continuous.

The selected direct evaluation avoids an FFT texture/synchronization pass for every visible water body and does not require maintaining a shallow-water grid across chunk boundaries. FFT oceans or local interaction simulation may be added independently if their visual benefit and GPU cost justify them. The current engine mesh has approximately block-scale vertices, so wavelengths below two metres should be expressed through shading normals rather than geometric displacement. Shore attenuation uses packed oceanity as a proxy, not measured bathymetry. Wave-dependent roughness and visual calibration still require in-game evaluation.

The selection follows the direct Gerstner evaluation and geometric-versus-normal separation described in [NVIDIA GPU Gems, chapter 1](https://developer.nvidia.com/gpugems/gpugems/part-i-natural-effects/chapter-1-effective-water-simulation-physical-models). [Tessendorf's spectral ocean notes](https://jtessen.people.clemson.edu/reports/papers_files/coursenotes2004.pdf) are the FFT alternative considered for large open water. The selected model has not yet been timed against that alternative in this engine.

`SystemRenderTerrain` registers its `OnRenderBefore` callback at order 0.995 of the Before stage, after VGE's uniform-ring begin callback at -1000. `LiquidDepthRenderHook` replaces only the shader/draw block inside `ChunkRenderer.OnRenderBefore`; the engine retains its liquid-depth framebuffer, clear, matrix scope, profiler marker and following primary-framebuffer bind. `LiquidDepthRenderer` uses an owned SPIR-V program and UBOs with the engine-owned liquid pools. The color OIT pass draws only when the owned depth pass completed and consumes its exact `LiquidWaveFrame` snapshot. If depth preparation is unavailable, both paths leave their vanilla submissions enabled; a partial depth-pass failure avoids a duplicate depth draw. The two shaders import the same wave implementation, so their submitted water geometry uses identical displacement.

The owned shaders do not preserve vanilla murkiness discard or water fog and do not sample the engine liquid-depth texture. Material textures and authored colormaps still animate; local fog spheres and perception tint remain independent compatibility effects.

Above-water aerial perspective uses VGE atmosphere resources. Water-aware opaque composition restricts aerial perspective to aggregate air length and replaces opaque underwater fog for a recognized water camera. Camera classification reuses the engine underwater flag without an additional contact texture or probe pass. It does not track animated surface contact. Other engine scene consumers still use their existing underwater behavior; consumer coverage remains open, and exact animated contact belongs to the waterline work.

The depth target remains an engine resource for existing consumers. Color has a small clip-space depth bias for shoreline layering, so exact rasterized depth values are intentionally not identical to the unbiased liquid-depth pass even when geometry matches.

## Failure and lifecycle

Preparation and complete atlas/atmosphere availability are checked before taking liquid submission ownership. Unavailable inputs leave vanilla rendering enabled. A runtime exception logs its stack and disables the owned renderer for the world session. If some owned submission may already have occurred, vanilla is suppressed for that invocation only to avoid double accumulation; subsequent invocations use vanilla. Leaving the world resets the failure state. Arbitrary GL/context failures are not guaranteed recoverable.

Neither renderer allocates a screen target. They sample current framebuffer references at their own render boundaries, including after resize. Shader reload follows the existing program library. Engine liquid depth and OIT resources remain engine-owned.

## Validation and acceptance

Focused tests cover production SPIR-V linking, GPU readback of consecutive pool-origin UBO ranges, retained previous allocations, transform/transparency restoration, installed engine bypass boundaries in either atlas-patch order, and real Harmony hook installation. Existing material/surface, atmosphere and uniform-ring suites remain part of validation.

The earlier combined focused run passed 142/142 tests with no skips, including fresh SPIR-V compilation. This is headless validation, not live-game visual acceptance.

The captured bucket-contract regression run passed 18/18 liquid tests after a fresh shader build. `LiquidTransparencyTests` renders the production program into six independent float targets with the captured blend factors. It checks opacity 0, 0.1, 0.5 and 1, actual transmitting-water optics, and differently tinted overlapping layers in both draw orders. It models the installed three-bucket compositor on the CPU for a known-background resolve; it does not execute the engine compositor or test texture-array attachment wiring. `LiquidSunHighlightTests` also allocates all six outputs while retaining its bucket-0 highlight readback. These checks validate shader outputs and blending, not live callback state or the original opacity symptom.

User-run acceptance remains required for water/lava and other liquids, mini-dimensions, previews, reload/resize, world changes, day/night and both PBR modes. Inspect color/depth shoreline alignment, wave motion and the missing replacement underwater medium explicitly. GPU timing has not been measured. The parent liquid task remains open.

## Sun highlight at the screen edge

Investigation against installed Vintage Story 1.22.7 found a screen-visibility dependency in the vanilla liquid path. `SystemRenderSunMoon.OnRenderFrame3DPost` queries samples passed around the sun quad and sets `targetSunSpec = clamp(samples / 1500, 0, 1)`. `OnRenderFrame3D` smooths that value into `DefaultShaderUniforms.SunSpecularIntensity`; `ChunkRenderer.RenderOIT` supplies it to `chunkliquid.fsh`, where it multiplies the water specular term. A clipped or occluded sun can therefore suppress vanilla water highlights.

The VGE liquid shader does not consume this value. Its solar irradiance comes from `AtmosphereModSystem`, independently of the sun quad. `LiquidSunHighlightTests.FixedReceiverRetainsSunlightAcrossViewportEdge` renders the actual precompiled liquid program with fixed world-space sunlight and a fixed water receiver. Camera rotation moves sun NDC Y from 0.992354 to 1.012532; recovered receiver RGB changes from 0.787089 to 0.787002 (ratio 0.999889). The test passes with shadows disabled and zero aerial contribution. It isolates the shader's view transform and direct highlight, not live engine shadow contents, renderer selection, or final post-processing.

The user confirmed in RenderDoc that the affected draw used vanilla `chunkliquid`. The ownership handoff rejected the installed engine layout: `ChunkRenderer` allocates each pool array with `textureIds.Length + 3` capacity, while VGE required equal lengths. That readiness check returned before submission and before setting the Harmony suppression flag. `LiquidMeshSource.TryGetAtlasPools` now checks coverage and non-null entries only for the active atlas prefix; unused trailing capacity does not trigger fallback. The suppression transpiler remains unchanged. User-run RenderDoc verification of `pbr_liquid` ownership and off-screen highlights remains required after rebuilding/restarting.

The ownership regression executes the installed `ChunkRenderer` constructor under `LiquidMeshSourceHook`: one atlas produces four slots, with three null spare entries. It also exercises actual runtime atlas growth and rejects missing active entries or insufficient capacity. The focused source/hook/shader/sun-highlight run passed 9/9 tests after the readiness fix.
