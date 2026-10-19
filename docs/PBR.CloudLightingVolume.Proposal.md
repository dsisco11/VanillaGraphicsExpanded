# Persistent cloud lighting volume candidate

## Status and purpose

Proposed design for discussion; no implementation or replacement of the current renderer is approved by this document. An implementation tasklist follows only after design approval.

The candidate would retain cloud-space illumination in a three-dimensional GPU volume and progressively update it with compute shaders. Many view samples and successive frames could reuse that illumination. The experiment asks whether this reduces total cloud rendering cost and improves lighting stability at an acceptable memory cost and update delay.

The candidate must be easy to remove. It owns its rendering algorithm, resources, settings and shaders independently of the existing volumetric renderer. It borrows the established native weather/input and scene-rendering contracts through a small integration boundary.

The initial design is a persistent **lighting** volume with current native column geometry remaining authoritative. It does not require a complete persistent density texture. Density and lighting may eventually use different resolutions; adopting full density storage is a separate decision supported by measurements.

## Relationship to current rendering

[Cloud rendering](PBR.Clouds.md) describes the active renderer. [Realtime cloud work](PBR.CloudRealtime.todo) governs that renderer's existing work. This proposal neither marks those tasks complete nor silently changes their contracts.

The candidate deliberately differs in two areas: illumination can come from compatible earlier updates, and multiple scattering uses a progressive diffuse approximation rather than the existing additional scattering octave. Its retained lighting has explicit age and compatibility information. It must not be presented as a same-frame shadow result or as mathematically equivalent transport.

Native weather remains the density source. Native columns are 50 world units wide and supply extinction and lower/upper height bounds. The candidate preserves finite map support, physical units, connected cuboid faces, placement and wind anchoring, both PBR selections, foreground/liquid clipping and the native six-attachment OIT composition contract.

A persistent lighting volume does not store the final image. The candidate still integrates extinction and illuminated source along the current view. Camera direction determines the directional scattering response; scene depth, aerial perspective and OIT weights are also view-dependent. Those remain in the view renderer.

## Isolation and integration boundary

Candidate runtime code belongs under `PBR/Clouds/LightingVolume/`. Candidate shader roots and private includes belong under a matching `shaders/cloud_lighting_volume/` asset directory. Tests belong in a corresponding focused fixture area. Each file owns one named responsibility; the renderer coordinates owners rather than implementing their algorithms.

| Candidate owner | Responsibility |
| --- | --- |
| Renderer | Admit inputs, coordinate bounded updates, integrate the view and submit OIT output |
| Domain | Define cloud-material coordinates, grid extent and retained-region identity |
| Storage | Own textures, iteration buffers, validity metadata and resource lifetime |
| Scheduler | Select regions for direct-light and scattering updates under fixed work budgets |
| Direct-light solver | Compute directional transmission through cloud density |
| Scattering solver | Refine the diffuse multiple-scattering approximation |
| Programs and parameters | Own typed shader bindings and candidate-specific constants/options |

Names above describe responsibilities, not a requirement for a generic framework or a class for every dispatch. Production GPU texture, buffer, program, binding, command submission, barrier and cleanup abstractions remain shared infrastructure. Tests use those same abstractions.

The dependency direction is candidate -> native input/public scene contracts and general rendering infrastructure. Existing transport, reconstruction, temporal history, hierarchy and shadow-cache owners must not depend on candidate types. The candidate does not call into `CloudVolumeReconstruction`, `CloudShadowCache` or the existing transport shaders to perform its rendering.

The expected integration surface is narrow:

- A developer-selectable renderer choice at native volumetric consumer preparation in `CloudNativeRuntime`. The default remains the existing renderer during evaluation. Off and Simple keep their existing meanings.
- If necessary, one small cloud-consumer lifecycle interface exposing preparation, reset and disposal in addition to the existing renderer callback. It exists only to let the native owner manage either consumer; it does not abstract algorithms or intermediate textures.
- Candidate shader registration/build discovery and a separate candidate settings/debug registration. User settings that eliminate shader paths select actual compiled options.
- Existing scene-lighting admission and depth demand continue to observe the selected volumetric owner. They do not require candidate-specific render branches.

Only one volumetric consumer may own visible drawing. Selection changes retire the old consumer and its callbacks before admitting the replacement at a safe lifecycle boundary. Selection and enablement are change-driven. The candidate must not run invisibly alongside the normal renderer during ordinary play.

Initial preparation failure leaves native drawing available according to the existing ownership policy. Once admitted, failed work withdraws candidate publication; it does not trigger a second renderer halfway through a frame. Reload, explicit selection or ordinary ownership recovery controls retry. No settings migration is needed.

Native input hooks and weather ticking are reused, not duplicated. Current native map textures are borrowed only for synchronous work with matching publication certificates. After dispatch completion, the candidate retains its own computed results and metadata, never a previous-frame native texture borrow.

## Domain, resolution and geometry

