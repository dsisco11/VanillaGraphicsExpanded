# Persistent cloud density and illumination volume

## Status and intent

Stored-volume renderer specification, planned in [PBR.CloudVolume.todo](PBR.CloudVolume.todo). The selected contract below governs the production volumetric renderer; completion records alone do not establish live performance or temporal acceptance.

Represent the clouds themselves as persistent three-dimensional density and illumination fields. Compute density when cloud content changes, amortize direct-light transport and multiple scattering across frames, and reuse those results for every view. Native weather supplies source content and placement; after volume publication, transport and visible geometry sample the stored density.

The intended benefit is to move procedural shape evaluation and lighting integration out of the per-pixel view loop. Cloud-space history should survive camera motion and ordinary day/night or weather evolution. Screen-space temporal accumulation amortizes view sampling; stored illumination remains independent of that view history.

The final visibility integral remains view-dependent. A density-and-lighting volume cannot directly supply a finished pixel for arbitrary viewpoints, depth occlusion and anisotropic scattering. This proposal therefore retains a lightweight texture-based view integration. Eliminating all per-pixel integration would require a separate representation/rendering decision; rasterizing widely separated slices is not the proposed solution.

## Ownership and integration

Keep the renderer self-contained under a new `PBR/Clouds/Volume/` domain and matching private `shaders/cloud_volume/` directory. Own density generation, storage, transport, scheduling and view integration there. Use the established GPU textures, shader programs, typed bindings, submission and barrier abstractions, including in tests.

Reuse the small native volumetric-consumer boundary, native weather publication, scene lighting admission, depth inputs and native OIT output contract. Keep native weather and general GPU dependencies separate from volume transport. Use the cloud camera/material reprojection and generic blue-noise helpers for the view. Reuse a helper only when its contract is genuinely independent of those algorithms.

Use one Volumetric consumer at the existing native mode boundary. Off and Simple retain their native
mode semantics. Reload, world/context teardown and mode release retire owned resources. Settings
require no migration. Configuration that removes shader features selects compiled variants.
## Persistent representation

Use a bounded, shallow 3D volume in cloud-material coordinates, with independent horizontal and vertical spacing. The initial architecture uses dense textures and a coarse occupancy grid; sparse brick allocation and clipmaps are not prerequisites. Density and lighting initially share a lattice to keep their spatial identity and publication straightforward. Separate lighting resolution is a later measured optimization.

The volume covers the admitted finite native cloud domain and an explicit vertical extent containing its occupied support. Native column descriptors are inputs to density generation only. Rendering and light transport must not silently fall back to querying native procedural density outside the stored volume.

Grid placement is stable under camera motion. Wind is applied once through the material-to-world transform. Integer recentering retains overlapping material cells and initializes entering slabs. Prefer circular addressing to full-volume relocation. Exact cell indexing must preserve logical adjacency across the circular seam; finite outer boundaries have empty guard texels and never wrap into the opposite edge of the weather domain. The address transform must distinguish these two cases explicitly.

An out-of-range height change requests a bounded domain replacement rather than silently clipping clouds. Freeze density publication for an affected change until its storage is admitted, retaining the last coherent volume with its age exposed. Large domain replacements and first activation require an explicit bounded initialization period; no claim of immediate complete clouds is made. Domain replacement failure follows the existing consumer ownership/recovery policy and cannot expose unrelated old texels.

### Density generation

Store extinction in inverse world metres in an `R16F` 3D texture. Generate it in compute from current native extinction, height bounds, connected cuboid faces and the chosen perturbation function. Procedural detail is evaluated during these updates, never during direct, diffuse or view sampling.

Rasterize occupied coverage into voxel values rather than merely testing whether each voxel centre lies inside a cuboid. Thin clouds intersecting a cell must contribute to its value. A cell's extinction represents its volume-averaged extinction; partial cuboid coverage can be calculated from cell/cuboid overlap, with a bounded generation-only quadrature for detail. This is a sampled approximation: it does not preserve an arbitrarily thin or sharp silhouette exactly.

Density updates have their own budget and priority, ahead of lighting updates that depend on them. Coalesce repeated descriptor changes, preserve pending age, and update neighboring cells influenced by connected-face/detail changes. Bound changed-column demand vertically by the old/new occupied heights, including intersecting cells and a one-texel margin; departing columns use their old support and entering columns their new support. Connected-face neighbors remain included horizontally. Ordinary wind translation does not regenerate unchanged material content. Shape evolution does. Once a completed summary proves the pending density queue empty, skip selection/generation/occupancy/completion dispatches until another observation adds possible demand. Empty-priority GPU stages return uniformly without changing fair selection.

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

Treat each stored density, direct-visibility and diffuse-source texel as constant throughout its physical voxel. Fetch the crossed cell by exact texel address once per interval, without trilinear density/lighting reconstruction. Query exact region identity separately. Every allocated texel contains a usable initialized value, so an unfinished lighting region has a defined coarse source rather than arbitrary memory. Validity distinguishes initialized approximations from refreshed illumination; it must not turn occupied density invisible. Screen-space reconstruction retains hardware bilinear filtering; it is separate from the piecewise-constant volume model.

## Progressive transport

### Direct light

For selected occupied nodes, integrate the stored extinction toward each relevant light and retain transmission. Empty-space traversal uses the volume's occupancy structure. The transport boundary is the finite density domain, with explicit truncation if a bounded light-distance limit is selected. A truncated integral must not be described as a complete shadow ray.

Apply current light intensity, colour and view-dependent phase response when drawing. Intensity-only changes therefore do not require recomputing direct transmission. Direction changes enqueue refresh while retaining compatible old values. Inactive sources receive no work; activation prioritizes their coverage.

Density changes enqueue dependent light paths, not just changed occupied nodes. Begin with conservative domain-wide lighting demand if necessary, coalesced once per update; optimize influence regions only when the simpler policy's measured backlog warrants it. Publication timestamps must not imply that stale remote shadows match new density.

### Multiple scattering

Retain an RGB isotropic source approximation for transported environment light and higher-order scattering. Selected regions gather radiance along a bounded direction set through the stored density, using extinction, segment lengths and scattering albedo. The recurrence reads direct first-scattering illumination plus the previously published diffuse source. It is light transport, not repeated unweighted blurring of colour.

Each iteration reads a coherent field and writes a separate destination. Untouched values survive publication by repairing the spare from the preceding successful batch's bounded physical block list before the next gather. Both fields start equal; initialization and recenter maintenance write both. A gather changes at most 128 blocks, so repairing those blocks restores equality everywhere before the next partial overwrite. Gather rays still read one coherent old texture across all sampled blocks. A scale change, a spare with an older scale, or an untracked refinement requires a full copy/re-encode instead. Drawable snapshot publication remains separate and isolated from these working fields.

The incoming environment is an explicit boundary condition and is included once. Empty voxels do not scatter, but rays may pass through empty gaps to another cloud. Directional single scattering is evaluated for the viewing direction from cached direct transmission; the diffuse field must not double-count that outgoing first-scattering contribution.

Specify the recurrence, units, boundary conditions and finite radiance bounds before implementation. Select direction counts, distance limits and iteration budgets from the declared transport contract. This remains an approximate isotropic closure, not a full directional radiance field or a promise of physical convergence.

## Scheduling and history retention

