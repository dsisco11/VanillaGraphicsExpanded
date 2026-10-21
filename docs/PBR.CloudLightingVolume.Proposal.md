Planning status: superseded by [the cloud volume design](PBR.CloudVolume.Proposal.md). This document is an inactive design record, not current architecture or implementation authority.

# Persistent cloud lighting volume candidate

## Status and purpose

Candidate design and numerical contract, implemented through [the candidate tasklist](PBR.CloudLightingVolume.todo). Domain/storage maintenance, density queries, descriptor observation, bounded work scheduling, persistent direct transmission, progressive diffuse solving and full-resolution view submission are available through an independent developer-selectable consumer. The existing renderer remains the default. Cost, packaging and user appearance qualification remain open.

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
- One small cloud-consumer interface exposing preparation, reset, disposal and physical radiance-bound admission in addition to the existing renderer callback. It lets the native owner manage either consumer without abstracting algorithms or intermediate textures.
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
- An RGB isotropic scattered-source term representing transported environment illumination and diffuse multiple scattering. It excludes the directional single-scattering response evaluated during drawing. Environment is included in this term, not added a second time at composition.
- Region identity, content observation and age metadata in integer records, plus exact per-node validity/occupancy bitmasks in diffuse alpha. Node masks use unfiltered texel access; they are not interpolated as lighting.

The selected contract below uses RG16F direct transmission and scaled RGBA16F diffuse RGB, with unscaled node masks in alpha and separate integer region metadata. Diffuse RGB uses an exposure-independent scale to fit physical radiance into half-float range; lighting arithmetic remains float32. Iterative scattering requires distinct readable and writable storage. Allocation accounting includes both buffers, remapping storage, region metadata and view targets.

Use hardware trilinear sampling of lighting within valid regions. Do not implement ordinary trilinear interpolation with eight manual texture fetches. Validity must cover the complete filtering footprint: cells next to an invalid region use initialized coarse values until the required footprint is valid. Filtering must not mix unrelated wrapped regions at grid boundaries.

An illustrative 16-byte resident payload per voxel costs 32 MiB at 256x256x32, 256 MiB at 512x512x64, and 2 GiB at 1024x1024x128. These figures are payload arithmetic, not complete allocation estimates or selected defaults. For a fixed world extent, doubling each dimension multiplies work and storage by eight.

The candidate starts with no lighting mip chain. Adding levels would need explicit boundary and validity semantics; implicit derivatives inside divergent integration loops are not an acceptable substitute for that design.

## Direct illumination and progressive scattering

Direct-light compute work evaluates bounded density integrals from selected lighting nodes toward the relevant sources. Store transmission, not a final view color. Zero-intensity sources need no new work; when a source becomes relevant, prioritize its direct-light coverage and use the initialized fallback until compatible data exists.

The first candidate uses bounded deterministic integration for direct illumination. It does not accumulate jittered direct-light estimates indefinitely across changing density. The scheduler amortizes the spatial work across frames. This makes its principal tradeoff stale illumination rather than screen-space sampling noise.

The multiple-scattering candidate uses a diffuse, isotropic closure: the directional first-scattering source seeds transport, and subsequent orders are approximated as an angular average. A bounded fixed direction set and bounded ray segments gather previously published source radiance through the density field. Direction weights are nonnegative and normalized. Segment contributions include extinction and scattering albedo with physical path lengths; repeated unweighted texture blur is not the solver.

The source gathered along a ray consists of directional first scattering evaluated for that ray's outgoing direction plus the retained diffuse higher-order source. The local angular average produces the updated higher-order term. This avoids adding the same first-scattering term again during final view composition. Empty space contributes no scattering but does not terminate transport between occupied regions. The finite domain has an explicit incoming environment boundary; it is not a wrapping light-transport domain.

The selected direction set, segment schedule and scattering parameters are specified below. They are approximation choices to evaluate, not claims of physical convergence or a replacement for a full directional radiance distribution.

Each iteration reads a coherent published source field and writes separate storage. Untouched regions retain their prior values when the next field is published. The required copy or per-region publication mechanism is included in measured cost; partial updates must not swap in uninitialized or older untouched texels. Image-write and texture-read barriers follow existing GPU abstractions. No invocation observes another invocation's partially written iteration as completed history.

## Scheduling, changes and retained history

Use bounded work budgets for direct-light nodes, scattering nodes and integration steps. Updating one eighth of the nodes each frame gives an eight-frame revisit interval, approximately 133 ms at 60 FPS, before additional scattering iterations. Budget and maximum observed age are reported separately; a fixed work budget does not guarantee a fixed refresh latency under sustained changes.

Priority is newly admitted regions, incompatible/changed geometry, newly relevant direct sources, visible old regions, then background refinement. Include an age-based fairness rule so offscreen regions cannot starve indefinitely. The scheduler tracks backlog and observed age rather than discarding pending work when a new native publication arrives.

