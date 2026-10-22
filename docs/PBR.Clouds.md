# Cloud rendering

VGE owns separate Simple and Volumetric shaders while native Vintage Story weather owners
produce geometry and column controls. The native `cloudRenderMode` preference remains authoritative:
0 is Off, 1 is Volumetric, and 2 is Simple. Rendering works independently of LumOn and uses the
same [completed atmospheric publication](PBR.Atmosphere.Publication.md) in both PBR selections.
Implementation, current sampling and remaining acceptance/qualification work are tracked in the
[cloud volume checklist](PBR.CloudVolume.todo). Stored-density ownership is described below.

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
The stored-volume generation API described below retains owned descriptors
and density; the stored-volume consumer uses these owned fields. Native `TextureCol` remains allocated/generated but is not a VGE
physical-lighting input. Native owners alone dispose their resources.

## Simple cloud shading

Simple clouds use physical directional and environment lighting without volume marching,
self-shadowing, three-dimensional noise or temporal targets. Native shape and coverage controls
remain authoritative; perception's factor 20 is applied locally without changing engine uniforms.
Terrain depth occludes the geometry. The adjusted OIT depth and separate liquid/underwater treatment
retain native composition behavior. Volumetric feature controls do not affect Simple clouds.
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
preserve particle occlusion. View and reconstruction combine material and visibility depth
before tracing or reconstruction. These scene-depth reads explicitly select mip zero without
coarse scene-depth traversal, tile rejection or a shared-HZB format change. Shared resource reuse alone does not establish a performance improvement.

## Stored density-volume resources

`PBR/Clouds/Volume/` owns an independent storage foundation for persistent cloud density and
illumination. Volumetric mode uses its stored-field view and the shared cloud resolution controls. Its storage contract is
specified in [the density-volume proposal](PBR.CloudVolume.Proposal.md).

`CloudVolumeDomain` uses fixed 19.53125-unit horizontal cells, 64 vertical interior cells and
zero-density guards. The initial vertical band is 800 units; an out-of-band envelope selects a
snapped replacement band, doubling up to 25,600 units without cropping cloud support. Horizontal
material keys map to circular texture slots. Repeat in X/Z joins adjacent material keys across
physical texture seams; ClampToEdge in Y and logical empty guards bound finite support.

`CloudVolumeFields` owns R16F density, RG16F direct visibility, two RGBA16F diffuse fields with
zero alpha, one latest native-descriptor snapshot and exact block metadata/occupancy buffers.
View and transport fetch exact physical texels for piecewise-constant cells; textures retain mixed wrap
state. Maximum field-set accounting is 394,825,104 bytes, including the 4 MiB parameter/scratch
allowance. The allocated scratch buffer uses 4,190,208 bytes and the maintenance/copy UBOs use
128 bytes. Density operations use five independent 128-byte UBOs plus one shared 128-byte selector
UBO, a 32-byte validation/completion summary and 12-byte indirect arguments. Lighting work uses one shared
208-byte UBO and 180-byte indirect arguments.
Direct transport uses three 128-byte UBOs; diffuse transport uses two 208-byte UBOs. The combined
addition is 2000 bytes. The view owns one 240-byte UBO across its variants and block publication
shares a 16-byte UBO across its three compiled operations and owns a 16-byte argument record,
plus two 64-byte temporal parameter blocks, leaving 1568 bytes reserved inside that allowance. Reconstruction borrows the
shared frame UBO without owning a separate parameter buffer.
Screen-sized trace/history targets and the 32 KiB blue-noise texture are charged separately below.
A separate drawable snapshot owns R16F density, RG16F direct,
RGBA16F diffuse and exact region/occupancy buffers: 249,952,000 bytes (238.373 MiB) at maximum extent.
A raw RGBA32F native observation snapshot adds at most 640,000 bytes. Maximum working plus drawable
payload is 615.518 MiB; two working domains plus drawable/raw snapshots total 992.052 MiB. The
512 MiB ceiling applies to each working field set; the combined volume payload fits the 1 GiB peak
ceiling. Screen-sized view targets and driver allocation overhead are additional.

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
vertical demand covers old/new occupied heights and intersecting cells with a one-texel margin,
rather than every vertical block. Empty columns with no previous support enqueue no geometry work.
The snapshot advances after barriers and certificate checks without retaining a native texture.

Generation intersects each voxel with native cuboids. Detail-off stores summed exact overlap-volume
fractions times native extinction. At Medium, detail-on averages eight stratified points inside each nonempty
overlap using bounded stationary noise and connected-face erosion. Half-float overflow fails the
batch instead of clamping extinction. Generation explicitly narrows to a representable half value,
compares its packed 16-bit representation exactly with the old R16F texel, and stores only changes. A shared block flag counts
changed blocks in density summary word7. This storage equality test has no visual threshold.
Noise and descriptor evaluation occur only in this generator;
subsequent ray consumers can read stored density alone.

Density work uses the existing scratch buffer: jobs occupy 64..4159. A 256-lane merge preserves
pending ages and clears selection flags; parallel cyclic prefixes select the fair quota. Four
byte-wise histogram reductions locate the oldest-work threshold; another prefix selects exact
region-index ties and emits the remaining jobs. No invocation scans the whole domain or maintains
a global heap. Initial fills use the entire 4096-job budget for fair service and skip the age
reductions; subsequent batches reserve half for fairness and half for oldest pending work.
Repeated observations update demand without resetting first-pending age. The fair prefix retains
cyclic order; the priority suffix emits the exact selected set in physical-index order.

