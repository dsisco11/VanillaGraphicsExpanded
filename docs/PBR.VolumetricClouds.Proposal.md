# VGE-owned volumetric clouds driven by native weather

Status: complete proposal approved by the user on 2026-10-16 following the proposal logic review.
Approval establishes the design contract; this document is not an implementation checklist and
does not by itself authorize runtime implementation.

Implementation checklist: [PBR.VolumetricClouds.todo](PBR.VolumetricClouds.todo).

The [realtime work checklist](PBR.CloudRealtime.todo) defines the follow-up bounded quality policy.
Its policy specifies uniform primary distance steps with blue-noise ray offsets and deterministic
midpoints of quadratic shadow intervals. It supersedes per-column sampling and PMJ requirements
for that future integrator and qualifies quarter-dimension tracing before changing the default here.
Exact arbitrary-column attenuation below remains a legacy/reference property; fixed-step production
integration uses algorithm-correctness tests and optional reference diagnostics. Automated tests do
not enforce approximation quality, including for detail-disabled discontinuities. The realtime
checklist records correctness qualification; appearance remains user-reviewed.
The current renderer retains its existing behavior until those implementation tasks are verified.
Native ownership, density support, physical inputs, OIT and deferred cast-shadow scope still apply.

The settled design uses separate VGE-owned Simple and Volumetric shaders, native mesh/column
geometry, roughly cubic volumetric clouds with restrained shader noise, and existing OIT composition.
Volumetric mode uses half-resolution tracing with temporal reconstruction and depth-aware upsampling,
a full-resolution history-free reference mode, and one additional scattering octave beyond single
scattering. Cast cloud shadows are deferred until virtual shadow maps. The details below express
these decisions and their implementation/validation constraints; routine tuning is not a separate
design-approval gate.

## Intended result

Replace native cloud drawing with a VGE-owned volumetric renderer while preserving the base
game's weather-dependent cloud distribution, column heights and bounds, altitude and movement.
Preserve roughly cubic cloud masses with modest fluff from 3D shader noise inside the native
column volume. Use ray-marched three-dimensional density, light transmittance, approximate
multiple scattering, scalable tracing and reconstruction, and integration with atmospheric lighting.
This is an independent implementation for VGE's rendering backend.

Simple mode retains native mesh geometry and weather-driven shape with a separate VGE-owned shader.
It preserves a cheaper block-shaped option without volumetric ray marching or 3D noise.

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

## Simple cloud rendering

Preserve the user's cloud modes: Off draws no clouds; Simple uses native mesh geometry with a
dedicated VGE-owned simple cloud shader; Volumetric uses the native column map with VGE-owned
ray marching and restrained fluff. Both visible cloud shaders use VGE atmospheric lighting,
scene-color storage and the existing OIT integration. Cast shadows remain deferred in both modes.

Retain native mesh generation, tile attributes, weather updates, placement and block-shaped
geometry. The VGE simple shader must evaluate the geometry's native shape controls while replacing
native lighting with VGE's lighting inputs; legacy brightness/color attributes must not be mistaken
for physical illumination. Simple shading does not require the volumetric density or noise model.

The approved handoff intercepts `CloudRenderer.OnRenderFrame` at its existing OIT order `0.35`
while VGE owns Simple rendering. Preserve the native Simple-mode check and call native `CloudTick`
exactly once when unpaused. After that call, obtain the current `cloudTilesMeshRef`,
`QuantityCloudTiles` and committed placement, submit the VGE draw, and suppress the original
callback body. When VGE relinquishes ownership, let the original callback run normally. Do not
unregister the callback, replace the native shader field, or reproduce native weather updates.

VGE owns both vertex and fragment shader stages. The borrowed mesh is an instanced cube template;
the vertex shader supplies much of its final shape. Preserve thickness-dependent height, tile
placement, undulation, curvature and neighboring-face coverage using the native input layout:

| Input | Contract |
| --- | --- |
| Vertex position, base color and face flags | Native cube template; base color is artistic metadata |
| Normalized tile offsets | Rescale by `32767` for native instance placement |
| Four neighboring thickness values | Shared-side visibility and fading |
| Own thickness and thin-cloud mode | Native height controls |
| Undulation mode and opaqueness | Movement and coverage controls |
| Cloud brightness | Legacy artistic metadata, not physical illumination |

Consume the existing instance stream of twelve shorts with a 24-byte stride without copying or
rebuilding the mesh. Supply matching projection/model-view transforms, tile offset, cloud extent,
altitude and animation inputs. Preserve the native perception effect through a shader-local input
equivalent to its factor of `20`, without temporarily mutating shared engine uniforms.

Replace native daylight/brightness multiplication, sunset coloring and precomputed fog coloration
with VGE lighting and fog treatment. Retain the Simple path's adjusted OIT depth, coverage behavior
and liquid-depth interaction. Map glow outputs explicitly to VGE's output contract rather than
interpreting native bloom/glow metadata as physical cloud lighting.

`CloudTick` can dispose and replace the mesh during a view-distance rebuild, so obtain the mesh
after ticking and do not cache it across callbacks. While paused, draw the retained mesh with current
camera transforms without ticking. VGE reloads its own shader independently; leave the native shader
field and reload subscription intact for restoration. On disable, remove the interception's ownership
so native behavior resumes according to the current setting. On world exit, release VGE resources
and borrowed references, leaving mesh disposal to the native owner. This handoff is source-backed;
runtime validation of lifecycle transitions and visual behavior remains required.

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
The proposed mapping preserves native optical thickness before detail is added. The installed
`cloudvolumetric.fsh` integrates `1 - exp(-segmentLength * map.r)` with segment length expressed
in units of 50 world units. Thus `map.r` is an extinction control per native tile unit, despite
its opacity name; it is not a coverage alpha and must not be clamped to one or converted with
`-log(1 - alpha)`. It already includes native edge fading and perception effects; do not apply
those factors twice.

Let `p = TextureMap.r`, `L = 50` world units for the inspected native producer/consumer pair,
and `u` be the renderer's explicit metres-per-world-unit conversion. For valid nonnegative finite
inputs, use:

```
sigmaBase = p / (L * u)                  // inverse metres
sigmaT(x) = sigmaBase * detail(x)        // inside the native volume; zero outside
Tsegment = exp(-sigmaT(x) * dsMetres)
```

For a complete span of uniform density with detail disabled (`detail = 1`), this reproduces native segment transmittance for the same
ray and column intersection, independent of lighting. For example, `p = 2` across 50 world units
gives `T = exp(-2)`, approximately 0.135. This is an optical-depth equivalence, not a promise of
identical output color or OIT reconstruction. The installed map and volume shaders hardcode 50;
do not silently substitute a changed CPU tile size without reconciling the producer's units.

For restrained interior variation, use a bounded noise value `n(x)` in `[0,1]` and
`detail(x) = (1 + a * (2*n(x) - 1)) * edge(x)`, with `0 <= a < 1` and
`edge(x)` in `[0,1]`. The exposed-edge factor is one in the interior and softens density inward
near the boundary of the connected cloud volume. Disable both variation and edge erosion for the
native-transmittance reference. Interior variation alone cannot excavate zero-density holes;
zero native extinction stays zero. Use the same density evaluation for view and light samples.

Noise amplitude, spatial scale and exposed-edge softness remain visual tuning parameters. A
centered noise multiplier does not guarantee preserved ray opacity, and edge erosion reduces
optical thickness. Do not claim exact coverage preservation or apply automatic density compensation.
Compare thin clouds and overcast scenes against the detail-disabled reference, keeping changes
modest enough to preserve weather intent and the approved cubic silhouette. Reject nonfinite or
negative map inputs rather than allowing invalid extinction into the transport calculation.

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
that order. Gate the native volumetric draw callback directly while VGE owns its replacement;
leave native registration and settings watchers intact. The retained map callback owns weather
ticking, tile commits and map generation;
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
draw; the callback ownership gate suppresses that draw whenever VGE owns the volumetric path,
including after setting changes, without patching the watcher. Off remains off. Simple uses the
separate VGE shader and native
mesh producer described above. Mode transitions must select exactly one visible cloud path and
preserve the selected producer's updates without forcing the user's setting to Volumetric.