Native generation identity is not a reason to clear every lighting texel. The candidate keeps its own compact previous column descriptors and compares current support/extinction on the GPU. Ordinary smoothly changing descriptors enqueue refresh while retaining compatible lighting; large support changes invalidate affected regions and initialize coarse values. The selected contract below defines this classification in physical terms. A content version alone is not proof that geometry is unchanged.

Density changes can alter illumination outside the changed column. Direct-light update demand extends through the configured shadow-influence distance down-light; diffuse demand includes the solver's propagation support. Broad changes may enqueue the whole domain, while frame work remains bounded. Changed geometry and its dependent lighting must never be certified fresh merely because only local density was updated.

Sun/moon direction changes refresh directional visibility. Weather, atmospheric intensity and sky color changes refresh diffuse lighting progressively. They do not discard all history by default. Retained diffuse radiance can lag these inputs; arbitrary RGB intensity ratios must not be used to pretend a mixed-source diffuse field updates exactly.

World/context replacement, incompatible resource layout, shader transport changes and nonoverlapping domain replacement discard incompatible storage. Resize changes view targets without automatically clearing a compatible cloud-space solution. Pause stops weather advancement through the native owner; retained compatible illumination can still be refined under the configured policy.

New or invalid regions use current unshadowed direct illumination and the current coarse environment term, with no retained higher-order contribution. This explicit fallback can look too bright until updated. The selected initial policy switches to completed compatible data without transition smoothing; invalid geometry/history is never blended into a new region. Fallback coverage and age must be inspectable in the existing debug visualizer menu.

## View rendering and temporal behavior

The candidate owns a minimal view integrator and output path. It clips the current camera ray to native support and current scene depths, evaluates density, fetches cached lighting, applies current directional phase response and integrates attenuation/radiance. It then applies the existing atmosphere/underwater and native OIT conventions through public scene contracts or narrowly scoped private adapters.

It does not adopt the existing volumetric reconstruction owner, shadow cache, history textures or its settings record. Candidate view sampling and resolution controls are explicit. Shared camera, atmosphere publication, lighting calibration and encoding helpers remain reusable infrastructure.

The initial view path uses deterministic sampling and no screen-space accumulation, so qualification can reveal whether persistent illumination alone is sufficiently stable. A reduced-resolution path may use a candidate-owned spatial depth-aware resolve. Its sampling artifacts, edge quality and cost are measured rather than hidden behind an inherited temporal filter. A later temporal reconstruction decision requires separate justification.

This proposal does not promise elimination of view integration. Slice rasterization or cell rasterization would still integrate many depth contributions and introduce ordering/overdraw tradeoffs. Those are separate experiments, not additional required renderer paths in this candidate.

## Evaluation and removal criteria

Evaluate complete cost: native observation/descriptor comparison, domain maintenance, direct updates, scattering updates, iteration publication/copying, view integration, spatial resolve, CPU submission and total resident allocation. Include warm stable views, moving camera/wind, evolving weather, low sun, source transitions and cold initialization. Report update backlog and lighting age alongside frame timings.

Compare the existing renderer and candidate with matched cloud inputs, extent and declared settings. Record differences in approximation and view resolution. A faster converged warm frame does not qualify initialization spikes or persistent update backlog. The selected experimental constants below do not establish a performance target or qualified production quality setting.

Automated checks cover specified arithmetic, units, finite values, native support, region mapping, bounded scheduling, source separation, read/write hazards, lifecycle and OIT contracts. They must not enforce approximation accuracy, convergence, noise, image similarity or appearance. Reuse focused fixtures and actual GPU program abstractions; avoid a large duplicated test catalog. Build/test execution uses subagents, cached incremental builds and affected checks. Full packaging and broad measurements belong to final candidate qualification. The user runs visual acceptance; tooling does not launch or close the game.

All candidate resources are released on retirement. Returning to the existing renderer restores its unchanged settings and rendering path. Removal consists of deleting the candidate directories, candidate registrations/settings/debug entries and the single selection integration. No existing cloud shader should contain a candidate algorithm branch, and no existing cloud resource should require candidate state.

## Selected candidate contract

These are initial experimental constants and finite algorithms, not performance or appearance claims. Changing them later requires updating this contract and recording the affected comparison settings. The existing renderer's defaults do not change.

### Native admission and shared boundary

Source inspection establishes the following integration points:

| Existing source | Candidate contract |
| --- | --- |
| `CloudNativeRuntime.PrepareVolume`, `WithdrawForReload`, `VolumeMaximum` | Select, prepare/reset and bound the admitted volumetric consumer |
| `CloudModeOwnership.TryActivate`, release notification | One visible owner; retirement precedes replacement |
| `CloudNativeMapAdapter.CompleteGeneration`, `CloudMapInput` | Borrow matching current native map and committed placement after native OIT .30 generation |
| `CloudVolumeRenderer` admission and native output contract | Candidate OIT .31 submission follows the same scene/frame checks through its own renderer |
| `SceneLightingPublicationRenderer.Assemble` | Admit candidate physical bounds before Opaque scene storage freezes |
| `DepthHierarchyEnablement` and ownership notification | Shared material depth demand follows actual ownership, independent of LumOn/SSAO |

Introduce an internal `ICloudVolumeConsumer : IRenderer, IDisposable` with `Prepare()`, `Reset()` and `Maximum(AtmosphereLighting, localFogMaximum)`. The existing renderer implements this by its existing preparation/reset and `CloudVolumeLighting.Maximum` contracts; the candidate supplies its own bound. No settings record, intermediate texture or integration helper crosses this interface. `CloudNativeRuntime.VolumeMaximum` delegates to the admitted consumer, and the scene publication owner does not learn candidate types.

The selector is `Clouds.VolumeRenderer = Existing | LightingVolumeCandidate`, default Existing, separate from native `cloudRenderMode`. Candidate controls live in their own configuration record. An applied-config notification marks a pending selection; apply it at Before .002 before Opaque storage admission. Dispose the old owner (which unregisters its OIT callback), reconcile native mode, prepare and activate the new one. A change after this boundary waits until the next frame. Native Off/Simple retirement still follows existing ownership rules. No candidate dispatch occurs when unselected.

A failed candidate preparation leaves the native draw unsuppressed. An admitted candidate failure latches its failed state until reset/reload/selection change and suppresses duplicate rendering, consistent with current ownership. A current map is not available at early preparation: prepare programs and support checks then; initialize domain storage at the first certified OIT input. Before initialization completes, the functioning candidate draws its specified coarse fallback through its own view program. Resource failure withdraws that frame and follows the admitted-failure policy, rather than claiming lighting readiness.

Permitted shared infrastructure: `CloudMapInput`/`CloudPlacement` and runtime publication access; `VgeFrameRenderer` camera inputs; `AtmosphereLighting` and the matching aerial textures; `SceneLightingPublicationRenderer` admission; `LightingStorage`/calibration and public scene encoding/aerial helpers; `DepthHierarchyRenderer` current mip zero; native primary visibility/liquid depth and transparent target; GPU owners, shader libraries, typed bindings, pipelines and `RenderingCleanup`. Consult [render enablement](Rendering.Enablement.md) for change-driven demand. The private density/view implementation may inspect existing formulas as specifications, but must not import the current cloud transport include graph or retain its owners.

### Coordinates, coverage and allocation

Let W be the certified native map width, restricted to 1..200 as in current map parameter admission. Let v be camera-relative world displacement. Native local coordinates are `q = v - placement.Offset + (25*W, 0, 25*W)`. Native density addresses `floor(q.xz/50)` and heights `50*map.b .. 50*map.a` in q.y. Runtime metres per world unit is one.

Use unwrapped material anchor `A = 50*(CenterXZ - WindTileXZ - W/2)` in CPU double precision. Material position is `m = q + (A.x,0,A.z)`. Store integer lattice origins separately and subtract them before converting local positions to GPU floats. Do not reduce grid identity modulo 12800; that periodic reduction is only for procedural noise. Fractional wind is already in Offset and is not added again. Wind/recenter changes that produce the same m preserve lighting identity.

Horizontal lighting cells are 100 world units wide, vertical cells 25. The native horizontal material interval is `[A, A+50*W]` on each axis. Snap each grid lower edge down to a multiple of 100 and allocate `ceil(W/2)+1` interior cells per horizontal axis. The extra cell covers any snap misalignment and keeps dimensions constant as the native origin moves between aligned and half-cell positions. There are at most 101 interior cells per horizontal axis. The vertical interval is fixed at q.y `[-400,400]`, giving 32 cells. Node locations are cell centers; add one guard node at each end of each axis. Maximum texture dimensions are therefore 103x34x103, in X/Y/Z order. The guard exists for filtering, never as native density support.

A region groups absolute material node keys by `floor(nodeKey/4)` on each axis, at most 4x4x4 nodes. Allocate `ceil((interiorCount+3)/4)` region slots per horizontal axis to cover any alignment, with eight vertical regions. Maximum region count is `ceil(104/4)^2*8 = 5408`. Unused slots have no jobs. A node has an absolute material lattice key and a domain generation. Exact per-node flags certify direct source channels, diffuse publication and whether the node lies in occupied current support. Regions hold timestamps and scheduling state, not interpolated validity.

