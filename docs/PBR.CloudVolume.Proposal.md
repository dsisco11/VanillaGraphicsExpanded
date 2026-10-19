# Persistent cloud density and illumination volume

## Status and intent

Proposed standalone candidate; no implementation or tasklist is authorized by this document alone. This is a new design, separate from [the lighting-only candidate](PBR.CloudLightingVolume.Proposal.md) and its existing tasklist. Approval would establish the basis for a new implementation plan; existing completion records do not establish completion of this design.

Represent the clouds themselves as persistent three-dimensional density and illumination fields. Compute density when cloud content changes, amortize direct-light transport and multiple scattering across frames, and reuse those results for every view. Native weather supplies source content and placement; after volume publication, transport and visible geometry sample the stored density.

The intended benefit is to move procedural shape evaluation and lighting integration out of the per-pixel view loop. Cloud-space history should survive camera motion and ordinary day/night or weather evolution. Screen-space temporal accumulation is not a prerequisite for stable illumination.

The final visibility integral remains view-dependent. A density-and-lighting volume cannot directly supply a finished pixel for arbitrary viewpoints, depth occlusion and anisotropic scattering. This proposal therefore retains a lightweight texture-based view integration. Eliminating all per-pixel integration would require a separate representation/rendering decision; rasterizing widely separated slices is not the proposed solution.

## Ownership and integration

Keep the candidate self-contained under a new `PBR/Clouds/Volume/` domain and matching private `shaders/cloud_volume/` directory. Own density generation, storage, transport, scheduling and view integration there. Use the established GPU textures, shader programs, typed bindings, submission and barrier abstractions, including in tests.

Reuse the small native volumetric-consumer boundary, native weather publication, scene lighting admission, depth inputs and native OIT output contract. Do not depend on the existing renderer's transport, shadow cache, hierarchy or temporal reconstruction algorithms, or on the lighting-only candidate's resource owners. Reuse a helper only when its contract is genuinely independent of those algorithms.

Select one visible consumer at a safe lifecycle boundary. Selection retires the previous consumer before activating this one; no hidden background candidate runs during normal play. Existing renderer selection remains available for comparison. Reload, world/context teardown and selection changes release all candidate resources. Settings require no migration. Configuration that removes shader features selects compiled variants.

## Persistent representation

Use a bounded, shallow 3D volume in cloud-material coordinates, with independent horizontal and vertical spacing. The initial architecture uses dense textures and a coarse occupancy grid; sparse brick allocation and clipmaps are not prerequisites. Density and lighting initially share a lattice to keep their spatial identity and publication straightforward. Separate lighting resolution is a later measured optimization.

The volume covers the admitted finite native cloud domain and an explicit vertical extent containing its occupied support. Native column descriptors are inputs to density generation only. Rendering and light transport must not silently fall back to querying native procedural density outside the stored volume.

Grid placement is stable under camera motion. Wind is applied once through the material-to-world transform. Integer recentering retains overlapping material cells and initializes entering slabs. Prefer circular addressing to full-volume relocation. Hardware filtering must see logically adjacent texels across the circular seam; finite outer boundaries have empty guard texels and never wrap into the opposite edge of the weather domain. The address transform must distinguish these two cases explicitly.

An out-of-range height change requests a bounded domain replacement rather than silently clipping clouds. Freeze density publication for an affected change until its storage is admitted, retaining the last coherent volume with its age exposed. Large domain replacements and first activation require an explicit bounded initialization period; no claim of immediate complete clouds is made. Domain replacement failure follows the existing consumer ownership/recovery policy and cannot expose unrelated old texels.

### Density generation

Store extinction in inverse world metres in an `R16F` 3D texture. Generate it in compute from current native extinction, height bounds, connected cuboid faces and the chosen perturbation function. Procedural detail is evaluated during these updates, never during direct, diffuse or view sampling.

Rasterize occupied coverage into voxel values rather than merely testing whether each voxel centre lies inside a cuboid. Thin clouds intersecting a cell must contribute to its value. A cell's extinction represents its volume-averaged extinction; partial cuboid coverage can be calculated from cell/cuboid overlap, with a bounded generation-only quadrature for detail. This is a sampled approximation: it does not preserve an arbitrarily thin or sharp silhouette exactly.

