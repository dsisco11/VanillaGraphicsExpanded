# Cloud rendering

VGE owns separate Simple and Volumetric shaders while native Vintage Story weather owners
produce geometry and column controls. The native `cloudRenderMode` preference remains authoritative:
0 is Off, 1 is Volumetric, and 2 is Simple. Rendering works independently of LumOn and uses the
same [completed atmospheric publication](PBR.Atmosphere.Publication.md) in both PBR selections.
Implementation, current sampling and remaining acceptance/qualification work are tracked in the
[realtime cloud checklist](PBR.CloudRealtime.todo). Height-summary ownership is described below.

## Scheduling and ownership

`CloudModSystem` maintains `CloudNativeRuntime` at Before order .002, after shared camera
publication and before physical scene-lighting range admission. Preparation demand-loads the
selected dedicated shaders. A functioning prepared consumer transfers visible ownership;
unavailable initial preparation leaves native drawing intact. Once admitted, missing per-frame
inputs omit the owned contribution. Draw failures keep native drawing suppressed until reset,
reload or mode release, avoiding a second draw with a different lighting convention.

Native `FluffyClouds.CloudRendererMap` retains its OIT .30 callback, worker, uploads, texture
allocation and shader subscriptions. Its callback alone ticks volumetric weather and generates
the map. VGE's independently registered `CloudVolumeRenderer` draws at OIT .31. A direct gate
on the current native volumetric callback suppresses its visible draw while the replacement
owns that mode. Native settings watchers can freely register the native callback again; its
gate still applies. VGE does not tick this producer or modify its registration.

Simple rendering intercepts the current `CloudRenderer.OnRenderFrame` at its native OIT .35
position. It calls native `CloudTick` once when unpaused, then borrows the resulting mesh and
committed placement for synchronous drawing. The native 12-short, 24-byte instanced stream is
used directly. Rebuilds may replace/dispose the mesh during the tick; VGE never retains that
borrow across callbacks. Zero-width or unavailable camera input skips drawing after the update.
Pause retains geometry and uses current camera transforms without ticking. Native time-speed
scaling stays inside the selected native tick, with the original delta time forwarded unchanged.

Mode changes retire the previous owned consumer before another can activate. Disable releases
VGE callbacks and suppression, so the native path follows the current preference. No saved mode
is restored and no cloud preference is written. Context changes revoke map certificates and
retire owned consumers; world teardown also drops native references. New world identities and
previous-world root rejection prevent stale borrowers from reacquiring old storage.

## Borrowed map contract

`CloudNativeMapAdapter` observes generation, allocation and raw-upload boundaries. Allocated
textures are insufficient: publication requires validated dimensions/attachments, both initialized
raw inputs, a linked native program and a successful generation. Failed in-place work withdraws
availability. Generation completion copies matching committed placement; consumers require the
same frame, owner, world, allocation and shader certificates immediately before submission.
Ordinary completed worker commits advance content identity; texture names alone certify nothing.

VGE samples `TextureMap` read-only. Its red component is extinction control per native tile unit;
blue/alpha carry lower/upper vertical bounds divided by tile size. The inspected 1.22.7 GPU
producer/consumer hardcode 50 world units. Runtime uses one metre per world unit:

```
sigmaBase = map.r / (50 * metresPerWorldUnit)
T = exp(-sigmaT * segmentLengthMetres)
```

Red may exceed one and is never converted from alpha. It already includes native fading and
perception effects. Negative/nonfinite inputs are rejected. The finite native grid bounds the
supported domain; no border stretching or invented weather extends it. VGE never reads worker
`Tiles`, reads map texels back to the CPU, copies the map for its immediate draw, or generates
a complete density volume. Native `TextureCol` remains allocated/generated but is not a VGE
physical-lighting input. Native owners alone dispose their resources.

## Cloud height summaries

`CloudHierarchy` builds coarse RG32F min/max height summaries at the volumetric draw boundary,
after native map generation and before reduced tracing. The native map remains the logical leaf
level; owned mip zero reduces 2-by-2 native columns. Higher mips combine minimum lower and maximum
upper bounds in world units relative to the cloud layer. Logical power-of-two padding contributes
empty intervals without fetching out-of-map texels. Empty summaries use positive/negative infinity.