Use one bounded, shallow, axis-aligned lighting grid covering the admitted native horizontal domain and a configured vertical cloud-layer band. Choose horizontal and vertical spacing independently. Do not allocate a cubic grid simply because the resource is a 3D texture.

Grid placement is stable in cloud-material coordinates. Camera movement and integer native recentering must not change the identity of an unchanged cloud location. Fractional wind changes the world-to-material transform once. A whole-cell grid shift retains overlapping texels through explicit region remapping and initializes newly admitted slabs. The initial design uses a dense grid with bounded dimensions; sparse bricks, clipmaps and multiple resolution levels are outside this candidate's initial scope.

Horizontal coverage follows the finite native map. Vertical allocation has a stable margin rather than resizing to every animated height bound. Density outside an admitted lighting region still renders using the defined coarse lighting fallback; the candidate must not silently clip the cloud to its lighting allocation. Reallocation or a large displacement initializes a new domain and revokes incompatible history.

View integration evaluates native support explicitly. Hardware filtering of lighting must not create density outside native cuboids. Lighting may be smoother than geometry. Thin clouds that fall between lighting nodes require the coarse fallback or an explicitly represented occupied cell; an uninitialized interpolated value is never valid illumination.

For the initial candidate, density queries use private shader logic implementing native support and bounded procedural detail. They do not import the current renderer's transport include graph. A small amount of isolated density arithmetic is acceptable for this experiment; any future extraction of shared helpers needs a concrete stable contract rather than a general refactor prerequisite.

This preserves sharp support without requiring a lighting texel to resolve the current two-world-unit edge softness. Cuboid styling helps tolerate coarse illumination, but does not excuse light leakage, disappearing thin features or visible grid motion. Those remain user-reviewed approximation tradeoffs.

## Stored quantities and filtering

Store linear physical lighting quantities, independent of exposure and final scene encoding:

- Direct transmission for solar and lunar sources, in separate scalar channels. Current source color/intensity is applied at view time. A direction change creates update demand; an intensity-only change does not invalidate direct transmission.
- An RGB isotropic scattered-source term representing the retained diffuse multiple-scattering approximation. It excludes the directional single-scattering response evaluated during drawing.
- Region identity, validity, content observation and age metadata. Metadata uses exact integer access; it is not interpolated as lighting.

The precise format selection is an implementation design decision bounded by a declared memory budget. A plausible starting point is RG16F direct transmission and RGBA16F diffuse source, with separate coarse-region metadata. Iterative scattering requires distinct readable and writable storage. Allocation accounting must include both buffers, initialization storage, metadata and view targets.

Use hardware trilinear sampling of lighting within valid regions. Do not implement ordinary trilinear interpolation with eight manual texture fetches. Validity must cover the complete filtering footprint: cells next to an invalid region use initialized coarse values until the required footprint is valid. Filtering must not mix unrelated wrapped regions at grid boundaries.

An illustrative 16-byte resident payload per voxel costs 32 MiB at 256x256x32, 256 MiB at 512x512x64, and 2 GiB at 1024x1024x128. These figures are payload arithmetic, not complete allocation estimates or selected defaults. For a fixed world extent, doubling each dimension multiplies work and storage by eight.

The candidate starts with no lighting mip chain. Adding levels would need explicit boundary and validity semantics; implicit derivatives inside divergent integration loops are not an acceptable substitute for that design.

## Direct illumination and progressive scattering

Direct-light compute work evaluates bounded density integrals from selected lighting nodes toward the relevant sources. Store transmission, not a final view color. Zero-intensity sources need no new work; when a source becomes relevant, prioritize its direct-light coverage and use the initialized fallback until compatible data exists.

The first candidate uses bounded deterministic integration for direct illumination. It does not accumulate jittered direct-light estimates indefinitely across changing density. The scheduler amortizes the spatial work across frames. This makes its principal tradeoff stale illumination rather than screen-space sampling noise.

The multiple-scattering candidate uses a diffuse, isotropic closure: the directional first-scattering source seeds transport, and subsequent orders are approximated as an angular average. A bounded fixed direction set and bounded ray segments gather previously published source radiance through the density field. Direction weights are nonnegative and normalized. Segment contributions include extinction and scattering albedo with physical path lengths; repeated unweighted texture blur is not the solver.

The source gathered along a ray consists of directional first scattering evaluated for that ray's outgoing direction plus the retained diffuse higher-order source. The local angular average produces the updated higher-order term. This avoids adding the same first-scattering term again during final view composition. Empty space contributes no scattering but does not terminate transport between occupied regions. The finite domain has an explicit incoming environment boundary; it is not a wrapping light-transport domain.

The exact direction set, segment schedule and scattering parameters must be specified before implementing this solver. They are approximation choices to evaluate, not claims of physical convergence or a replacement for a full directional radiance distribution.

Each iteration reads a coherent published source field and writes separate storage. Untouched regions retain their prior values when the next field is published. The required copy or per-region publication mechanism is included in measured cost; partial updates must not swap in uninitialized or older untouched texels. Image-write and texture-read barriers follow existing GPU abstractions. No invocation observes another invocation's partially written iteration as completed history.

