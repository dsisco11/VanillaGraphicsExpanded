# Integrated PBR liquid rendering

## Ownership and engine boundary

`LiquidRenderer` registers at OIT order 0.369, immediately before the installed engine terrain OIT callback at 0.37. VGE owns `LiquidShaderProgram`, precompiled SPIR-V, frame/draw UBOs and texture bindings. The engine retains mesh creation, pool culling/submission, OIT framebuffer ownership and transparency composition. See [the renderer proposal](PBR.LiquidRenderer.Proposal.md).

`LiquidMeshSourceHook` captures the completed engine renderer and atlas tile metric at construction. The renderer reads current atlas/pool arrays each invocation, rather than retaining arrays across atlas additions. Leave-world and disposal drop borrowed references without deleting engine resources.

`LiquidRenderHooks` inserts a conditional bypass from the engine liquid shader selection through its stop call. Matrix pushes/pops, transparent terrain and meta-block submission remain in place. The original liquid instructions remain available as a vanilla fallback. The previous `PbrLiquidShaderPatches` source injection and liquid atmosphere interception are removed.

## Shader and buffer contracts

The SPIR-V program explicitly reimplements the mesh-pool `IShaderProgram` calls for origin, model-view transform and preview transparency. Every such write publishes a new draw UBO range through the existing uniform ring, so subsequent pools cannot overwrite data used by earlier draws. Mini-dimension restoration follows the engine's existing interface calls. Frame lighting/material-animation inputs are captured once per invocation; integer/vector writes use `CpuUniformBuffer` typed operations.

`LiquidShaderProgram` exposes typed frame, draw and texture inputs; its CPU buffers and generic texture binder are private. Frame setters stage data until `ApplyInputs` publishes it on the active program, while draw setters publish immediately for engine mesh-pool calls. Texture properties choose the target, unit and sampler internally. The renderer supplies borrowed texture IDs without managing GL binding details.

All precompiled stages pass through `ShaderSourceLayout`, which emits explicit locations and resource bindings from their contracts before compilation. Liquid sampler slots and uniform locations are declared individually in `Rendering/Contracts/Liquid.cs`; vertex attributes, varyings and liquid block bindings also appear explicitly in GLSL. Shared includes receive each program's contract bindings during compilation, so their texture units are not tied to liquid rendering. UBO members use std140 offsets rather than standalone uniform locations.

The installed engine exposes only a getter for its SSBO mode. One cached typed field accessor temporarily selects the existing non-SSBO liquid mesh layout and restores the previous mode in a finally block. No per-frame reflection lookup is performed. Program ownership uses `UseScope`; the existing program-stop hook releases contract samplers before engine rendering resumes. The GL cache is invalidated at entry because preceding engine callbacks bind resources directly.

Shared includes own vertex flags and normal decoding, climate/season colormaps, atlas-local animation, perception tint and local fog spheres. Liquid-specific flow rates and still-texture blending remain in the liquid shader. `oit.glsl` supplies straight-alpha accumulation and glow output; `pbr_shadowcoords.glsl` shares cascade coordinates independently of fragment shadow filtering.

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

The combined focused run passed 142/142 tests with no skips, including fresh SPIR-V compilation. This is headless validation, not live-game visual acceptance.

User-run acceptance remains required for water/lava and other liquids, mini-dimensions, previews, reload/resize, world changes, day/night and both PBR modes. Inspect the documented interim surface/depth mismatch and missing replacement underwater medium explicitly. GPU timing has not been measured. The parent liquid task remains open.
