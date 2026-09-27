# Terrain displacement material and mode contract

Status: the material amplitude field, atlas metadata and adaptive stage core are implemented in
[the adaptive core](PBR.Tessellation.AdaptiveDisplacement.md). [Relief and mode selection](PBR.Relief.md)
and [rendering-consumer integration](PBR.Tessellation.RenderingConsumers.md) are implemented.
Production cost and live appearance validation remain in [PBR.BaselineShading.todo](PBR.BaselineShading.todo).
This contract builds on [the depth-map audit](PBR.Tessellation.DepthMapAudit.md).

## Material selection

Use the existing canonical texture glob -> resolved material mapping and priority rules. The baked
atlas tile and its resolved material own displacement; a block face samples that tile through its
existing UVs. Rotating a texture rotates its height pattern without block-rotation metadata or
per-axis overrides. A shared texture/material has one displacement definition; distinguishing two
blocks using exactly that texture is outside this contract.

Add an optional material-level `displacement` object alongside the existing BRDF members. It is not
part of `BRDFProperties`: changing geometry is separate from the BRDF. Do not add displacement to
file-wide defaults initially, so existing broad defaults cannot accidentally opt in every texture.
No new mapping-rule override or authored height-file format is required for the initial implementation.
Use the normal/depth atlas already produced for the selected texture.

Accepted material authoring example (production displacement remains gated):

```json
{
  "roughness": 0.85,
  "displacement": { "amplitudeMetres": 0.02 }
}
```

`amplitudeMetres` defaults to zero and is the opt-in: positive values permit the global selected mode;
zero explicitly disables the effect. A separate enabled boolean would duplicate that decision.
Use finite values in [0, 0.05] metres. This is a conservative initial engineering limit, not a measured
quality optimum. Reject invalid authoring values with a material diagnostic and resolve displacement
to zero; do not silently turn negative amplitudes into inverted relief. Built-in materials remain
opted out until their textures have been reviewed.

Resolve this value with the material and transport it to the GPU using atlas/tile metadata owned by
the material system. Do not overload normal/depth alpha (height), BRDF emissive channels or face flags.
The storage layout belongs to implementation; it must retain material identity across atlas rebuilds
and include displacement metadata in relevant material cache invalidation. A missing lookup is zero.

## Height and physical units

One block equals one metre. For atlas alpha H, use:

    signedHeight = clamp(2 * H - 1, -1, 1)
    offsetMetres = amplitudeMetres * signedHeight
    displacedPosition = basePosition + geometricNormal * offsetMetres

Neutral height is fixed at 0.5 for this initial contract; there is no configurable zero level. The
amplitude is maximum absolute displacement, so peak-to-trough range is twice the amplitude. Use the
unperturbed geometric normal for displacement, not the baked normal map. Edge constraints and distance
fade may reduce this offset but cannot exceed its bound. Culling expansion must cover the maximum
permitted outward offset even when visible subdivision is reduced.

`scale.depth` has already affected the atlas bake and must not be applied again. It is dimensionless;
it neither opts a material in nor supplies metres. Missing/unpopulated atlas data uses neutral height;
missing material metadata, invalid UV rectangles, degenerate tangent frames and non-finite samples
produce zero displacement. A constant 0.5 map must exactly reproduce the base geometry.

Do not interpolate different material IDs or amplitudes across a primitive. A patch must have a
consistent tile/material identity; otherwise use factor one and zero displacement. Shared triangle
edges within a face must agree. Incompatible tile boundaries and perpendicular face corners require
an edge constraint in the adaptive-displacement subtask; amplitude alone cannot prevent cracks.

## Eligible receivers and topsoil

Initial geometric displacement supports opted-in opaque terrain and topsoil plus their matching
shadow draws. The same texture appearing on an entity, held item, liquid or transparent receiver does
not enable tessellation there. Wind-deformed/foliage receivers are excluded initially using the engine's
existing render flags; being in an opaque pool alone is not sufficient eligibility.

Topsoil uses the primary terrain UV/tile as the authoritative height and material selection. Its
secondary texture remains a color layer and contributes no independent height; preserve both UV
streams for shading. If the primary tile cannot be identified unambiguously, disable displacement
for that patch. This avoids two independently selected heights competing for one geometric surface.

