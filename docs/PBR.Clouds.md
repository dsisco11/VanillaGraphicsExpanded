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
the map. VGE's selected volumetric consumer draws at OIT .31. A direct gate
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
supported domain; no border stretching or invented weather extends it. Live consumers do not read
worker `Tiles`, read native map texels back to the CPU or copy the map for their immediate draw.
The independent stored-volume generation API described below retains candidate-owned descriptors
and density; the stored-volume consumer uses these owned fields. Native `TextureCol` remains allocated/generated but is not a VGE
physical-lighting input. Native owners alone dispose their resources.

## Cloud height summaries

`CloudHierarchy` builds coarse RG32F min/max height summaries at the volumetric draw boundary,
after native map generation and before reduced or reference tracing. The native map remains the logical leaf
level; owned mip zero reduces 2-by-2 native columns. Higher mips combine minimum lower and maximum
upper bounds in world units relative to the cloud layer. Logical power-of-two padding contributes
empty intervals without fetching out-of-map texels. Empty summaries use positive/negative infinity.

Each GPU generation rebuilds the summaries even when CPU controls were not uploaded. Compatible
frames reuse storage; source lifetime changes, reload, reset and disposal retire it. Publication
requires completed image-write barriers, sampler visibility and the exact current native descriptor
plus the reduction executable revision. Source observation precedes depth/lighting admission so
an unavailable draw cannot retain an invalid source lifetime. Native textures remain borrowed.
Certified root heights intersect the horizontal map and current foreground-depth interval before
primary samples are allocated in both reduced and reference rendering. This envelope includes
empty gaps between native columns or separated layers; no occupied-length prepass is performed.
The resulting interval defines count, spacing, sample positions and physical-length weights.
Certified empty bounds skip the march. Unavailable, mismatched or failed bounds conservatively
retain the full horizontal interval with direct native support checks. That fallback has a different
lattice; it is not an equivalent acceleration of layer budgeting. Bounds are certified independently
of any optional traversal selection, including current GPU-native height animation.
Secondary solar/lunar rays retain their origin-anchored schedule. Their global height rejection,
finite horizontal domain and independent shadow distance never use main-view layer or foreground
clips. The reference uses the same primary interval policy and midpoint jitter, retaining independent
bounded shadow marching.
Root clipping expands the height interval by eight binary32 epsilons times the maximum of one,
the absolute root heights and the ray's absolute vertical coordinate bound over its finite span.
This admits boundary uncertainty, including near-parallel rays, instead of losing inclusive native
support. Only exact-zero directions are parallel; small nonzero slopes compute finite entry/exit;
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
including native support whose sampled detailed density vanishes. Nonintersecting projected rays
and zero spans store RGB zero. A nonzero chord with zero sampled density has zero extinction and
tauMax. Optical-depth accumulation saturates at 1e30. Every generated texel is a finite compact
record before the whole texture is admitted; alpha never stores source flags or invalid markers.

View rays, retained shadow marches and cache generation share the same sampled density definition. A diagnostic generation variant
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

Volumetric density uses native horizontal footprints and vertical bounds. A periodic procedural
value-noise field modulates density inside that envelope; exposed faces soften inward while occupied
shared faces remain connected. Broad faces and stepped cubic masses remain. Noise follows native
placement, integer recentering and committed/residual wind without a second advection.
Consecutive samples in a native column retain its descriptor. Needed neighboring descriptors are
fetched at the current sample, keeping the ray-local cache small across secondary marches.
Neighbor occupancy is retested at each sample height. Faces farther than the current nearest face
or erosion width need no neighbor lookup. Zero detail amplitude skips the noise lookup; zero edge
support returns before noise. All consumers use the same material anchoring and sampled field.
Disabling detail also disables edge erosion, restoring piecewise constant native density. Finite
primary sampling need not integrate a discontinuous column field exactly.

### Procedural perturbation

The shared density shader evaluates one smooth value-noise field. Integer corner hashes replace
transcendental hashing. Each sample wraps its lower lattice coordinate once, derives the wrapped
upper coordinate and shares six coordinate products across eight corner hashes. Smoothstep and
trilinear interpolation keep the field bounded in [0,1].

For material coordinate m, the lattice coordinate is m*P/12800, where
P=max(1,floor(NoiseScale*12800+.5)). Integer lattice wrapping preserves the 12800-world-unit
material period, including negative coordinates and one-cell periods. Native placement, recentering
and committed/residual wind use the same material anchoring in view, light and cache queries.