Each GPU generation rebuilds the summaries even when CPU controls were not uploaded. Compatible
frames reuse storage; source lifetime changes, reload, reset and disposal retire it. Publication
requires completed image-write barriers, sampler visibility and the exact current native descriptor
plus the reduction executable revision. Source observation precedes depth/lighting admission so
an unavailable draw cannot retain an invalid source lifetime. Native textures remain borrowed.
Unavailable, mismatched or failed construction selects direct native support checks at the same
scheduled positions. A certified root may reject a complete primary ray, but never narrows its
integration span or changes its sample count, spacing or blue-noise offset. Its entry/exit distances
bound a conservative index range on that original lattice, skipping native column queries in
proven-empty air before and after the supported interval. Neighboring indices are retained around
the range endpoints to admit floating-point boundary uncertainty. All surviving positions, segment
weights and OIT depths still use their original indices. Secondary solar/lunar
rays use the root height interval to reject rays or remaining positions outside all supported cloud heights.
Their finite horizontal domain
and independent shadow distance never use camera visibility or scene-depth clipping. The full-view
reference uses the same anchored shadow schedule without requiring a height-summary resource.
Root clipping expands the height interval by eight binary32 epsilons times the maximum of one,
the absolute root heights and the ray's absolute vertical coordinate bound over its finite span.
This admits boundary uncertainty, including near-parallel rays, instead of losing inclusive native
support. Near-parallel rays compare both finite-span heights instead of assuming constant height;
nonfinite root heights retain direct checks. The tolerance does not depend on grid-crossing clocks.
No column DDA or repeated root-to-leaf descent participates in primary or retained secondary transport.
Cache generation has its own bounded native-support walk, described below. Lower summary mips are
reduction intermediates for the root, rather than a second integration schedule.

`cloud_volume_light.glsl` uses deterministic arithmetic midpoints of quadratic distance intervals.
For distance D and count N, boundaries are `D*(i/N)^2`; each midpoint evaluates shared density,
weighted by its complete interval length in metres. Finite-domain rejection skips unsupported
positions without rescaling D or redistributing samples. Optical depth stays finite, with no
optical-depth early exit. This marching path serves the full-resolution reference, the uncached
light and unavailable-cache fallbacks. It does not publish cloud shadows onto scene receivers.

Reduced tracing uses a same-frame light-space attenuation cache by default for the stronger
published solar/lunar source. `Clouds.ShadowQuality` selects disabled self-shadowing (0), or logical
256x256 (1, default), 512x512 (2) and 1024x1024 (3) light-space maps. Zero retains unoccluded direct
lighting and the approximate scattering octave for both sources, with no cache generation/lookups
or secondary shadow marching, including in the full-resolution reference.
Reference and reduced transport share the `VGE_CLOUD_SELF_SHADOWING` compile-time option;
quality zero selects a precompiled variant with shadow marches and cache lookup code excluded.
The CPU owner skips attenuation generation and releases its resources when disabled. `CloudShadowCache`
owns a persistent square RGBA32F texture and regenerates it before each admitted trace, using at
most 64 density evaluations per texel. RGB stores front depth in world units along the current
light-space ray, nonnegative mean extinction in inverse metres, and dimensionless maximum optical
depth. Alpha is unused; source identity and publication are discrete whole-cache certificates in
the existing CPU binding and traversal inputs. Payload sizes are 1, 4 and 16 MiB respectively,
including the unused fourth format channel. RGBA32F supports compute image writes and linear
sampling. The lookup uses one hardware-bilinear parameter sample at explicit LOD zero.
Changing quality retires incompatible
storage; disabling caching releases its resources and skips generation. Projection covers the
finite native map and certified root heights independently of scene depth or camera visibility.

