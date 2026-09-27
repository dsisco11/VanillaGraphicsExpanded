# Terrain tessellation and depth-map audit

Audit date: 2026-09-26. Scope: the first tessellation subtask in `PBR.BaselineShading.todo`.
This is a source/installed-IL audit, not an implementation or live rendering validation.

## Draw topology and ownership

The checked-out API allocates mesh pools as `EnumDrawMode.Triangles`
(`../vsapi/Client/MeshPool/MeshDataPool.cs:108-147`). Cube and quad helpers use index triples
`0,1,2` and `0,2,3`; pool insertion offsets existing indices rather than reconstructing faces.
SSBO pool placement requires four-vertex alignment. Three-control-point triangle patches can
therefore reuse the triangle index stream; four-control-point patches cannot simply reinterpret it.
This conclusion covers the engine triangle contract, not arbitrary malformed mod-supplied meshes.

`SystemRenderTerrain.OnRenderOpaque` calls `ChunkRenderer.RenderOpaque`; the installed renderer
selects `Chunkopaque` and then `Chunktopsoil` before their pool-manager draws. The checked-out
`MeshDataPoolManager.Render` performs culling, sets each pool's camera-relative `origin`, and handles
mini-dimension transforms before `MeshDataPool.RenderMesh` submits grouped indexed draws.
Preserve those operations and the index counts/byte offsets.

Installed `ClientPlatformWindows.RenderMesh(MeshRef,int[],int[],int,bool)` calls
`GL.MultiDrawElements` with unsigned-int indices. Both paths read topology from `VAO.drawMode`:
SSBO path at IL_0043/0051, ordinary path at IL_0075/0083. The SSBO path binds the global index buffer
and face data at binding 3; the ordinary path binds the mesh's index buffer. Do not replace either
binding scheme or convert the vertex IDs to patch-local IDs.

**Proposed narrow interception:** a terrain-pool render scope identifies an eligible pool and a
successfully linked tessellated program. Inside that scope, change only the topology argument at
these two platform multi-draw call sites to `Patches`, preserving all counts and offsets. A validated
Harmony transpiler can substitute the topology operand without duplicating the platform renderer.
Set three patch vertices through the state cache and restore prior state on scope exit, including
exceptions. Ordinary draws bypass the override. Avoid permanently changing shared VAO metadata or
patching every block tessellator. The undisplaced-path subtask must verify this hook against the
installed method and reject unexpected IL instead of applying a partial patch.

A shader-name-only guard is insufficient for shadows: `ChunkRenderer.RenderShadow` uses
`Chunkshadowmap` for multiple pool entries (including indices 0, 1 and 2). Retain pool provenance
when selecting the displaced shadow variant; do not enable it for every shadow draw. The existing
normal/depth texture setter hook does not include `ShaderProgramChunkshadowmap.set_Tex2d2D`, so
shadow atlas binding is an explicit integration gap.

## Vertex interfaces and displacement boundaries

Installed `chunkopaque.vsh` has two layouts: ordinary xyz/UV/light/render-flags/colormap attributes,
and SSBO face reconstruction using `faces[gl_VertexID / 4]` and `gl_VertexID & 3`. `chunktopsoil.vsh`
adds a second UV stream in both layouts. `chunkshadowmap.vsh` has its own smaller interface and MVP.

Keep original indexed vertex processing before the control/evaluation stages. Forward required
attributes explicitly, including flat material/atlas metadata, both topsoil UVs, lighting, and flags.
Moving projection to the evaluation stage must preserve vertex/global warping, shadow coordinates,
normal outputs and the engine's final clip-space depth bias. Recompute position-dependent outputs
from displaced positions rather than interpolating stale projected/shadow coordinates. The existing
fragment derivative-based tangent-frame helper cannot be called from a tessellation evaluation
shader; derive a frame from patch positions/UVs or supplied normals instead.

Pool origins and mini-dimension transforms matter for edge decisions: camera-relative positions
from different pools are not automatically a common, stable edge key. Canonical shared-edge inputs,
height continuity and culling bounds remain implementation requirements. Source inspection does not
establish that all neighboring faces share UVs, normals or compatible height values.

## Height atlas and scale