View-distance changes can recreate the grid and textures during a native commit. Obtain the new
handles and dimensions after generation, invalidate affected VGE history, and never sample retired
resources. Keep native shader-reload subscriptions intact; re-establish map readiness after reload
and invalidate incompatible VGE history. Reload success must not be inferred from texture allocation.

On VGE disable, withdraw its draws and release native callback suppression. Native registration and
simple draw/shader behavior follow the current cloud setting, not a preference saved at activation.
Re-enable reacquires current owners and readiness.
Avoid permanent changes to user settings. On world exit, drop borrowed references and dispose only
VGE-owned resources; the native owner retains responsibility for its worker and resource teardown.
Prevent stale world identities or mixed-generation map metadata from reaching consumers. The callback
contract is source-backed; readiness detection and lifecycle transitions still require runtime validation.

## Density integration and lighting

March view rays through the bounded cloud volume, clipped against scene geometry, accumulating
linear in-scattered radiance and transmittance. Use extinction in inverse metres after an explicit
world-unit conversion, and dimensionless transmittance. Terminate optically saturated rays and
skip regions whose density bound proves they are empty.

Both cloud shaders use VGE's completed atmosphere publication for solar/lunar illumination and environmental response.
Respect the publication's units, source attenuation and generation identity; do not multiply already
attenuated lighting by the same atmospheric transmittance again. Cloud transport remains separate
from the atmospheric integrator, as required by
[PBR.Atmosphere.Publication.md](PBR.Atmosphere.Publication.md).

Evaluate directional single scattering and cloud self-shadowing, with one additional scattering
octave as the approved multiple-scattering approximation for Volumetric mode. This
additional octave approximates higher-order transport; it is not an explicit simulation of just
one extra physical bounce. Keep its cost bounded while providing softer, brighter thick-cloud
interiors. Exact contribution, attenuation, phase and energy-compensation coefficients are
implementation tuning choices informed by reference diagnostics and user appearance review. Native cloud
brightness, daylight and sunset tint are not substitutes for physical incident illumination.
The full-resolution reference mode uses this same lighting model. This decision does not add
volumetric transport to Simple mode or change the deferral of cast cloud shadows.

Shadows cast by clouds onto terrain, water, other scene receivers or atmospheric consumers are
deferred until virtual shadow maps are implemented. Do not introduce a separate Beer shadow map,
retained previous-frame cloud-shadow publication, or earlier map-generation schedule for those
consumers in this work. Their integration will be specified with the virtual-shadow-map work.

Light attenuation within the cloud volume remains part of cloud shading. Use bounded secondary
light marching toward contributing solar/lunar sources to
evaluate self-shadowing with the same density definition, including noise, as view-ray integration.
Light sample counts and stepping are implementation tuning details, not a separate design decision.
Visible attenuation
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

Apply the existing VGE atmospheric aerial-transport and local-fog composition contract once along
cloud contributions. Exclude native pre-lit colors and pre-applied fog, and verify the existing
consumer paths during implementation planning to avoid duplicate atmospheric attenuation or
weather-based dimming. This is integration verification, not an unresolved rendering-model choice.
Inside-cloud views must
integrate from the camera rather than treating clouds as a distant sky overlay. Reflection views
must use their own camera/depth contract and must not reuse main-view history blindly.

## Quality and temporal behavior

The approved default for Volumetric mode is half-resolution tracing (half the view width and
height), with temporal reconstruction and depth-aware upsampling to full resolution. Preserve
silhouettes, disocclusion and in-cloud movement, and reconstruct the cloud contributions needed by
the existing OIT contract rather than flattening them into a single depthless color overlay.