`CloudVolumeParameters` transports an independent tangent for each celestial source between
lighting captures. The shortest rotation carries that tangent through both vertical poles without
switching reference axes. Initialization chooses a well-conditioned axis once; a discontinuous
near-antipodal direction change keeps the previous tangent projected into the new plane (an exact
antipode uses a half turn about that tangent). The shader reorthogonalizes the staged tangent and
uses its cross product with the current light for the second axis. Solar and lunar orientations
advance independently even when not dominant. A dominant-source transition selects that source's
current frame and regenerates its attenuation; it does not rotate or reuse the other source's data.
This small geometric state contains no opacity, publication certificate or temporal shadow history.
It is owned by the parameter/program lifetime; recreation may choose a new initial grid orientation.

The grid is centered on the current native map's local horizontal midpoint and certified height
midpoint. Each axis extent is the full native box's projected half extent, so off-camera support
remains covered. Texel centers subdivide that current box without world-grid snapping: native
recentering moves the box with the committed map, wind follows the existing material coordinates,
and changed root heights update its center and scale immediately. Continuous motion can therefore
change sample phase and footprint; orientation transport removes the axis-switch discontinuity,
but does not promise stationary shadows under all moving inputs. Retaining an old origin or extent
would need additional coverage and source certification. Generation and lookup instead derive the
same current frame from the same staged parameters and root. Every admitted draw rebuilds current
attenuation, and a missing or revoked publication remains unavailable regardless of orientation.
These geometric changes do not reset visible-cloud temporal history for ordinary weather or light
motion; its existing lifecycle, medium and reprojection admission rules remain authoritative.

Generation estimates crossed native columns from the root-clipped chord as
`1 + ceil(abs(light.x) * length / 50) + ceil(abs(light.z) * length / 50)`.
Estimates above 16 retain the original 64 uniformly weighted midpoint evaluations across that chord.
This avoids expensive native-grid scans on long or grazing rays; narrow or disjoint features can
still be missed by that finite lattice. Eligible shorter rays measure occupied geometric arc length,
validate their complete stratum schedule without density evaluation, then stream its integration. Empty native columns and vertically
disjoint intervals do not spend density evaluations. Exact-zero slab clipping admits small nonzero
directions that enter the domain or a column's height interval over the finite ray.

For occupied length L, allocation is `min(64, max(1, ceil(L / 50)))`, with no samples for empty support.
Each midpoint is located on occupied arc length and weighted only by its occupied stratum length,
including strata crossing disjoint support intervals. World-length weights convert to metres exactly
as in other cloud integration. Each support pass has a defensive 401-column limit for the maximum
200x200 native grid. A density-free dry run validates the complete support schedule before
any detailed density evaluation, avoiding per-sample position arrays. If a bounded support pass cannot finish, the same ray uses the
original uniform quadrature over its root/domain-clipped chord; it never spends a second density
budget or publishes an unavailable sentinel among valid texels. Long/grazing chords retain the
same uniform fallback selected by the crossing estimate.

For a supported chord [front,back] and integrated tauMax, mean extinction is
`tauMax / ((back-front) * metresPerWorldUnit)`. Front is the geometric supported chord entry,
including native support whose sampled procedural density vanishes. Nonintersecting projected rays
and zero spans store RGB zero. A nonzero chord with zero sampled density has zero extinction and
tauMax. Optical-depth accumulation saturates at 1e30. Every generated texel is a finite compact
record before the whole texture is admitted; alpha never stores source flags or invalid markers.

The shared full-frequency density definition remains unchanged. A diagnostic generation variant
uses two opposite quarter-texel footprint rays with half the total density budget each. Independently
clipped rays contribute equally weighted optical depth to their containing chord. This approximation
is not selected for production and has no image-quality acceptance threshold.