The multiplier is 1+amplitude*(2*noise-1). It multiplies native extinction and the unchanged inward
exposed-edge factor once; native support and conservative density bounds remain valid. Detail-off,
zero amplitude and empty support avoid noise evaluation. Perturbation needs no texture allocation,
asset generation or sampler binding. Primary-ray blue-noise masks are independent. Finite sampling
can alias fine detail; appearance and performance require user review and matched measurements.

Primary tracing intersects the finite horizontal map, certified layer envelope and admitted
foreground/liquid distance before selecting a budget. For this clipped length L, including empty gaps, it selects
`N=min(max,max(min,floor(max*min(1,L/distance))))` and `ds=L/N`. Positions are
`tStart+(i+jitter)*ds`; native support checks reject empty points without reallocating their budget.
The default budget distance is 10000 world units, with ViewMax 384 and ViewMin 1. Below the cap,
this gives nominal spacing near 26 world units, subject to the minimum and integer count. Explicit
saved distances remain supported; nonfinite distances restore 10000. Changing the distance
invalidates history through the existing settings identity. Shader reload/reset handles activation
of a changed sampling policy; ordinary weather, layer motion and lighting retain history.
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

The Half selection uses ceil-half width/height tracing with a rotating 2x2 pixel phase,
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
| `ViewBudgetDistance` | 10000 | 50..10000 world units; clipped layer distance at which the maximum applies |
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

The 384/1/10000 primary selection assigns 384 scheduled positions to clipped rays of at least
10000 world units. Primary and secondary quadrature have independent algorithm-correctness checks.
Final live performance and appearance remain unqualified in the realtime checklist. Bounded work alone does not
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

## Stored density-volume resources

`PBR/Clouds/Volume/` owns an independent storage foundation for persistent cloud density and
illumination. `Clouds.VolumeRenderer=2` selects its independent full-resolution view. Its storage contract is
specified in [the density-volume proposal](PBR.CloudVolume.Proposal.md).

`CloudVolumeDomain` uses fixed 19.53125-unit horizontal cells, 64 vertical interior cells and
zero-density guards. The initial vertical band is 800 units; an out-of-band envelope selects a
snapped replacement band, doubling up to 25,600 units without cropping cloud support. Horizontal
material keys map to circular texture slots. Repeat in X/Z joins adjacent material keys across
physical texture seams; ClampToEdge in Y and logical empty guards bound finite support.

`CloudVolumeFields` owns R16F density, RG16F direct visibility, two RGBA16F diffuse fields with
zero alpha, one latest native-descriptor snapshot and exact block metadata/occupancy buffers.
Textures use hardware trilinear filtering; no shared sampler override replaces their mixed wrap
state. Maximum field-set accounting is 391,679,376 bytes, including the 1 MiB parameter/scratch
allowance. The allocated scratch buffer uses 1,044,480 bytes and the maintenance/copy UBOs use
128 bytes. Density operations use six 128-byte UBOs, a 32-byte validation/completion summary and
12-byte indirect arguments. Lighting work uses six 208-byte UBOs and 24-byte indirect arguments.
Direct transport uses three 128-byte UBOs; diffuse transport uses two 208-byte UBOs. The combined
addition is 2884 bytes. The view owns one 256-byte UBO across its variants and block publication
owns a 16-byte UBO, leaving 812 bytes reserved inside that allowance. There are no private view
targets or screen-space history. A separate drawable snapshot owns R16F density, RG16F direct,
RGBA16F diffuse and exact region/occupancy buffers: 249,952,000 bytes (238.373 MiB) at maximum extent.
A raw RGBA32F native observation snapshot adds at most 640,000 bytes. Maximum working plus drawable
payload is 612.518 MiB; two working domains plus drawable/raw snapshots total 986.052 MiB. The
512 MiB ceiling applies to each working field set; the combined owned payload fits the 1 GiB peak
ceiling. Driver allocation overhead is additional and remains a measurement concern.

`CloudVolumeStorage` admits format/dispatch/device limits before allocation. Incoming fields
initialize at most 4096 physical blocks per call. They contain zero density, unit direct visibility
and a bounded caller-supplied diffuse fallback. Initialization alone does not certify weather
density: publication additionally requires its caller's density-completion certificate. A previous
complete domain remains published during replacement, with at most two field sets resident.
Both sets fit the 1 GiB replacement ceiling; each fits the 512 MiB resident ceiling.

Compatible recentering retains overlapping material slots without relocation textures, clears
new/guard cells and revokes touched block completion metadata. Exact owner/world/representation,
context and executable identities prevent unrelated storage reuse. Reset/disposal releases complete
and partial allocations. Failed pending initialization retires the incoming set; a failed in-place
scroll or diffuse publication withdraws sampling until recovery.

