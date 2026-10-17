# Cloud rendering

VGE owns separate Simple and Volumetric shaders while native Vintage Story weather owners
produce geometry and column controls. The native `cloudRenderMode` preference remains authoritative:
0 is Off, 1 is Volumetric, and 2 is Simple. Rendering works independently of LumOn and uses the
same [completed atmospheric publication](PBR.Atmosphere.Publication.md) in both PBR selections.
Implementation and acceptance evidence is tracked in [the cloud checklist](PBR.VolumetricClouds.todo).
Conservative empty-region skipping is specified by the [height hierarchy proposal](PBR.CloudHierarchy.Proposal.md)
and qualified in its [implementation checklist](PBR.CloudHierarchy.todo).

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
Unavailable, mismatched or failed construction selects the existing column traversal explicitly.
Reduced view and secondary solar/lunar rays use the same certified summaries to reject the root or
skip groups whose complete horizontal ray segment misses their height interval. Owned mip indices
are one below logical hierarchy levels; the finest summaries descend directly to native columns. Secondary rays operate
in the finite native map domain independently of camera visibility and scene-depth clipping.
The deterministic full-resolution reference retains native-column DDA for view and light rays.

Surviving view and light intervals keep the original DDA clocks, sample counts, PMJ indices and integration
order. Group skips replay boundary additions without native column fetches, preserving accumulated
float rounding and tied-axis crossings. Nodes already covering the current ray height descend
without computing an exit clock. Work-limit fallback resumes native leaf work instead of discarding
density. These reductions in leaf queries do not establish a GPU performance improvement.

`cloud_volume_light.glsl` integrates secondary optical depth after density and optional hierarchy
helpers are available. Its 1e7-world-unit limit, finite optical-depth saturation and per-column
quadrature are shared by accelerated and DDA paths. Solar/lunar phase decorrelation remains at
the directional-light caller. This does not publish cloud shadows onto scene receivers.

Source observation precedes foreground-depth and lighting admission. An ordinary content generation
withdraws the old certificate while retaining compatible storage; owner, world, allocation or executable
replacement retires it even if another missing input prevents drawing. Reduced submission revalidates
the native borrow after pass preparation, then selects certified summaries or DDA immediately before
the trace/history/resolve boundary. A revoked borrow submits none of those passes and invalidates
history. Optional reduction failure leaves ordinary DDA rendering available until reset; it does not
release visible ownership or admit native duplicate drawing. Resize/reload reset both owned domains;
mode release and world teardown dispose them. Simple and Off allocate no cloud-height hierarchy.

## Density, lighting and scene composition

Volumetric density uses native horizontal footprints and vertical bounds. Periodic procedural
3D noise modulates density inside that envelope; exposed faces soften inward while occupied
shared faces remain connected. Broad faces and stepped cubic masses remain. Noise follows native
placement, integer recentering and committed/residual wind without a second advection.
Disabling detail also disables edge erosion, restoring constant-column optical depth.

Reduced tracing jitters sample positions within each occupied integration segment using a
cloud-owned 128-sample, Owen-scrambled PMJ sequence. It reuses the shared Sobol-based generator
used by LumOn, with independent cloud seed and GPU lifetime; LumOn need not be enabled.
View and secondary-light marches use separate sequence dimensions, with cell, view-sample and
solar/lunar decorrelation. Full-resolution pixel identity selects a sequence offset, and the
temporal index advances once per four-phase cycle so each scheduled pixel visits the complete
sequence. Segment lengths, native column bounds and sample counts are unchanged.
This sequence is not a spatial blue-noise mask. No density noise is added by sampling jitter.
The full-resolution reference retains deterministic midpoint quadrature and has no PMJ sampler;
constant-density mode also retains midpoint sampling. The sequence texture persists across
ordinary frames and is retired with reconstruction resources on resize, reload or disposal.

View and bounded secondary solar/lunar marches share this density definition. HG single
scattering uses g=.6 and one isotropic approximate multiple-scattering octave with contribution
and occlusion .5/.5. This approximates higher-order transport rather than a second explicit
physical bounce. Environment uses the published E/pi response. Simple uses physical directional
and environment inputs without volume marching, self-shadowing, 3D noise or temporal resources.
Native brightness/daylight/base-color attributes are not physical incident illumination.
Simple retains native shape/coverage controls and applies perception's factor 20 locally without
temporarily changing engine uniforms.