Temporary words 32..37 hold the frozen cursor, quota and radix state. At maximum extent the
281 by 256-bin histograms occupy 32768..104703; chunk counts occupy 245760..246040 and
246272..246552. Density selection and lighting selection execute in separate, barrier-ordered passes,
so their overlapping histogram/summary area is reused safely. No additional volume-sized allocation is
added. Selector variants share one immutable-on-submission UBO through the existing uniform
publication allocator. GPU indirect arguments submit selected jobs without CPU list readback.
Same-frame repeats consume no additional budget.
After a fenced summary reports no remaining demand, `MaintenanceRequired` stays false until another
observation; unchanged/wind-only frames skip the density dispatch chain and summary read. Lighting
continues through the normal final fence. An observation with identical descriptors still validates
and compares its certified snapshot. GPU priority stages return uniformly for an empty priority
quota; fair cursors and selected sets are unchanged. Selected and Changed counts distinguish queued
work from blocks whose stored density changed.

Incoming domains freeze their descriptor snapshot during bounded storage initialization and density
fill. The previous domain remains usable until all initial density jobs finish; newer observations
are deferred until publication. A later out-of-band observation requests another replacement and
does not write cropped density into the active domain. Block records expose pending/completed
revisions and frame ages; `DensityState` exposes snapshot frame and last actual batch completion.

Every changed stored block marks neighboring occupancy records conservatively before readers. A separate
barrier-separated pass repairs them from stored density including one texel around each block and
circular seams, retaining a conservative neighboring-cell halo. Completion stamps advance only after repair and
successful arithmetic/certificate checks. Recenter maintenance marks affected occupancy records for
repair even if their entering/guard density is already zero. Only changed stored density marks
drawable density blocks dirty and direct/diffuse dependents stale
without clearing compatible illumination. Failure withdraws sampling and latches the generator;
recentered coverage also dirties global lighting because maintenance may already have cleared old
support and finite transport exits changed. Field-owned LayoutChanged survives submission until
successful density completion, independently of the changed-texel count.
explicit recovery resets both the generator and its storage. Context/executable identity is checked
on both sides of work. Driver execution time, summary-read stalls and live appearance are unqualified.

`cloud_volume/traversal.glsl` provides the stored-density DDA used by ray consumers.
It clips rays to the finite native rectangle and admitted vertical band, handles zero direction
components without division, and retains the current physical block and next unwrapped planes.
Crossings advance all tied axes, including the short physical edge block at the circular seam.
Only an occupancy value of zero skips a block; pending repair values remain conservatively occupied.
The ray caches unknown, empty and occupied classifications in one integer state. The decoded
classification controls both interval length and whether the consumer integrates that interval.
Occupied intervals follow exact voxel entry/exit planes and fetch density once per crossed cell.
Density and cell-centred direct/diffuse source are constant within that cell; clipped lengths enter
closed-form extinction and source integration. All ray consumers have a 2048-operation cap. Lack of progress or cap overflow fails the ray;
consumers must reject an incomplete prefix rather than treating it as completed transport.

`CloudVolumeWork` selects separate direct and diffuse lists after ordered density/occupancy writes. Blocks
with certified zero density and conservative support do not consume lighting jobs or backlog counts.
Their pending revisions remain uncompleted and become eligible when density makes them occupied.
It reserves half the configured direct/diffuse job budgets for persistent round-robin service, then chooses incompatible,
newly active, visible stale and other old pending work. Physical blocks spanning a logical window
seam are conservatively visible. Offscreen jobs retain fair service. The direct list encodes
`block*2+source`; the direct owner orders solar/lunar RG writes to avoid channel
read-modify-write races. GPU-selected indirect arguments contain separate three-word records for
direct and diffuse dispatch; no CPU readback selects work.

Lighting scratch header words 16/17 hold selected counts, 18/19 fair cursors and 22/23 backlog.
Words 29/30 hold solar/lunar counts. The common selected block-source list retains global selection
and completion semantics; solar and lunar region lists at 16768..17023 and 17024..17279 partition it
without changing the combined direct job budget. The six three-uint indirect records hold the common
direct count, diffuse dispatch, solar dispatch, lunar dispatch, conditional full diffuse copy and
bounded previous-block repair. Each source launches only its
own groups; inactive sources are skipped and empty source counts launch zero invocations.
Word 25 is the accumulated failure mask (bits 1/2/4 for lighting, 8 for chained density), 26 completion status and 27 deferred diffuse
dependency demand. Word 31 carries density failure across scratch reuse. Direct jobs occupy 16384..16639 and diffuse jobs 16640..16767. Bounded
selection summaries begin at words 32768 and 800000. These ranges do not overlap density work. Density demand in word 6
coalesces until the lighting selector consumes it; density and lighting use separate revision
namespaces. Selection preserves completed stamps and pending ages, and never certifies work as fresh.

Each batch freezes the exact field/window revision, density snapshot/completion and immutable
physical atmosphere snapshot. Source direction changes dirty the matching direct channel and
diffuse transport; intensity/environment changes dirty diffuse only. Inactive direct sources
receive no jobs. Activation revokes their completed-channel identity, requiring initialized fallback
until the new channel is solved. Camera/frustum, fog and exposure changes retain compatible fields.
Both finite nonzero sources remain eligible regardless of relative brightness; there is no faint-light
cutoff or weighted budget partition. Intensity changes retain transmission and affect diffuse demand.
Successful direct/diffuse publication requests later diffuse refinement without restarting pending
ages. The selector visits a retained compact list of blocks with positive stored density; certified
empty blocks keep pending demand without receiving transport jobs.