Recenter maintenance scans the bounded physical allocation in batches of at most 4096 blocks,
finishing its ordered scan before exposing the new window. This scan can require 18 dispatches
at maximum coverage; its cost is separate from subsequent weather-density generation jobs.
Retained pending revisions and first-pending ages survive that scan even when whole-block
completion stamps must be revoked.

Diffuse publication first copies the complete old field into its existing spare. A refinement
callback writes the spare, and completion/certificate checks precede the texture/scale swap.
Barriers cover image writes, texture sampling and integer metadata. Physical RGB is capped and
quantized downward to representable half values during encoding/rescaling, retaining the declared
radiance bound. The caller skips this operation when no diffuse work is selected. This storage
foundation has separate direct/diffuse transport owners and a stored-field view consumer.

`CloudVolumeDensity` submits private `cloud_volume/density` operations during a certified native-map
borrow. Validation freezes the same raw observation into an owned RGBA32F texture and reduces its
height envelope. A later zero-timeout fence poll admits the 16-byte envelope/status read and selects
a fully containing band before changing the canonical descriptor snapshot. Generation consumes the
owned observation; no native texture wrapper survives the callback. The 32-byte density completion
summary is likewise read only after its producer fence signals. No CPU readback selects individual jobs.

The latest RGBA32F snapshot holds extinction in inverse metres, lower/upper heights in metres and
an exact valid marker. Finite zero-extinction columns canonicalize to valid empty support; occupied
columns require ordered finite heights. Its circular slots use native material-column keys and
explicit previous-window membership, so departed keys cannot masquerade as entering content.
Observation compares exact content, independent of publication generation. A matching native
generation/upload/allocation/window skips redundant observation. Wind-only placement changes retain
material data. Changed and departed columns enqueue their footprint plus connected-face neighbors;
the snapshot advances after barriers and certificate checks without retaining a native texture.

Generation intersects each voxel with native cuboids. Detail-off stores summed exact overlap-volume
fractions times native extinction. Detail-on averages eight stratified points inside each nonempty
overlap using bounded stationary noise and connected-face erosion. Half-float overflow fails the
batch instead of clamping extinction. Noise and descriptor evaluation occur only in this generator;
subsequent ray consumers can read stored density alone.

Density work uses the existing scratch buffer: words 0..63 are header/reserved, jobs occupy
64..4159 and the bounded priority heap occupies 8192..12287. The rest remains reserved. Initial
fills use up to 4096 pending physical blocks per eligible call; subsequent batches reserve half
for round-robin fairness and half for oldest pending work. Repeated observations update demand
without resetting first-pending age. GPU-produced indirect arguments submit selected jobs without
CPU list readback. Same-frame repeats consume no additional budget.

Incoming domains freeze their descriptor snapshot during bounded storage initialization and density
fill. The previous domain remains usable until all initial density jobs finish; newer observations
are deferred until publication. A later out-of-band observation requests another replacement and
does not write cropped density into the active domain. Block records expose pending/completed
revisions and frame ages; `DensityState` exposes snapshot frame and last actual batch completion.

Every generated block marks neighboring occupancy records conservatively before writes. A separate
barrier-separated pass repairs them from stored density including one texel around each block and
circular seams, covering trilinear filter support. Completion stamps advance only after repair and
successful arithmetic/certificate checks. Density changes mark direct/diffuse dependents stale
without clearing compatible illumination. Failure withdraws sampling and latches the generator;
explicit recovery resets both the generator and its storage. Context/executable identity is checked
on both sides of work. Driver execution time, summary-read stalls and live appearance are unqualified.

`cloud_volume/traversal.glsl` provides the stored-density DDA used by candidate ray consumers.
It clips rays to the finite native rectangle and admitted vertical band, handles zero direction
components without division, and retains the current physical block and next unwrapped planes.
Crossings advance all tied axes, including the short physical edge block at the circular seam.
Only an occupancy value of zero skips a block; pending repair values remain conservatively occupied.
Occupied steps use nominal half-voxel spacing for view/diffuse or one-voxel spacing for direct rays,
shortened at block/domain endpoints. One hardware trilinear fetch evaluates each midpoint density.
Operation caps are 2048 and 1024 respectively. Lack of progress or cap overflow fails the ray;
consumers must reject an incomplete prefix rather than treating it as completed transport.

