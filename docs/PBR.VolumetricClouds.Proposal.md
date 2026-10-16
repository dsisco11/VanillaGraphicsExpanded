# VGE-owned volumetric clouds driven by native weather

Status: proposal for review. This document does not authorize implementation or define an
implementation checklist.

## Intended result

Replace native cloud drawing with a VGE-owned volumetric renderer while preserving the base
game's weather-dependent cloud distribution, column heights and bounds, altitude and movement.
Preserve roughly cubic cloud masses with modest fluff from 3D shader noise inside the native
column volume. Use the approach documented
for Unreal Engine: ray-marched three-dimensional density, light transmittance, approximate
multiple scattering, scalable tracing and reconstruction, and integration with atmospheric lighting.
This is an independent implementation for VGE's rendering backend, not a dependency on Unreal.

The renderer must support ground, inside-cloud and above-cloud views in both PBR modes. It must
participate in celestial occlusion, environmental lighting, fog and water reflections. Shadows cast
by clouds onto the scene or atmosphere are deferred until virtual shadow maps are implemented.
The owning requirement remains the unchecked volumetric-cloud item in
[PBR.BaselineShading.todo](PBR.BaselineShading.todo).

## Evidence and feasibility

Inspection of the installed Vintage Story 1.22.7 `VSEssentials.dll` and game shader assets found
two cloud paths: the ordinary mesh renderer and the `FluffyClouds` volumetric renderer. The latter
contains `CloudRendererMap`, which prepares tile data, and `CloudRendererVolumetric`, which draws it.

Weather patterns maintain two-dimensional cloud noise caches. The weather reader blends old/new
patterns, four neighboring weather regions, and rain-cloud overlay contributions. `CloudRendererMap`
samples this information into a moving grid, blends tile values on a background worker, and commits
them on the main thread. Its default tile size is 50 world units. Grid width is
`clamp(8 * DesiredViewDistance / CloudTileSize, 20, 200)` tiles.

`CloudRendererMap.WriteTexture` packs raw controls into two textures. The installed
`assets/game/shaders/cloudmap.fsh` then converts them into opacity and vertical bounds in one output,
and precomputed color in another. That color includes native daylight, sky-glow coloring, fog,
point lights and night vision. `cloudvolumetric.fsh` traverses the horizontal tile grid and integrates
constant-density vertical intervals using those colors before submitting OIT contributions.

These inputs are sufficient to preserve broad weather structure. They are not a complete 3D density
field, physical extinction coefficients, or a direct measurement of visible sky coverage. VGE must
supply internal density detail and optical interpretation within the native bounds. Source inspection establishes feasibility;
it does not establish visual quality, performance, or a validated ownership handoff.

## Ownership boundary

Retain the native weather, tile and column-map producer as the source of cloud distribution and
geometry. Consume its completed column map with matching placement metadata. VGE owns density evaluation, shader programs,
noise resources, light transport, render targets, history and composition.

The native producer is retained deliberately: reimplementing regional weather, tile scheduling,
blending, movement and column-height formulas would broaden the change beyond replacing cloud
rendering. Do not introduce a second producer or replacement vertical profiles from raw weather controls.

Use native `TextureMap` as the authoritative column envelope and opacity control. Do not use
`TextureCol` as physical lighting. Native map generation remains a data dependency, while visible
cloud rendering and lighting are VGE-owned. Do not modify installed game shader assets.

The native producer allocates and uploads its own GPU data and generates the column map, including
an unused color attachment. Retain this pass and account for its costs even though VGE draws independently. Removing that overhead
requires an explicit, verified separation of producer work from native rendering resources;
full ownership of the VGE shader does not imply that separation already exists.

## Column-data interpretation and appearance

| Native input | Proposed interpretation |
| --- | --- |
| `TextureMap.r` | Native opacity control, mapped explicitly to extinction |
| `TextureMap.ba` | Native lower and upper column bounds in tile-relative vertical units |
| Tile footprint and tile size | Horizontal column bounds and coordinate scale |
| Cloud level | Base altitude of the cloud layer |
| Committed center, grid dimensions and wind offsets | Consistent placement and advection |
| `TextureCol` | Unused by VGE cloud lighting |

The native map stores vertical bounds divided by tile size. Preserve that coordinate convention
when reconstructing the column volume, then convert path lengths to the units used for extinction.
Define the opacity-to-extinction mapping explicitly: native opacity is not an inverse-metre value.
It already includes native edge fading and perception effects; do not apply those factors twice.