A mixed-material pool can still be submitted as patches: non-eligible patches have subdivision one
and offset zero. Do not rebuild or split chunk meshes merely to separate opted-in materials.
Shadow routing must retain the same material eligibility and displacement bounds as the main pass.

## Requested and effective rendering modes

Define a global enum setting `TerrainSurfaceDetailMode` with values `Disabled`, `Relief`, and
`Tessellation`. Default to `Relief`. Persist the requested mode separately from the effective mode;
resource/capability failures must not rewrite the user's preference.

| Mode | Eligible material behavior | Geometry and depth |
| --- | --- | --- |
| Disabled | Existing base UV/normal-map shading | Ordinary triangle geometry; no new relief |
| Relief | Reuse bounded POM stepping/refinement with material amplitude and atlas-safe UVs | Initial implementation changes UV shading only; no silhouette or fragment-depth change |
| Tessellation | Signed height displaces subdivided geometry | Displaced raster depth, normals and matching shadows; full-height POM is off |

The table describes surface-detail behavior. The independent `UndisplacedTessellationLevel`
control can still subdivide undisplaced terrain in Disabled or Relief mode. Its current
user-selected default is 2; use 0 when measuring an ordinary-triangle baseline.

Relief initially retains the existing indentation convention: `depthMetres = amplitudeMetres *
clamp(1 - 2*H, 0, 1)`. Positive height is not extruded by this approximation. Convert metres into tile
UV displacement using the actual world-position/UV tangent metric, not an assumed one-metre texture
repeat. Reuse existing step/refinement bounds, distance fade and atlas clamps; the material amplitude
replaces an ambiguous global physical scale for this new mode. These are deliberately different
geometric capabilities, so switching between modes is not guaranteed to be pixel-identical.

No combined relief+tessellation mode initially. A future combined mode requires a defined residual
height function. Tessellation must not fade automatically to full-height relief underneath it.
Distance transitions within a mode must preserve the shared-edge rules and temporal history contract.

The mode defaults to Relief to enable POM; explicit Disabled or Tessellation selections are retained. Do not migrate legacy POM
settings. The obsolete boolean and UV-space scale are removed; material amplitudes provide metres.
Existing step/refinement/fade settings remain active controls.

Height-atlas generation is required by an active detail mode even if normal-map shading is disabled.
Normal-map enablement remains an independent shading preference. If height resources are unavailable,
use the ordinary path until coherent resources exist; no invalid texture sampling is allowed.

## Capabilities, publication and fallback

Select modes/program generations outside draw loops. Tessellation requires supported stage execution,
the engine-owned runtime GLSL/link path, sufficient queried patch/interface/texture limits, registered patch
state ownership, and complete main/shadow variants for eligible pools. Do not infer support solely
from the engine's SSBO option. Relief requires the height atlas and a valid linked relief variant.

Unsupported tessellation or failed required program preparation produces effective Disabled with a
single explanatory diagnostic. Do not silently select Relief, which has different visibility behavior.
Retain the existing ordinary triangle programs for fallback. Per-material missing data disables only
that material's displacement; a failed global program contract disables the new mode globally.

Publish a new mode only when its required programs, material metadata and resources are coherent.
Never submit patch primitives to a non-tessellated program or triangles to a tessellated program.
If the engine's destructive reload invalidates the previous generation, use the ordinary path until
replacement preparation succeeds. Changes in mode/amplitude/height data must update appropriate bounds,
material caches and temporal-history validity; preserve unrelated work and avoid global cache erasure
as a substitute for defining these dependencies. Both PBR lighting modes follow this same selection.

## Completion and implementation checks

This definition resolves identity, opt-in, physical units, neutral/missing-data behavior, topsoil
ownership, receiver scope, mode relationships, migration and fallback. No production/schema edits or
GPU validation are required to define the contract, and none were performed here.

Carry into implementation tests: zero/neutral identity, amplitude bounds and non-finite authoring,
rotated UVs, topsoil secondary-layer independence, missing/partial atlas data, material cache rebuild,
normal-maps-off/detail-on, mixed pools, incompatible patch metadata, explicit mode selection,
capability rejection and coherent main/shadow mode replacement. Seam behavior and performance remain
owned by the subsequent implementation and validation subtasks.