`CloudVolumeWork` selects separate direct and diffuse lists only after density publication. It
reserves half each 64/32-job budget for persistent round-robin service, then chooses incompatible,
newly active, visible stale and other old pending work. Physical blocks spanning a logical window
seam are conservatively visible. Offscreen jobs retain fair service. The direct list encodes
`block*2+source`; the direct owner orders solar/lunar RG writes to avoid channel
read-modify-write races. GPU-selected indirect arguments contain separate three-word records for
direct and diffuse dispatch; no CPU readback selects work.

Lighting scratch header words 16/17 hold selected counts, 18/19 fair cursors and 22/23 backlog.
Word 25 is the solver arithmetic-failure channel mask, 26 completion status and 27 deferred diffuse
dependency demand. Direct jobs occupy 16384..16447 and diffuse jobs 16448..16479. Bounded
selection summaries begin at words 32768 and 196608. These ranges do not overlap density work. Density demand in word 6
coalesces until the lighting selector consumes it; density and lighting use separate revision
namespaces. Selection preserves completed stamps and pending ages, and never certifies work as fresh.

Each batch freezes the exact field/window revision, density snapshot/completion and immutable
physical atmosphere snapshot. Source direction changes dirty the matching direct channel and
diffuse transport; intensity/environment changes dirty diffuse only. Inactive direct sources
receive no jobs. Activation revokes their completed-channel identity, requiring initialized fallback
until the new channel is solved. Camera/frustum, fog and exposure changes retain compatible fields.
Successful direct/diffuse publication requests later diffuse refinement without restarting pending
ages. The selector admits all physical blocks conservatively; empty-node work reduction belongs
to the solver, and total refresh latency remains a measurement concern.

Work selection uses barrier-separated initialization, a 256-lane parallel region merge, local
classification and hierarchical top-K reduction, then bounded final list emission. Each pending
candidate's priority/frustum and waiting age are calculated once. Shared-memory sorting retains
the first 32/16 fair candidates and 64/32 priority candidates per disjoint chunk. Four-child
reductions preserve those exact global winners; the final emitter removes fair/priority overlap.
The priority oversupply ensures that excluding fair winners still fills the remaining budget.
Stable job-index ties and cyclic cursors define deterministic priority ordering and fair-service bounds.
Only the bounded final emitter and completion loops use a single lane; there is no whole-domain
serial scan or repeated visibility calculation inside a heap comparator.

At maximum extent, the first summary level contains 562 direct and 281 diffuse chunks. Its
packed indices/priority keys/counts occupy words 32768..190970; the quarter-sized reduction spare
occupies at most 196608..236355, alternating levels after SSBO barriers. Both fit existing scratch;
no full per-job array or extra GPU allocation is needed. The source-domain extent determines
reduction dispatch sizes; no CPU job readback or queue selection is introduced.

Solvers report successful channels through `Complete` against the unchanged selected batch.
GPU failure bits suppress completion writes; a one-word status read verifies rejection without
reading lists. Repeated completion or changed field/density certificates cannot advance stamps.
Failures withdraw sampling until reset; the view consumes only successful published fields.

`CloudVolumeDirect` consumes the frozen work batch and authoritative stored density only. Each
occupied node traces toward its relevant source through the retained coarse DDA to the finite
domain exit, at one-voxel nominal spacing. It accumulates actual segment lengths in metres,
saturates optical depth at 80 without early termination, and stores dimensionless exp(-tau) in
RG16F. Empty nodes skip integration. Incomplete rays and nonfinite arithmetic set the channel's
failure bit; no failed batch advances completion, and partial in-place writes withdraw sampling.

Activation initializes each newly relevant channel to unshadowed across the bounded allocation
before selected updates. This full-field image pass has no ray integrals; its read/write traffic
and GPU cost are separate from the 64-job transport allowance and remain unqualified. Compatible
direction-stale values survive ordinary direction changes. Brightness/environment changes reuse
direct transmission; future consumers apply current illuminance and phase separately. Disabled
direct work creates no transport program and dispatches no integrals. Solar/lunar variants compile
source selection independently and execute with image barriers between them, preserving companion
channels without RG read-modify-write races. Reset/context/executable changes revoke the solver's
certificates; storage retains ownership of all fields.