Maintain separate bounded queues for density generation, direct visibility and diffuse refinement. Prioritize entering/changed density, newly relevant lighting, visible stale regions and background refinement; reserve fair service for offscreen work. A newer observation updates a pending job's target content without resetting its waiting age.

Publish successful work after the required barriers, with its actual source revision and update time. No dispatch reads and writes the same diffuse iteration concurrently. Working density can be updated in place between ordered update passes; rendering reads a disjoint completed snapshot. Temporary resources are justified by publication or iteration hazards. GPU-to-GPU evolution does not require a CPU transfer buffer.

Day/night, weather illumination, fog and exposure changes must not continuously clear cloud-space history. Current direct intensity responds immediately; diffuse energy refreshes progressively. Geometry changes update density and mark affected lighting stale. World changes, incompatible material coordinates, shader representation changes and layout replacement revoke incompatible history.

Initialize missing lighting to a finite, defined approximation, for example unshadowed direct transmission and albedo-scaled environment source. Retain compatible stale illumination while its refresh is pending. Sharp transitions between fallback and solved light are a risk to evaluate; any smoothing belongs to cloud-space update publication and must not require screen-space reprojection or blend across incompatible density identities.

Report actual work, oldest pending age, time since completed updates and fallback coverage. Work budgets do not guarantee a particular refresh latency, and persistent data does not guarantee that weather changes are visually immediate.

## Traversal acceleration candidates

Evaluate DDA (digital differential analyser) traversal and distance-field skipping for view rays, direct-light rays and diffuse transport rays. These accelerate traversal of the density volume; they do not replace extinction integration or the scattering model. The proposed renderer performs volumetric ray integration, not a general Monte Carlo path tracer.

**DDA is the first candidate for the coarse occupancy grid.** Traverse exact block entry/exit intervals, skip certified empty blocks, and integrate occupied intervals with the selected density sampling rule. Retain traversal state as the ray advances instead of repeatedly searching from the domain root. Voxel-level DDA is also an option, but visiting every empty voxel can cost more than traversing coarse empty blocks. Measure the useful level of subdivision rather than assuming finer traversal is faster.

**A signed distance field, or an unsigned empty-space distance field, is an alternative or complementary accelerator.** The useful quantity is a conservative lower bound on distance to potentially nonzero density. A distance to a visually selected isosurface is insufficient: it could skip low-density cloud material outside that surface. Distance skipping applies in certified empty space; occupied intervals still require volumetric integration. An unsigned bound may therefore supply the required benefit without storing an interior sign.

Distance bounds must account for anisotropic voxel dimensions, the selected density support footprint and distance quantization. Ordinary interpolated distance values are not automatically safe step lengths; specify a conservative reconstruction/error margin before using them to skip. Occupancy must likewise include every nonzero constant cell.

Publish acceleration metadata consistently with density. Newly occupied cells can invalidate distance bounds well beyond the edited region: revoke affected certificates or fall back to conservative DDA/no skipping until their bounds have been repaired. Removing density may retain older, smaller safe skip distances. Neither stale occupancy nor a stale distance field may certify new cloud material as empty. Domain remapping and finite boundaries obey the same material-identity rules as density.

Compare coarse DDA, distance skipping and a possible hybrid using total construction/update plus traversal cost, auxiliary memory and weather-change behavior. Do not commit to building both acceleration structures. Keep their ownership private to the renderer, and preserve actual occupied segment lengths and optical integration when switching traversal strategies.

## Rendering the published volume

Intersect the view ray with the published volume and current scene depth, including particle visibility and liquid clipping. Traverse conservative empty blocks without changing occupied integration semantics. In occupied intervals, sample stored density, direct visibility and diffuse source, then perform front-to-back extinction and radiance integration. Native descriptors and procedural noise are absent from this shader path.

Traverse exact physical voxel entry/exit intervals with retained DDA state, skipping conservative empty blocks. Density and cell-centred direct/diffuse lighting are constant within each voxel. Integrate its clipped length using `alpha=1-exp(-sigma*lengthMetres)` and `radiance+=T_before*alpha*source`; update transmission once per cell. This is closed-form transport for the selected constant-cell model, not an exact reconstruction of unresolved subcell geometry. Partial first/last voxels, tied crossings, anisotropic spacing, circular seams and scene-depth clipping retain their actual lengths. Each crossed occupied cell produces one interval.

Voxel boundaries can appear harder and cached lighting can change abruptly between cells; the selected coarse shape requires user-run appearance review. There is no automated approximation-quality gate. Bucket responses still use each cell interval's near endpoint and the atmospheric/local medium is still evaluated at the contribution-weighted representative distance; those depth-dependent effects remain approximations. All three bucket products and six native outputs remain. Reduced views use blue-noise distributed spatial sampling and temporal reprojection; full-resolution reference bypasses both. Exact occupied-cell intervals are unchanged.

The view obeys the shared cloud resolution controls: FullResolutionReference selects divisor1, otherwise TraceScale selects divisor2 (half) or4 (quarter, the default). Stage applied settings through the existing change-driven configuration boundary. Resolution changes retain compatible density and lighting. Compile the divisor as a structural shader option; full resolution draws native OIT directly with no spatial reconstruction or intermediate targets. Reduced views trace at ceil(viewport/divisor) with exact occupied-cell intervals, then reconstruct at full resolution. Reduced view history retains compatible physical integration results across frames. Compare complete trace-plus-reconstruction cost separately from unchanged-setting arithmetic savings.

Each reduced pixel stores one RGBA32F integrated premultiplied radiance/opacity value, one RGB32F three-bucket transmission value and one RGBA32F clip-depth/completion/representative-distance/receipt record. This compact layout is the implementation, with no expanded-output alternative or runtime selector. Clip to the minimum material/particle/liquid projection depth over the corresponding two-by-two (half) or four-by-four (quarter) full-resolution footprint using hardware depth gathers. Full resolution clips only its own pixel. Completion zero is invalid, one complete with contribution, two complete with exactly zero weight. Initialize neutral compact values and invalid metadata before tracing; a failed prefix never becomes a valid sample.

Use exact metadata for the four filter-support texels: hardware bilinear filtering is admitted only when all are valid and none extends behind the destination's scene depth. Otherwise select the nearest compatible texel centre, or emit no contribution when none is compatible. Entirely empty support or an empty selected fallback skips continuous reads and native identity blends. Two hardware-filtered continuous samples provide cloud value V=(radiance,opacity) and bucket transmission B. Final composition emits revealage 1-V.a, bucket revealage B, glow (0,0,0,V.a), and accumulation V*(1-B[i]) for each of the three native buckets. No intermediate glow or native accumulation textures are allocated.

Filtering compact values before expansion deliberately selects F(V)*(1-F(B[i])), rather than independently filtering expanded products F(V*(1-B[i])). These are different reconstruction approximations where coverage, radiance and bucket weights vary together; no equivalence is claimed. All three buckets, native blend meanings and exact occupied-cell intervals remain. No manual bilinear interpolation or per-pixel lighting traces are introduced.

Spatial reconstruction can soften silhouettes and thin layers. Conservative depth clipping and nearest-compatible fallback may thin clouds at foreground edges; they prevent unqualified blending through foreground depth discontinuities but cannot reproduce full-resolution geometry. User-run appearance review remains required. Reduced viewport sizes round upward, with normalized coordinates mapping both passes to the same full-view camera. Atmospheric shafts use full-view fragment coordinates.