Work selection uses barrier-separated initialization, a 256-lane parallel region merge, local
classification and hierarchical top-K reduction, then bounded final list emission. Each pending
candidate's priority/frustum and waiting age are calculated once. Shared-memory sorting retains
the configured fair and priority candidate counts per disjoint chunk. Four-child
reductions preserve those exact global winners; the final emitter removes fair/priority overlap.
The priority shortlist supplies unique jobs after fair winners; bounded candidates can leave ray allowance unused.
Stable job-index ties and cyclic cursors define deterministic priority ordering and fair-service bounds.
Only the bounded final emitter and completion loops use a single lane; there is no whole-domain
serial scan or repeated visibility calculation inside a heap comparator.

At maximum extent, the first summary level contains 562 direct and 281 diffuse chunks. Its
packed indices/priority keys/counts occupy words 32768..663050; the quarter-sized reduction spare
occupies at most 800000..958355, alternating levels after SSBO barriers. Both fit existing scratch;
a compact ascending physical-block list occupies words970576..1042400 at maximum extent, with
counts/offsets in970000..970575. No extra GPU allocation is needed. GPU-produced occupied counts
determine indirect reduction dimensions; the host submits a bounded number of levels with
no CPU job readback. Work operations share one208-byte uniform record and180-byte command buffer.

Solvers report successful channels through `Complete` against the unchanged selected batch.
GPU failure bits suppress combined completion writes; a final status/count read verifies rejection without
reading lists. Repeated completion or changed field/density certificates cannot advance stamps.
Failures withdraw sampling until reset; the view consumes only successful published fields.

`CloudVolumeDirect` consumes the frozen work batch and authoritative stored density only. Each
occupied node traces toward its relevant source through retained cell DDA with coarse empty skipping to the finite
domain exit. It accumulates actual segment lengths in metres,
at Medium and above saturates optical depth at80 without early termination, and stores dimensionless exp(-tau) in
RG16F. Very Low/Low instead permit their declared completed-interval optical exit with zero residual transmission. Empty nodes skip integration. Incomplete rays and nonfinite arithmetic set the channel's
failure bit; no failed batch advances completion, and partial in-place writes withdraw sampling.

Activation initializes each newly relevant channel to unshadowed across the bounded allocation
before selected updates. This full-field image pass has no ray integrals; its read/write traffic
and GPU cost are separate from the configured direct transport allowance and remain unqualified. Compatible
direction-stale values survive ordinary direction changes. Brightness/environment changes reuse
direct transmission; future consumers apply current illuminance and phase separately. Disabled
direct work creates no transport program and dispatches no integrals. Solar/lunar variants compile
source selection independently and execute with image barriers between them, preserving companion
channels without RG read-modify-write races. Reset/context/executable changes revoke the solver's
certificates; storage retains ownership of all fields.

`CloudVolumeDiffuse` refines at most 128 selected blocks after ordered direct completion. Each
256-thread group processes 256/N voxels with N independent ray lanes per voxel; N Y slices
cover a complete 8x4x8 block. A leader validates each node once, all lanes reach the shared-memory
barriers, and the leader combines N results in the declared direction order. The two shared
arrays use 4096+4096/N bytes per group (4608 at Medium). Any failed ray rejects the node/batch through the existing failure
mask. Every occupied node gathers the profile-selected normalized, balanced directions with equal weights through stored density
along exact constant-cell intervals. Each ray continues through empty gaps until the finite domain exit
or the profile range closure (400 metres at Medium), unless a lower-profile optical exit omits the tail. The incoming sum is
`sum(T_before*alpha*(Q(sample,-rayDirection)+D_read(sample)))+T_end*Environment`, with
`alpha=1-exp(-sigma*actualSegmentMetres)`, albedo .9, HG g=.6 and
`Q=.9*sum(currentIlluminance*cachedTransmission*HG)`. The new isotropic source is .9 times the
angular average. Environment enters once at the closure; outgoing directional first scattering
remains a separate view operation. Zero-density/guard nodes skip gathers and receive the
finite .9*Environment fallback. Alpha remains zero.

A same-scale nonempty batch repairs the existing spare from the preceding successful batch's
physical block list. Initialization and recenter maintenance write both fields; outside that list
the fields already match. At most 128 complete blocks need copying, including partial edges, with
no neighbor halo: the gather samples only the coherent old texture, including one
exact texel fetch each for density, old diffuse and optional cached direct visibility for
the current physical cell. Self-shadowing selects actual compiled variants; disabling it removes the
direct sampler. Disabled diffuse creates no solver and does no copy. GPU-selected indirect arguments
skip copy/gather groups for an empty list. Final fenced status/count inspection accepts only a
successful nonempty destination; empty work retains the old scale/texture and repair history.
Scratch word38 retains the previous count and words17280..17407 retain its list, after the two
source-specific lists and outside density selector temporaries32..37. Successful-only completion
replaces that history; solver reset retains it with its owning fields. Copy word39 counts actual
texels in the existing deferred status read; no additional readback is introduced. Separate indirect
records select full copying or bounded repair without reading job counts on the CPU.
Each field tracks its own scale. If either differs from the target scale, or an untracked callback
invalidated repair history, copy/re-encode the entire source before gathering. A scale transition
can require two consecutive full copies to bring both fields to the new scale. Same-scale repair
preserves stored half values exactly. `LastCopyBytes` charges texture read/write traffic separately
from `LastJobs`: zero for initial equal fields, at most524,288 bytes for bounded repair, or
280,077,600 bytes for a maximum-domain full copy. Metadata/dispatch overhead is separate; no extra
texture or buffer is allocated. Drawable snapshot copying remains independent. These counts do not
establish GPU execution time.

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