A snapped whole-cell shift remaps overlapping lighting lattice keys into the spare texture sets, initializes new/guard nodes and swaps only after barriers. Lighting copy source and destination are distinct. Descriptor history uses the circular addressing contract below and does not move overlapping records. A dimension change retires old textures before allocating replacements; a nonoverlapping move initializes a fresh generation. Native width, owner, world, allocation or producer-executable changes also revoke descriptor/lighting identity, even if dimensions happen to match. Ordinary content generation does not. Allocation never temporarily doubles the stated ceiling. The fixed vertical band does not chase animated heights. Out-of-band density and thin occupied support without occupied valid lighting neighbors use the view fallback; they remain visible.

Lighting interpolation requires all eight exact node metadata entries in the hardware footprint to match the current domain and the requested channel's valid state. For an occupied query, require those nodes to be occupied as well; otherwise use the whole-channel fallback. This deliberately conservative first policy may use fallback frequently for isolated/thin clouds. It prevents invalid or empty node radiance being interpolated as initialized cloud lighting. It does not guarantee absence of leakage between distinct occupied masses. Read metadata exactly and perform one hardware `textureLod(...,0)` per admitted lighting texture, not eight manually weighted lighting samples. Clamp texture addressing, validate world coverage before addressing and initialize guard nodes as invalid. No toroidal storage, mip generation or implicit derivatives.

| Owned allocation | Bytes |
| --- | ---: |
| RG16F direct plus equal-size remap spare | 8*V |
| Two RGBA16F fields: scaled diffuse RGB and exact unscaled node mask in alpha | 16*V |
| Previous-observation and hard-change-reference RGBA32F native descriptors, circularly indexed | 32*W*W |
| Region state and remap spare, 16 uint words per region each | 128*R |
| Fixed work lists, reduction scratch, counters and candidate parameter allowance | 65536 |
| View intermediate textures | 0 |

Here `V = Nx*Ny*Nz` includes guards, `R` is region count. At maximum dimensions, V=360706 and total owned payload is 10,694,704 bytes (10.20 MiB). Set a 32 MiB owned-payload ceiling. Count actual driver allocation separately during qualification; the payload limit cannot certify physical residency. Wrapped native targets and shared depth/atmosphere/UBO services remain borrowed. No column-classification texture, descriptor-remap spare, dedicated node-flag texture, density texture or third diffuse copy is allocated. Scratch must fit its stated allowance; if implementation requires more, revise accounting before allocation.

Validate dimensions, checked byte arithmetic, available 3D texture sizes, texture/image units, compute workgroup/SSBO limits and required image formats through rendering capabilities at preparation. Use continuous `Texture3D` explicitly: the generic factory defaults to a 2D array. A required format/limit failure rejects candidate preparation or initialization; no silent precision/resolution substitution. Dispose partial allocations. Shader compilation and actual format support are implementation evidence, not asserted by this design review.

### Density, ray schedules and source units

Private density follows the existing native support/connected-face and bounded integer value-noise specification, with candidate constants detail on, amplitude .15, edge width 2 world units and noise scale .08. `sigma = map.r/(50*metresPerWorldUnit)` times inward edge/detail modulation, zero outside valid native support. The density multiplier is in `[0,1.15]`. Detail-off is a compiled variant; current native invalid descriptors (nonfinite, negative extinction or upper<=lower) are empty.

Direct integration has 32 midpoint samples over `L=min(1500, horizontal native exit distance)` from an occupied node toward a source. Intervals are uniform over [0,L], with exact midpoint `L*(i+.5)/32`, weight L/32 in world units converted to metres. Density returns zero in gaps or outside current column heights. No main-view clipping, early optical-depth termination, extra direction jitter or root traversal. Accumulate nonnegative optical depth with saturation at 1e30 before exp; clamp transmission to [0,1] before storage. An inactive source has T=1 as fallback; when active, it receives its own scheduling demand. Both meaningful sources remain supported.

Use scattering albedo a=.9 and HG g=.6. These are candidate approximation parameters, not measured cloud material constants. Define an outward lookup direction d from a receiver toward an emitter/sample; light directions l point toward celestial sources. The directional source toward the receiver along -d is

`Q(x,d) = a * sum_sources(E_source * T_source(x) * HG(dot(d,l_source),g))`.

E is published RGB illuminance (lux), HG is normalized per steradian, and Q is RGB source radiance (nits). This sign convention matches viewing toward the sun producing the forward-scattering peak. The same Q convention applies when a scattering ray looks outward from x to y and gathers light traveling back along -d.

Let D be the isotropic source from environment transport and higher scattering. At a new/invalid occupied query use `D_fallback = a*Environment` and T=1. Environment is the already published E/pi used as an isotropic incoming radiance approximation; do not introduce another weather dimming or irradiance conversion. Empty points contribute no scattered source. A valid view source is `Q(x,viewDirection)+D(x)` with no additional environment or old approximate octave.