Each history additionally stores required observation age in R16F. The default-off compiled `VGE_CLOUD_TEMPORAL_DIAGNOSTICS` option adds a separate RGBA16F attachment for effective prior weight, fresh/carried/fallback status, rejection code and displayed age. Its fragment output and composition sampler exist only in that variant. Normal rendering allocates no diagnostic images or bindings. Switching diagnostic variants rebuilds only framebuffer metadata and adds/retires optional images; physical transport, confidence and age survive unchanged. No diagnostic CPU readback is added.

Private compact trace and two normal history sets cost about103 bytes per reduced pixel (ceil edges add a bounded excess): approximately116.3/29.1MiB at3440x1377 for half/quarter and203.7/50.9MiB at4K, plus a32KiB blue-noise texture. Diagnostic capture adds16bytes per reduced pixel across both histories, for119bytes nominal (134.4/33.6MiB at3440x1377 or235.3/58.8MiB at4K). Full resolution allocates none. These screen resources are additional to volume payload limits and retire on resize/reload/release. Normal filtering writes four attachments; diagnostic filtering writes five. Native composition requires six draw buffers/attachments. Filtering reserves eleven fragment texture units normally and twelve for diagnostic composition. No extra density or lighting volume is allocated. Measure total reconstruction, blending and allocation cost rather than treating quarter ray count as a fourfold total speedup.

Apply current directional phase functions to cached transmission, add the retained diffuse source once, and integrate in physical units. Apply the established atmosphere/local-medium treatment and scene encoding once, preserving both PBR modes and all six native OIT attachment meanings. Preserve independent camera and atmospheric calibration identities. Retained lighting bounds must cover the older energy still present in the volume, not only the current sky intensity.

Bind the existing shared frame publication in both tracing and reconstruction. Derive view extents and shaft coordinates from its full-resolution screen size and compiled divisor; consume its fog colour, density and minimum directly, including for CPU fog-headroom admission. Do not republish those frame values in private cloud UBOs. Flat-fog geometry and underwater state remain private because the shared frame does not publish them. The private view block owns volume/material parameters and atmospheric source values absent from existing shared blocks; composition owns no private parameter UBO; trace and temporal filtering each own a 64-byte shared-layout material/sampling block; diagnostic composition borrows the filter block.

Retain immutable view-pass descriptions and pipeline preparation by framebuffer identity, attachment revision, extent, compiled executable and context. Refresh them when those dependencies change, and release cached target references with screen-resource retirement. Cached preparation never retains an active attachment borrow: each immediate pass validates and borrows its current images, then releases them through the existing graphics boundary. Scene, camera and sampled-resource references remain synchronous borrows cleared after submission, including failure; cache reuse does not waive current publication or executable certificates.

## Resolution and memory are design constraints

Cuboid styling makes axis-aligned structure plausible, but coarse voxels still round silhouettes under filtering, erase thin layers and leak light. Full-domain resolution is therefore a central decision rather than an incidental setting.

The base field payload above is 22 bytes per voxel: 2 density + 4 direct + 16 diffuse ping-pong. Illustrative allocations, excluding guards, metadata, occupancy and driver overhead:

| Grid dimensions, X/Y/Z | Base payload | Horizontal spacing across 10,000 world units |
| --- | ---: | ---: |
| 256 × 64 × 256 | 88 MiB | 39.1 units |
| 512 × 64 × 512 | 352 MiB | 19.5 units |
| 1024 × 64 × 1024 | 1,408 MiB | 9.8 units |

These illustrate the tradeoff; the selected contract below adopts the middle option with explicit guards and overhead. At 50-unit native column width, the first option has very little horizontal shape resolution. Across an 800-unit vertical extent, 64 cells are 12.5 units tall; they cannot preserve two-unit edge detail. The old lighting-only allocation and memory ceiling cannot simply carry over.

Before implementation planning fixes constants, select a memory ceiling, domain extent and density resolution together, and record the resulting smallest representable cloud feature. If affordable dense storage cannot represent the intended clouds, revise the layout toward sparse occupied bricks or multiple coverage levels before building a full solver. Do not compensate by reintroducing procedural density into every view sample, silently shrinking visible coverage, or claiming cuboid style makes resolution irrelevant.

## Evaluation and acceptance

Measure density generation, direct updates, diffuse transport, diffuse copy/publication, occupancy maintenance and final view integration separately, alongside total cloud GPU time. Include static views, camera motion, wind, evolving weather, source transitions, startup and domain recentering. Distinguish steady-state amortization from update spikes and stale-light delay. Keep the game running or closed according to the user's instruction and disclose GPU contention.

Use focused tests for declared math, finite values, resource lifetimes, region identity, publication ordering, work bounds, density use across all consumers and OIT contracts. Do not introduce approximation-quality, convergence, image-difference or noise thresholds. Visual shape, stability, leakage, stale illumination and acceptable resolution are user-reviewed results; passing a headless test does not establish them.

Implementation iterations use cached incremental builds and affected checks through subagents. Reuse unchanged passing evidence. Full comparison matrices and canonical packaging belong to a frozen candidate's final qualification, not every edit. Never launch the game for appearance acceptance.

The design succeeds only if it delivers genuinely stored volumetric geometry and reusable lighting, acceptable user-reviewed appearance and measured total cost at an explicit memory budget. Replacing procedural arithmetic with texture fetches is an architectural change, not an automatic performance guarantee.

## Selected representation and numerical contract

The user selected the approximately 352 MiB base-field experiment: 512 by 64 by 512 cells across approximately 10,000 by 800 by 10,000 world units, assessing coarse shape first. The following guard, metadata and execution choices make that experiment implementable. They are experiment constants, not measured performance defaults or approximation-accuracy requirements. Implementation remains separately requested through the tasklist.

### Layout and memory limits

All quality levels retain the same grid and storage limits. Horizontal cell spacing is fixed at `10000/512 = 19.53125` world units. For native width W in 1..200, use `ceil(50W/19.53125)+1` interior horizontal cells, plus one guard at each end. The extra interior cell accommodates arbitrary native alignment without changing spacing. At maximum coverage this is 515 by 66 by 515 allocated texels, with 64 interior vertical cells. Physical work blocks are 8 by 4 by 8 texels; partial edge blocks are bounded by allocation dimensions. The maximum is 65 by 17 by 65 blocks (71,825).

Initially select an 800-unit vertical band around the observed occupied-height envelope with one vertical cell of free margin at each end. Snap its lower boundary to vertical cell spacing. If it cannot fit, double band extent until the envelope plus those margins fits; retain 64 vertical cells, with spacing `bandExtent/64`. Cap band extent at 25,600 units and reject unsupported input rather than crop it. Ordinary changes contained in the band do not resize it. A band move/expansion is a domain replacement with changed vertical feature scale explicitly reported in diagnostics. Do not shrink it during a retained-domain lifetime. Empty input uses [-400,400]. The initial 12.5-unit vertical cell becomes coarser only for explicitly reported band expansion, never to satisfy a ray budget.