Density updates have their own budget and priority, ahead of lighting updates that depend on them. Coalesce repeated descriptor changes, preserve pending age, and update neighboring cells influenced by connected-face/detail changes. Ordinary wind translation does not regenerate unchanged material content. Shape evolution does.

An update publishes a complete spatial region, including affected boundary cells, after its dispatch and barriers. Readers see the published density texture, never procedural content from a newer native snapshot. Gradual spatial refresh can temporarily combine regions of different ages; those ages are explicit. Incompatible domain or algorithm changes initialize a new coherent representation rather than mixing coordinate systems.

### Stored illumination

| Field | Proposed format | Meaning |
| --- | --- | --- |
| Density | R16F | Published extinction, including shape/detail |
| Direct visibility | RG16F | Solar and lunar transmission through published density |
| Diffuse source, two fields | RGBA16F | RGB isotropic scattered source, with alpha reserved for a specifically defined validity contract if needed |
| Region metadata | Integer buffers | Material identity, density/lighting revisions, pending age and update status |
| Coarse occupancy | Integer buffer or compact texture | Conservative nonempty blocks for skipping empty view/transport intervals |

Do not allocate per-voxel RGBA32F descriptors, redundant flag fields or relocation spares by default. Metadata belongs at region granularity unless a concrete lookup contract requires per-voxel state. Half-float storage does not require half-precision arithmetic: integration uses float32, and physical diffuse radiance uses a documented exposure-independent scale when needed for range. Alpha has no invented lighting meaning.

Use hardware trilinear filtering for continuous density and lighting. Query exact region identity separately. Every allocated texel contains a usable initialized value, so an unfinished lighting region has a defined coarse source rather than arbitrary memory. Validity distinguishes initialized approximations from refreshed illumination; it must not turn occupied density invisible. Avoid eight manual lighting fetches solely to reproduce trilinear filtering.

## Progressive transport

### Direct light

For selected occupied nodes, integrate the stored extinction toward each relevant light and retain transmission. Empty-space traversal uses the volume's occupancy structure. The transport boundary is the finite density domain, with explicit truncation if a bounded light-distance limit is selected. A truncated integral must not be described as a complete shadow ray.

Apply current light intensity, colour and view-dependent phase response when drawing. Intensity-only changes therefore do not require recomputing direct transmission. Direction changes enqueue refresh while retaining compatible old values. Inactive sources receive no work; activation prioritizes their coverage.

Density changes enqueue dependent light paths, not just changed occupied nodes. Begin with conservative domain-wide lighting demand if necessary, coalesced once per update; optimize influence regions only when the simpler policy's measured backlog warrants it. Publication timestamps must not imply that stale remote shadows match new density.

### Multiple scattering

Retain an RGB isotropic source approximation for transported environment light and higher-order scattering. Selected regions gather radiance along a bounded direction set through the stored density, using extinction, segment lengths and scattering albedo. The recurrence reads direct first-scattering illumination plus the previously published diffuse source. It is light transport, not repeated unweighted blurring of colour.

Each iteration reads a coherent field and writes a separate destination. Untouched values must survive publication. The initial simple design copies the published diffuse field before a partial batch, then overwrites selected regions and swaps only after completion. This full-field bandwidth cost is part of the candidate's cost and must be measured; it is not hidden in the update budget. A more elaborate per-region publication scheme requires evidence that the copy is a bottleneck.

The incoming environment is an explicit boundary condition and is included once. Empty voxels do not scatter, but rays may pass through empty gaps to another cloud. Directional single scattering is evaluated for the viewing direction from cached direct transmission; the diffuse field must not double-count that outgoing first-scattering contribution.

Specify the recurrence, units, boundary conditions and finite radiance bounds before implementation. Do not inherit the old candidate's direction counts, distance limits or iteration budgets simply because they already exist. This remains an approximate isotropic closure, not a full directional radiance field or a promise of physical convergence.

## Scheduling and history retention

Maintain separate bounded queues for density generation, direct visibility and diffuse refinement. Prioritize entering/changed density, newly relevant lighting, visible stale regions and background refinement; reserve fair service for offscreen work. A newer observation updates a pending job's target content without resetting its waiting age.