The production owner is `MaterialAtlasNormalDepthGpuBuilder`, which runs the height-bake chain and
`pbr_heightbake_pack_to_atlas.fsh`. Do not use the older placeholder `pbr_normaldepth_bake.fsh` as the
height contract. Production output is RGBA16F: packed tangent normal in RGB, signed height encoded
as `0.5 + 0.5 * clamp(height * depthScale, -1, 1)` in alpha. `ReadHeightSigned` decodes `2*A-1`.
Transparent texels and newly allocated/unpopulated atlas tiles receive neutral alpha 0.5.

`PbrOverrideScale.Depth` is a dimensionless multiplier applied during baking; it is not a physical
metre displacement. Do not multiply by it again during tessellation. A physical displacement
amplitude and material opt-in are still to be specified by the next subtask. Current height content
is generated from albedo, so neutral initialization prevents startup extrusion but does not identify
which finished materials should be allowed to displace geometry.

`MaterialAtlasTextureStore` creates normal/depth textures with nearest filtering;
`Texture2D.Create` allocates exactly one mip level. There is no ready-made height mip hierarchy to
select for distant geometry. Sampling in the evaluation stage needs explicit LOD and an atlas-safe
filtering policy (such as tile-clamped interpolation at level zero initially). Any new mip hierarchy
must preserve tile isolation; ordinary whole-atlas mip generation is not established as safe.
Sampler-object overrides must be controlled explicitly when the new path binds the atlas.

Existing `vge_parallax.glsl` POM reads only indentation: `2*clamp(0.5-height01,0,0.5)`, with bounded
steps/refinement, distance fade and tile clamping. Signed geometry displacement cannot blindly reuse
that one-sided depth value or apply full POM on top of full displacement. Topsoil's two texture layers
also require an explicit choice of authoritative displacement texture in the material-mode subtask.

## Existing pipeline support and remaining gaps

Already present:
- `ShaderStageKind` and stage contracts include tessellation control/evaluation; control without
  evaluation is rejected by the graphics contract.
- ShaderBuildTool recognizes `.tcsh`/`.tesh`, maps their compiler stages and treats them as intermediate
  interfaces in `ShaderSourceLayout`.
- `SpirvStageLoader` maps both OpenGL stage types. `GpuProgram.Spirv` attaches/links their handles,
  then releases stage objects after linking despite the engine lacking dedicated stage slots.
- `PreparedProgramBinary` includes every stage kind and entry point in the cache identity.

Missing for production terrain: actual terrain tessellation contracts/stages, engine-to-VGE program
integration preserving engine uniform setters, intermediate interface validation, eligible pool/draw
routing, patch-vertex state ownership, capability policy, and shadow atlas/program routing. The
current engine source-patching path prepares vertex/fragment/optional geometry stages; the VGE
SPIR-V machinery does not by itself replace the engine terrain programs. Choose that integration
explicitly in the undisplaced-path subtask rather than assuming stage-enum support completes it.

No `PatchVertices`/tessellation-limit state handling was found in the rendering abstraction. Query
support and limits once for the active context before choosing the variant; account for tessellation
availability, SPIR-V support, per-stage interfaces and texture units. Three patch vertices fit the
specified minimum; the hardware tessellation limit is not an appropriate default quality budget.
The OpenGL specification requires patch draws for tessellation and lists minimum maxima of 32 patch
vertices and 64 tessellation level. See [OpenGL 4.6 specification, table 23.58](https://registry.khronos.org/OpenGL/specs/gl/glspec46.core.pdf).
No active-context capability query or performance measurement was performed in this audit.

## Evidence and completion boundary

Read checked-out API mesh/pool source, current VGE atlas/build/runtime code and installed
`G:/Vintagestory/assets/game/shaders/{chunkopaque,chunktopsoil,chunkshadowmap}.vsh`.
Installed IL evidence:
- `artifacts/basegame-SystemRenderTerrain.il` and `artifacts/basegame-ClientPlatformWindows.il`.
- Fresh exports `artifacts/PbrColor/Tessellation-ChunkRenderer.il` and
  `artifacts/PbrColor/Tessellation-MeshDataPool.il`, generated with `artifacts/ildasm/dotnet-ildasm.exe`.

Cross-checked the proposed interception against both multi-draw branches, compared the height
consumer against the actual atlas producer, and reconciled stage support with the separate engine
program-loading path. The audit subtask is complete; implementation feasibility still requires the
planned undisplaced GPU draw validation. No production code changed, tests ran, or game launched.