Use these owned payload limits: 512 MiB per working field set, 1 GiB combined working/replacement/drawable payload. These limits reserve headroom above the complete selected payload; they are not extra allocated pools. GPU driver overhead is reported separately from owned payload and must not be represented as known exact bytes.

| Allocation at maximum native width | Bytes |
| --- | ---: |
| R16F density + RG16F direct + two RGBA16F diffuse fields, 17,504,850 texels | 385,106,700 |
| One RGBA32F latest native descriptor snapshot, 200 by 200 | 640,000 |
| 64-byte records for 71,825 physical blocks | 4,596,800 |
| One uint conservative occupancy per block | 287,300 |
| Fixed work-list, reduction, parameter and selection allowance | 4,194,304 |
| One working domain | 394,825,104 (376.535 MiB) |
| Isolated drawable R16F density, RG16F direct, RGBA16F diffuse and region/occupancy metadata | 249,952,000 (238.373 MiB) |
| Owned raw native observation for deferred validation | 640,000 |
| Working plus drawable/raw observation | 645,417,104 (615.518 MiB) |
| Two working domains plus drawable/raw observation, conservative peak | 1,040,242,208 (992.052 MiB) |

There are no direct-field remap spares or descriptor history spares. The canonical descriptor snapshot is required because bounded generation may outlive the native texture borrow. The raw observation snapshot freezes validation input before CPU height admission; neither descriptor representation is sampled by the view. The drawable snapshot isolates rendering from unfinished solver writes. The fixed allowance includes all volume buffers outside the explicitly counted records/occupancy, including the publication parameters. Implementation must itemize it and reject overflow, not allocate uncounted scratch. Screen-sized view targets are accounted separately under Rendering the published volume. No distance texture is allocated.

Ordinary diffuse batches repair at most 128 previous blocks: at most 524,288 bytes of texture read/write traffic, with less for partial edge blocks. The first same-scale batch repairs none. Full copy/re-encoding costs 280,077,600 bytes (267.103 MiB) at maximum allocation when either field's scale differs from the destination scale or repair history is unavailable. A scale transition can therefore require two successive full copies, bringing both ping-pong fields to the new scale. Retain the previous successful list/count in existing scratch; no extra volume or buffer is allocated. Drawable publication additionally copies changed complete blocks and coherent metadata. Full-channel publication is reserved for first use, domain changes, direct activation or diffuse encoding changes. Replacement prepares only one incoming working domain at a time. New observations coalesce into it instead of allocating a third working domain.

Nominal native columns have about 2.56 horizontal cells; the 800-unit band's vertical cells are 12.5 units tall. Two-unit edge detail and arbitrarily thin silhouettes are not resolved. This is the accepted coarse-shape experiment; visual acceptance still belongs to the user.

### Native inputs and coordinate identity

Use the completed native map publication at the existing consumer boundary. For native width W, camera-relative displacement d and committed placement Offset, native-local position is `q = d - Offset + (25W, 0, 25W)`. Native column width is 50 world units. Its material lower-corner key is `K = CenterXZ - WindTileXZ + columnIndex`, and its material lower corner is `50 * (K - W/2)`. Preserve integer keys and calculate bounded relative GPU coordinates from CPU double/integer arithmetic. Do not apply a second wind displacement to stored voxels.

The native red channel is extinction per tile unit, not opacity: `sigma = map.r / (50 * metresPerWorldUnit)`, with one metre per world unit in the current scene contract. Lower/upper heights are `map.ba * 50`. Reject nonfinite/negative extinction and invalid height intervals. Native pre-lit colour is not a lighting input. Generation consumes certified native data synchronously and retains only volume-owned results.

Native material keys are distinct from voxel keys. For horizontal native lower corner A, first interior voxel key is `floor(A/cellSize)`; the first guard is one key earlier. Voxel centres are `(key+0.5)*cellSize`. Store X/Z at positive-modulo key slots in the allocated horizontal dimensions and use hardware Repeat there; Y uses ordinary bounded addressing and ClampToEdge. This makes the physical wrap seam connect adjacent material keys. The two logical outer guards are zero density, and view/transport clip to the admitted finite native rectangle, so the finite outer edge cannot wrap into occupied weather. Generation clears newly entering/guard slots before publication. Width changes replace the domain, never reinterpret a previous modulo layout.

Physical work-block indices remain fixed while the logical window moves. Each texel's current key is recovered from its slot and the published window. Retained texels keep data; entering cells are initialized. A block touched by new cells or guard changes becomes pending, and its completion stamps are revoked conservatively for the whole block; this does not clear compatible retained lighting. Blocks may straddle the logical window seam. Algorithms use the slot-to-key mapping, not an assumption of contiguous material keys inside a physical block. Guard slots cannot contribute scattering sources.

### Generation, initialization and geometry publication

Retain one latest valid descriptor snapshot with `(sigmaBase, lowerMetres, upperMetres, valid)` per native column. Comparison reads the old slot before writing its replacement, emits affected-block demand, and updates window identity after successful completion. Keys entering the native window initialize without comparing unrelated prior slots. Any finite content change requests density work; compare exact input content rather than using an approximation threshold. Width/window identity and source revision live in publication metadata. Failed in-place snapshot work revokes it and requests a full refresh.

For each voxel intersect each overlapping native cuboid and compute exact intersection volume. With detail disabled, sum `sigmaBase * overlapVolume/voxelVolume`. At the selected horizontal spacing a cell overlaps at most four native columns. At Medium, with detail enabled, evaluate eight equally weighted stratified midpoint samples inside each nonempty overlap and multiply its volume fraction by their average shaping factor. This avoids missing a thin overlap merely because the whole voxel centre is outside it. Detail can still deliberately erode support.

Use one bounded smooth value-noise field with integer corner hashing and material period 12,800 units, nominal wavelength 100 units, amplitude .15. Hash corners to [0,1], interpolate trilinearly with per-axis cubic weights `f*f*(3-2*f)`, and use multiplier `1+.15*(2*noise-1)`. Let e be distance to the nearest exposed cuboid face, excluding connected faces whose neighboring column covers the quadrature point's height. Multiply noise modulation by `smoothstep(0,2,max(e,0))`; total shaping is nonnegative and at most 1.15. The 100-unit wavelength is deliberately broader than the density grid; two-unit erosion affects cell-average coverage rather than claiming a resolved edge. Detail disabled uses shaping one and compiles out noise and erosion. Neighbor-column changes enqueue the old/new footprint plus adjacent native columns needed for connected-face tests, then their intersecting voxel blocks. Noise is stationary in material coordinates; weather descriptors provide shape evolution.

Use float32 generation arithmetic, and require the resulting nonnegative extinction to fit finite R16F. Reject unsupported overflow rather than silently clamp physical extinction. Explicitly convert the result to a representable half value before image storage. Compare its packed 16-bit representation exactly with the old stored texel; only changed blocks require drawable density copying, occupancy repair and dependent lighting demand. This is representation equality, not a perceptual threshold. The occupancy word keeps traversal halo/repair state in bits0..1, an exact-origin certificate in bit2 and a nine-bit positive-cell count in bits3..11. Generation counts represented nonzero half values using its existing lanes; maintenance counts retained cells while clearing entering/guard cells. Halo repair preserves the count. Scheduling excludes certified zero-origin blocks while retaining their demand and age. Unknown support conservatively reserves256 origins. Traversal reads only the low support bits and retains its neighboring-cell halo, which may overclassify empty blocks but never removes support. Half-float underflow/rounding is part of the declared stored representation, not certified preservation of arbitrary tiny native density.