For each updated occupied node x, gather the six axis directions (+/-X,+/-Y,+/-Z), weights 1/6. For each d use `L=min(300, horizontal native exit distance)` and 12 uniform midpoint intervals. At each midpoint y, alpha=`1-exp(-sigma(y)*dsMetres)`, with the same finite optical-depth arithmetic. Accumulate

`I_d = sum_i(T_before_i * alpha_i * (Q(y_i,d)+D_read(y_i))) + T_end*Environment`

then `D_write(x) = a * (sum_d I_d)/6`. This includes incident environment once at each ray endpoint; reaching the 300-unit truncation distance uses that same environment boundary approximation. The vertical lighting-band boundary is not a density boundary: rays continue sampling native density and use fallback lighting outside cached coverage. There is no wrapped transport and no secondary direct ray march inside Q: it reads published direct transmission or uses T=1 fallback. Each occupied output replaces its prior D; it does not add the entire previous value again. Empty output nodes store zero and have occupancy false.

This finite angular closure does not claim exact cloud multiple scattering or convergence quality. The extra factor a at the receiving node represents the next scattering event; Q already contains the emitter's first scattering albedo. It cannot be replaced by a blur without changing this contract.

### Physical bounds and coherent publication

Retained daytime illumination can outlive a dimmer current publication. Candidate `Maximum` therefore includes retained-source bounds, not only current sun/sky values. Before Opaque, maintain CPU componentwise high-water envelopes of solar/lunar and environment over the current storage lifetime, including the current completed publication. Do not wait for a GPU readback to discover retained maxima.

With `pMax=(1+g)/(4*pi*(1-g)^2)`, let scalar `Qcap = a*pMax*(peak(E_sunEnvelope)+peak(E_moonEnvelope))` and `EnvCap=peak(EnvironmentEnvelope)`. Set `Dcap = a*(Qcap+EnvCap)/(1-a)`. This deliberately loose nonnegative bound covers initialized D and the recurrence because ray integration weights sum to at most one. Clamp finite computed D componentwise to [0,Dcap] before storing, making the cap part of the finite algorithm and preventing accumulation roundoff from exceeding storage admission. Nonfinite outputs invalidate their region and use fallback, rather than publishing NaNs. Use scaled RGBA16F RGB with the encoding and decoded-value bounds below; alpha carries the unscaled node mask.

Diffuse storage has one positive power-of-two scale S per complete field, measured in nits per stored unit. Encode `Dencoded = D/S` and decode `D = clamp(textureLod(field,uv,0).rgb*S,0,Dcap)` before all view or scattering use. A uniform scale preserves hardware trilinear interpolation. Half-float rounding can move a stored value above its pre-store physical cap, so the decoded clamp is required as well as the existing pre-store clamp. Never scale, clamp as radiance or interpolate alpha for validity. Quantization and very small values rounding to zero are accepted representation tradeoffs, not grounds for increasing sample budgets or adding approximation-quality tests.

At initialization choose `S = 2^ceil(log2(max(1,Dcap)/32752))`, calculated in CPU double precision and admitted as an exactly representable float32 power of two. This places the encoded cap at or below 32752, leaving headroom beneath half-float overflow. Reject nonfinite caps, scales, reciprocals or decoded bounds that cannot be represented by the float32 calculation path. Storage scale is independent of camera exposure, scene encoding and atmospheric lookup encoding.

As the lifetime source envelope grows, the desired scale may increase to the next required power of two; never shrink it while retaining the field. Every published field carries its own immutable scale. During the existing full diffuse-copy pass, re-encode untouched texels using the old field's scale and the new destination scale; scheduled updates perform float32 physical calculations and encode with that same destination scale. Publish the destination texture and scale together only after successful completion and barriers. Until then, all readers use the old field and its old scale. If there is no diffuse batch, defer the scale change until the next batch; there are no new values to encode in the meantime. Remapping preserves the source field scale. Failed updates retain the old coherent field/scale pair. Rescaling uses the already allocated spare and copy pass, not a third field or a history clear. The scalar metadata fits the existing parameter allowance.

Reserve `Qcap+Dcap+ReconstructionMaximumNits+localFogMaximum` through `LightingStorage.UpperBound`, with the existing volume accumulation margin `1+16384/2^23`. Check double-precision cap calculations and float representability at preparation. Unrepresentable inputs reject submission. Exposure never changes stored D. High-water envelopes may decrease only when all associated retained lighting is cleared. This is conservative and can reserve too much scene headroom after a bright period; report it, do not silently replace it with current-only bounds.

Freeze current source directions/intensities, envelope and settings before Opaque; OIT validates the same admitted atmosphere/scene contract. Source changes arriving later defer to the next frame. Current native map descriptors are still observed after native .30 generation. Cached illumination is explicitly approximate relative to that map.