### Stored-volume view and configuration

In ConfigLib, set native cloud mode to **Volumetric**. `Clouds.Detail`, `SelfShadowing`, `Diffuse`
control stored-field features as advanced disable switches; `Clouds.Quality` selects the workload profile. `Clouds.LightingRaysPerUpdate` controls persisted-lighting throughput. `TraceScale` and `FullResolutionReference`
govern view resolution. Configuration snapshots commit at the Before preparation boundary;
view-only changes retain compatible density and lighting. Reload or mode release resets a latched
failure. `CloudStoredVolumeConsumer` is the sole Volumetric consumer, drawing at OIT .31.
Before Opaque, `CloudVolumeAdmission` retains source envelopes and admits Qcap+Dcap, aerial/local
medium headroom and the 2048-operation float accumulation allowance. Draws require that admitted
atmosphere snapshot, a valid atmosphere calibration identity, and the independent current camera /
frozen photometric publication. Atmospheric and engine-light calibration generations need not be
equal. Missing transient inputs omit the contribution. Initial density fill is bounded and neutral;
replacement retains the old complete domain and its initialized lighting while new density fills.

`CloudVolumeSolution` coordinates `CloudVolumeUpdates` and an isolated `CloudVolumePublication`.
Before drawing, zero-timeout fence polls retire a completed chain or a CPU domain-admission step.
Native validation retains its envelope fence, and initial/replacement density completion retains a
summary fence before storage admission. For an admitted active domain, density generation, occupancy
repair, selection, direct tracing, conditional diffuse copy/gather and combined completion submit
after the draw as one chain with explicit image/sampler/storage/indirect barriers. Shared scratch
is reused only after preceding GPU consumers. `CloudVolumeSubmission` captures one engine boundary
for the union of the prepared density, scheduling, maintenance/copy and selected transport programs.
Their typed dispatches reuse that authority with one entry barrier and one visibility barrier after
each producer; there is no internal native-state restoration or duplicate pre-dispatch barrier.
External bindings restore on successful and failed exit. Individual owners retain standalone
boundary support, and the later completed drawable copy retains separate authority. No boundary
survives a callback or changes CPU admission/fence ordering. No intermediate lighting fence or CPU diffuse-count
decision separates these passes. Final fenced lighting status/count and density-summary reads
validate the result before texture/encoding/bounds and drawable publication. Failed density suppresses
lighting completion; any solver failure prevents publication. Empty diffuse lists never swap an
uncopied spare. Timeout retains the previous drawable and submits no additional update; later weather
coalesces behind the frozen chain. A retiring publication can submit its next eligible bounded chain
after the draw in that same callback. A completed lighting batch copies changed whole blocks into the drawable
snapshot through typed `cloud_volume/publish` compute authority and barriers. Density/direct/diffuse
dirty bits are 4096/8192/16384 in working region flags. Metadata and occupancy are copied coherently
for all blocks; no CPU reads job lists. First publication, changed domains or replaced working fields
copy all channels; source activation copies direct, and an encoding change copies diffuse in full.
Publication uses three compiled operations under one engine boundary: initialize a 16-byte
argument record, copy metadata with one invocation per physical block while compacting dirty IDs,
then copy images indirectly with one 256-lane group per selected block. Selection-summary scratch
starting at word32768 is reused only after its fenced consumers retire; the maximum71825-word list
ends before the second summary range. Persistent cursors, completion status and diffuse repair
history remain intact. Indirect groups use64 columns and up to1123 rows, with uniform padded exits;
an empty list dispatches zero image groups. Full-channel cases skip compaction and copy the complete
physical grid, including other dirty channels. Dirty bits clear after the block's valid lanes submit
their writes; partial-edge lanes still reach the completion barrier. Storage, command, image and
texture barriers order all operations and later readers; no new fence or CPU count read is added.
Ordinary publication copies only flagged channels/blocks. Working fields can then change without
altering the drawable snapshot. Initial activation remains neutral until a completed snapshot exists.

OpenGL submissions remain in the engine command stream. Fences remove immediate CPU waits and
same-callback completion requirements; they do not provide an explicit asynchronous compute queue,
guarantee graphics/compute overlap, or remove the update's GPU cost. Update service bounds count
eligible batches, which can span multiple rendered frames. Diagnostics skip unfinished updates.

Lighting work selection synchronizes each 256-lane group's eligible count before sorting. Empty
initial and reduction groups publish invalid winner slots and zero backlog without either sorting
network; nonempty groups retain exact fair/priority ordering. Region demand and pending ages are
unchanged, and empty selection does not advance its fair cursor. Later occupancy/source changes
make retained demand eligible. Exact zero-to-positive or positive-to-zero density membership changes
mark the compact block list dirty; count/prefix/scatter rebuild it before selection. Lighting-only
changes and positive-density value changes reuse it. The demand merge still visits all region
metadata so remote source/density invalidation reaches dependent occupied receivers; candidate
classification visits only compact occupied IDs. No CPU count read is introduced.

`CloudVolumeCoordinates` converts the current camera/native placement into the published domain's
material coordinates, including during replacement; native wind translation enters once. View
settings retain density and lighting. Detail changes replace geometry; lighting feature changes
revoke queue/executable certificates and request refresh while compatible density/source envelopes
remain. Resize releases framebuffer metadata without clearing material-space history. Reload,
mode release and world/context teardown retire all owned resources.

