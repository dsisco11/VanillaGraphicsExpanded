# Texel-aligned terrain relief through tessellation

Status: proposal; the stepped topology has not been implemented or validated on the GPU.

Related plan: [PBR.BaselineShading.todo](PBR.BaselineShading.todo). This document proposes an alternative terrain-displacement shape within that plan's surface-detail scope. It does not mark existing tasks complete or authorize unrelated rendering changes.

## Objective and constraints

Make eligible terrain surfaces appear to consist of small voxel-like columns aligned with their material heightmap: flat texel-sized tops, vertical walls at height differences, and sharp geometric lighting.

Generate the additional geometry through tessellation control/evaluation shaders during drawing. Do not generate a detailed mesh on the CPU, upload a replacement detail mesh, or cache generated geometry in persistent GPU mesh buffers. Continue using the engine's existing coarse terrain vertices and indices. Ordinary CPU draw configuration and material metadata remain permitted.

This is surface relief, not a volumetric voxel representation. A single heightmap cannot describe overhangs, tunnels, or multiple surfaces along the displacement direction. Faces remain internally triangulated; the objective is square tops and rectangular walls in the rendered surface.

Keep existing material ownership, terrain submission, shader patching, direct lighting, shadow rendering, and LumOn integration wherever their contracts remain applicable. Do not introduce a geometry shader or compute-generated mesh as an implicit fallback.

## Current implementation

The inspected implementation uses:

- Three existing indices per input patch in `TerrainTessellationDrawHook`.
- `layout(vertices=3) out` in `terrain.tcsh`.
- `layout(triangles, fractional_odd_spacing, ccw) in` in `terrain.tesh`.
- Generated triangle-barycentric interpolation in `TerrainTessellationStages`.
- Camera-derived adaptive edge levels, with a configured maximum of eight rounded down to seven effective fractional-odd segments.
- Height from the normal/depth atlas alpha channel at LOD zero, smooth boundary pinning, and position-dependent distance fade.
- A displacement amplitude bounded to 0.05 metres, shared by the existing geometry and bounds policy.

The current tessellation control shader restricts displacement to eligible complete tile-corner triangles with compatible normals and flags. Wind-deformed and cropped geometry are excluded. These restrictions are useful starting conditions; changing the tessellation domain does not automatically justify widening eligibility.

These contracts cannot produce the requested result by merely replacing fractional spacing with equal spacing. A regular displaced grid connects different heights with slopes, and the current segment budget cannot resolve even a 16-by-16 heightmap plus wall transitions.

## Proposed ownership and shader integration

Keep tessellation orchestration under `PBR/Tessellation`. Put stepped coordinate mapping in a shared shader include used by visible and shadow tessellation programs. The stage generator remains responsible for transporting the engine interface; material-atlas code remains responsible for tile metadata and height storage.

The implementation must distinguish:

1. **Existing face data:** engine positions, UVs, flags, lighting, and patch identity.
2. **Logical tessellation coordinates:** the regular parameter grid used to classify top and transition regions.
3. **Mapped surface coordinates:** texel boundaries, cell heights, and displaced positions produced by the evaluation shader.

Do not interpolate every output using the mapped UVs indiscriminately. Geometric position, base-surface identity, material sampling coordinates, shadow coordinates, and lighting varyings have different meanings. Extend the typed stage generator with explicit interpolation rules rather than scattering replacement expressions through unrelated patches.

## Face patches from existing indices

A quad tessellation domain is the proposed basis. The local engine API documents the face index pattern `0,1,2,0,2,3`. A candidate submission uses those six existing indices as one input patch and selects entries 0, 1, 2, and 5 as its four corners. No new index buffer is required for that pattern.

This is an integration hypothesis, not a verified contract for every installed engine draw. Before changing submission:

- Trace opaque and topsoil pool uploads, draw offsets, counts, and multi-draw ranges; prove that each selected range starts and ends on complete six-index faces.
- Check both relevant engine vertex-data paths, including SSBO-backed terrain metadata.
- Verify rotated and mirrored UVs, winding, duplicate corners, and per-face flags.
- Trace shared shadow programs, including their non-terrain users. A six-index patch setting must never regroup unrelated entity triangles.
- Retain all four actual corner values. Inferring a missing corner as a parallelogram is unsafe because engine vertex warping can make the original face non-planar.

Input patch size and the tessellation domain are separate concepts. The six-index input may be reduced to four output control points for quad evaluation, but the generated stage interface must change accordingly.