Each node stores the numeric integer mask `occupied | (solarValid<<1) | (lunarValid<<2) | (diffuseValid<<3)` in alpha, giving values 0..15 exactly representable in half-float. This is numeric conversion, not float bit reinterpretation. Fetch masks with `texelFetch(field,node,0).a` and convert to uint; hardware-filtered samples contribute RGB only. Domain generation and absolute-key identity remain in the field publication/region records, not packed into alpha. Newly admitted and guard nodes start at mask zero. Invalidating a channel clears its bit; disappearing support clears all bits. RGB at invalid nodes is ignored and may be zeroed during initialization.

At each eligible OIT callback: finish prior readers through the existing ordered submission/barrier contract, compare descriptors/remap, then run a separate node-owned occupancy/invalidation pass. It loads the published RGBA texel, preserves encoded RGB and updates only the mask. Multiple column changes first coalesce into region demand; they never race to write the same node. These mask edits occur outside any diffuse/view sampling pass.

Update scheduled direct regions next. A direct compute pass has exactly one invocation per admitted source-node, writes that source's transmission and performs an image-load/store of its node's RGBA texel, preserving RGB and the other mask bits. Solar and lunar passes execute separately with barriers between them so they cannot overwrite each other's read-modify-write changes. Complete image-write and texture-fetch barriers before diffuse or view readers. This is an explicitly ordered in-place metadata update, not simultaneous sampling and writing of the published texture. On failure withdraw candidate publication; do not expose a valid mask for an incomplete direct result.

For a diffuse batch, copy the full published RGBA field to its spare using the existing typed compute copy: convert RGB only if the destination scale changes and copy alpha exactly. Overwrite selected nodes reading the old field; preserve occupancy/direct bits in the destination and update only diffuse validity with the completed RGB result. After barriers publish the destination field and scale together and swap. Empty selection performs no diffuse copy/swap; completed direct and invalidation mask edits remain in the current field. Copy traffic is 16*V bytes read+write per batch, about 5.50 MiB at the maximum grid. Count the additional RGBA read-modify-write traffic of direct/metadata passes separately during qualification; packing flags saves storage but does not establish an overall bandwidth or timing gain. A failed diffuse batch cannot publish its spare. Failure handling withdraws the frame, and any retained old field is eligible only for still-compatible regions.

Keep the alpha-bearing fields allocated when diffuse transport is disabled but cached direct lighting still requires node masks. Such frames run no scattering/refinement batch and view lighting uses the coarse diffuse fallback; direct flags still use the ordered update path above. Initial allocation accounting retains the two fields in either mode rather than promising a separate flags-only layout.

On a lighting-grid remap, use the spare direct and RGBA diffuse/mask fields as destinations, initialize new nodes with zero masks, preserve retained masks and swap together after barriers; subsequent diffuse batching may reuse the now-spare field. Descriptor observation follows circular addressing without a copy pass. Descriptor comparison writes coalesced region demand for the current native window. There is no overlapping lighting relocation or partially visible texture swap. Physical fallback values are evaluated from the current frozen source at lookup, so invalid texels do not need a full-volume rewrite on every atmosphere change.

Region state also remaps into its explicitly allocated spare by absolute region key. Copy overlapping records with their timestamps and revisions unchanged. A newly admitted node inside a retained region makes that region dirty with new-node priority; retain any older pending demand and node data. Entirely new regions initialize zero last-update timestamps and pending current demand. A region is fresh only after all its admitted nodes have completed the channel update. Publish region state with the remapped fields, translate a round-robin cursor by its absolute region key where overlapping, and otherwise continue from the next retained key in cyclic order. Do not restart at index zero on every small shift. The 43-frame bound below applies to a fixed admitted domain; continuous admission of new regions is measured separately.

### Change classification and bounded scheduling

Both descriptor textures are W-by-W circular stores addressed by stable integer material-column key. For native texel i, define `K = CenterXZ - WindTileXZ + i`; its material lower corner is `50*(K-W/2)`. The slot is componentwise positive modulo `K mod W`, including negative keys. W is fixed within a domain lifetime. Every W-by-W admitted key window maps bijectively to these slots, so there is exactly one writer per slot in an observation pass. Access descriptors with exact texel/image operations, never hardware filtering.

Retain the last successfully observed key-window origin and domain identity in publication metadata within the existing parameter allowance. A current key may reuse a slot's history only if it lies inside that previous window and the domain identity matches. This window-membership check establishes the slot's old identity without a separate per-slot key texture. Newly entering keys initialize both observation and hard-change reference records from current input and request new-column invalidation; stale records from a departed key are never compared as the new key's history. A width/domain change or nonoverlapping replacement revokes the old window before observation. CPU integer arithmetic computes origins, overlap and positive-modulo offsets without converting absolute keys to float; GPU comparisons use bounded relative coordinates.