`CloudVolumeViewRenderer` uses the shared `TraceScale` (half/quarter) and `FullResolutionReference` controls. Full resolution draws directly into native OIT without intermediate targets. Reduced views trace into private ceil-divided targets, then reconstruct the
six native Transparent products at full resolution through existing graphics pipelines. It clips against finite stored coverage,
material depth, particle visibility and liquid depth at mip zero. Coarse DDA retains traversal
state and skips only certified empty support. Occupied intervals use exact voxel boundaries,
actual clipped metre lengths and cell-constant R16F density, RG16F transmission and scaled diffuse
RGB. There is no native descriptor/procedural-noise lookup or low layer sample cap. Reduced views use screen-space sampling history; full reference remains deterministic.
Operation-limit failure discards the whole pixel prefix. Optional optical termination occurs only
after a completed segment at the profile threshold. The internal zero-threshold reference variant compiles the exit out.

One retained plane/crossing set switches between fine cells and coarse empty blocks along the original ray. Empty blocks jump to
coarse exits; occupied cells advance all tied voxel axes once. Physical indices wrap explicitly at
circular seams, and cell entry is corrected using original-ray crossing parameters when position
reconstruction rounds onto an adjacent plane. Nonprogress rejects the ray. Each returned interval
retains its own physical sample-cell address even though traversal state has already advanced.
All consumers fetch exact texels for the piecewise-constant volume model. Screen-space compact
reconstruction uses hardware bilinear filtering.
The constant-cell model can expose harder voxel edges and cell-to-cell lighting changes; no automated
image-quality threshold qualifies that tradeoff.
The outgoing phase direction is sample-to-camera. Current directional first scattering and cached
diffuse enter once; disabled self-shadowing uses unit transmission, and disabled diffuse uses
.9*current Environment. Those user-disabled sampling paths compile out. After physical front-to-back
integration, the shared affine atmosphere/local-medium treatment and frozen scene encoding apply
once. Bucket transmissions use each interval's near depth; overall transmission, glow metadata and
three weighted accumulations preserve the native OIT convention in both PBR selections.

Each `CloudVolumeViewTargets` set owns three compact float32 textures; each history also owns R16F observation age and an optional RGBA16F diagnostic attachment: RGBA physical radiance/opacity, RGB bucket
transmissions and RGBA clip-depth/completion/representative-distance/receipt metadata. Reduced tracing uses conservative two-by-two (half) or four-by-four (quarter) full-view
depth gathers and the unchanged occupied schedule. Intermediate writes use no blending; reconstruction
applies native blending once. `cloud_volume/reconstruct` reads exact four-texel metadata and uses two
hardware bilinear continuous samples where all support texels are complete and depth-compatible.
At depth discontinuities it selects the nearest compatible texel centre; no compatible sample leaves
native products untouched. The filtered cloud value and bucket transmissions generate all six native
outputs during final composition. There are no intermediate glow or accumulation buffers and no
alternative expanded-output path. Filtering before bucket expansion is the selected spatial approximation.
Metadata distinguishes invalid, complete contributing and complete exactly-empty rays. Empty support
or an empty selected fallback skips continuous reads and native identity blends. Failed prefixes stay
invalid. Reduced-resolution silhouettes can soften or thin, particularly at foreground depth boundaries;
there is no procedural lighting fallback. Reduced views retain physical temporal history. Odd sizes round up, and atmosphere shaft
coordinates remain in full-view pixels. Compact trace and two history targets cost approximately 103 bytes per reduced pixel normally (119 with diagnostics) (ceil edges add excess): half/quarter uses
about116.3/29.1MiB at3440x1377 or203.7/50.9MiB at4K normally, separately from volume payloads. Full resolution
allocates none. Resize and renderer release retire reduced targets
without clearing retained cloud lighting. The debug panel reports allocation. Native composition
requires six draw buffers/attachments; reduced filtering reserves eleven fragment texture units for shared blue noise and history age, with twelve required for diagnostic composition.

`CloudVolumeViewPass` retains immutable pass descriptions and graphics-pipeline preparation for each
view pass. Its key includes framebuffer identity, attachment revision, extent, blending mode and
shader executable/context. Stable draws reuse this preparation and the renderer's pipeline list and
submission callback. Each `BeginPass` still validates and borrows current images; cached metadata
holds no active pass reference. Resize, resolution changes and renderer retirement release cached
target references before screen resources, while executable changes refresh preparation. Current
camera, atmosphere, field and shader certificates remain draw-boundary checks. All sampled-resource
and frame references are cleared after synchronous submission, including failed draws.

Tracing and reconstruction bind the same `VgeFrameUBO`. Full-view dimensions, the derived ceil-divided
extent, shaft coordinate scaling and fog colour/density/minimum come from that immutable frame.
Fog headroom admission reads the same CPU snapshot. The shared frame owner captures current render
fog after the native camera/ambient callbacks; the cloud consumer does not reread ambient fog for
these values. Only flat-fog geometry and underwater state remain private medium parameters. Physical
solar/lunar/environment values remain in the view parameters because the shared frame, point-light
and photometric-storage blocks do not publish those atmospheric source values.

The existing **VGE debug visualizers** menu includes **Cloud Stored Volume** with stored density,
region identity, completed-age and initialized-fallback overlays. Its panel reports dimensions,
spacing, resident/replacement payload, density and lighting work/backlog, completed work, fallback
blocks and diffuse copy bytes. Diagnostic metadata reads occur only while this view is active,
at most once per second. Existing bounded generation/completion status reads remain separate
algorithm costs. Reconstructed view cost, copy bandwidth, shape and lighting latency still
require measured performance and user appearance review.