Publish successful work after the required barriers, with its actual source revision and update time. No dispatch reads and writes the same diffuse iteration concurrently. Density can be updated in place between ordered passes because no earlier-frame reader remains active; temporary resources are justified only by actual publication or iteration hazards. GPU-to-GPU evolution does not require a CPU transfer buffer.

Day/night, weather illumination, fog and exposure changes must not continuously clear cloud-space history. Current direct intensity responds immediately; diffuse energy refreshes progressively. Geometry changes update density and mark affected lighting stale. World changes, incompatible material coordinates, shader representation changes and layout replacement revoke incompatible history.

Initialize missing lighting to a finite, defined approximation, for example unshadowed direct transmission and albedo-scaled environment source. Retain compatible stale illumination while its refresh is pending. Sharp transitions between fallback and solved light are a risk to evaluate; any smoothing belongs to cloud-space update publication and must not require screen-space reprojection or blend across incompatible density identities.

Report actual work, oldest pending age, time since completed updates and fallback coverage. Work budgets do not guarantee a particular refresh latency, and persistent data does not guarantee that weather changes are visually immediate.

## Traversal acceleration candidates

Evaluate DDA (digital differential analyser) traversal and distance-field skipping for view rays, direct-light rays and diffuse transport rays. These accelerate traversal of the density volume; they do not replace extinction integration or the scattering model. The proposed renderer performs volumetric ray integration, not a general Monte Carlo path tracer.

**DDA is the first candidate for the coarse occupancy grid.** Traverse exact block entry/exit intervals, skip certified empty blocks, and integrate occupied intervals with the selected density sampling rule. Retain traversal state as the ray advances instead of repeatedly searching from the domain root. Voxel-level DDA is also an option, but visiting every empty voxel can cost more than traversing coarse empty blocks. Measure the useful level of subdivision rather than assuming finer traversal is faster.

**A signed distance field, or an unsigned empty-space distance field, is an alternative or complementary accelerator.** The useful quantity is a conservative lower bound on distance to potentially nonzero density. A distance to a visually selected isosurface is insufficient: it could skip low-density cloud material outside that surface. Distance skipping applies in certified empty space; occupied intervals still require volumetric integration. An unsigned bound may therefore supply the required benefit without storing an interior sign.

Distance bounds must account for anisotropic voxel dimensions, density's hardware-filtering footprint and distance quantization. Ordinary interpolated distance values are not automatically safe step lengths; specify a conservative reconstruction/error margin before using them to skip. Occupancy must likewise include density that filtering can spread across a cell boundary.

Publish acceleration metadata consistently with density. Newly occupied cells can invalidate distance bounds well beyond the edited region: revoke affected certificates or fall back to conservative DDA/no skipping until their bounds have been repaired. Removing density may retain older, smaller safe skip distances. Neither stale occupancy nor a stale distance field may certify new cloud material as empty. Domain remapping and finite boundaries obey the same material-identity rules as density.

Compare coarse DDA, distance skipping and a possible hybrid using total construction/update plus traversal cost, auxiliary memory and weather-change behavior. Do not commit to building both acceleration structures. Keep their ownership private to the candidate, and preserve actual occupied segment lengths and optical integration when switching traversal strategies.

## Rendering the published volume

Intersect the view ray with the published volume and current scene depth, including particle visibility and liquid clipping. Traverse conservative empty blocks without changing occupied integration semantics. In occupied intervals, sample stored density, direct visibility and diffuse source, then perform front-to-back extinction and radiance integration. Native descriptors and procedural noise are absent from this shader path.

Choose step spacing from voxel dimensions and ray direction. An initial deterministic rule limits each step's movement to at most half a voxel along every grid axis:

`step <= 0.5 / max(abs(direction.x)/cellSize.x, abs(direction.y)/cellSize.y, abs(direction.z)/cellSize.z)`.

Clip intervals at domain, depth and skipped-block boundaries, and use their actual lengths in `alpha = 1 - exp(-extinction * lengthMetres)`. Midpoint quadrature remains approximate, but the view no longer uses a low layer-distance sample cap disconnected from the stored spatial frequency. A safety iteration limit may protect execution; it must not silently increase spacing or truncate ordinary occupied rays. Domain, resolution and traversal bounds must be admitted together so normal supported rays fit.