Update order is snapshot/queue merge, selected density writes, occupancy repair, direct work, diffuse work and drawable publication. Steps may span frames; the current view is submitted before future updates. A density block reaches the drawable publication only after all its texels and affected occupancy neighbors complete. Views read disjoint resources while working fields change. A density failure withdraws the candidate contribution and latches recovery; it cannot continue drawing an in-place partially changed field. Small successful edits retain older compatible lighting as stale. No hard-change percentage threshold or extra reference descriptor texture is needed.

First activation initializes textures and fills density in bounded batches; the candidate produces neutral OIT until every initial density block is initialized. Freeze that domain's one descriptor snapshot for this entire initial fill. During replacement, keep drawing the old complete domain in its old material coordinates while the incoming domain fills from its own frozen snapshot. Old density observation pauses; compatible old lighting may continue within the shared frame work limits. Do not apply the new layout to old texels. Publish the new domain after its initial density is complete and lighting is initialized to fallback; then retire the old one. Only then observe the latest native publication, coalescing skipped intermediate changes. This deliberately stale startup snapshot avoids endless restart and needs no descriptor history texture. If the latest envelope needs another band, initiate the next replacement while retaining the just-completed domain; never write out-of-band observations into it. Ordinary active-domain observation likewise checks the full new envelope before replacing its descriptor snapshot. Old-domain age is visible, and continuous layout motion may produce successive bounded replacements rather than a freshness guarantee.

### Lighting, recurrence and encoding

The following numerical transport describes Medium; profile-specific quadrature, range and optical exits are defined under Quality workloads below. Set scattering albedo `a=.9` and HG anisotropy `g=.6` as cloud material choices, not quadrature settings. Direct T is integrated to the finite density-domain exit with no light-distance cutoff, using exact physical-cell DDA intervals. This bounds spatial sampling cost through the selected lattice rather than inheriting a short shadow-ray range. Accumulate optical depth with saturation at 80 (whose transmission is effectively zero at storage precision), and store `exp(-tau)` in the matching RG16F channel. Do not terminate based on an unrecorded approximation threshold.

At location x and outgoing direction w, `Q(x,w)=a*sum(E_s*T_s(x)*HG(dot(w,light_s)))`, with unit-integral HG and physical directional illuminance E. The current view uses current E and directions; cached T may be direction-stale. Use eight normalized cube-corner directions with weights 1/8 for diffuse transport, a symmetric initial angular rule without axis-only preference. Each diffuse ray gathers for at most 400 metres or the domain exit, with exact physical-cell DDA intervals. This distance spans about twenty horizontal cells, allowing transport across several native columns while bounding update work; it is an explicit local transport approximation.

For each gather ray accumulate `I=sum(T_before*alpha*(Q(sample,-rayDirection)+D_read(sample))) + T_end*Environment`. Then `D_write=a*average(I)`. The environment closure is used both at the finite domain exit and at the 400-metre truncation; the latter is an explicit approximation of unrepresented incoming radiance. Empty gaps attenuate nothing and do not terminate traversal. Final view source is `Q(directionTowardCamera)+D`, with no additional environment term; directionTowardCamera is the negative camera-to-sample ray direction. Initialize D to `a*Environment` and T to one. Disabled diffuse uses that initialized-form source dynamically without launching scattering jobs; disabled self-shadowing uses T=1 and launches no direct jobs.

For retained componentwise high-water source envelopes, HG maximum is `(1+g)/(4*pi*(1-g)^2)`. Define `Qcap=a*pMax*sum(Ecap)` and `Dcap=a*(Qcap+EnvironmentCap)/(1-a)`. Nonnegative normalized quadrature and extinction make this a bound on the declared recurrence including its initialization. Admit bounds in double precision and reject unrepresentable source inputs. Retained caps decrease only when their associated lighting is discarded. Reserve physical scene headroom using the established upper-bound helper for `Qcap+Dcap+atmosphereReconstructionMaximum+localFogMaximum` plus the view accumulation allowance derived below.

Store diffuse RGB divided by `S=2^ceil(log2(max(1,maxComponent(Dcap))/32752))`; evaluate physical transport in float32. To keep repeated half-float rounding inside the declared energy bound, clamp each nonnegative physical component to its Dcap, then encode to the largest nonnegative half value no greater than that clamped component divided by S. Implement this using representable-half conversion and, if conversion rounded upward, the preceding nonnegative half value. Supply that exactly representable value to image storage. Apply the same bounded encoding during initialization and scale-changing copies; do not rely on ordinary nearest-half rounding or spare scale range as a radiance-bound proof. This is a finite representation contract, not an approximation-quality test.

Alpha is reserved zero, not a per-node flag. Track the encoding scale of each ping-pong field. Before a partial batch, repair the previous successful block list if both fields already use the destination scale; otherwise copy/re-encode the entire source into the spare. The previous list describes complete physical blocks, including edge/guard texels, and survives empty work, recentering and solver reset. Maintenance writes both fields in newly initialized cells. Selected nodes then use the destination scale. Successful-only GPU completion replaces the repair list; failure withdraws the working domain without publishing its destination. Publish texture and scale together after barriers. No batch means no copy, scale change, repair-history replacement or swap. Untracked refinement invalidates bounded repair history. Successful direct updates may be in-place between readers, but a failed update withdraws the contribution rather than exposing partial writes. Scene admission covers the maximum retained caps of both domains during replacement, plus current sources, before scene encoding freezes.

### Metadata, budgets and source changes

Each physical block has sixteen uint words: flags; latest pending density revision; completed density revision; density first-pending and last-completed frames; pending and completed solar revisions; pending and completed lunar revisions; direct first-pending frame; solar and lunar last-completed frames; diffuse pending and completed revisions; diffuse first-pending and last-completed frames. Domain/window/scale identities live in the publication header. Direct first-pending is conservative for either active source. Counter wrap explicitly resets metadata/history before ambiguous ordering. These are scheduling revisions of partially updated fields, not a false claim of one globally atomic weather snapshot.

Use at most4096 density blocks per eligible update. Clouds.LightingRaysPerUpdate uses zero for the quality profile budget; positive values select2,560 to327,680 rays in2,560-ray units. Medium resolves to81,920. Each unit reserves512 direct rays and2,048 diffuse rays; unused allowance never transfers between solvers. Charge one ray per positive stored voxel/source and the compiled profile direction count per positive diffuse voxel. Unknown origin support reserves256 voxels per block until certified. The default allowances remain16,384 direct and65,536 diffuse rays, and maximum allowances65,536 and262,144. Source relevance is unchanged; this is not a GPU millisecond target.