`CloudVolumeDiffuse` refines at most 32 selected blocks after ordered direct completion. Every
occupied node gathers eight normalized cube-corner rays, equally weighted, through stored density
at half-voxel nominal spacing. Each ray continues through empty gaps until the finite domain exit
or the explicit 400-metre transport closure. The incoming sum is
`sum(T_before*alpha*(Q(sample,-rayDirection)+D_read(sample)))+T_end*Environment`, with
`alpha=1-exp(-sigma*actualSegmentMetres)`, albedo .9, HG g=.6 and
`Q=.9*sum(currentIlluminance*cachedTransmission*HG)`. The new isotropic source is .9 times the
angular average. Environment enters once at the closure; outgoing directional first scattering
remains a separate view operation. Zero-density/guard nodes skip gathers and receive the
finite .9*Environment fallback. Alpha remains zero.

A nonempty batch first copies the full published diffuse field to the existing spare, re-encoding
untouched values if scale grows. The gather samples only the coherent old texture, including one
hardware trilinear fetch each for density, old diffuse and optional cached direct visibility at
an occupied midpoint. Self-shadowing selects actual compiled variants; disabling it removes the
direct sampler. Disabled diffuse creates no solver and does no copy. A one-word selected-count
read skips copy/scale/swap for an empty list, without reading jobs. `LastCopyBytes` charges the
complete source/destination copy separately from `LastJobs`; maximum copy traffic remains
280,077,600 bytes per batch. These counts do not establish GPU execution time.

Componentwise solar/lunar/environment envelopes remain with the field publication. Double-precision
admission derives conservative Qcap/Dcap, retains older larger caps, and rejects unsupported
float32 transport bounds. Fog, exposure and camera changes do not clear them. Successful gathers
clamp to Dcap and encode downward to representable half values in the destination scale. Failure
bits suppress completion; a failed destination never swaps or changes published encoding/bounds.
Completion occurs against the unchanged work certificate before the texture/scale publication,
which advances its field revision. Successful updates request further refinement without resetting
pending ages. Resetting the solver releases executables while field-owned envelopes persist;
retiring the field retires its envelopes. Live appearance, copy cost and refresh latency remain
unqualified.

### Stored-volume view and selection

In ConfigLib, set native cloud mode to **Volumetric**, then select **Volumetric renderer = 2
(stored-volume)**. `0` retains the existing renderer and `1` retains the lighting-only candidate.
The persisted equivalent is `Clouds.VolumeRenderer=2`, with controls under `Clouds.StoredVolume`:
`Detail`, `SelfShadowing`, `Diffuse` (all true by default) and `StopTransmittance` (.005 by default).
Apply ConfigLib changes normally; selection commits at the next Before preparation boundary.
If editing the JSON outside ConfigLib, restart the client to load it. To recover a latched GPU or
shader failure, switch **Volumetric renderer** to **0**, apply, then **2**, apply. The normal client shader-reload action also resets it.
No migration or automatic promotion is involved. Native Off and Simple keep their meanings.

`CloudStoredVolumeConsumer` uses the existing native consumer boundary at OIT .31. The previous
consumer retires before the new one prepares; inactive candidates do no generation or lighting.
Before Opaque, `CloudVolumeAdmission` retains source envelopes and admits Qcap+Dcap, aerial/local
medium headroom and the 2048-operation float accumulation allowance. Draws require that admitted
atmosphere snapshot, a valid atmosphere calibration identity, and the independent current camera /
frozen photometric publication. Atmospheric and engine-light calibration generations need not be
equal. Missing transient inputs omit the contribution. Initial density fill is bounded and neutral;
replacement retains the old complete domain and its initialized lighting while new density fills.

`CloudVolumeSolution` coordinates `CloudVolumeUpdates` and an isolated `CloudVolumePublication`.
Before drawing, zero-timeout fence polls advance completed density/occupancy, direct and diffuse
steps. Status/count buffers are mapped only after their producing commands have completed. Timeout
retains the previous drawable publication and submits no additional update. After drawing, at most
one bounded update step is submitted; later native observations and lighting changes coalesce behind
the frozen pending step. A completed lighting batch copies changed whole blocks into the drawable
snapshot through typed `cloud_volume/publish` compute authority and barriers. Density/direct/diffuse
dirty bits are 4096/8192/16384 in working region flags. Metadata and occupancy are copied coherently
for all blocks; no CPU reads job lists. First publication, changed domains or replaced working fields
copy all channels; source activation copies direct, and an encoding change copies diffuse in full.
Ordinary publication copies only flagged channels/blocks. Working fields can then change without
altering the drawable snapshot. Initial activation remains neutral until a completed snapshot exists.

OpenGL submissions remain in the engine command stream. Fences remove immediate CPU waits and
same-callback completion requirements; they do not provide an explicit asynchronous compute queue,
guarantee graphics/compute overlap, or remove the update's GPU cost. Update service bounds count
eligible batches, which can span multiple rendered frames. Diagnostics skip unfinished updates.