## Scheduling, changes and retained history

Use bounded work budgets for direct-light nodes, scattering nodes and integration steps. Updating one eighth of the nodes each frame gives an eight-frame revisit interval, approximately 133 ms at 60 FPS, before additional scattering iterations. Budget and maximum observed age are reported separately; a fixed work budget does not guarantee a fixed refresh latency under sustained changes.

Priority is newly admitted regions, incompatible/changed geometry, newly relevant direct sources, visible old regions, then background refinement. Include an age-based fairness rule so offscreen regions cannot starve indefinitely. The scheduler tracks backlog and observed age rather than discarding pending work when a new native publication arrives.

Native generation identity is not a reason to clear every lighting texel. The candidate keeps its own compact previous column descriptors and compares current support/extinction on the GPU. Ordinary smoothly changing descriptors enqueue refresh while retaining compatible lighting; large support changes invalidate affected regions and initialize coarse values. Define classification in physical terms before implementation. A content version alone is not proof that geometry is unchanged.

Density changes can alter illumination outside the changed column. Direct-light update demand extends through the configured shadow-influence distance down-light; diffuse demand includes the solver's propagation support. Broad changes may enqueue the whole domain, while frame work remains bounded. Changed geometry and its dependent lighting must never be certified fresh merely because only local density was updated.

Sun/moon direction changes refresh directional visibility. Weather, atmospheric intensity and sky color changes refresh diffuse lighting progressively. They do not discard all history by default. Retained diffuse radiance can lag these inputs; arbitrary RGB intensity ratios must not be used to pretend a mixed-source diffuse field updates exactly.

World/context replacement, incompatible resource layout, shader transport changes and nonoverlapping domain replacement discard incompatible storage. Resize changes view targets without automatically clearing a compatible cloud-space solution. Pause stops weather advancement through the native owner; retained compatible illumination can still be refined under the configured policy.

New or invalid regions use current unshadowed direct illumination and the current coarse environment term, with no retained higher-order contribution. This explicit fallback can look too bright until updated. Transitioning to valid results may be smoothed locally, but invalid geometry/history is never blended into a new region. Fallback coverage, age and transition behavior must be inspectable in the existing debug visualizer menu.

## View rendering and temporal behavior

The candidate owns a minimal view integrator and output path. It clips the current camera ray to native support and current scene depths, evaluates density, fetches cached lighting, applies current directional phase response and integrates attenuation/radiance. It then applies the existing atmosphere/underwater and native OIT conventions through public scene contracts or narrowly scoped private adapters.

It does not adopt the existing volumetric reconstruction owner, shadow cache, history textures or its settings record. Candidate view sampling and resolution controls are explicit. Shared camera, atmosphere publication, lighting calibration and encoding helpers remain reusable infrastructure.

The initial view path uses deterministic sampling and no screen-space accumulation, so qualification can reveal whether persistent illumination alone is sufficiently stable. A reduced-resolution path may use a candidate-owned spatial depth-aware resolve. Its sampling artifacts, edge quality and cost are measured rather than hidden behind an inherited temporal filter. A later temporal reconstruction decision requires separate justification.

This proposal does not promise elimination of view integration. Slice rasterization or cell rasterization would still integrate many depth contributions and introduce ordering/overdraw tradeoffs. Those are separate experiments, not additional required renderer paths in this candidate.

## Evaluation and removal criteria

Evaluate complete cost: native observation/descriptor comparison, domain maintenance, direct updates, scattering updates, iteration publication/copying, view integration, spatial resolve, CPU submission and total resident allocation. Include warm stable views, moving camera/wind, evolving weather, low sun, source transitions and cold initialization. Report update backlog and lighting age alongside frame timings.

Compare the existing renderer and candidate with matched cloud inputs, extent and declared settings. Record differences in approximation and view resolution. A faster converged warm frame does not qualify initialization spikes or persistent update backlog. No performance target or default quality setting is selected by this proposal.

Automated checks cover specified arithmetic, units, finite values, native support, region mapping, bounded scheduling, source separation, read/write hazards, lifecycle and OIT contracts. They must not enforce approximation accuracy, convergence, noise, image similarity or appearance. Reuse focused fixtures and actual GPU program abstractions; avoid a large duplicated test catalog. Build/test execution uses subagents, cached incremental builds and affected checks. Full packaging and broad measurements belong to final candidate qualification. The user runs visual acceptance; tooling does not launch or close the game.

All candidate resources are released on retirement. Returning to the existing renderer restores its unchanged settings and rendering path. Removal consists of deleting the candidate directories, candidate registrations/settings/debug entries and the single selection integration. No existing cloud shader should contain a candidate algorithm branch, and no existing cloud resource should require candidate state.

The design is ready for an implementation plan only after agreement on this isolated lighting-volume scope. Before implementation, resolve the initial world extent/resolution/memory budget, direct/scattering work budgets, diffuse quadrature and stale-data transition policy. Those choices must be concrete without expanding the candidate into a general volume-rendering framework.