Both shaders apply existing aerial or underwater/local-fog transport once, then use the frozen
frame's scene-storage encoding. Physical source bounds join admission before Opaque; exposure
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
virtual-shadow-map owner; no Beer shadow map or earlier producer callback is introduced here.

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
owns one immutable nine-layer RGBA32F `Texture2DArray`, with three disjoint three-layer views.
Each view has a persistent framebuffer with its three local layers attached to the payload outputs.
Layer zero stores integrated physical RGB radiance with overall transmission in alpha; layer one
stores three bucket transmissions with a zero spare channel. Layer two stores first/last
contributing distance and current opaque/liquid depth. The two optical layers share acceptance
and interpolation weights. Temporal sampling binds the current trace and previous history views;
resolve binds the newly filtered history view. Typed view bindings reject overlap with active
framebuffer images, and active target passes borrow both their view and the shared storage owner.

The three blocks rotate between current trace, previous history and next history after successful
OIT submission and history commit. A skipped or failed draw invalidates content history without
advancing block roles. The block cursor is independent of the PMJ frame index. Framebuffer
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
differences distinguish camera/cloud movement from grid recentering. History requires consecutive
frames and compatible owner/world/allocation/shader, extent/projection, calibration, medium,
settings and completed lighting. Monotonic routine uploads may retain history, with current
support/depth and optical-change checks on the GPU. Cuts, missing input, replacements, mode
release, reload and reset invalidate history. Accepted history has weight .75, clamped to a
compatible current neighborhood; attenuation jumps above .05 reject it. Spatial reconstruction
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
| `FullResolutionReference` | false | Full view, midpoint transport, no history or upsampling |
| `ViewSamples` | 4 | 1..16 subdivisions per occupied column |
| `LightSamples` | 1 | 1..4 subdivisions per occupied column |
| `Detail` | true | Noise and exposed-edge erosion; false gives native-transmittance reference |
| `DetailAmplitude` | .15 | 0..0.95 bounded interior modulation |
| `EdgeWidth` | 2 | .01..10 world units of inward softness |
| `NoiseScale` | .08 | .001..1 noise cells per world unit |

Nonfinite controls restore defaults. The same immutable quality selection supplies admission
and submission. Reference mode is history-free, not a claim of converged light transport.
Changing quality or reference selection invalidates incompatible history. Simple ignores these
volumetric controls.

Reduced targets and pipelines persist across stable frames. Resize/reload retire their owned
resources; wrappers never delete engine attachments. Allocation is bounded before creation to
view dimensions <=16,384 and nominal trace/history image storage <=1 GiB. The three sets require
144 bytes per reduced pixel, excluding native producer data and other resources. This is a
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

Deterministic reconstruction fixtures retain per-frame reference comparisons. Detailed PMJ
sampling is evaluated separately: a complete sequence at matching trace pixels measures
transmission convergence against higher-budget midpoint references, whose numerical stability
is checked independently. Full-view bucket and accumulation payloads are compared as a temporal
mean against the same-budget midpoint reference with the existing spatial error budgets.
Changing the reference subdivision count also changes the finite-budget OIT bucket weights;
it cannot establish a converged bucket reference merely by increasing density samples.
Finite values, attenuation bounds and clipping remain per-frame requirements. The actual
bounded temporal filter is checked for noise reduction separately from offline sequence
averaging; its fixed history weight and rejection/clamping do not imply vanishing noise or
an unbiased transport estimator.

`CloudHierarchyPerformanceTests` is an opt-in comparison of same-candidate DDA and certified
hierarchy traversal through the production reconstruction scheduler. It records paired GPU
construction, trace, temporal, resolve and independently measured total costs, plus CPU submission,
adapter identity, allocation reuse and raw samples. The accelerated total includes a fresh reduction
for each native generation. Synthetic map/depth authoring and shared native producer/scene-depth work
remain outside both measured paths. Those controlled workloads do not establish live whole-frame
costs or reproduce captured weather. Large separated-height gaps are adversarial shader-domain
inputs, not a claim about normal native weather. Measurement cases are disabled in routine tests.

These receipts establish source/automated contracts, not user visual
acceptance or live frame time. Weather appearance, cubic fluff, motion, in-game lifecycle
transitions and hardware-specific GPU/CPU/memory budgets remain acceptance activities in the
linked checklist.