`CloudVolumeCoordinates` converts the current camera/native placement into the published domain's
material coordinates, including during replacement; native wind translation enters once. View
settings retain density and lighting. Detail changes replace geometry; lighting feature changes
revoke queue/executable certificates and request refresh while compatible density/source envelopes
remain. Resize releases framebuffer metadata without clearing material-space history. Reload,
selection release and world/context teardown retire all candidate resources.

`CloudVolumeViewRenderer` draws one procedural full-resolution triangle through existing graphics
pipelines into the six native Transparent attachments. It clips against finite stored coverage,
material depth, particle visibility and liquid depth at mip zero. Coarse DDA retains traversal
state and skips only certified empty support. Occupied intervals use half-voxel axis spacing,
actual clipped metre lengths, hardware-filtered R16F density, RG16F transmission and scaled diffuse
RGB. There is no native descriptor/noise lookup, low layer sample cap, jitter or screen-space history.
Operation-limit failure discards the whole pixel prefix. Optional optical termination occurs only
after a completed segment and compiles out when StopTransmittance is zero.

The outgoing phase direction is sample-to-camera. Current directional first scattering and cached
diffuse enter once; disabled self-shadowing uses unit transmission, and disabled diffuse uses
.9*current Environment. Those user-disabled sampling paths compile out. After physical front-to-back
integration, the shared affine atmosphere/local-medium treatment and frozen scene encoding apply
once. Bucket transmissions use each interval's near depth; overall transmission, glow metadata and
three weighted accumulations preserve the native OIT convention in both PBR selections.

The existing **VGE debug visualizers** menu includes **Cloud Stored Volume** with stored density,
region identity, completed-age and initialized-fallback overlays. Its panel reports dimensions,
spacing, resident/replacement payload, density and lighting work/backlog, completed work, fallback
blocks and diffuse copy bytes. Diagnostic metadata reads occur only while this view is active,
at most once per second. Existing bounded generation/completion status reads remain separate
algorithm costs. Full-resolution view cost, copy bandwidth, shape and lighting latency still
require measured performance and user appearance review.

## Independent lighting-volume candidate

`PBR/Clouds/LightingVolume/` owns an independent lighting-grid candidate. `Clouds.VolumeRenderer`
selects `Existing` (0, default) or `LightingVolumeCandidate` (1), separately from native Off/Simple/
Volumetric selection. Candidate controls live under `Clouds.LightingVolume`; existing quality settings
are retained unchanged when switching. Its numerical rendering contract is described in the
[lighting-volume proposal](PBR.CloudLightingVolume.Proposal.md).

`LightingVolumeDomain` uses unwrapped material lattice keys with 100x25x100-unit cells and a
fixed vertical band. CPU double-precision anchors become bounded native-local GPU offsets;
fractional wind enters through committed native placement once. Guard nodes remain invalid.
`LightingVolumeStorage` owns continuous RG16F direct volumes, scaled RGBA16F diffuse/mask fields,
native descriptor textures and exact region buffers. Capability and payload admission precede
readiness. Overlapping grid shifts use distinct source/destination fields and region records;
replacement retires old storage before allocating new storage. Reset releases the entire set.

The maintenance executable initializes defined fallback storage and preserves overlapping data
through barrier-complete remapping. Diffuse copies re-encode RGB while retaining exact alpha
masks and publish the texture with its power-of-two physical scale. Failed copies withdraw
readiness while preserving the old texture/scale pair. Private density helpers retain current
native cuboid support and connected faces. Lighting lookup checks all eight exact mask entries
before using one hardware trilinear sample per admitted field; invalid coverage uses unshadowed
direct and coarse environment fallback. These helpers perform no screen-space accumulation.

`LightingVolumeWork` borrows a matching current native publication synchronously. GPU comparison
updates two exact circular descriptor histories using unwrapped CPU window keys and bounded GPU
offsets. The previous observation tracks each completed comparison; the hard-change reference
advances only after local invalidation demand, catching accumulated small changes. Column writers
atomically coalesce region demand. A separate node-owned pass updates occupancy and channel masks
while retaining encoded RGB. Failed in-place work withdraws readiness and requires explicit reset;
missing current input skips work without advancing completion timestamps.

