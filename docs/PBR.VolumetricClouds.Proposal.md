# VGE-owned volumetric clouds driven by native weather

Status: proposal for review. This document does not authorize implementation or define an
implementation checklist.

## Intended result

Replace native cloud drawing with a VGE-owned volumetric renderer while preserving the base
game's weather-dependent cloud distribution, altitude and movement. Use the approach documented
for Unreal Engine: ray-marched three-dimensional density, light transmittance, approximate
multiple scattering, scalable tracing and reconstruction, and integration with atmospheric lighting.
This is an independent implementation for VGE's rendering backend, not a dependency on Unreal.

The renderer must support ground, inside-cloud and above-cloud views in both PBR modes. It must
participate in celestial occlusion, cloud shadows, environmental lighting, fog and water reflections.
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
supply the volume shape and optical interpretation. Source inspection establishes feasibility;
it does not establish visual quality, performance, or a validated ownership handoff.

## Ownership boundary

Retain the native weather and tile producer as the initial source of cloud distribution. Capture
its committed raw data into VGE-owned storage. VGE owns density evaluation, shader programs,
noise resources, light transport, shadow products, render targets, history and composition.

The native producer is retained deliberately: reimplementing regional weather, tile scheduling,
blending and movement would broaden the change beyond replacing cloud rendering. Direct weather-reader
sampling remains an alternative only if the retained producer cannot satisfy a safe lifecycle
contract. It is not a second concurrently active producer or an automatic fallback.

Do not use native `TextureCol` as physical lighting. Do not use the derived `TextureMap` as the
authoritative density model: its opacity and bounds already include native shape formulas,
edge fading and perception effects. Do not modify installed game shader assets.

The native producer currently allocates and uploads its own GPU data. Retaining it may initially
retain those resources and costs even though VGE draws independently. Removing that overhead
requires an explicit, verified separation of producer work from native rendering resources;
full ownership of the VGE shader does not imply that separation already exists.

## Weather-data interpretation

| Native input | Proposed interpretation |
| --- | --- |
| `SelfThickness` | Broad cloud amount and vertical development control |
| `CloudOpaqueness` | Artistic input to a bounded density/extinction mapping |
| `ThinCloudMode` | Blend between thin and vertically developed density profiles |
| `UndulatingCloudMode` | Shape and undulation control |
| Cloud level | Base altitude of the cloud layer |
| Committed center, grid dimensions and wind offsets | Consistent placement and advection |
| `Brightness` | Legacy artistic metadata; excluded from physical illumination by default |

Normalize the native nonnegative short-valued controls according to their 0–32767 representation.
Define and document the conversion to physical extinction rather than treating native opacity as
inverse metres. Preserve the weather controls' broad intent without requiring the native block-shaped
silhouette or native brightness formula.

Generate bounded 3D density from smoothly sampled weather controls, a vertical profile, and
VGE-owned base/detail noise. Detail must preserve clear regions and remain subordinate to the
weather envelope. Coordinate anchoring must keep fine structure stable when the tile grid recenters
and move it consistently with cloud advection. A second independent wind translation must not
double-advect the native distribution.

The finite weather grid defines the supported domain. Edge treatment must avoid stretched border
tiles or an abrupt visible cutoff. Missing regional weather is not evidence of physically clear
sky: the adapter must distinguish unavailable input from valid zero cloud amount where the native
interface permits it. No extension beyond available weather data is implied by a longer trace distance.

## Publication and native lifecycle

Capture a coherent snapshot at the main-thread commit/upload boundary before the worker is released
to mutate tiles again. Include tile values, dimensions, committed spatial state and a generation
identity. Do not read the public `Tiles` array opportunistically from the draw callback. Per-frame
camera-relative placement and continuous wind motion must be derived consistently from that committed
state; camera movement alone must not require rebuilding the weather texture.