Evaluate 3D noise at ray-march samples inside each native column's horizontal footprint and vertical
bounds. Use restrained density variation and inward edge erosion to add fluff while preserving
broad faces, stepped heights and a recognizably cubic silhouette. Do not expand density beyond
the native bounds, smooth away the native steps, or replace the masses with rounded cumulus shapes.
Adjacent occupied columns form a connected volume: avoid independently eroding their shared
interior faces into artificial seams. Detail must preserve clear regions. A complete generated
3D density texture is not required; reusable noise textures or procedural shader noise can supply detail.
Coordinate anchoring must keep fine structure continuous across columns and stable when the tile grid recenters
and move it consistently with cloud advection. A second independent wind translation must not
double-advect the native distribution.

The finite weather grid defines the supported domain. Edge treatment must avoid stretched border
tiles or an abrupt visible cutoff. The derived map alone cannot distinguish unavailable regional
weather from valid zero opacity; do not infer additional weather availability from it. No extension
beyond available native columns is implied by a longer trace distance.

## Publication and native lifecycle

Retain `CloudRendererMap.OnRenderFrame` at OIT order `0.30`. Replace the native
`CloudRendererVolumetric` draw at `0.31` with an independently registered VGE cloud renderer at
that order. The retained map callback owns weather ticking, tile commits and map generation;
VGE must not call `CloudTick` separately. This preserves one native update path and supplies the
completed map before VGE draws. Cast shadows are deferred, so they require no earlier producer
callback or retained previous-frame publication.

Borrow the completed native map read-only for same-frame drawing, obtaining its current texture
handle, dimensions and matching spatial placement after generation. This path requires neither
CPU readback nor a snapshot copy. Bounds depend on per-frame undulation as well as committed tiles,
so a tile commit alone does not identify the completed map. Do not read the worker-mutated `Tiles`
array from VGE drawing. A retained texture handle does not freeze its contents or extend its lifetime.

Acquire native map and draw-owner instances after their creation at `LevelFinalize`. Readiness
requires initialized tile data uploaded to the map inputs and successful map generation, not merely
allocated textures. Until ready, or if generation fails, mark the current map unavailable and omit
the VGE cloud contribution. Do not assume that an in-place failed update preserves a usable snapshot.

Preserve native pause and time-speed behavior. While paused, the map callback skips `CloudTick`
and its animation-clock advance but still updates camera-relative placement and generates the map.
VGE consumes that result without independently advancing cloud motion.

Native mode `0` is Off, `1` is Volumetric, and `2` is Simple. In Volumetric mode the native map
callback runs its producer work. Native settings changes can also re-register the native volumetric
draw, so the ownership adapter must keep that draw withdrawn whenever VGE owns the volumetric path,
including after setting changes. Off remains off. The Simple-cloud path is explicitly undecided:
this proposal does not yet select preservation of native Simple clouds or their replacement by VGE.

View-distance changes can recreate the grid and textures during a native commit. Obtain the new
handles and dimensions after generation, invalidate affected VGE history, and never sample retired
resources. Keep native shader-reload subscriptions intact; re-establish map readiness after reload
and invalidate incompatible VGE history. Reload success must not be inferred from texture allocation.

On VGE disable, withdraw its draw and restore native volumetric registration according to the current
cloud setting, not a preference saved at activation. Re-enable reacquires current owners and readiness.
Avoid permanent changes to user settings. On world exit, drop borrowed references and dispose only
VGE-owned resources; the native owner retains responsibility for its worker and resource teardown.
Prevent stale world identities or mixed-generation map metadata from reaching consumers. The callback
contract is source-backed; readiness detection and lifecycle transitions still require runtime validation.

## Density integration and lighting

March view rays through the bounded cloud volume, clipped against scene geometry, accumulating
linear in-scattered radiance and transmittance. Use extinction in inverse metres after an explicit
world-unit conversion, and dimensionless transmittance. Terminate optically saturated rays and
skip regions whose density bound proves they are empty.

Use VGE's completed atmosphere publication for solar/lunar illumination and environmental response.
Respect the publication's units, source attenuation and generation identity; do not multiply already
attenuated lighting by the same atmospheric transmittance again. Cloud transport remains separate
from the atmospheric integrator, as required by
[PBR.Atmosphere.Publication.md](PBR.Atmosphere.Publication.md).

Evaluate directional scattering and cloud self-shadowing, with a controlled multiple-scattering
approximation. A small octave count is the proposed starting model; energy compensation, phase
parameters and density conversion require comparison against reference results. Native cloud
brightness, daylight and sunset tint are not substitutes for physical incident illumination.

Shadows cast by clouds onto terrain, water, other scene receivers or atmospheric consumers are
deferred until virtual shadow maps are implemented. Do not introduce a separate Beer shadow map,
retained previous-frame cloud-shadow publication, or earlier map-generation schedule for those
consumers in this work. Their integration will be specified with the virtual-shadow-map work.