### Stored-volume temporal view

Reduced tracing refreshes one location per 2x2 reduced tile each frame and covers every pixel in
each aligned four-frame sweep; initial/incompatible views trace every reduced pixel.
Every fresh ray integrates complete exact voxel intervals. A physically smaller raw target and two alternating
history targets prevent read/write feedback. The raw target has ceil(history width/2) by ceil(history height/2) texels; the view maps each ray onto its selected reduced-grid location. Cold frames trace the full reduced grid into invalid previous-history storage before filtering into the disjoint destination. Compact metadata is RGBA32F: clip depth, completion,
representative distance and raw traversal visits or filtered history count. Radiance stays physical
through filtering and is encoded only during final native composition.

`CloudReprojectionHistory`, `CloudTemporalParameters`, cloud-owned blue-noise storage and
rotation-only reconstruction provide the view history certificate and sampling state. General `blue_noise.glsl`
reads an atlas with an explicit mask count; `interleaved_sampling.glsl` maps a supplied rank to a
four-frame 2x2 schedule without depending on clouds or a texture layout. The stored-volume payload
adapter owns filtering of its compact radiance/opacity and bucket values without borrowing previous
transport or shadow-cache state. The temporal pass distinguishes freshly traced, history-only and spatial-fallback locations. Compatible untraced pixels preserve prior physical transport without blending toward neighbors. Fallback fills unsupported holes but has zero observation confidence and cannot become reusable history. Failed fresh rays remain invalid. A newly empty ray rejects contributing history; removal is observed within at most seven frames under the constrained schedule.

The default compiled constrained schedule reads one of eight32x32spatial masks per four-frame
sweep, repeating every32frames. Both trace and temporal classification call `sampling.glsl` with
the same frame and tile. The existing32KiB atlas is reused, with one selection lookup. Its masks
come from deterministic tileable void-and-cluster generation; cyclic phase-label rotations for
masks1..7 are selected by four coordinate-descent passes minimizing equal adjacent-sweep orders,
including wrap. This is a constrained schedule, with no genuine STBN spectral claim.

Ordinary pixels refresh once per aligned sweep with1..7frame gaps.
The existing expiry-at-seven gate remains strict: a seven-frame gap carries only through
age six before a fresh trace. Odd-edge tiles clamp their selected coordinate and can repeat an edge
pixel. The32-frame period wraps seamlessly with the unsigned frame counter. Cold/resize frames
and full-reference bypass retain their existing behavior. Fixed-centre rays, temporal confidence,
lighting response, target sizes and persisted-volume work selection are unchanged. From an arbitrary
schedule position,18fresh observations require at most75frames.

Camera reconstruction and projection use rendering-owned `camera_reprojection.glsl`, also consumed
by LumOn velocity generation and its history debug projection. UV uses the OpenGL bottom-left
convention. Homogeneous reconstruction divides by W; position projection uses W=1 and the shared
`vgeFrame.prevViewProjMatrix`, whose fourth column includes the double-precision render-origin
delta narrowed only after subtraction. Surface velocity keeps its own validity flags and inclusive
UV boundary policy; cloud lookup requires finite half-open [0,1) coverage and positive clip W.

Cloud rays instead reconstruct a displacement with inverse-view W=0. Their placement delta already
contains camera and material motion once, so project that previous-eye-relative displacement with
the shared previous matrix at W=0 and add the compatible projection's fourth column. Neither the
native eye offset nor shared origin rebasing enters this direction transform a second time. Cloud
foreground endpoints use position projection, including the native eye offset, without cloud wind.
No private previous-camera matrix is stored or uploaded. The retained projection is only a CPU
compatibility certificate; placement, owner and successful-frame identity remain cloud-owned.
Shared camera history advances independently of cloud drawing. Only consecutive successfully
composited cloud frames may consume it; a skipped/failed frame rejects reuse rather than projecting
older cloud textures through the wrong camera. No LumOn texture, enablement state or velocity pass
is required. Representative range, support classification, age and confidence still follow the
cloud-specific rules below; sharing projection math does not make volume correspondence exact.

All exactly nonzero bilinear support taps must agree on empty/contributing completion, real-observation confidence, foreground support and representative-distance uncertainty. Exactly zero-weight taps are irrelevant. If that footprint is incompatible, select its nearest individually compatible texel centre and sample both physical textures there through hardware filtering. Its own confidence and age apply; no rejected tap enters the transport. If none qualifies, history remains unavailable. Fresh empty rays continue to reject contributing history. This retains boundary observations without interpolating across a discontinuity, at the cost of a possible displacement within the four-tap footprint. Compare current scene endpoints projected into the previous camera against previous clip metadata, using the shared origin-rebased previous view-projection and current inverse view, independent of cloud wind and layer height. The representative cloud distance is a proxy; the max(2 world units,5 percent) gate may conservatively reject edges. Current material/particle/liquid footprint clipping remains in effect.

Untraced pixels with zero material-relative translation map their viewing direction into the old
camera independently of distance, then obtain classification and distance from the nearest admitted
support there. Translation or wind instead uses the nearest completed fresh observation in the
existing 3x3 neighborhood as independent current classification/distance evidence. Its projected
previous-camera range gates the mapped history; missing evidence produces untrusted fallback.
Neither path reads old same-screen metadata to guess the moving ray's identity. Carried range comes
from the admitted physical footprint, using bilinear metadata weights or the selected nearest centre,
and is converted from previous-camera range onto the current ray with the material translation.
This is a bounded volume-position approximation, especially near depth discontinuities; fresh rays
keep their own measured range and certified empty rays store zero. Resource sizes, ray budgets and
the per-tap expiry threshold are unchanged.