Lookup hardware-filters the compact RGB parameters before evaluating cumulative optical depth as
`tau(z) = min(tauMax, meanExtinction * max(0,z-front) * metresPerWorldUnit)`.
The finite ShadowDistance response is `max(0,tau(depth)-tau(depth-ShadowDistance))`, evaluating both
endpoints from that same fetched record. Direct and additional-octave transmissions are evaluated
once each from that window depth. Empty records give unit transmission. Separated layers and gaps
are represented by a single mean-extinction ramp; their depth structure is intentionally lost.
This changes the shadow approximation and does not promise equivalent images. Only footprints
between the outer texel centers are admitted; edge-crossing footprints and malformed records use
bounded marching. A linear-clamp typed sampler is used, but coverage checks prevent implicit border
extension. Filtering parameters differs from averaging transmission; no exact area or gap preservation
is claimed. No manual neighboring-ray transmission averaging remains. Source and native
publication must match the complete cache, independently of sampled parameters. The other meaningful
light retains marching; neither physical source is discarded.

Generation uses the trace's current map, placement, detailed density and lighting. Image-write and
texture-fetch barriers precede lookup; native publication is revalidated before submission. Missing
root bounds, failed generation, invalid projection/coverage or invalid records select bounded
marching. No attenuation is reused from an earlier frame. Compatible allocations persist between
draws and retire on source-lifetime replacement, invalidation, resize, reload, disable and teardown.

Source observation precedes foreground-depth and lighting admission. An ordinary content generation
withdraws the old certificate while retaining compatible storage; owner, world, allocation or executable
replacement retires it even if another missing input prevents drawing. Reduced submission revalidates
the native borrow after pass preparation, then selects certified root bounds or direct support immediately before
the trace/history/resolve boundary. A revoked borrow submits none of those passes and invalidates
history. Optional reduction failure leaves direct support checks available until reset; it does not
release visible ownership or admit native duplicate drawing. Resize/reload reset both owned domains;
mode release and world teardown dispose them. Simple and Off allocate no cloud-height hierarchy.

## Density, lighting and scene composition

Volumetric density uses native horizontal footprints and vertical bounds. Periodic procedural
3D noise modulates density inside that envelope; exposed faces soften inward while occupied
shared faces remain connected. Broad faces and stepped cubic masses remain. Noise follows native
placement, integer recentering and committed/residual wind without a second advection.
Consecutive samples in a native column retain its descriptor. Needed neighboring descriptors are
fetched at the current sample, keeping the ray-local cache small across secondary marches.
Neighbor occupancy is retested at each sample height. Faces farther than the current nearest face
or erosion width need no neighbor lookup. Zero detail amplitude skips procedural noise; zero edge
support returns before noise. The procedural noise field and material anchoring are unchanged.
Disabling detail also disables edge erosion, restoring piecewise constant native density. Finite
primary sampling need not integrate a discontinuous column field exactly.

Primary tracing clips the ray to the finite horizontal map and admitted foreground/liquid
distance. For its complete length L, including empty gaps, it selects
`N=min(max,max(min,floor(max*min(1,L/distance))))` and `ds=L/N`. Positions are
`tStart+(i+jitter)*ds`; native support checks reject empty points without reallocating their budget.
Every surviving point evaluates shared density at that position. Positive floating-point opacity
weight evaluates incident lighting; an exactly zero weight skips source evaluation and shadow rays
while retaining density, support, transmission and bucket updates on the complete scheduled prefix.
This also applies when termination is disabled and does not introduce a visibility threshold.
Extinction uses the step length in metres, and native OIT bin weights apply at each primary step. Uniform density
over the complete span retains analytic attenuation when early termination is disabled or never
triggers; a terminated ray represents its integrated prefix. A finite lattice can miss narrow features.
Primary integration stops after a completed contributing step when overall transmission is strictly
below `StopTransmittance` (default .005, supported 0..0.005). Zero disables this exit, including when
transmission is exactly zero. Radiance, bucket products and support bounds include the stopping
step; no residual-support traversal extends metadata behind it. Shadow marching uses its complete
bounded schedule without an optical-depth threshold. Changing this control invalidates history.

Contributing distance metadata covers contributing steps clipped to the admitted ray span,
with up to one-step geometric uncertainty.