Unsupported geometry must preserve its existing rendering. Do not solve failed eligibility by culling the patch. Define and validate program/submission routing before enabling this mode in mixed pools. A quad-domain pass-through also requires care: it must not silently change the original two-triangle interpolation or diagonal on warped faces. If safe routing is unavailable without changing the allowed architecture, report that as an integration blocker.

## Stepped parameter mapping

Use a regular quad domain with `equal_spacing` and matched integer outer/inner levels. Reserve alternating parameter intervals for texel tops and transitions between tops.

For one axis containing `N` height cells, the candidate lattice has `2N` vertices and `2N-1` intervals. For integer vertex index `i` from zero through `2N-1`:

```text
cell(i)     = floor(i / 2)
boundary(i) = floor((i + 1) / 2)
position(i) = boundary(i) / N
```

For two adjacent cells, the mapped horizontal positions are `0, 1, 1, 2` in cell units. The first and last intervals span flat tops. The middle interval has zero horizontal extent but may have different endpoint heights, forming a vertical wall.

Apply the mapping independently to U and V. Each lattice vertex samples the height of its selected cell:

- Top/top intervals form one texel top.
- Transition/top and top/transition intervals form walls along height differences.
- Transition/transition regions map to a vertical line at a texel corner and should produce degenerate triangles.
- Equal neighboring heights collapse their connecting wall.

Recover lattice indices from `gl_TessCoord` using the exact effective integer levels, with explicit handling of floating-point rounding and endpoints. Non-square tiles need independent U/V dimensions and corresponding edge levels.

This mapping is a candidate to prove, not a claim that the driver will emit the desired surface without further work. Validate the actual quad-domain connectivity, corner closure, winding, degenerate regions, and rasterization on the target GL implementation. Do not rely on an implementation-specific choice of diagonal inside a quad cell.

## Material alignment and height rules

Use tile metadata and height-atlas dimensions to identify exact cell boundaries. Sample cell height with integer `texelFetch` at LOD zero. Verify atlas rectangle rounding, padding, and tile-edge addressing rather than assuming normalized extents convert to integer pixel bounds without adjustment.

Define “heightmap pixel” as a texel in the resident height atlas for the first prototype. If authored heightmaps have been resampled, those cells may differ from the original image pixels. Inspect that transformation before claiming authored-pixel alignment; preserving source-image cells could require additional material metadata.

Preserve the existing signed height interpretation and amplitude limit initially. Discrete lateral cells do not require quantizing heights to fixed vertical voxel increments; vertical quantization is outside the initial scope.

Apply distance fade consistently to every vertex of a cell's top. Evaluating fade independently at its corners would recreate sloped tops. Neighboring cells can have different faded heights, with their wall connecting those results. For a warped base face, a constant displacement alone does not guarantee a planar square top; the initial prototype uses planar faces, and production eligibility or base-surface reconstruction must explicitly address warping.

Outer material boundaries need their own closure contract. The current smooth pinning ramp is unsuitable as a per-vertex stepped-surface rule. Evaluate a texel-wide neutral border, or explicit boundary-to-base transition geometry, against adjacent faces and their different extrusion normals. The `2N-1` mapping counts internal transitions only; extra closure geometry may increase the required subdivision budget. Neither border policy is selected until seam tests demonstrate acceptable coverage and appearance.

## Shading, material sampling, and shadows

Shared top/wall vertices cannot simultaneously carry both geometric face normals through ordinary smooth interpolation. For the prototype, reconstruct the planar triangle normal in the fragment shader from derivatives of displaced position, with correct orientation. Validate how this feeds the G-buffer and the existing BRDF path. The current height-gradient normal reconstruction must not smooth away the intended steps.

Wall material sampling also needs an explicit rule. Prototype sampling from the higher adjacent cell, using the same rule regardless of view direction. Transport logical coordinates separately so collapsed wall UV extent does not accidentally select another atlas tile or produce unstable derivatives. Resolve emission, roughness, normal-map treatment, and cutout coverage consistently with that choice.

Visible and near/far shadow programs must evaluate the same height cells, geometry mapping, and detail selection. Preserve existing clip-space depth-bias handling and recompute dependent camera/shadow coordinates from displaced geometry.

Keep the undisplaced base position/normal for existing terrain patch identity. Preserve displacement-reactive temporal rejection. This proposal does not upgrade LumOn Surface Cache or trace geometry to detailed voxel relief; coarse indirect geometry remains a known approximation requiring visual evaluation.

## Detail limits and expected cost

For the candidate internal-transition lattice:

| Cells per axis | Required segments per axis | Triangles in a regular square domain |
|---|---:|---:|
| 8 | 15 | 450 |
| 16 | 31 | 1,922 |
| 32 | 63 | 7,938 |

Counts use `2 * (2N-1)^2`, include degenerate transitions, and exclude extra boundary closure. They describe generated topology, not measured GPU work or performance.

Read `GL_MAX_TESS_GEN_LEVEL` and existing patch/output limits. OpenGL specifies a minimum supported tessellation level limit of 64; this is not a claim about the current GPU's queried value. A 64-cell axis would need 127 segments under this mapping, beyond a limit of 64.

Do not retain the current fractional screen-space factors for this grid. Detail selection must choose discrete cell resolutions that remain aligned with material boundaries. Exact per-texel geometry is a near-field objective; coarser representations require a defined height reduction rule and seam/transition treatment. Fading displacement to the base surface is a candidate transition, but it does not make arbitrary mismatched tessellation grids safe.

Larger tiles require an explicit resolution policy or more input patches. A tessellation control shader cannot simply emit an arbitrary collection of independent patches. Any patch-splitting design must satisfy the prohibition on CPU-generated detail meshes and be evaluated separately. Do not silently clamp levels while continuing to claim full texel resolution.

## Validation and adoption criteria

First prove the candidate mapping in an isolated headless GL test using coarse input patches, synthetic height textures, and actual tessellation shaders. CPU-authored coarse test inputs and expected-result checks are allowed; generating the detailed reference mesh as the rendering implementation is not.

Required cases include:

- Constant height, a single step, checkerboard heights, an isolated high cell, and an isolated low cell.
- Horizontal and vertical transitions, equal-height neighbors, and four distinct heights meeting at a corner.
- Non-square tiles, atlas borders, UV rotations/reflections, and each face orientation.
- Multiple camera angles and grazing views, with culling enabled; verify winding, coverage, silhouette, wall normals, and degenerate-corner behavior.
- Flat cell tops and exact boundary positions, with geometry capture or readback as well as rendered depth/normal checks.
- Neutral displacement and failure/unsupported cases preserving the original base surface.
- Matching visible and shadow depth, including both shadow cascades and detail transitions.

Only after that proof, validate the real engine draw grouping and shader interfaces. Extend existing terrain capture, tessellation, shadow, and history tests rather than creating a parallel renderer for production.

Measure GPU time and generated primitive counts on matched near-field workloads, separately for visible and shadow passes. Compare against both undisplaced terrain and the current adaptive displacement path. Include large repeated surfaces and high-frequency heightmaps; collapsed triangles still incur upstream tessellation work.

The user performs live game validation. Do not launch the game automatically. Adoption requires verified seams, correct shadows/material response, acceptable measured cost, and explicit reporting of remaining indirect-lighting approximations. Until then, retain the current working displacement implementation as the production behavior.

## Source references

- [Draw interception and patch size](../VanillaGraphicsExpanded/HarmonyPatches/TerrainTessellationDrawHook.cs)
- [Generated stage interpolation](../VanillaGraphicsExpanded/PBR/Tessellation/TerrainTessellationStages.cs)
- [Program eligibility and GPU limits](../VanillaGraphicsExpanded/PBR/Tessellation/TerrainTessellationPrograms.cs)
- [Control shader](../VanillaGraphicsExpanded/assets/vanillagraphicsexpanded/shaders/includes/tessellation/terrain.tcsh)
- [Evaluation shader](../VanillaGraphicsExpanded/assets/vanillagraphicsexpanded/shaders/includes/tessellation/terrain.tesh)
- [Current height and subdivision policy](../VanillaGraphicsExpanded/assets/vanillagraphicsexpanded/shaders/includes/tessellation/terrain_displacement.glsl)
- [Material tile records](../VanillaGraphicsExpanded/PBR/Materials/MaterialAtlasTextureStore.Displacement.cs)
- Local engine API: `D:/CODE/VintageStory/vsapi/Client/Model/Mesh/MeshData.cs`, `AddIndices` and `AddQuadIndices`; documents the six-index face convention, but does not prove every installed draw range follows it.
- [GLSL 4.60 specification](https://registry.khronos.org/OpenGL/specs/gl/GLSLangSpec.4.60.html): tessellation domain, spacing, and stage interfaces.
- [OpenGL 4.6 specification](https://registry.khronos.org/OpenGL/specs/gl/glspec46.core.pdf): tessellation generation and implementation-dependent limits.