When the compatible material-to-view transform is exactly unchanged, the CPU certifies the identity
mapping in the existing temporal block. The filter uses the destination pixel's exact integer history
coordinate, preserving its own confidence and age. It does not reconstruct an almost-identical
bilinear footprint through floating-point matrix roundoff. Any rotation or material translation uses
normal reprojection; foreground depth is still checked in the previous scene camera independently.

Only fresh updates can be constrained by neighborhood bounds, requiring four same-support, distance-compatible observations spanning two pixels on each axis. A single observation cannot erase history. Bounds apply to opacity and bucket transmission; RGB differences alone do not replace history. Supported coverage-bound replacement explicitly rejects prior information. The fresh prior weight is min(count,7)/(min(count,7)+1), multiplied by clamp(1-4*opacity delta,0,1). Colour changes retain that response instead of accelerating selected fresh pixels ahead of their carried neighbours. Count is 1/(1-effective prior weight), up to eight, rather than an admission/frame counter. A carry transports confidence without an independent-observation increment; zero weight or a clipping replacement starts at one. Counts describe declared blend confidence, not statistical independence of correlated rays.

At saturated confidence and unchanged coverage, each fresh observation retains7/8 of the remaining
lighting-step difference. Eighteen fresh observations reduce that difference below one tenth;
the schedule reaches them within75 rendered frames. Response is frame-dependent,
not a guaranteed wall-clock latency, and foreground/support rejection can start a new observation.
The response uses the ordinary ray schedule, targets and passes with no transition burst. It does
not synchronize stored illumination: completed direct/diffuse blocks may still represent different
source ages while bounded transport work refreshes them.

For a fully qualified bilinear footprint, refresh age is `sum(weight * tapAge)` and confidence is
the weighted harmonic mean `1 / sum(weight / min(tapCount,8))`, clamped to [1,8]. These weights
match the admitted physical footprint, so a lightly weighted old or weak tap has proportionate
influence. Interpolation cannot exceed the strongest input confidence or create an independent
observation. Nearest-compatible fallback uses the selected centre's own values. The exact stationary
path preserves its own metadata. Carried age adds one; a genuine fresh trace resets it to zero.
The fractional R16F age measures refresh of contributing spatial support, not the oldest component
of previously accumulated radiance. Each tap still expires at age seven before filtering. If no
fresh observations enter the support, its minimum surviving age increases once per carry until
all of it expires; interpolation alone cannot renew stale data. Foreground and support gates remain
independent of freshness and confidence.

Each history owns a required R16F refresh-age field. Unrefreshed support expires at age seven. The default-off `VGE_CLOUD_TEMPORAL_DIAGNOSTICS` compiled option separately adds an RGBA16F record for prior weight, status (fresh1/carried2/fallback3/invalid0), rejection and displayed age. Normal temporal GLSL has no diagnostic output; normal composition has no diagnostic sampler or diagnostic UBO. The ordinary age sampler/output remain production validity metadata. Optional diagnostic images and their framebuffer attachments exist only during diagnostic capture; toggling them preserves physical image identities, confidence and age. Temporal diagnostics add no runtime CPU readback.

Normal storage is approximately103bytes per reduced pixel: two46-byte histories plus44-byte raw trace at one quarter their area, with ceil-edge overhead and32KiB blue noise. Diagnostic capture adds16bytes per reduced pixel across both histories, bringing the nominal total to119. Full resolution allocates none. Trace/filter UBOs use64bytes; only diagnostic composition borrows the filter UBO. Filtering writes four attachments normally and five when diagnostics are compiled in.

The Cloud Stored Volume visualizer selects that compiled variant and adds update-class colors (fresh green, carry blue, fallback orange, invalid red), effective prior weight (black0 to white1), reason color and observation age (black0 to white7). Reason codes:0 filtered admission,1 no compatible frame,2 projection outside coverage,4 incompatible completion/count,5 foreground depth,6 malformed physical transport,7 representative distance,8 expired age,9 failed fresh trace,10 supported-bound replacement,11 zero reactive weight,12 nearest-compatible admission. Code12 indicates retained information, not rejection; status and effective weight still determine whether it contributes. Reason colors use fract(code*(.37,.61,.83)); code0 is green. The visualizer does not change physical sampling or create CPU readbacks.

Camera/material motion enters exactly once. Routine weather uploads, light/fog/exposure evolution
and partial volume publication preserve compatible history. Camera cuts, incompatible storage/layout,
world/context/shader identity, frame gaps, resolution/features/debug or underwater-mode changes revoke
it. Commit and target rotation follow successful final submission only. Resize/reload/retirement
release all view history without clearing compatible cloud-space density or illumination. These
screen resources add no solver fence, CPU wait or new 3D field. User-run temporal appearance and
measured total performance remain separate acceptance activities.

## Cloud quality profiles

`Clouds.Quality` selects 0 Very Low, 1 Low, 2 Medium (default), 3 High or 4 Very High.
It does not change TraceScale, the four-frame fresh-ray cadence, grid spacing or memory limits.