Provide a full-resolution, history-free reference mode for validation. It uses the same density,
lighting and OIT contracts while bypassing temporal accumulation and reduced-resolution upsampling.
It is a comparison mode, not a claim of converged or exact light transport. Simple mode remains
ordinary mesh rendering and does not use this volumetric reconstruction path.

Provide bounded controls for view/light sample counts and detail evaluation. Exact ray counts,
noise settings and additional quality presets are implementation tuning choices informed by
measurement, not prerequisites for this approved resolution/reconstruction strategy.

History must account for camera and cloud motion, weather changes, lighting changes, grid replacement,
resize and reload. Reprojection must distinguish a grid-coordinate change from actual cloud motion.
Reject incompatible history and bound stale contributions during rapid transitions. Do not assume
that history remains valid merely because a texture handle is unchanged.

Exact budgets remain open until measured on the target renderer. Count native
producer overhead, native uploads, noise and weather storage, trace targets
and history in the total memory and frame-time assessment.

## Acceptance and implementation qualification

Acceptance requires weather-coherent clear/thin/thick cloud scenes; moving sunlight and moonlight;
ground, in-cloud and above-cloud views; grid recentering and view-distance changes; pause and time
changes; fog, terrain, particles, water and reflection ordering; and clean disable/reload/world changes.
Validate roughly cubic silhouettes, modest fluff, preserved native heights/bounds and the absence
of artificial seams between connected columns. Validate both PBR modes. Focused automated checks can establish data, math and lifecycle contracts;
the user performs in-game visual acceptance. GPU timing and memory measurements are separate evidence.

Validate Off/Simple/Volumetric switching and VGE disable/re-enable, including shader reload and mesh
rebuild in Simple mode. Simple must retain the native block-shaped appearance and avoid duplicate
native/VGE draws. Qualify its rendering cost separately from the volumetric path.

Compare the default volumetric mode against the full-resolution, history-free reference during
camera/cloud movement, disocclusion, weather and lighting transitions, and history invalidation.
Measure the tracing, reconstruction and memory costs separately. Reference-mode captures and
performance qualification are explicit validation activities, not mandatory full-resolution work
in routine correctness checks.

Carry the following details into implementation planning and qualification rather than treating
them as additional design decisions:

- Verify the native optical-depth conversion against the installed producer/consumer units, and
  tune noise strength, scale and exposed-edge softness against the cubic appearance requirement.
- Verify existing atmosphere, local-fog, environmental and reflection consumers so physical inputs
  and attenuation are applied once, preserving the established OIT and storage contracts.
- Select bounded view/light stepping and temporal rejection parameters, and qualify finite-domain
  edges, disocclusion and lifecycle transitions against the reference modes.
- Record target hardware, resolution, reference scenes and measured GPU/memory costs. Establish
  performance acceptance budgets from explicit targets; do not invent unmeasured performance claims.

The complete proposal has been reviewed for internal logic and approved. Implementation planning
and runtime qualification remain separate; proposal approval does not establish implementation
correctness, visual acceptance or measured performance.

No completion claim follows from this proposal, and the parent requirement remains unchecked.

## Sources

- Installed Vintage Story 1.22.7 `VSEssentials.dll`: `FluffyClouds.CloudRendererMap`,
  `CloudRendererVolumetric`, `FluffyCloudsModSystem`, and the native weather reader/pattern classes.
- Installed `assets/game/shaders/cloudmap.fsh` and `cloudvolumetric.fsh`.
- VGE [SceneColorLegacyPatches.cs](../VanillaGraphicsExpanded/PBR/SceneColor/SceneColorLegacyPatches.cs)
  and [SceneColorCompatibilityPublication.cs](../VanillaGraphicsExpanded/PBR/SceneColor/SceneColorCompatibilityPublication.cs).

The native findings are version-specific. Reverify the relevant producer and registration contracts
when the installed game changes.