For each current slot, its single invocation reads its old observation/reference before writing replacements. It compares current native data against that old history only when identity is valid. If the slot replaces a departing column, derive the departed key from the previous window and slot, and enqueue its removal influence as well as the entering column's influence before overwriting. These operations read no neighboring descriptor slots. Coalesced region demand is processed in later barrier-separated passes. Other descriptor consumers cannot run concurrently with observation. Publish the new observed-window header only after the entire pass and its barriers succeed; a failed pass revokes descriptor history before recovery, since an in-place update cannot be rolled back. No relocation or staging buffer is needed.

Keep previous observations and hard-change references on the GPU and compare every current native texel once per publication, treating unsupported/out-of-map cells as empty. Their RGBA record is `(extinction, lowerHeight, upperHeight, descriptorValid)`; the final component is exactly zero or one and distinguishes rejected native input from a valid empty column. The classification is deterministic:

- Any finite numerical support/extinction change requests refresh. Unchanged generation/content does not fabricate work; generation identity alone does not clear lighting.
- Empty/occupied transition, invalid-to-valid transition, either height endpoint moving by more than 25 world units, or an extinction change greater than 25% relative to `max(referenceSigma,newSigma,1e-6 inverse metres)` invalidates local covered regions plus their filter-neighbor regions. Compare changes against both the previous observation and the retained hard-change reference. Initialize that reference for a new material column, and advance it only after scheduling local invalidation. Thus many small changes cannot evade the incompatibility threshold indefinitely; this reference is a conservative invalidation anchor, not a claim that every region was last solved at that descriptor.
- Smaller changes retain illumination as dirty, never fresh. Node occupancy/coverage is checked against current native support before lookup, so disappeared geometry is not rendered from history.

Classification exists only in the comparing invocation's local variables. Emit local invalidation/new-column demand directly with `atomicOr` into an observation-demand word in each affected region record. Include regions intersecting the changed old/new support and their filter-neighbor regions; a departing slot emits demand for its old material footprint before replacement. Multiple columns may target the same region, but no comparison invocation writes node masks, timestamps or non-atomic region fields.

Any change also atomically sets a domain-wide refresh bit in the existing fixed scheduler header. This avoids every changed column independently looping over the entire region array. Before comparison, clear the per-observation demand words and header bits in the existing bounded scheduler preparation; retain persistent dirty state, first-pending ages and cursors. After the descriptor pass, issue an SSBO barrier. The existing one-invocation-per-region scheduling pass merges local observation demand and the global refresh bit into persistent jobs, updates pending revisions and initializes first-pending age only for newly pending work. It also exposes local invalidation demand to the later node-owned mask pass. Issue the corresponding barriers before node invalidation and job consumers. Do not clear observation demand until its consumers have finished; failure follows the existing publication-withdrawal policy. There is no intermediate per-column classification allocation or separate classification-consumption pass.

To keep the first scheduler small and conservatively cover nonlocal effects, any changed native density marks the whole admitted direct and diffuse domain dirty. This is a superset of the 1500-unit down-light and repeated 300-unit diffuse influence. Local hard changes invalidate local filtering footprints; dependent remote lighting remains explicitly stale until refreshed, not certified fresh. Any published direct or diffuse batch likewise marks diffuse dependents dirty for later iterations. Repeated dirty marking coalesces and never resets a queue's age/cursor. This trades unnecessary refresh work for a bounded simple dependency policy; localized influence optimization is outside the initial candidate.

Region state is a fixed 16-word record: domain generation, flags, per-source last-update frame (2), diffuse last-update frame, first-pending frame for direct sources/diffuse (3), completed observation revision per channel (3), pending revision per channel (3), an observation-demand word and a per-channel arithmetic-failure mask. The final word uses bits zero/one/two for solar/lunar/diffuse failures; a solver resets its own bit before a selected region batch. Any failed node invalidates that region's channel footprint, and completion leaves the channel pending instead of advancing its stamp. These revisions describe scheduling/publication, not stored descriptor snapshots. The two descriptor textures above retain the actual prior observation and hard-change reference. Advance the prior observation after each completed comparison; do not advance the reference merely because a new generation arrived. Wraparound of frame/revision counters causes an explicit metadata reset rather than ambiguous age ordering.

Direct budget is 512 region-source jobs per eligible frame, at most 32768 source-node integrals and 1,048,576 density samples. Diffuse budget is 256 region jobs, at most 16384 nodes, 98304 rays and 1,179,648 midpoint samples. Partial edge regions use only their interior nodes. These limits exclude linear descriptor scans and field copies, which are separately bounded by W,V,R and measured. No path increases integration counts to meet an approximation gate.