Reduced tracing shares one spatial blue-noise offset along each primary ray. A cloud-owned
32-by-256 R32F texture stores eight tileable 32-by-32 void-and-cluster rank masks. Full-view pixel
coordinates select the spatial position and the frame index selects a mask modulo eight, advancing
independently of reconstruction phases. The history-free reference uses midpoint primary samples.
Secondary samples have no spatial or temporal jitter. The primary noise texture persists across
ordinary frames and retires with reconstruction resources on resize, reload or disposal. Sampling
jitter does not change the density definition or add density noise.

View and bounded secondary solar/lunar marches share this density definition. HG single
scattering uses g=.6 and one isotropic approximate multiple-scattering octave with contribution
and occlusion .5/.5. This approximates higher-order transport rather than a second explicit
physical bounce. Environment uses the published E/pi response. Once per primary ray,
the shader bounds each light by its unoccluded single-plus-octave response. If every channel is
strictly below 1e-5 of the matching environment channel, it retains the unoccluded response and
omits the shadow march. This bounds the omitted attenuation by 0.001% of the environment per
channel. A zero environment channel prevents this shortcut, and meaningful night sources retain
their normal shadows. The weak constant response joins the environment outside the sample loop.
Sampling controls, density and optical products are unaffected. Simple uses physical directional
and environment inputs without volume marching, self-shadowing, 3D noise or temporal resources.
Native brightness/daylight/base-color attributes are not physical incident illumination.
Simple retains native shape/coverage controls and applies perception's factor 20 locally without
temporarily changing engine uniforms.

Simple applies its existing surface response. Volumetric tracing first accumulates unencoded cloud
radiance and the opacity measure `w_i = T_i * alpha_i` at each contributing primary point. Its
representative distance is `sum(w_i * t_i) / sum(w_i)`. Empty points and zero-weight intervals do not
move that distance. After the march, the existing affine aerial or underwater/local-fog response
is applied once to `radiance / sum(w_i)` at the representative distance, then multiplied by
`sum(w_i)`. This scales incoming air/fog light by cloud coverage. A ray without contributing
opacity keeps neutral output. Encoding occurs once at direct submission or later full-view resolve.
The reduced physical payload already contains this medium response; temporal and resolve passes
do not apply it again. Shader reload retires incompatible history. Routine lighting and fog changes
retain history, bounded by the current neighborhood; switching between aerial and underwater
transport invalidates the previous medium mode.

The compact payload shares one radiance across all native buckets, so this mean is shared rather
than adding bucket-specific colors or representative distances. Each contributing step still
updates all three native bucket transmissions at its own depth, and support/clipping metadata is
unchanged. Distance-varying atmospheric response is approximated at the mean; source-distance
correlations and deep or disjoint support can differ from per-sample transport. Diagnostics and
user appearance review qualify that tradeoff without automated approximation-quality thresholds.
Solar and lunar phase terms are fixed for the view ray and evaluated before its primary loop;
when self-shadowing is enabled, optical depth uses same-frame cached attenuation for the selected
reduced-trace source and independent bounded marching for the remaining paths. Quality zero uses
the unoccluded directional response for both sources.

Both modes use the frozen frame's scene-storage encoding. Physical source bounds join admission before Opaque; exposure
does not affect attenuation, depth, coverage or physical history. The native pre-lit map color,
sunset coloration and duplicate weather attenuation do not enter owned shading.

Cloud contributions use the existing Transparent six-attachment OIT contract: three bin
revealages, overall transmission, glow metadata and three weighted RGBA accumulations. Volume
integration retains interval-dependent bucket weights instead of assigning one depth to the
whole cloud. Native Reveal/Reveal/Glow/Add/Add/Add blending and resolve compose clouds with
sky, sun, moon, stars, particles, translucent geometry and water. Terrain depth occludes Simple
geometry; volume rays clip to current opaque and liquid depths. Simple preserves its adjusted
OIT depth and separate liquid/underwater treatment. Existing bucket sorting is approximate.