GPU scheduling retains dirty state, pending ages and material-key cursors. Density changes mark
all dependent lighting dirty, while hard local changes also invalidate filtering footprints.
Source direction changes retain compatible transmission as stale; newly active sources invalidate
only their own direct channel. Intensity changes dirty diffuse lighting without rescaling retained
RGB. Half the 512 direct and 256 diffuse job budgets goes to persistent round-robin selection;
the remainder uses a bounded priority heap ordered by incompatibility, new source, frustum visibility,
pending age and lattice index. Job lists and counters stay GPU-owned. The exact region records
distinguish enqueue time, last actual update and completed/pending observation revisions.

The completion operation accepts only successful selected channel batches from the same frame
and grid; selection itself never certifies illumination. Successful batches request later diffuse
dependency work. The selected candidate coordinates these primitives through `LightingVolumeSolution`.
Their correctness checks do not qualify
runtime scheduling cost, lighting latency or visual appearance.

`LightingVolumeDirect` consumes only the selected jobs and source directions frozen by the work
owner for the same frame, native generation/upload, allocation and grid. It stores independent
solar/lunar transmission in RG16F; illuminance and exposure do not enter that storage. Each
occupied node integrates 32 uniform midpoints over at most 1500 world units, clipped only to the
finite native horizontal domain. Native density still determines vertical support and empty gaps.
Optical-depth arithmetic saturates at a finite bound, with no optical early exit or view-depth clip.
Native extinction converts to physical units before bounded detail modulation, with protected
arithmetic ordering to avoid intermediate overflow for large finite controls.
Density detail selects an actual compiled variant owned by `LightingVolumeShaderOptions`.

Solar and lunar execute in separate ordered passes. A selected region uses one invocation per
admitted node, preserves the other transmission channel and encoded diffuse RGB, and edits only
its own validity bit. Image/storage/sampling barriers separate each pass and completion. Numerical
failure clears that channel's validity throughout the region and leaves its update pending;
dispatch or certificate failure withdraws the complete storage publication. Completion records
only successful region channels. Inactive sources request no executable or dispatch, and native
texture references are released after synchronous submission. The selected candidate owns the solver's
reset/disposal lifetime; the solver has no independent native callback.

`LightingVolumeScattering` refines at most 256 selected regions from the published diffuse field.
Each occupied node gathers six equally weighted axis rays, with twelve uniform midpoints over
at most 300 world units per ray. Native horizontal support clips transport; the vertical lighting
band does not. Empty gaps preserve transmission. Directional first scattering uses current physical
solar/lunar illuminance, normalized HG response and cached transmission, with no nested shadow march.
The incoming environment appears once at each finite ray endpoint. The published diffuse source
already includes transported environment and higher orders; view composition uses first scattering
plus diffuse without adding environment again. Direct-disabled programs compile out transmission
lookups; diffuse-disabled work creates no solver executable, dispatch, copy or swap.

Every diffuse batch copies the complete RGBA field to its existing spare, then overwrites selected
nodes while sampling only the old field. Hardware trilinear RGB sampling requires eight exact valid
mask entries. Output preserves occupancy/direct bits, replaces diffuse RGB and validity, and clears
diffuse validity throughout a numerically failed region. Barriers precede successful-channel age/
revision completion and atomic texture/scale publication. Untouched texels retain RGB and exact
alpha. Failed batches withdraw readiness and retain the previous coherent texture/scale pair.

`LightingVolumeBounds` retains componentwise physical source envelopes and derives conservative
first/diffuse source bounds independently of exposure. Its scene-admission method includes aerial
reconstruction, local fog and accumulation margin. Half-float diffuse storage uses a monotonic
power-of-two scale; the full copy re-encodes retained RGB when it grows. Both pre-store and decoded
physical values are bounded. Envelopes may shrink only with cleared lighting. The parameter blocks
remain within the reserved 2048 bytes, and no additional volume or scratch allocation is needed.
The full diffuse copy accounts for 16 bytes per allocated texel (about 5.50 MiB at maximum extent).
Direct updates additionally load/store 16 bytes of mask-bearing RGBA per source-node, plus 8 bytes
of RG transmission; node invalidation loads/stores 16 bytes per allocated texel. Refinement's selected
destination load/store adds 16 bytes per updated node. These are logical payload traffic counts;
filtered reads, metadata traffic, caching and actual GPU time require separate measurement.
The candidate consumer admits lifetime source envelopes through the generic selected-consumer
boundary before Opaque. Focused arithmetic/publication checks do not establish runtime cost or appearance.

`LightingVolumeRenderer` submits the candidate through its own full-resolution view program. It
borrows a complete current camera, frozen photometric publication, native map, independent material/
particle/liquid depths, completed aerial products and native OIT target. It owns procedural geometry,
graphics pipelines and a small native-height reduction program; its view program remains with the
shader library. The view owner neither ticks weather nor registers a callback. Lighting updates
remain separate owners, so draw submission does not hide descriptor or refinement work.