| Level | Shape samples per overlap | Diffuse directions / range | View stop | Direct/diffuse stop | Default update rays |
| --- | ---: | ---: | ---: | ---: | ---: |
| Very Low | 1 | 2 / 100 | .020 | .020 | 20,480 |
| Low | 1 | 4 / 200 | .010 | .005 | 40,960 |
| Medium | 8 | 8 / 400 | .005 | disabled | 81,920 |
| High | 27 | 16 / 600 | .0025 | disabled | 163,840 |
| Very High | 64 | 32 / 800 | .00125 | disabled | 327,680 |

Detail, SelfShadowing and Diffuse remain advanced disable switches at every level. A positive
LightingRaysPerUpdate overrides the table's budget; zero selects it automatically. Stopping is
owned by the profile, with no separate persisted stopping control. Each capture resolves one
immutable profile at configuration application, not during voxel/ray execution. Structural profile
choices compile into generation, transport and scheduler executables. Existing disabled feature
variants still remove their samplers and dispatches.

Lower levels sacrifice angular/range detail, generation quadrature and faint transport tails.
A direct optical exit stores zero remaining visibility; a diffuse exit drops the remaining source
and environment tail after a completed interval. This can darken illumination or strengthen shadows.
Medium and above retain full direct-domain transport and the declared finite-range environment
closure. Safety-limit or arithmetic failure never becomes a valid optical exit. Diffuse directions
are two opposite diagonal rays at Very Low, four balanced horizontal diagonals at Low, eight cube
corners at Medium, and two/four rotated cubes at High/Very High. Weights are equal and nonnegative;
the material albedo, HG response, encoding bounds and coherent iteration remain unchanged.

Budget accounting charges N rays per positive diffuse origin and one per direct origin/source.
A 256-thread diffuse group covers 256/N origins; N slices cover each complete physical block.
The fixed shortlist and whole-block capacities can leave allowance unused. An override too small
to fit even one occupied block cannot refresh that block; it never exceeds the user's ray limit.
More rays per voxel can increase refresh latency despite a larger total budget. No profile promises
an FPS ratio or physical convergence.

Profile changes finish the current frozen update before replacing executables. Changed density
quadrature/detail uses bounded replacement storage while the last drawable snapshot remains visible.
Lighting-only changes preserve geometry and drawable fields, revoke scheduler/solver certificates,
and refresh compatible stale illumination under the new rule. Budget-only changes retain all
compatible history. The existing grid, guards, metadata, 512 MiB working and 1 GiB peak limits are
unchanged; profile variants add executable storage, not persistent volume or screen textures.
Coarser density, separate lighting grids and changed temporal cadence are deferred design options.
They are not part of this control: the shared lattice avoids new resampling/identity contracts,
and the established cadence retains its validated history expiry and motion behavior.

## Persisted lighting update budget

`Clouds.LightingRaysPerUpdate` sets an upper ray budget per submitted lighting update, not per rendered frame or view pixel. Zero (the persisted default) selects the quality profile budget; positive overrides range from2,560..327,680 in2,560-ray steps. Medium resolves to81,920. Each unit reserves512 direct rays and2,048 diffuse rays. One positive stored voxel/source costs one direct ray; one positive diffuse voxel costs the profile direction count in gather rays. Disabled solvers/sources and insufficient demand leave allowance unused; it never transfers between solvers. Budget changes retain fields, pending ages, fair cursors and view history; in-flight updates finish under their frozen setting.

Generation records exact represented positive-cell counts in the existing occupancy word: bits0..1 retain traversal support, bit2 certifies the origin count and bits3..11 store0..256 positive cells. Circular maintenance recounts retained cells as it clears entering/guard cells. These counts are independent of the conservative traversal halo. Certified zero-origin blocks retain pending demand but receive no lighting jobs. Unknown support remains conservative. Density/source changes still invalidate dependent occupied receivers across the domain.

Selection derives direct shortlist capacity from the direct ray allowance and diffuse capacity from its ray allowance divided by the actual rays per full block, capped at128. Medium defaults to64 direct and32 diffuse priority candidates, plus half as many fair candidates. Cheaper angular rules can therefore refresh more origins within their ray limit. Their unique union can refill unused ray capacity, bounded at256 direct source jobs and128 diffuse blocks. Complete-block updates preserve coherent diffuse repair and successful-only publication. Fair service reserves the original half-block capacity in rays, rounding odd jobs upward and admitting at least one whole diffuse block when its full ray allowance can fit it; priority uses remaining allowance. Candidate limits and whole-block rounding can underspend even with more pending work. No partial block is stamped fresh. The default reserves16,384 direct and65,536 diffuse rays; a higher budget changes throughput, not numerical transport.

The same fixed4MiB working scratch accommodates all settings without geometry/history reallocation. More than64 candidates in either list uses the compiled1,024-key selector; smaller pools use256keys. Existing fenced status reports separate ray allowances, charged rays and unknown jobs. Certified charges are actual ray launches for successful work; unknown charges are bounds. Developer diagnostics report occupied origins, backlog/age, unused allowance and empty origin lanes after completion. Ordinary rendering adds no diagnostic scan or synchronous readback.

## Native compatibility and qualification

Native lookup resources and cloudmap.fsh remain available for native map production.
`SceneColorLegacyPatches` retains conditional native Simple and Volumetric conversion for unavailable
initial ownership and normal mode restoration. Native geometry and producer resources remain borrowed;
`SceneColorCloudInputs` retains native color bounds separately from physical cloud bounds.

Focused tests cover declared transport arithmetic, physical units, finite values, native mode
ownership, clipping, OIT/encoding and resource lifetimes. They impose no approximation-quality
thresholds. Live appearance, effective temporal retention and total frame cost remain separate
acceptance activities in the cloud volume checklist.