Environment consumers, including water, use the shared completed atmosphere response. Clouds
do not publish cast shadows or add a second weather dimming to that response. There is currently
no directional reflected-camera cloud consumer in the owned water renderer: reflected cloud
imagery remains an explicit unsupported integration dependency. Main-view depth/history is never
substituted for a reflection view. Cloud cast shadows on receivers or atmosphere await the
virtual-shadow-map owner. The internal attenuation cache has no receiver/atmosphere consumer and
does not introduce an earlier native producer callback.

## Shared depth inputs

Volumetric tracing, its full-resolution reference and reconstruction borrow mip zero of the
shared `DepthHierarchyRenderer` material-depth texture. The PBR composition root owns
`PipelineEnablement`, whose shared Rx conditions track SSAO demand, frozen LumOn generation and
actual Volumetric ownership notifications. `DepthHierarchyEnablement` binds their combined cached
condition to the depth renderer. See [render enablement](Rendering.Enablement.md). The hierarchy is available
with LumOn and SSAO disabled when Volumetric clouds own rendering. Simple and Off do not
independently request it. Disabled transitions withdraw publication and
release depth resources once; stable frames read only the cached enablement flag. The native
postprocessing flag toggles within every screen frame and is not a persistent enablement factor.
The shared owner rebuilds current scene depth once per enabled frame in Opaque before the cloud
OIT callback and retains allocation/lifecycle ownership.
Cloud submission requires matching current frame, camera identity, extent and a valid resource;
a missing or stale publication invalidates cloud history and skips submission.

Material receiver correction removes opaque-pass particle depths for deferred lighting. Clouds
retain primary visibility depth as an additional clip, alongside separate liquid depth, to
preserve particle occlusion. The common shader helper combines material and visibility depth
before tracing or reconstruction. These scene-depth reads explicitly select mip zero without
coarse scene-depth traversal, tile rejection or a shared-HZB format change. Cloud-height summaries
are a separate cloud-owned resource. Shared resource reuse alone does not establish a performance improvement.

## Reconstruction and controls

Volumetric mode defaults to ceil-half width/height tracing with a rotating 2x2 pixel phase,
bounded temporal accumulation and depth-aware full-view reconstruction. `CloudVolumeReconstruction`
owns one immutable nine-layer RGBA32F `Texture2DArray` for the Half selection, with three disjoint three-layer views.
Each view has a persistent framebuffer with its three local layers attached to the payload outputs.
Layer zero stores integrated physical RGB radiance with overall transmission in alpha; layer one
stores three bucket transmissions with primary sample spacing in its fourth channel (zero for
an empty trace). Spacing is support metadata in world units, not an optical factor. Layer two stores first/last
contributing distance and current opaque/liquid depth. The two optical layers share acceptance
and interpolation weights. Temporal sampling binds the current trace and previous history views;
resolve binds the newly filtered history view. Typed view bindings reject overlap with active
framebuffer images, and active target passes borrow both their view and the shared storage owner.

The default Quarter selection traces ceil-quarter view width/height into one three-layer array,
then reconstructs ceil-half view width/height into two alternating three-layer history blocks in a
separate six-layer array. Each frame observes one position in every 2x2 half-grid block; four frames
complete the cycle. The representative full-view coordinates of half-grid history stay fixed.
Odd edges clamp scheduled positions to the actual reconstruction extent; tiny views remain valid.
At each half-grid destination, current depth rejects incompatible trace taps. A bounded 3x3 trace
neighborhood supplies shared optical weights and a current envelope. Sampled positions blend
accepted history with weight .75; unsampled positions reuse accepted history with weight 1, clamped
to that envelope. Without compatible history, compatible current taps supply the reconstruction.
No compatible current support produces neutral OIT and cannot resurrect stale history. Support
intervals are projected between neighboring rays and clipped to destination foreground; metadata
records the destination's current depth. This conservative neighborhood rejection can discard valid
old cloud detail when sparse current observations do not cover it.