Keep complete physical-block jobs. Candidate selection scales diffuse shortlist capacity by the compiled rays per block (clamped1..128); direct capacity follows its ray allocation. Medium defaults to64 direct and32 diffuse priority candidates, plus half as many fair candidates. This avoids reducing refresh opportunity merely because a gather uses fewer rays. Their unique union can refill unused ray capacity, capped at256 direct block-source jobs and128 diffuse blocks. Half the original whole-block job capacity, rounded up for odd counts, defines the fair ray reservation; for diffuse this reservation expands up to one full block when the total allowance can fit it; cyclic candidates are admitted until the next complete block would exceed that reservation. The remaining ray allowance admits unique priority winners in initialized, newly active, visible and oldest order, skipping complete jobs that cannot fit. A fair cursor advances only past admitted jobs, guaranteeing service for persistent eligible demand. Ray totals cannot exceed the separate allowances. This is bounded refill, not an exhaustive knapsack: candidate caps, complete-block rounding and insufficient demand can leave allowance unused even when other work exists. Empty-origin blocks consume no jobs; empty lanes inside admitted blocks launch no rays. Pending revisions and age survive empty eligibility and reactivation.

Lists and summary storage retain their existing maximum capacities and fixed4MiB scratch allowance. Scratch words40..45 report solver allowances, charged rays and unknown selected jobs with the existing fenced status read. Exact certified origin counts equal launched rays on a successful update; unknown charges are explicitly upper bounds. Failed work advances no completion stamps. Complete-block repair, coherent diffuse reads, publication and source-dependency refresh remain unchanged.


Density selection uses parallel region merge and cyclic prefixes, followed by four byte-wise age histograms and a stable physical-index tie prefix. Preserve the exact fair/oldest selected sets, cursor and ages; an initial fill spends its entire budget on fairness and needs no age threshold. Histograms reuse lighting-selection scratch in barrier-ordered passes; density consumers finish before lighting overwrites the shared summaries. Selector operations share one uniform publication record so the fixed allowance does not grow with executable count. Partition the selected direct jobs into solar/lunar region lists and independent indirect arguments without changing the combined budget or completion list.

Lighting selection reduces fair and priority winners in bounded 256-lane groups. After synchronizing the eligible count, an empty initial or reduction group writes the same invalid winner slots and zero backlog without running either shared sorting network. Nonempty groups retain the exact ordering. Region demand, ages and fair cursors survive empty eligibility; occupancy or source changes can make that demand eligible again. Retain an ascending compact list of physical blocks with positive represented density. Generation and circular maintenance invalidate membership only when a block crosses zero/positive support or lacks a certificate. On a dirty list, count occupied blocks per256-region group, prefix group counts and scatter IDs before selection. Lighting-only changes and nonzero-to-nonzero count changes reuse the list; current exact counts still determine job cost. GPU-produced list counts drive indirect selection dimensions without CPU readback. The full-domain metadata demand merge remains necessary for remote invalidation. Keep fair cursors keyed to physical jobs, independent of compact rank. The list/counts/offsets use scratch words970000..1042400, above reduction storage, within the fixed4MiB allowance. Work variants share one208-byte uniform record and a180-byte indirect buffer; bounded counts and successful publication preserve lifecycle ownership.

Use 256-thread direct groups for the complete spatial block. Diffuse groups also contain 256 threads, mapped to 256/N voxels with N independent directions per voxel; N group slices cover a block (N=8 at Medium). Validate a voxel once, reduce its N contributions in the declared order, and reject any failed direction before publishing. Every lane must reach shared barriers, including empty, guard and partial-edge voxels. Workgroup/thread counts describe available parallelism, not measured hardware occupancy or saturation. Use the selected compiled profile direction set and schedule.

At maximum allocation, an initial density fill needs at most 18 eligible batches when its queue receives the entire density budget. At the default lighting budget, after initialization, fairness alone services a fixed pending density set within 36 density batches, both direct channels within 4490 direct batches, and diffuse within 4490 diffuse batches. Each batch may span multiple rendered frames while completion is pending; these service bounds are not elapsed-frame or responsiveness guarantees. Occupied-block counts can be much smaller. This exposes a central experiment risk: density residency is feasible while lighting refresh may still require budget or solver changes. Report actual occupied work, ages and backlog before deciding those changes. No convergence assertion follows from one revisit.

A density batch with changed stored values coalesces one domain-wide direct/diffuse demand; completing already-identical density does not. Recentered coverage also requests domain-wide refresh, accounting for changed finite transport exits and guard/entering maintenance writes that precede generation comparison. A direct batch requests diffuse refresh. Direction changes dirty that source's T plus D; intensity/environment changes dirty D only. A source is relevant when its finite physical intensity is nonzero; activation requests its blocks with priority and initializes its channel to unshadowed where prior identity is incompatible. Retain this rule even when one source is much fainter: no relative-light cutoff or budget reweighting is selected. Inactive sources receive no direct work. Compatible old T/D remain readable and explicitly stale; changes never restart queue age. Exact block stamps advance only after successful completion against that dispatch's frozen inputs.

Invalid/new regions use initialized sources and never hide occupied density. The baseline switches to completed lighting without an extra blend texture or history filter. Abrupt lighting updates are an explicit user-review risk, independent of view-history reuse. Layout/world/representation changes replace the domain. View-only settings, camera, exposure and fog retain it.

### Nonblocking updates and drawable publication

Draw the last completed drawable snapshot while the next bounded update is pending. Submit future work after the draw, keep at most one update sequence in flight, and poll GPU fences with zero timeout on later frames. Validation freezes the native observation during its same-callback borrow. Its envelope/status fence remains a CPU admission boundary before selecting or replacing the domain. Initial/replacement density fills likewise retain their completion summary boundary before admitting a complete working domain.

For an admitted retained domain, submit density generation/occupancy repair, lighting selection, direct tracing, diffuse repair/gather and one combined lighting completion in the same command stream. Explicit image, sampler, storage and indirect-command barriers order producer/consumer passes and shared scratch reuse. No direct-to-diffuse fence or intermediate CPU job-count read is required. GPU-selected counts emit zero copy/gather groups when diffuse work is empty. Separate indirect records admit bounded repair or the required full re-encode. Gather reads the coherent old field; the spare matches it outside the selected destination blocks. Deferred status includes copied-voxel accounting without a separate CPU readback.

Contiguous update submission borrows native state once through the union of its prepared executable binding footprints. Density, scheduling, storage and transport retain their typed program owners and certificate checks; internal dispatches reuse that boundary and close producer visibility once after each pass. One entry barrier covers preceding work. Native state restores at callback exit on success or failure. Validation/domain-admission fences still separate CPU decisions, and completed drawable copying retains its own later boundary. No boundary spans a frame or a pending fence.

A final fence covers the complete chain. After it signals, read combined lighting status/diffuse count and the density summary, validate retained executable/context and field certificates, and accept the successful texture/encoding/bounds pair. An empty diffuse list never swaps its untouched spare or grows its encoding. Density failure propagates into lighting status, and any solver failure suppresses the combined lighting completion stamps and drawable publication. CPU density completion metadata remains frozen during lighting submission, then advances after its consumers retire. Submit the next eligible chain in the same callback that retires the previous chain; no fixed delay or idle callback is inserted. No ordinary render callback waits for a producer to finish. The density initialization/domain-admission boundary, finite work budgets, GPU execution time and repeated validation of evolving native observations remain throughput limits.

Copy completed dirty density/direct/diffuse blocks into the disjoint drawable snapshot, with coherent occupancy and completion metadata. Only after successful validation may texture/domain/encoding bindings change together. Lifecycle/source identity changes revoke pending publication; exposure, fog and ordinary atmospheric evolution retain the previous completed snapshot. OpenGL still submits into the engine command stream: this design removes immediate CPU readback waits and same-callback completion, but does not promise an explicit independent compute queue or hardware graphics/compute overlap.