Light attenuation within the cloud volume remains part of cloud shading: it supplies cloud
self-shadowing and uses the same density definition as view-ray integration. Visible attenuation
of the sky and celestial disks also remains in scope through OIT. Environmental response and water
reflections must be accounted for explicitly without adding the deferred cast-shadow system.

## Scene integration

Retain the base game's existing volumetric-cloud OIT integration. The VGE renderer replaces density
evaluation and lighting while submitting compatible cloud contributions to the existing accumulation
and revealage buffers and resolve. Preserve the native depth-dependent bucket weighting and revealage
behavior along the volume traversal rather than assigning the whole cloud to one representative depth.
Preserve the frozen frame's lighting storage scale and range admission; coverage and depth do not
receive lighting exposure or encoding.

Clouds must attenuate the sky, sun, moon and stars behind them while remaining correctly occluded
by terrain. Particles, translucent surfaces and water can lie before, within or behind the volume.
Use the existing OIT path for those interactions and retain the native scene/liquid-depth clipping
semantics. Its bucket-based ordering is an accepted approximation; this proposal does not require
exact surface/volume sorting or a new transparency representation.

The separate [transparency proposal](PBR.Transparency.Proposal.md) is not a dependency of this work
and is not approved by it. Validate the replacement against the current native cloud/OIT behavior,
including particles and water before, within and behind clouds.

Apply atmospheric aerial transport and local fog consistently along cloud contributions, avoiding
double fogging from either a pre-fogged source or later scene composition. Inside-cloud views must
integrate from the camera rather than treating clouds as a distant sky overlay. Reflection views
must use their own camera/depth contract and must not reuse main-view history blindly.

## Quality and temporal behavior

Provide bounded quality controls for trace resolution, view/light sample counts and detail
evaluation. Use lower-resolution tracing with depth-aware reconstruction
where it preserves silhouettes, disocclusion and in-cloud movement.

Temporal reconstruction is proposed where useful, not a prerequisite for a correct density model.
History must account for camera and cloud motion, weather changes, lighting changes, grid replacement,
resize and reload. Reprojection must distinguish a grid-coordinate change from actual cloud motion.
Reject incompatible history and bound stale contributions during rapid transitions. Do not assume
that history remains valid merely because a texture handle is unchanged.

Exact budgets and quality presets remain open until measured on the target renderer. Count native
producer overhead, snapshot/upload cost, noise and weather storage, trace targets
and history in the total memory and frame-time assessment.

## Acceptance and unresolved decisions

Acceptance requires weather-coherent clear/thin/thick cloud scenes; moving sunlight and moonlight;
ground, in-cloud and above-cloud views; grid recentering and view-distance changes; pause and time
changes; fog, terrain, particles, water and reflection ordering; and clean disable/reload/world changes.
Validate roughly cubic silhouettes, modest fluff, preserved native heights/bounds and the absence
of artificial seams between connected columns. Validate both PBR modes. Focused automated checks can establish data, math and lifecycle contracts;
the user performs in-game visual acceptance. GPU timing and memory measurements are separate evidence.

Before extrapolating an implementation plan, resolve:

- Whether Simple mode retains native mesh clouds or uses VGE clouds; the Volumetric callback contract is defined above.
- The opacity-to-extinction mapping, restrained noise/edge-softness tuning and finite-domain edge behavior.
- Aerial-transport and local-fog application without double attenuation; existing OIT composition is retained.
- The environmental/reflection consumers, excluding cast shadows deferred to virtual shadow maps.
- Target hardware, quality budgets and reference scenes used for qualification.

No completion claim follows from this proposal, and the parent requirement remains unchecked.

## Sources

- Installed Vintage Story 1.22.7 `VSEssentials.dll`: `FluffyClouds.CloudRendererMap`,
  `CloudRendererVolumetric`, `FluffyCloudsModSystem`, and the native weather reader/pattern classes.
- Installed `assets/game/shaders/cloudmap.fsh` and `cloudvolumetric.fsh`.
- VGE [SceneColorLegacyPatches.cs](../VanillaGraphicsExpanded/PBR/SceneColor/SceneColorLegacyPatches.cs)
  and [SceneColorCompatibilityPublication.cs](../VanillaGraphicsExpanded/PBR/SceneColor/SceneColorCompatibilityPublication.cs).
- [Epic: Volumetric Cloud Component](https://dev.epicgames.com/documentation/en-us/unreal-engine/volumetric-cloud-component-in-unreal-engine),
  covering volume ray marching, multiple-scattering approximation, shadowing and reconstruction.

The native findings are version-specific. Reverify the relevant producer and registration contracts
when the installed game changes.