The three Half blocks rotate between current trace, previous history and next history after successful
OIT submission and history commit. A skipped or failed draw invalidates content history without
advancing block roles. Quarter keeps its trace block fixed and alternates only its two history blocks. The block cursor is independent of the sampling frame index. Framebuffer
attachments stay fixed as roles rotate; each block caches its trace and temporal PSOs separately.
Internal passes use replacement writes and never sample live native OIT accumulation. Resolve
reconstructs each admitted reduced tap's native accumulations as
`vec4(radiance, 1 - transmission) * (1 - bucketTransmission)` before spatial interpolation.
This retains color/coverage covariance across neighboring taps. RGB encoding follows interpolation
at final scene submission. Non-emissive native glow RGB is zero; its opacity is
reconstructed as one minus overall transmission at submission.

Temporal clamping operates on radiance and attenuation factors rather than their OIT products.
This preserves each unfiltered trace's bucket formula but does not make temporal interpolation
commute with multiplication: changing cloud coverage or lighting can change reconstructed bucket
contributions. Spatial interpolation operates on reconstructed products to avoid introducing that
additional error between neighboring rays. The native full-resolution reference remains available
for quality comparisons.

`CloudTemporalHistory` retains only the previous compatible view certificate. Native placement
differences distinguish camera/cloud movement from grid recentering. Cloud reprojection uses a
rotation-only view with the previous projection, matching camera-relative ray reconstruction.
The native eye translation is excluded; placement deltas supply material/camera translation once.
Shared scene-camera matrices retain their native convention. History requires consecutive
frames and compatible owner/world/allocation/shader, extent/projection, calibration, transport mode
and settings. Routine completed-lighting publications, sun/moon motion, fog changes and monotonic
raw uploads retain history, with current
support/depth and finite-payload checks on the GPU. Cuts, missing input, replacements, mode
release, reload and reset invalidate history. Changing trace scale replaces incompatible allocations. Half accepted history has weight .75, clamped to a
compatible current neighborhood. Support midpoint tolerance includes the sum of current and previous
sample spacings; thickness tolerance includes twice that sum, covering both quantized endpoints.
Opaque/liquid depth rejection and the existing displacement limits remain independent of that
sampling uncertainty. Optical differences are accumulated within the current neighborhood envelope
without a separate attenuation-difference rejection threshold. Spacing remains the current support
receipt rather than a temporal average; Quarter converts contributing intervals into destination-ray
units and retains their maximum. Spatial reconstruction
uses four phase-aware reduced samples and current opaque/liquid depth. Foreground distances
reject incompatible samples; both optical payload images share normalized weights, including clear
samples at cloud edges. Clouds fully in front of both depth clips can be reconstructed across
background depth changes. If bilinear taps are rejected, a bounded 3x3 reduced stencil selects
the nearest depth-compatible payload. If none exists, submission is neutral OIT. Resolve
never evaluates density or lighting. Small features absent from the reduced samples and newly
exposed partial cloud intervals remain reconstruction limitations; reference mode is available
for comparison.

The persisted `Clouds` config and ConfigLib expose:

| Control | Default | Bounds/meaning |
| --- | --- | --- |
| `FullResolutionReference` | false | Full view, midpoint transport, no history or upsampling; overrides trace scale |
| `TraceScale` | Quarter (4) | Half (2) or Quarter view dimensions; reconstruction stays half dimensions |
| `ViewRaySampleMax` | 384 | 4..384 primary points across the complete clipped ray |
| `ViewRaySampleMin` | 1 | 1..min(16,max) points for short positive spans |
| `ViewBudgetDistance` | 50 | 50..10000 world units; distance at which the maximum applies |
| `LightRaySampleMax` | 10 | 2..64 scheduled points per contributing directional light |
| `ShadowDistance` | 1500 | 50..10000 world units; independent of view distance/clipping |
| `ShadowQuality` | 1 | 0 disables self-shadowing; 1/2/3 select 256/512/1024 pixels per axis |
| `StopTransmittance` | .005 | Fractional control, 0..0.005 in .0005 steps; strict post-step view exit, zero disables |
| `Detail` | true | Noise and exposed-edge erosion; false samples piecewise constant native density |
| `DetailAmplitude` | .15 | 0..0.95 bounded interior modulation |
| `EdgeWidth` | 2 | .01..10 world units of inward softness |
| `NoiseScale` | .08 | .001..1 noise cells per world unit |