No evolving blue-noise sequence or screen-space temporal accumulation is required by this baseline. Persistent lighting alone does not cure spatial undersampling; the voxel-relative view rule is essential. Hardware filtering reduces visible cell transitions but cannot restore detail absent from the density texture.

Use full-resolution direct OIT submission for the first correctness evaluation, explicitly charging its pixel cost. Reduced-resolution rendering with spatial reconstruction may be considered separately if required for performance; it must not be presumed free or inherited from the old renderer. Compare matched resolutions before drawing architectural performance conclusions.

Apply current directional phase functions to cached transmission, add the retained diffuse source once, and integrate in physical units. Apply the established atmosphere/local-medium treatment and scene encoding once, preserving both PBR modes and all six native OIT attachment meanings. Preserve independent camera and atmospheric calibration identities. Retained lighting bounds must cover the older energy still present in the volume, not only the current sky intensity.

## Resolution and memory are design constraints

Cuboid styling makes axis-aligned structure plausible, but coarse voxels still round silhouettes under filtering, erase thin layers and leak light. Full-domain resolution is therefore a central decision rather than an incidental setting.

The base field payload above is 22 bytes per voxel: 2 density + 4 direct + 16 diffuse ping-pong. Illustrative allocations, excluding guards, metadata, occupancy and driver overhead:

| Grid dimensions, X/Y/Z | Base payload | Horizontal spacing across 10,000 world units |
| --- | ---: | ---: |
| 256 × 64 × 256 | 88 MiB | 39.1 units |
| 512 × 64 × 512 | 352 MiB | 19.5 units |
| 1024 × 64 × 1024 | 1,408 MiB | 9.8 units |

These are not approved defaults. At 50-unit native column width, the first option has very little horizontal shape resolution. Across an 800-unit vertical extent, 64 cells are 12.5 units tall; they cannot preserve two-unit edge detail. The old lighting-only allocation and memory ceiling cannot simply carry over.

Before implementation planning fixes constants, select a memory ceiling, domain extent and density resolution together, and record the resulting smallest representable cloud feature. If affordable dense storage cannot represent the intended clouds, revise the layout toward sparse occupied bricks or multiple coverage levels before building a full solver. Do not compensate by reintroducing procedural density into every view sample, silently shrinking visible coverage, or claiming cuboid style makes resolution irrelevant.

## Evaluation and acceptance

Measure density generation, direct updates, diffuse transport, diffuse copy/publication, occupancy maintenance and final view integration separately, alongside total cloud GPU time. Include static views, camera motion, wind, evolving weather, source transitions, startup and domain recentering. Distinguish steady-state amortization from update spikes and stale-light delay. Keep the game running or closed according to the user's instruction and disclose GPU contention.

Use focused tests for declared math, finite values, resource lifetimes, region identity, publication ordering, work bounds, density use across all consumers and OIT contracts. Do not introduce approximation-quality, convergence, image-difference or noise thresholds. Visual shape, stability, leakage, stale illumination and acceptable resolution are user-reviewed results; passing a headless test does not establish them.

Implementation iterations use cached incremental builds and affected checks through subagents. Reuse unchanged passing evidence. Full comparison matrices and canonical packaging belong to a frozen candidate's final qualification, not every edit. Never launch the game for appearance acceptance.

The candidate succeeds only if it delivers genuinely stored volumetric geometry and reusable lighting, acceptable user-reviewed appearance and measured total cost at an explicit memory budget. Replacing procedural arithmetic with texture fetches is an architectural change, not an automatic performance guarantee.

## Decisions needed before a tasklist

- Agree the resident memory ceiling and the smallest cloud feature worth preserving; use these to select full-domain dense dimensions or revise the storage layout.
- Specify density coverage/detail generation, volume boundary behavior, transport recurrence and finite encoding bounds without importing unrelated old sampling constants.
- Select density/direct/diffuse work limits and initial view configuration, with explicit stale-data and initialization behavior.
- Select the initial acceleration strategy after accounting for coarse DDA and distance-field maintenance costs, conservative filtering bounds and density-change publication.

Approval of this proposal precedes extrapolation into a tasklist or runtime changes. The older candidate remains separately selectable until an explicitly approved replacement/removal decision.