`CloudRendererMap.OnRenderFrame` currently advances `CloudTick` only when native volumetric mode is
enabled. Unregistering the renderer or switching clouds off therefore cannot be used as the sole
replacement mechanism. The ownership adapter must preserve one update/commit path while suppressing
native map shading and volumetric drawing. The ordinary mesh-cloud path must also remain suppressed
while VGE owns clouds, including after user setting changes.

The exact hook and scheduling point require verification before implementation. In particular,
the snapshot must be available before its VGE consumers, preserve pause/time-speed behavior, and
avoid advancing native weather processing twice. Capture failures retain the last complete snapshot
only while its spatial domain and world identity remain valid. Before the first valid snapshot,
publish an explicit unavailable state and omit the owned cloud contribution.

View-distance changes replace the native grid and texture handles. Shader reload, world teardown,
feature disable and re-enable must release VGE resources and restore native registrations/settings
only according to recorded ownership. Avoid permanent changes to the user's cloud preference.
Never dispose engine-owned textures. Retired or mixed-generation data must not remain reachable.

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

Use a bounded light-transmittance representation for cloud shadows on terrain and atmospheric
consumers. Evaluate a Beer-style shadow map for scalable distant shadows, with secondary light
marching where close or in-cloud views justify the cost. All shadow and view evaluations must
use the same density definition and compatible weather state. Environmental attenuation and water
reflections must be accounted for explicitly rather than inferred from the visible cloud image.

## Scene integration

Expose accumulated cloud radiance and transmittance with the depth information needed by the
existing scene-composition contract. Preserve the frozen frame's lighting storage scale and range
admission; coverage and depth do not receive lighting exposure or encoding.

Clouds must attenuate the sky, sun, moon and stars behind them while remaining correctly occluded
by terrain. Particles, translucent surfaces and water can lie before, within or behind the volume.
A single cloud layer at one representative depth cannot establish correct ordering for all those
cases. Select and document the volume/transparency integration contract before implementation,
including the approximation imposed by the existing OIT representation.

The separate [transparency proposal](PBR.Transparency.Proposal.md) is not assumed implemented or
approved by this document. If required ordering cannot be delivered by the available composition
contract, record that dependency rather than implementing a parallel transparency system.

Apply atmospheric aerial transport and local fog consistently along cloud contributions, avoiding
double fogging from either a pre-fogged source or later scene composition. Inside-cloud views must
integrate from the camera rather than treating clouds as a distant sky overlay. Reflection views
must use their own camera/depth contract and must not reuse main-view history blindly.

## Quality and temporal behavior

Provide bounded quality controls for trace resolution, view/light sample counts, shadow-map extent
and resolution, and detail evaluation. Use lower-resolution tracing with depth-aware reconstruction
where it preserves silhouettes, disocclusion and in-cloud movement.

Temporal reconstruction is proposed where useful, not a prerequisite for a correct density model.
History must account for camera and cloud motion, weather changes, lighting changes, grid replacement,
resize and reload. Reprojection must distinguish a grid-coordinate change from actual cloud motion.
Reject incompatible history and bound stale contributions during rapid transitions. Do not assume
that history remains valid merely because a texture handle is unchanged.

Exact budgets and quality presets remain open until measured on the target renderer. Count native
producer overhead, snapshot/upload cost, noise and weather storage, shadow resources, trace targets
and history in the total memory and frame-time assessment.

## Acceptance and unresolved decisions

Acceptance requires weather-coherent clear/thin/thick cloud scenes; moving sunlight and moonlight;
ground, in-cloud and above-cloud views; grid recentering and view-distance changes; pause and time
changes; fog, terrain, particles, water and reflection ordering; and clean disable/reload/world changes.
Validate both PBR modes. Focused automated checks can establish data, math and lifecycle contracts;
the user performs in-game visual acceptance. GPU timing and memory measurements are separate evidence.

Before extrapolating an implementation plan, resolve:

- The native commit hook and single-owner scheduling contract, including cloud-mode changes.
- The weather-to-density mapping, layer thickness and finite-domain edge behavior.
- The volume/transparency and aerial-transport composition contract.
- The shadow representation and environmental/reflection consumers.
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