Cloud settings use a clean break: absent current fields receive defaults; obsolete fields are not
reinterpreted. Nonfinite controls restore
defaults. The immutable selection supplies admission
and submission. Reference mode is history-free, not a claim of converged light transport.
Changing quality or reference selection invalidates incompatible history. Simple ignores these
volumetric controls.

The current 10-point, 1500-unit secondary selection uses a finite approximation. Full midpoint
weights can misestimate short or discontinuous support, and finite distance can omit distant
occluders. Reference discrepancies are diagnostic, without automated approximation-quality gates.
Final selection still requires measured cost and user appearance review.

The 384/2/50 primary selection assigns 384 scheduled positions to rays of at least 50 world units.
Primary and secondary quadrature have independent algorithm-correctness checks. Realtime cost and the final
shipped selection remain unqualified in the realtime checklist. Bounded work alone does not
establish a speedup, particularly on short rays.

Reduced targets and pipelines persist across stable frames. Resize/reload retire their owned
resources; wrappers never delete engine attachments. Allocation is bounded before creation to
view dimensions <=16,384 and nominal trace/history image storage <=1 GiB. The three sets require
144 bytes per half-grid pixel for Half. Quarter requires 48 bytes per quarter-grid pixel plus
96 bytes per half-grid pixel, excluding native producer data and other resources. This is a
calculated texel allocation, not measured resident memory. Full-view resolve is bounded
texture reconstruction; density and light marching are confined to reduced tracing or the explicit
reference mode. Performance requires GPU measurement and is not established by allocation bounds.

## Compatibility and qualification

Native lookup resources and `cloudmap.fsh` remain for retained map production, including its
unused pre-lit color attachment. `SceneColorLegacyPatches` keeps conditional native Simple and
volumetric fragment conversion for current-setting restoration and unavailable initial ownership.
Their geometry stages and native map producer remain untouched. `SceneColorCloudInputs` and
`SceneColorCompatibilityPublication` conservatively retain native color bounds, including
possible mid-frame release, while owned physical bounds are admitted separately.

Headless fixtures qualify map certificates, post-tick borrowing, draw gates, mode transitions,
native registration restoration, teardown, physical transport, OIT payloads, depth clipping,
storage encoding and temporal reconstruction. Shader catalog and output-asset checks qualify
packaged owned binaries.

Automated acceptance covers the specified algorithms, physical units, finite values, attenuation
bounds, clipping, OIT/encoding arithmetic, deterministic replay, resource lifetimes and history
admission. Automated cloud tests enforce correctness without approximation-quality thresholds.
Mixed fixtures retain their independent correctness obligations.

Comparisons with exact or higher-budget transport, reconstructed reference images, temporal means
and frozen noise/RMS baselines may report diagnostics without approximation-quality assertions.
Reference sampling and bucket discretization must be stated when interpreting those measurements.
Repeated eight-frame periods are not independent new samples. Temporal rejection/clamping does not
imply vanishing noise or an unbiased estimator. Appearance is evaluated through user review.

`CloudAccelerationWorkTests` is an opt-in comparison of frozen direct support, conservative root
rejection and cached scheduled-point hierarchy rejection through the production reconstruction scheduler.
It records matched GPU
construction, trace, temporal, resolve and independently measured total costs, plus CPU submission,
adapter identity, allocation reuse and raw samples. Each summary-backed total includes a fresh reduction
for each native generation. Synthetic map/depth authoring and shared native producer/scene-depth work
remain outside both measured paths. Those controlled workloads do not establish live whole-frame
costs or reproduce captured weather. Large separated-height gaps are adversarial shader-domain
inputs, not a claim about normal native weather. Measurement cases are disabled in routine tests.

These receipts establish source/automated contracts, not user visual
acceptance or live frame time. Weather appearance, cubic fluff, motion, in-game lifecycle
transitions and hardware-specific GPU/CPU/memory budgets remain acceptance activities in the
linked checklist.