Half each budget is a persistent round-robin scan of dirty jobs; the remainder selects ordered priority buckets: new/incompatible, newly active source, visible stale, other stale/refinement. Within a bucket use oldest pending frame then lattice index. Visible means region AABB intersects the current view frustum, not that its illumination is needed only on screen. Work selection uses fixed GPU lists/counters and fixed bounded dispatches with an admitted job count, avoiding synchronous CPU map readback. A no-change, fully refreshed diffuse domain is still revisited round-robin to refine the recurrence. Direct revisits only occur on dirty demand.

The selected implementation uses a bounded single-invocation selection pass: scan each job once for fair allocation, then retain the best remaining candidates in a reusable bounded priority heap. This avoids a full-region scan for every output job. The scratch header occupies uint words 0..63; direct jobs occupy 64..575 and encode `regionIndex*2+source`, while diffuse jobs occupy 576..831 and contain region indices. The shared priority heap starts at word 832 and holds at most 512 entries. Header words 4/5 hold admitted direct/diffuse counts, 6/7 hold backlog at selection, and 8/9 hold the greatest actual-update age among pending direct/diffuse work. Per-region timestamps and revisions remain authoritative after completion. All lists, header, heap and reserved reduction storage fit the fixed allowance; 2048 bytes of that allowance are reserved for parameter blocks. Selection alone never advances a completion stamp: only the barrier-separated successful-channel completion operation does so, and it marks diffuse dependents dirty for the next update.

At the maximum 5408 regions, fairness alone revisits diffuse work within ceil(5408/128)=43 eligible frames and two active direct channels within ceil(10816/256)=43. This is about .72 seconds at 60 FPS, not an eight-frame guarantee. Dirty dependencies and paused/skipped frames can extend freshness/convergence latency. Report time since actual update, current dirty state and backlog separately; resetting the clock when merely enqueuing work is forbidden.

Any changed source direction dirties that source's direct domain and the diffuse domain; no per-frame history clear. Intensity/environment change dirties diffuse only, while current intensities immediately weight direct lookup. A source going from zero to nonzero invalidates its old direct channel and prioritizes it; old mixed-source D remains compatible but stale. No RGB ratio correction is applied. World/context, density-algorithm/settings changes, incompatible layout or shader transport revision clear retained illumination. Pause may continue refinement against unchanged native input; lack of a certified input skips work without advancing update age as if it had executed.

Initial transition policy is immediate use of completed compatible channels, with no extra transition texture or temporal blend. Fallback is T=1 and D=a*Environment, representing zero retained higher-order light. A hard-invalid footprint always uses fallback. Age, dirty state, channel coverage and fallback proportion are debug outputs in the existing menu, not a separate visualization setting.

### Deterministic view and initial controls

Choose full-resolution direct OIT submission for the initial candidate. There are no intermediate view textures, spatial resolve or screen-space history. This isolates cloud-space lighting behavior; it does not promise equal speed to the existing quarter-resolution renderer. Comparisons must include matched full-resolution controls and clearly label default-to-default resolution differences. A reduced-resolution resolve is deferred rather than left as an implementation choice.

Compute current map minimum/maximum valid heights on the GPU into a small candidate-owned reduction record; do not allocate or consume the existing height hierarchy. This reduction fits the fixed scratch allowance. Intersect its envelope, the finite native horizontal domain and min(material depth, native visibility depth, liquid depth). Use N=`min(96,max(1,floor(96*min(1,L/10000))))` uniform midpoint samples across the resulting span. At positive density, accumulate alpha, radiance and native bin products; stop only after a completed step with total T<.005. Settings permit stop=0 as a compiled no-termination variant. The sample cap, budget distance and explicit full-resolution mode are candidate controls, not changes to current defaults.

Native per-step bin weights use the same documented adjusted-depth and bell-curve formulas; their three transmission products and total transmission are independent of exposure. Apply atmosphere/underwater response once at the opacity-weighted representative distance, then encode once using the frozen scene scale. Emit the native Reveal/Reveal/Glow/Add/Add/Add products with glow RGB zero, never conventional alpha blending. Zero opacity yields neutral outputs. The source is Q+D, so no additional environment or historical approximate octave is added.

Initial user-visible controls are the renderer selector and a separate candidate record with view maximum 96, view budget distance 10000, stop .005, detail enabled, direct self-shadowing enabled and diffuse transport enabled. Numeric validity ranges mirror the specified hard caps: view maximum 4..96, budget distance 50..10000, stop 0...005; grid, transport quadrature/albedo and scheduler budgets are fixed experiment constants. Disabled detail, direct self-shadowing, diffuse transport and termination remove their paths with compiled options. Direct-off uses T=1 and allocates no direct jobs; diffuse-off uses D=a*Environment and runs no scattering jobs. Config changes affecting density or transport reset the relevant candidate solution at the safe boundary; view-only changes keep compatible cloud-space fields. No migrations.