Publication initializes one 16-byte indirect record, then copies all sixteen metadata words and occupancy with one invocation per physical block. That same pass appends each dirty block once to retired selection-summary scratch beginning at word32768; at most71825 words are needed, ending before the second summary range at word800000. Persistent cursors, completion status and diffuse repair history remain untouched. GPU-produced dispatch dimensions use at most64 columns and1123 rows, with uniform rejection of padded groups. Ordinary image copying dispatches one256-lane group per listed block; an empty list emits zero image groups. Required full-channel cases bypass compaction and use the complete physical grid, also copying any other dirty channels. Consume working dirty flags only after every valid lane in that block has submitted its image writes; partial-edge lanes participate in completion barriers. Explicit storage/command/image/texture barriers separate producers and readers. Metadata remains coherent even when no image changes. The three compiled operations share one16-byte parameter record; the16-byte argument buffer is charged inside the fixed allowance, leaving1568 bytes reserved after the shared lighting-work and temporal allocations. No new volume, list allocation, CPU count readback or publication fence is required.

### Initial acceleration selection

Select coarse occupancy DDA as the initial implementation. It requires only local occupancy maintenance plus the retained conservative density halo, retains traversal state and has a direct bound on crossed blocks. A distance field remains a later optional prototype: newly occupied support can invalidate long skips far from an edit, so distance-field maintenance cannot be presumed cheaper for evolving weather. No distance texture is included in the initial base allocation.

Occupancy covers every cell whose stored density can be positive; do not introduce a perceptual density threshold. A selected density update must make occupancy conservative before readers see that update. Removing support may leave extra occupied blocks temporarily, which costs traversal but cannot hide material. Guard and circular-seam handling follows logical adjacency, not physical texture-slot proximity.

### Traversal bounds to instantiate with the layout

All ray consumers share a 2048-operation cap and exact voxel intervals. A straight ray through the admitted 515x66x515 allocation crosses at most `515+66+515=1096` cells. Coarse empty-block jumps cannot increase the number of occupied cells visited; allow up to150 additional block-advance operations and16 rounding/end operations, giving at most1262 operations. Tied axes advance together. The shader cap never increases spacing or accepts a failed prefix. Each occupied interval samples one physical texel; empty intervals sample none. At Medium, diffuse rays additionally clip at400metres, and direct rays retain their full finite exit and tau80 saturation.

Initialize fine-cell crossings at block entry using the original ray and integer planes. Retain those planes and update physical indices with explicit circular seam branches. Crossings derive from original origin and per-ray reciprocal velocity rather than accumulated distances; entry correction and subsequent advances use that same float32 parameter convention. Reciprocal multiplication can round plane times differently from division. Overflowed reciprocals use division to avoid zero-times-infinity crossings; grid-velocity components that underflow to zero remain inert. No current cell is integrated twice. Zero axes do not divide, and nonprogress invalidates the ray. Source/encoding admission retains the existing `1+2048/2^23` accumulation allowance. All texture, operation and work budgets remain bounded; a1024-operation cap would not cover the admitted diagonal voxel walk.

At Medium the diffuse recurrence uses eight equally weighted cube-corner directions, albedo.9, one environment closure and unchanged bounded half encoding. Current view phase functions and source intensities apply once to each cell's cached source. The ray-budget setting changes selected job counts only; it does not change source relevance, individual ray integration or density generation.

### Quality workloads

