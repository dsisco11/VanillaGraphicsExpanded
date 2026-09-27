# Undisplaced production terrain tessellation

The engine retains its patched GLSL vertex/fragment shaders, managed shader objects, mesh buffers
and grouped draws. VGE compiles matching GLSL control/evaluation stages at runtime. This is the
explicit integration choice while terrain rendering remains engine-owned; it does not mix SPIR-V
and GLSL stages or replace the base-game renderer.

## Configuration and scope

`MaterialAtlas.UndisplacedTessellationLevel` defaults to `0` (disabled). Values `1` through `8`
enable uniform triangle subdivision for `chunkopaque` and `chunktopsoil`. Configuration reload uses
the existing shader reload path. This is an identity-path validation control, separate from the
future material amplitude and detail-mode contract in `PBR.Tessellation.MaterialAndModes.md`.

There is no height sampling, displacement, adaptive subdivision or relief change. Evaluation
interpolates the existing projected clip coordinates and smooth attributes. Flat face/atlas values
come from the original last vertex. Shadows remain ordinary draws of the same undisplaced surface.
Entities, liquids, transparent and geometry-stage programs are outside this initial scope.

## Linking and lifetime

The existing engine shader patch pipeline prepares immutable control/evaluation source templates
after committing the patched vertex source. TinyAst/GlslSchema supplies output names, types and
interpolation qualifiers. Original preprocessor directives and conditional presence markers keep
declarations and interpolation statements matched to the engine variant. There is no inspection
program, extra vertex compilation or link, GPU interface query, or varying-name allowlist.

Before engine compilation, `VGE_ENABLE_TESSELLATION` is selected through `IShader.PrefixCode`. Generated stages receive the same vertex prefix and gate
their declarations and bodies on this macro. Repeated compilation replaces VGE's own define block
without accumulating it or removing other owners' defines. Ordinary vertex/fragment interfaces are
unchanged, so failure does not require recompiling an incompatible ordinary variant.

Generated stages are compiled through `GpuShaderModule`, then linked with existing engine
vertex/fragment handles through `ShaderProgramLink`, before engine uniform locations are collected.
Unsupported output types, arrays and output blocks retain ordinary rendering.

Only a successfully linked candidate replaces the ordinary executable. Engine uniform collection
therefore targets the final program. Temporary tessellation objects are disposed;
the engine retains its vertex/fragment ownership. Metadata ties topology to both the managed object
and its executable ID, and is removed on disposal. A failed in-place engine compile retains metadata
for its still-installed executable. No persistent VGE binary-cache entry is introduced for this
engine-owned GLSL path.

## Draw interception and fallback

The five-argument grouped `ClientPlatformWindows.RenderMesh` hook changes triangle topology to
patches only for an active, successfully installed candidate. Both ordinary-index and SSBO-index
branches retain their existing offsets, counts, index formats and buffers. Patch size is three;
`GlStateCache` saves and restores it, including exceptional exits. Other program families retain
ordinary topology. An unexpected engine IL layout disables installation instead of partially
patching the draw method.

The generated GLSL 4.00 stages require OpenGL 4.0, sufficient patch/subdivision limits and
last-vertex provoking convention. Unsupported capabilities, interfaces, geometry stages or failed
candidate links retain ordinary terrain and emit a warning. These checks happen at linking, not
per draw.

The generated stages do not access SSBOs. The engine independently selects GLSL 4.30 for its SSBO
vertex variant; that variant still requires its existing OpenGL 4.3 support. Linking stages with
different GLSL versions does not require upgrading the pass-through tessellation stages.

## Verification boundaries

Focused headless tests cover installed opaque/topsoil interfaces, SSBO variants, identity raster
equivalence, managed ownership, ordinary fallback, topology policy and patch-state restoration.
The final test receipt is recorded in `PBR.BaselineShading.todo`. No game was launched and no live
performance or visual acceptance is claimed.

Source-preparation refactor validation: `artifacts/PbrColor/terrain-tessellation-source-preparation.trx`
records 51 passing tests and two existing skips. Coverage includes installed terrain variants,
conditional outputs, declaration-derived interpolation, repeated enable/disable prefix updates,
ordinary fallback and the actual grouped engine draw. Installed engine IL confirms source loading
completes before `ShaderProgram.Compile` and inserts `PrefixCode` after the version directive.

Version and tessellation limits come from the shared `GpuSupport` capability cache. Its compute-axis
limit capture now uses indexed queries, correcting the `InvalidEnum` that previously prevented
initialization. The mutable provoking-vertex convention is read through `GlStateCache` and refreshed
on state-cache invalidation, rather than treated as an immutable capability.