The reduction scans each native texel once, rejects malformed/empty controls and writes the finite
minimum/maximum heights into scratch words 1536–1537. A storage barrier precedes fragment reads.
The view intersects those heights, finite horizontal support and the nearest of three independent
foreground clips. It budgets deterministic uniform midpoints across that clipped layer span, with
at most 96 samples and a 10000-unit default budget distance. Density queries preserve sharp native
support independently of filtered illumination, including thin clouds and density outside the
lighting band. Invalid lighting footprints use unshadowed transmission and the coarse diffuse
environment source. The source is directional first scattering plus retained diffuse, with no
additional environment or approximate scattering octave.

The integrator retains separate total and three native bucket transmission products. A strict
post-step termination threshold has a compiled disabled variant; disabled detail, direct self-shadow
lookup and diffuse lookup also select actual precompiled variants. Atmosphere or local underwater
response applies once at the opacity-weighted representative distance, then physical RGB is encoded
once with the matching frozen scene scale. Reveal/Reveal/Glow/Add/Add/Add output policies preserve
native accumulation, with non-emissive glow RGB zero. The physical camera, atmosphere and encoding
contracts are shared by both PBR selections; there is no PBR-specific cloud-lighting branch.
Engine block/point-light calibration and atmospheric solar calibration have independent generation
identities. The frozen photometric buffer matches the camera calibration; the completed atmosphere
supplies its own valid identity and lookup scale. The two calibration counters are not compared.
Submission rejects unrepresentable current-source plus retained-diffuse, aerial and local-fog
arithmetic before drawing. Receiving albedo is applied per direct-source contribution so an
otherwise representable source sum does not overflow an intermediate before albedo multiplication.

The selected view has no intermediate texture, spatial resolve, screen-space history or dependency
on existing cloud hierarchy, shadow, transport or reconstruction owners. Its height and view
parameter blocks raise the total to 1904 of the reserved 2048 bytes; scratch and volume allocations
are unchanged. Reset retires private pipelines/reduction/geometry and releases synchronous borrows.

`CloudNativeRuntime` manages either renderer through `ICloudVolumeConsumer`: preparation, reset,
disposal, native callback and physical maximum. Applied configuration captures a pending selector
and candidate settings snapshot on the rendering thread. Before .002 applies selection, retires
the previous owner before preparing a replacement, and leaves native drawing available when initial
preparation fails. A change after this boundary waits for the next frame. Actual ownership continues
to drive shared material-depth demand in either PBR selection; scene publication has no candidate branch.

`LightingVolumeConsumer` coordinates the private solution and view at OIT .31 after certified native
map generation. Before-Opaque source admission retains brighter lifetime envelopes and current aerial/
local-fog bounds; the draw verifies the frozen scene inputs. Synchronous map wrappers are disposed
at callback exit, and no previous-frame native borrow is retained. Pause leaves native weather with
its existing owner; certified paused inputs can continue refinement. Missing current inputs skip
without advancing completed update ages. Admitted work failure withdraws publication and latches
until reset/reload/selection recovery, while native drawing remains suppressed for that ownership.

Resize retires borrowed target metadata and keeps compatible cloud-space lighting. There are no
candidate view targets to resize. World/context replacement, reload and consumer retirement release
every private owner. Detail/self-shadowing/diffuse setting changes clear the candidate solution at
early preparation; view budget/termination changes retain compatible lighting. Disabled shader paths
select precompiled variants, and disabled transport creates no corresponding jobs or solver executable.

Candidate controls default to 96 view samples, 10000-unit budget distance, .005 stop transmission,
with detail, self-shadowing and diffuse enabled. ConfigLib exposes the selector and these independent
controls. Grid, transport and scheduling budgets remain fixed. No screen-space temporal filtering is used.

The existing VGE debug menu contains Cloud Lighting Volume. Coverage colors distinguish native
density within/outside lighting coverage; validity colors distinguish complete valid footprints from
fallback across occupied view samples. Their spatial proportions expose fallback/coverage and the
immediate switch to completed lighting. The panel reports selected work, backlog, dirty regions,
actual update ages, allocation payload/counts and transition policy. Scheduler metadata is read only
while this view is active, at most once per second; GUI ticks read cached text. No new diagnostic
texture or framework is allocated. The coarse fallback is source-lit, so black illumination also
produces a black diagnostic view. Live appearance, timing and package qualification remain open.