The five compiled workloads and their configuration precedence are specified in
[PBR.Clouds.md / Cloud quality profiles](PBR.Clouds.md#cloud-quality-profiles). Level2 is Medium
and preserves the numerical baseline above. TraceScale and FullResolutionReference remain
independent. Detail/SelfShadowing/Diffuse can disable features at any level. Zero lighting-ray
budget resolves the profile default; a positive override remains a strict separate solver budget.
Quality owns view termination; there is no separate persisted stopping threshold.

Generation quadrature is 1/1/8/27/64 equally weighted overlap samples. Diffuse directions are
2/4/8/16/32 with ranges100/200/400/600/800 metres. Lower levels permit completed-interval
transmittance exits (.020/.005); these omit the residual tail and environment term, while Medium
and higher retain the complete finite direct ray and finite diffuse range closure. View stopping
thresholds are .020/.010/.005/.0025/.00125. Direction rules remain balanced and normalized with
nonnegative unit-sum weights; scattering and finite encoding bounds remain valid. Budget accounting
and indirect dispatch use the actual direction count. Unknown support reserves a full block.

A changed compiled profile waits for the frozen in-flight chain to retire. Changes to generation
quadrature or Detail initialize replacement geometry through the existing bounded storage owner,
retaining the completed drawable. Lighting-only changes retain geometry and stale drawable light,
reset affected scheduling/solver certificates and refresh under the new rule. Budget-only changes
retain compatible fields and screen history. No new CPU readback, wait, screen pass or volume is
required. All levels retain the current shared density/lighting lattice, guards and memory limits.
Coarser density and independent lighting grids remain deferred: they require measured shape/lighting
tradeoffs and a separate mapping/publication contract. Fresh-ray cadence is unchanged; altered
cadence also remains deferred pending explicit appearance acceptance.

### Shared rendering obligations

Preserve change-driven consumer enablement described in [Rendering.Enablement.md](Rendering.Enablement.md), native Before/OIT ownership and depth behavior in [PBR.Clouds.md](PBR.Clouds.md), and physical-source/encoding publication in [PBR.Atmosphere.Publication.md](PBR.Atmosphere.Publication.md). These are integration obligations, not a requirement to couple cloud transport to other rendering algorithms. Ordinary exposure, fog or source-intensity changes do not invalidate stored geometry. Camera and atmosphere calibration generations remain separate identities.

## Qualification risks retained by this selection

The selected coarse shape, lighting refresh latency, full-field copy traffic and reconstructed view cost are unqualified until measured and reviewed in game. Distance fields remain a conditional follow-up rather than a second mandatory allocation. This specification selects an implementable experiment; it does not claim that the experiment is already fast enough or visually accepted.

### Temporal view sampling contract

Use rendering-owned camera reconstruction/projection helpers and the existing shared frame UBO,
also used by surface-lighting velocity. Shared previous view-projection accepts current render-relative
positions with a double-precision origin delta composed into its fourth column. Cloud ray displacements
exclude native eye translation: project their previous-eye-relative position with the shared matrix at
W=0 plus the compatible projection's fourth column. Cloud placement supplies camera/material motion
once; foreground scene endpoints use W=1 position projection without wind. Do not upload a private
cloud previous-camera matrix or borrow scene-surface velocity. Keep projection compatibility and the
successful cloud-frame certificate local: shared history can advance through a skipped or failed
cloud draw, but that gap must revoke screen-history reuse. LumOn enablement is irrelevant.
UV is bottom-left OpenGL; cloud lookup requires finite half-open [0,1) coverage and positive clip W.
Surface validity and boundary policies remain surface-owned. These helpers do not replace the
volume-specific range, classification, foreground, age or confidence contracts below.

Reduced views retain two compact physical history sets and a disjoint current trace set at half their width/height. Incompatible frames reuse the invalid previous-history set as full reduced-grid trace scratch before filtering into the other set. One location per two-by-two reduced tile is refreshed each frame, with complete coverage in every aligned four-frame sweep; initial or incompatible views trace all reduced pixels. Every fresh ray retains exact occupied-cell transport. Full resolution bypasses temporal targets. Commit history only after successful final composition.

The compiled constrained refresh schedule uses eight spatial rank masks, one per four-frame sweep,
with a32-frame period. Each32x32mask is seeded by the
existing tileable void-and-cluster generator; four deterministic coordinate-descent passes choose
cyclic phase-label rotations for masks1..7 to reduce identical phase orders between adjacent sweeps,
including the periodic boundary. Relabeling preserves each mask's spatial phase populations; it
has not been spectrally qualified as genuine spatiotemporal blue noise. The shared sampling helper
performs one atlas lookup for both trace addressing and temporal classification. No ray-position jitter is introduced.

Every ordinary tile traces one pixel each frame and each pixel once per aligned sweep. Refresh gaps
are1..7frames across sweep boundaries; seven-frame expiry still admits
the last carried support before the next fresh trace. Partial tiles clamp to their final row/column
and can repeat that edge pixel. The period divides the unsigned frame-counter range, so wrap is a
normal period boundary. Cold/resize/incompatible frames trace the full reduced grid; full reference
bypasses this schedule. The atlas remains32KiB with no extra screen targets or sampling reads.
Eighteen fresh observations take at most75frames from an arbitrary schedule position;
this is a response bound, not an appearance guarantee.

A compatible untraced pixel carries hardware-filtered prior transport unchanged; neighboring current observations supply spatial fallback only when history is unavailable. Fallback has zero independent-observation count and cannot seed reusable history. A failed fresh ray remains invalid. Certified empty fresh rays reject contributing history. Removed cloud support is therefore observed at the next fresh phase, within at most seven frames under the constrained schedule, rather than triggering a blanket publication reset.

For untraced pixels with zero relative material translation, establish the previous address from
the rotated viewing direction without guessing cloud distance. Classification and representative
distance come from the nearest admissible support at that address, never the old same-screen
coordinate. With translation or wind, use the nearest completed current fresh neighbor within the
existing 3x3 neighborhood as an independent classification/distance proxy; reject if no such
observation exists. Project that proxy into the previous camera and validate previous support
against its classification and previous-camera distance. This bounded approximation can lose
history at depth discontinuities; it does not establish an exact volumetric correspondence.
Carried metadata follows the same admitted physical footprint (bilinear distance for a fully
qualified footprint, otherwise the selected centre). Convert its previous-camera range to current
ray range by solving `|direction*range + materialTranslation| = previousRange`, taking the positive
far root; reject a nonpositive solution. Fresh rays retain their own measured range. Empty support
stores zero range. No extra rays, search textures or persistent metadata are introduced.

Admit every exactly nonzero bilinear metadata tap before two hardware-filtered transport reads. Require matching empty/contributing support, finite payload, a real observation count, each tap's refresh age below seven, previous-camera clip support and representative-distance agreement within max(2 world units,5 percent). When the footprint mixes incompatible support, select its nearest individually admitted texel centre instead of rejecting all history. Both physical textures use that same centre through their hardware samplers; rejected taps never contribute. Confidence and age then come from the selected observation, rather than the rejected footprint. If no tap qualifies, reject history. A fresh empty ray still cannot retain contributing history. Project the current scene endpoint into the previous camera using the shared origin-rebased previous view-projection and current inverse view (including native eye translation) before comparing clip depths (2e-5 projection-depth tolerance). Cloud reprojection additionally applies material wind once. Distance is a contribution-weighted proxy with uncertainty, not an exact first-hit surface. These conservative gates may reduce retention near edges and moving foregrounds; nearest support also introduces a bounded screen-space displacement within the original four-tap footprint.

Only fresh pixels may use neighborhood bounds, and only with at least four same-support, distance-compatible fresh observations spanning two pixels in both axes. Unsupported sparse bounds do nothing. Bound opacity and bucket transmission, which describe integrated coverage; a radiance-only change does not establish different geometric support. If supported coverage bounds exclude prior transport, reject its contribution and reset confidence to one rather than claiming a clipped sample survived. Fresh blend weight is min(previous count,7)/(min(previous count,7)+1), multiplied by opacity reactivity clamp(1-4*abs(delta opacity),0,1). RGB changes do not reduce prior weight or trigger neighborhood replacement. Values remain physical until native composition. Routine lighting/weather/fog/exposure changes do not revoke the CPU certificate.

Colour changes therefore retain the declared observation response instead of making selected fresh pixels jump ahead of carried neighbours. At saturated confidence and unchanged coverage, the remaining step difference is multiplied by7/8 on each fresh observation;18 observations reduce it below one tenth. With stationary sampling this takes at most75 rendered frames, not a fixed wall-clock duration. This is response arithmetic, not a visual-quality guarantee. Foreground, empty/support, distance, failure and expiry rejection remain independent. No extra rays, texture reads, targets, passes or transition bursts are required. This screen response cannot synchronize partially refreshed diffuse or direct volume blocks: those remain completed but potentially stale publications under the existing transport budget and require separate evidence before changing their publication policy.

History count is the declared equivalent observation confidence 1/(1-effective prior weight), capped at eight on fresh updates. Carry transports confidence and increments refresh age; it never invents an independent observation. Zero prior weight and clipping replacement produce count one. Each support tap must have refresh age below seven before admission. Diagnostics distinguish address admission from actual old weight and from fallback. Screen allocation and bandwidth are additional to volume payload. User-run stationary, edge, camera/wind, ghosting and dynamic lighting/weather review and measured total cost remain acceptance requirements.

A fully admitted bilinear footprint uses the same weights for refresh age as for its physical
transport: `age = sum(weight * tapAge)`. Confidence is the weighted harmonic mean,
`count = 1 / sum(weight / min(tapCount,8))`, clamped to [1,8]. This is transported blend confidence,
not a claim that interpolation creates independent observations; it stays within the contributing
confidence range. A nearest-centre fallback uses only that centre's age and confidence.
Carry adds one to the weighted age; a real fresh trace resets refresh age to zero. Refresh age
describes time since contributing spatial support was refreshed, not the oldest radiance component
inside an accumulated observation. An expired tap never contributes. Without incoming fresh
observations, the minimum age of surviving support advances by one on every carry and expires;
spatial averaging cannot perpetuate unrefreshed data. Incoming fresh support lowers age only in
proportion to its bilinear contribution. All foreground, support and distance gates remain separate.

Certify an exactly unchanged compatible material-to-view transform on the CPU using zero relative
translation and identical projection/rotation. In that case use the destination's exact integer
history coordinate, avoiding a spurious neighboring metadata footprint from inverse/project roundoff.
This does not relax expiry or foreground checks; scene-camera occlusion is evaluated independently.
Actual camera rotation or material translation retains ordinary reprojection and footprint admission.

The lighting budget is staged with applied cloud settings and frozen for an in-flight update. Budget-only changes retain fields, demand age, cursors and view history. Scheduler capacities are compiled at256keys when the direct candidate pool is at most64 jobs, and1024keys above it. Maximum scratch is reserved independently of the selected budget. The configured direct/diffuse ratio is retained even when one solver has less eligible work.
