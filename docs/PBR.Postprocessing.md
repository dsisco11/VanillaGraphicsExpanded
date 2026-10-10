# Owned HDR postprocessing

VGE owns the complete scene postprocessing invocation. OwnedPostprocessHook replaces
ClientPlatformWindows.RenderPostprocessingEffects for a pending HDR scene; it never executes
the original scene pass after an owned failure. OwnedFinalCompositionHook likewise replaces
RenderFinalComposition for that scene with FinalDisplayShaderProgram. The engine's ScreenManager
scheduling, overlay callbacks and presentation blit remain intact. Non-scene menu rendering outside HDR scene ownership keeps
its display-referred engine path, rather than being interpreted as an HDR scene.

Installed 1.22.7 IL confirms: transparent merge, AfterOIT, return from RenderToPrimary,
RenderPostprocessingEffects, stage-8 overlays, RenderFinalComposition, post-final overlays,
and presentation. Evidence is in artifacts/SceneHdrRuntime/installed-postprocess*.il.txt.
The replacement preserves the original exit target (primary), full primary viewport and
standard blending for the following overlay stage.

## Responsibilities and order

Before deferred composition, LightShaftOcclusionRenderer runs at Opaque order 8.75, after
opaque receivers and before direct lighting (9), water transport (10.5), and final PBR composition
(11). It publishes current-frame depth-distance occlusion for aerial in-scattering. Its Before
callback withdraws the previous frame, so forward draws preceding publication see neutral occlusion.
AmbientOcclusionRenderer follows at Opaque order 8.8 and publishes spatial visibility for deferred
ambient/indirect lighting. It does not attenuate direct lighting or atmospheric scattering.

PostprocessPipeline coordinates separate algorithm owners:

1. CameraExposureRenderer meters the completed unexposed scene and publishes temporal EV.
2. BloomRenderer extracts and filters scene-linear HDR bloom.
3. LightShaftRenderer extracts and radially filters exposure-relative HDR light-shaft bloom.
4. RetainedPostprocessRenderer prepares owned RGBA16F
   scene/luminance output for antialiasing (or copies scene RGBA when disabled).
5. The owned final shader applies optional edge smoothing, adds owned glare, applies camera
   exposure and the shared tone curve once, then retains grading, vignettes and dithering.

All scene postprocess draws use typed VGE programs, GLSL 330, PSOs, render passes and one
restoring GraphicsCommandContext boundary. Camera metering and final composition each have
their own restoring boundary before and after the effects. No original engine findbright, blur, light-shaft, SSAO, bilateral or luma
shader is invoked by the replaced HDR pass. The copied SSAO implementation and its kernel
adapter have been removed. The owned horizon integration and joint spatial filter described below
replace the earlier neutral placeholder.

Final composition binds typed owned scene/luma, bloom, shaft and exposure inputs directly.
It writes only primary color through a private framebuffer borrowing the presentation image;
no sampled texture aliases that output. The engine framebuffer table and overlay/presentation
schedule are unchanged. Final grading, luminance-guided edge smoothing and screen effects are independently authored
in owned shader assets, followed by one final dither. Native controls are read, but the grading,
antialiasing and vignette appearance is not a pixel-equivalent reproduction of engine shaders.
The native FXAA switch selects this edge filter; the filter is not the FXAA algorithm. The engine final executable is not used for HDR scenes.

Publication is atomic across effects and retained operations; missing, stale or failed publication
raises an error. Final consumption is allowed once and the handoff ends scene publication even
on failure. Disabled glare publishes one persistent black image and releases effect storage;
disabled AO withdraws its current-frame publication and retires its hierarchy/filter storage. Manual camera exposure is independent.
SceneColorPostprocessBindingHook, PbrFinalDisplayPatches and the old exposure texture-unit scope
are removed. Copied display helpers and obsolete reference-patch fixtures have been removed.

## Original display implementation and deferred effects

No base-game shader implementation is copied into the owned final path. Gamma/brightness/contrast
controls drive an independently authored display transform. Native gamma 3 maps to neutral
grading after the single sRGB transfer; changing the slider applies a relative adjustment.
Grading is followed by luminance-based warm tinting and procedural damage/frost/glitch treatments. The edge filter uses the owned perceptual
alpha metric and bounded neighboring samples. Bloom uses the independently authored multiscale Gaussian pipeline below. Light shafts now use the independent radial algorithm below. AO uses the independently authored horizon integration and spatial reconstruction below.

## Horizon-integrated ambient visibility

AmbientOcclusionRenderer runs at Opaque order 8.8 from completed material receiver depth and
owned surface-array normals/materials. When particle separation publishes corrected receiver depth,
AO consumes that existing typed texture directly, matching deferred lighting; otherwise it uses
primary hardware depth. Native ssaoQuality zero, or the native postprocessing gate,
disables it; quality 1 uses three slices with four samples on each side, quality 2 and above uses
six slices with six samples on each side. Both evaluate ceil-half resolution and reconstruct to
full resolution. These are bounded 24/72 horizon candidates per reduced pixel. No second AO enable
setting, engine sample kernel or engine intermediate is used.

The independently authored shader reconstructs view positions with the inverse projection and
transforms encoded world normals with the current view rotation. Each 2x2 receiver footprint selects
its nearest real depth. Paired directional searches use a projected 1.25-block radius bounded
by the viewport extent, with approximately uniform radial spacing from a one-pixel minimum.
Independent deterministic pixel-dependent slice rotation and radial start offset distribute
sampling without frame-varying noise. Samples outside the viewport contribute no observation. Background depth and coplanar/below-surface
samples contribute unobstructed evidence, allowing a prior horizon to relax; they never raise
occlusion. The normal-direction bias remains 0.02.

For each view-space slice, project the normal into the slice, then integrate the cosine-weighted
visible interval between the two signed horizons analytically. Intersect the visible camera
hemisphere with the projected normal hemisphere before integrating; blend occluder horizon
cosines toward their unobstructed limits by bounded distance and transmission weights.
Located hierarchy leaves are projected into the slice before choosing the signed horizon. Divide the summed projected-normal-weighted
visible integrals by the matching unobstructed integrals. This keeps an unobstructed tilted plane
neutral despite finite angular sampling; there is no artistic contrast exponent or global darkness
clamp concealing invalid geometry. The mathematical approach is described by
[Jimenez et al., Practical Realtime Strategies for Accurate Indirect Occlusion](https://www.activision.com/cdn/research/PracticalRealtimeStrategiesTRfinal.pdf).
This implementation does not import a reference implementation or another renderer's shader source.

Samples fade by squared world distance within the radius. Each side maintains a running horizon
in cosine space, where larger values represent stronger occlusion. Stronger candidates replace
the horizon immediately; weaker subsequent candidates release 25% of the difference toward their
own evidence. The parameter is a dimensionless release fraction clamped to [0,1]: zero retains
the maximum horizon, one accepts each later weaker candidate in full. It does not represent
world-space thickness. Repeated equal wall evidence retains the horizon, while a thin foreground
object followed by background or coplanar evidence releases it gradually. Searches process
radial steps from near to far, and located hierarchy leaves select their actual signed slice side.
There are no extra neighboring-depth reads. This remains a single-depth-layer heuristic, without
recovered backside geometry or temporal history.
Occluder material transmission reduces horizon evidence; receiver transmission attenuates the
reconstructed occlusion once. Alpha-tested foliage contributes only where depth survives coverage;
its material transmission remains meaningful. Transparent OIT surfaces are not opaque AO receivers
or depth occluders. Visibility fades to neutral between 64 and 96 view-space blocks.

Running-horizon validation: **40 focused GPU tests passed**, including bounded release weights
(-1, 0, 0.25, 1, 2), ordered stronger/weaker/equal evidence, analytic quadrature, plane/sky
neutrality, continuous walls, thin strips, corners, transmission, current-frame motion, odd
resize/lifecycle and particle receiver metadata. The 257x129 strip and wall share their foreground
edge; a background region 5–19 pixels beyond contact has mean visibility 0.99014 versus 0.98597
at low quality and 0.99202 versus 0.98796 at high quality after filtering/reconstruction. This
compares geometry under the new algorithm, not a matched comparison against the old shader.
All **433 production** and **507 fixture** shader variants built successfully. Receipt:
`artifacts/horizon-relaxation-validation.log`. Five warm 1280x720 synthetic samples, including
shared HZB generation and three AO draws, gave median GPU times of **0.5704 ms** (low) and
**1.4397 ms** (high); allocation and readback are excluded. These are current synthetic costs,
not a speedup claim, live frame timing or visual acceptance. Resource counts remain unchanged.
Parent live visual and matched-scene performance acceptance remain open.

The shared [DepthHierarchyRenderer](../VanillaGraphicsExpanded/Rendering/DepthHierarchy/DepthHierarchyRenderer.cs)
publishes one mipmapped R32F minimum-hardware-depth texture at opaque order 8.7, after corrected
receiver depth at 8.5 and before AO at 8.8 and indirect lighting at 10. It runs independently of
LumOn when native AO is enabled; it builds once per frame only when either consumer is enabled.
AO and LumOn borrow that exact texture instance. A publication is identified by its allocation,
shared frame snapshot and captured integer frame index; Before withdraws it, and consumers reject
a mismatched frame/view. Native AO quality does not change the depth representation.

Mip 0 copies corrected receiver depth without resampling. Each later floor-sized mip conservatively
reduces every source texel whose normalized interval overlaps the destination texel. Odd axes can
require three taps; an axis that reaches one retains its sole texel. The chain continues to 1×1.
Typed image bindings write the local levels, and GPU buffer transfers fill the coarse tail after
the compute dispatch. Generation samples the separate corrected receiver texture, so no attached
framebuffer destination can feed back into its source. The complete mip range stays accessible to
consumers. No texture views, texture barriers, per-frame attachments or duplicate consumer pyramids
are needed; synchronization and the finishing-group protocol are described below.

AO selects an explicit mip from radial sample spacing, capped at level 4 and the available chain.
Background minima are rejected immediately. For accepted minima, a bounded descent through the
same proportional footprints finds a real source pixel; equal-depth candidates choose the source
nearest the requested sample. This preserves positions on tilted planes and samples that source's
normal/first-person identity and material transmission. The old min/max interval rejection is removed:
minimum depth supplies conservative candidates, and reconstructed source distance enforces the
world-radius bound. At most one coarse read plus four nine-tap descent steps occur per candidate;
one-dimensional and even-sized footprints use fewer taps. This bounds hierarchy work without
substituting a coarse synthetic surface for the real occluder.

Raw visibility stores (visibility, positive view depth, octahedral normal XY) in RGBA16F. A 3x3
spatial filter rejects depth/normal disagreement; a second 3x3 joint reconstruction compares those
samples against the full-resolution receiver. Depth weights use a 0.02 + 0.01*depth scale, a hard
three-scale rejection, and normal-dot raised to 32. Unmatched thin receivers and background remain
neutral instead of inheriting a neighboring wall's occlusion. Current-frame-only processing needs
no motion vectors, history, camera-cut heuristic or rejection cache. Disocclusions cannot retain
previous-frame AO, but screen-space sampling may still change during camera movement and lacks
off-screen/hidden geometry. Temporal accumulation is deliberately not enabled without the required
reprojection and history validation.

PBR composition consumes visibility with its matching receiver depth at sampler unit 12. The
current-frame validity flag and depth agreement prevent stale or unrelated sampling; first-person
visibility proxies bypass world AO as both receivers and occluders. Clean refraction captures made
before AO publication remain neutral; they never sample the previous frame. Standalone and LumOn
PBR composition split incoming integrated illumination into diffuse and specular responses before
applying visibility. Both use the same material/Fresnel response and the same visibility helper.
The diffuse factor is scalar AO. Indirect specular visibility is
`clamp(pow(clamp(N·V,0,1)+AO, exp2(-16*roughness-1))-1+AO,0,1)`, with AO and perceptual
roughness bounded to [0,1]. The actual reconstructed view direction and receiver normal determine
N·V; there is no camera-axis substitute. This empirical [Lagarde approximation described by
Filament](https://google.github.io/filament/main/filament.html#lighting/occlusion/specularocclusion)
approaches diffuse visibility for rough surfaces and depends more strongly on view angle for smooth
surfaces. It does not recover directional specular occlusion or a bent normal.

Existing diffuse/specular strength inputs independently interpolate each visibility from neutral;
each indirect term is multiplied once. Standalone environment light and LumOn PBR indirect use
the same policy; LumOn's compatibility mode remains diffuse-only and uses only diffuse strength.
Forward/OIT and first-person receivers without a matched AO publication retain unoccluded local
lighting. Legacy combine/debug shaders have no matched visibility input and retain AO=1;
they share the material/view response and use the actual normal rather than inventing a bend.
No additional texture, target, binding or pass is introduced. Direct sun, point lights, emission, sky, water/aerial transport and generated glare
are not multiplied by AO. Final composition has four samplers and no AO input.

Diffuse/specular occlusion validation: **95 focused GPU tests passed**, including six AO composition
theories covering **96 internal numeric scenarios** across standalone, LumOn diffuse-only and
LumOn PBR modes. Known direct/emissive radiance remains separate while partial AO and independent
strengths constrain single application of visibility. Coverage includes dielectrics/metals,
roughness 0/0.05/0.5/1, N·V 0.1/0.5/1, an off-axis receiver with a camera-facing normal,
neutral/full/partial AO, zero/partial/out-of-range strengths, depth mismatch and first-person
proxies. Additional tests cover direct lighting, HDR, the full LumOn pipeline, diagnostic splits,
mode changes and clean refraction capture/restoration. All **433 production** and **507 fixture**
shader variants built successfully. Receipts: `artifacts/indirect-occlusion-validation.log`
(initial run: 90 passing, five fixture camera setup failures) and
`artifacts/indirect-occlusion-final-validation.log` (95 passing after explicit fixture camera/light
snapshots). These headless results do not establish live appearance or matched-scene performance;
parent acceptance remains open.

Separate GpuResourceCollection owners manage the shared hierarchy and AO's three visibility images.
The hierarchy uses one typed compute dispatch and no framebuffer targets; AO retains its framebuffer borrowers.
The compute owner retains its dispatch block and is retired on shader reload. A shared `DepthHierarchy`
GPU profile scope records generation separately from either lighting consumer; LumOn reads that scope for its HZB timing counter.
Stable frames reuse storage; resize, reload, world teardown and disabled consumers withdraw publication and retire it.
Disabling AO alone retires its visibility images while the hierarchy remains if indirect lighting requires it.
Raw/filtered ceil-half images and full-resolution visibility use eight bytes per texel. AO payload is
8*(WH + 2*ceil(W/2)*ceil(H/2)); the shared hierarchy adds
4*sum(max(1,W>>m)*max(1,H>>m)) across all mips. At 1920×1080 these images total about **34.28 MiB**,
with **one hierarchy dispatch, four coarse-tail transfers and three AO draws**. Indirect lighting no longer
allocates or generates a second hierarchy. There are no history images or per-frame GPU allocations.
The hierarchy also owns a four-byte completion counter and staging for levels seven onward; this is 624 bytes
at 1920×1080, separate from the image payload. These counts are analytical; measured results are recorded below.

The downsampler adapts the [single-pass downsampler finishing-group pattern](https://gpuopen.com/manuals/fidelityfx_sdk/techniques/single-pass-downsampler/)
to conservative minimum hardware depth and OpenGL's image limits. A 256-invocation workgroup owns one texel
at mip six (or the last level for smaller inputs). It backtracks the exact floor/ceiling proportional
footprints to establish its local tile and overlapping halo. It copies a disjoint share of mip zero,
then computes levels one through six using two shared arrays totaling 20,480 bytes. A separate shared
four-byte flag distributes the finishing election. Halo calculations overlap, but each global mip texel
has exactly one writer. This retains odd-edge coverage, tile-seam minima and one-texel tails, including
one-pixel-wide or one-pixel-high sources.

Every invocation publishes its image writes before lane zero atomically increments the completion counter.
The last finishing workgroup reads the globally coherent mip-six image and computes the remaining levels;
other groups exit immediately. There is no spin-wait or assumed dispatch-wide barrier. Buffer visibility
and workgroup barriers separate the coarse levels, and the elected group resets the counter for the next frame.
Host barriers make image writes visible to texture sampling, buffer writes visible to pixel transfers,
and the reset visible to the next dispatch. Seven explicit image views suffice for all supported extents.
Coarse levels use a small persistent SSBO because OpenGL's compute image limit can be smaller than the
hierarchy's mip count. GPU pixel-unpack transfers copy only this coarse tail into the same R32F texture;
there is no mapping, CPU readback or extra compute dispatch. Context image, shared-memory, workgroup,
texture and storage-block limits are validated before generation. The former raster copy/reduction
programs now exist only in the fixture catalogue for generic graphics tests and matched measurements.

Matched synthetic measurements on 2026-10-09 used an NVIDIA GeForce RTX 4090 on Windows,
identical patterned R32F inputs, the former raster implementation retained only as a test fixture,
and the production compute owner. Every mip matched exactly before measurement. Eight warm elapsed-query
samples alternated raster/compute ordering; the table records the final 17-check batch. CPU timings include each path's required submission and
state-preservation work, excluding allocation and readback. These are headless synthetic costs, not live frame timings.

| Source extent | Raster GPU median | Compute GPU median | Raster CPU submission median | Compute CPU submission median | Shared image bytes | Compute scratch bytes |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 1280×720 | 0.0932 ms | 0.0353 ms | 3.0807 ms | 0.1794 ms | 4,915,052 | 256 |
| 1920×1080 | 0.0963 ms | 0.0538 ms | 2.4539 ms | 0.1254 ms | 11,058,620 | 624 |
| 3840×2160 | 0.1505 ms | 0.1828 ms | 1.4261 ms | 0.1053 ms | 44,236,220 | 2,544 |

The raster chain required 11/11/12 draws and persistent framebuffer targets at those extents.
The compute path requires one dispatch, zero framebuffer targets and 4/4/5 coarse-tail GPU transfers.
Image payload stays identical; only the listed compact scratch storage is added. Compute reduced GPU
cost at 720p and 1080p and CPU submission cost at all three extents. At 4K its GPU cost increased
by 0.0323 ms (about 21%); this change does not establish a universal GPU speedup. The shader uses exact
two-texel arithmetic on even axes and computes tile ownership boundaries once per level, retaining
proportional reduction on odd axes. Receipts: `artifacts/hzb-compute-optimized-validation.log`
and `DepthHierarchyMeasurementTests`. Earlier unoptimized measurements are historical. A preceding optimized batch had comparable GPU medians
but different CPU durations; these individual runs do not establish a fixed submission-time speedup.

Single-dispatch qualification passed **104 distinct focused checks**: 17 reduction/binding/measurement
cases, 66 AO/LumOn/graphics integration cases and 21 publication/lifecycle/preparation cases, without
failures or skips in the final batches. They cover independent interval-overlap reduction, odd tile
seams, one-pixel axes, repeated frames and counter reuse, borrowed image/SSBO restoration, corrected
receiver publication, AO-only/current-frame/view identity, resize/reload/world teardown and enabled
consumer transitions. Exhaustive axis backtracking through 32,768 pixels checked 8,372,799 tile cases:
the largest mip-one span was 63 texels, and every unique-write interval fit inside its computed halo.
The 4,096/1,024 shared-array capacities therefore cover these extents. Production and fixture builds
published 433 and 507 variants respectively. Receipts: `artifacts/hzb-compute-optimized-validation.log`,
`artifacts/hzb-compute-integration.log` and `artifacts/hzb-compute-lifecycle.log`.
The corrected-receiver fixture explicitly initializes its attachment disposal lifetime, so it also
passes after earlier tests retire the global resource manager. Live visual acceptance remains part
of the parent lighting work.

Shared-hierarchy refinement validation passed **159 focused checks**, without failures or skips.
They cover all conservative mips against an independent interval-overlap reducer, odd dimensions,
one-texel axes/tails, source-mip exclusion and restored state, analytic integrals against numerical
quadrature, AO-only publication, native enable/quality, once-per-frame reuse, frame/view identity,
attachment replacement, resize/reload/teardown, contact/transmission/motion and LumOn tracing,
near-field and temporal consumers. The incremental build reused all **436 production** and
**506 fixture** shader variants. Receipt: `artifacts/horizon-validation/focused-final.log`.
An additional **75 unique checks** passed: six HDR runtime cases across native AO quality and
both lighting modes, 67 installed-surface declaration/interface variants, and two corrected particle
receiver publication cases. The latter change primary depth after receiver separation and verify
that every generated hierarchy mip still contains the corrected material depth. Runtime fixtures
now publish shared camera/light snapshots through the production owners. Installed-surface
declarations anchor at the unconditional version header so imported extension/include guards
cannot hide route variables or place them after patched helpers. Receipts:
`artifacts/horizon-validation/focused-runtime-2.log` (73 passing cases; two fixture failures
superseded by the next receipt) and `artifacts/horizon-validation/focused-particle-final.log`
(two passing cases). Together these establish **234 unique passing checks**.

On the validation RTX 4090, five warmed GPU timer samples of the complete hierarchy plus three
AO draws at 1280×720 measured quality 1 median **0.7731 ms** (0.6011–1.4664 ms), quality 2
median **2.0900 ms** (1.4807–2.2436 ms). Logical AO payload was **11,059,200 bytes**; the shared
hierarchy added **4,915,052 bytes**. These exclude allocation/readback and are synthetic workload
measurements, not live frame cost, physical bandwidth or visual acceptance. They are not a matched
performance comparison with the earlier implementation. The parent AO task retains user-run
visual and matched-scene performance acceptance.

Before shared-hierarchy refinement, focused validation passed **35 unique checks**, with no skips: flat/tilted planes, a perpendicular
corner, contact detail, cutout gaps, material transmission, first-person exclusion, far fade,
current-frame motion, odd sizes, reuse/resize/disposal, native quality/disable, corrected particle
receiver depth, final display and all three deferred lighting modes. Composite checks preserve
direct light and emission and exercise the existing roughness-dependent indirect specular AO
response. The shader-enabled build compiled 438 production and 507 fixture variants with zero
errors (114 existing warnings). Receipts: `artifacts/SceneHdrRuntime/ambient-occlusion-build4.log`,
`ambient-occlusion-final32.trx`, `ambient-occlusion-composite-passed.trx` and
`ambient-occlusion-managed-final.log` in the same directory.

Before shared-hierarchy refinement, on the validation RTX 4090, five warmed GPU timer samples of the complete six-draw synthetic
1280x720 contact scene measured quality 1 median **0.1188 ms** (0.1178–0.1229 ms), quality 2
median **0.2161 ms** (0.2068–0.2335 ms). Logical owned storage was **13,478,400 bytes**.
These exclude allocation/readback and are not live frame cost, physical bandwidth or visual
acceptance. User-run motion/foliage/contact appearance and matched-scene performance remain open.

Follow-up visibility diagnostics passed 16 AO geometry/lifecycle checks, including six 1280x720
contact/corner cases at view depths 3, 10 and 20. Minimum contact visibility was 0.542/0.643/0.740;
perpendicular-corner visibility was 0.564/0.778/0.981. These synthetic fixtures establish a
non-neutral signal, not that the effect reaches a particular live frame. The distant grazing
corner is weak before filtering. Receipt: `artifacts/SceneHdrRuntime/ambient-occlusion-realistic-final.trx`.
The reported in-game absence remains unconfirmed pending a frame capture.

## Bloom

Bloom reads only the completed scene-linear HDR primary color, after liquids, transparency,
clouds and late held items. Metering runs first; generated glare never feeds camera exposure.
Native graphics settings own bloom enable. Disabled bloom publishes the persistent black image
and retires the pyramid; there is no second VGE enable switch or engine bloom shader dependency.

Extraction starts at ceil-half resolution. Four symmetric bilinear source samples are selected
individually before averaging, so a small bright source is not rejected solely because averaging
it with a dark neighbor puts it below the threshold. Rec.709 luminance is multiplied by exp2(EV)
only for selection. For threshold T and fractional knee K, the RGB selection weight is
smoothstep(T-TK, T+TK, exposed luminance); it is bounded from zero to one. K=0 selects a hard
threshold. T=0 explicitly bypasses selection. One shared RGB weight preserves source chromaticity;
strength scales extracted radiance once. Storage remains unexposed, with no display transform or
extra exposure multiplication. The final display shader applies exposure once to scene plus glare.

A four-tap reduction chain is completed before filtering. Each level receives horizontal and
vertical dense Gaussian passes, with coefficients proportional to exp(-16.7 tap²/radius²) at
every integer texel from -radius through +radius, normalized to sum to one. Sigma is radius/sqrt(33.4), so the
support ends roughly 5.78 standard deviations from the center. Its unnormalized boundary
weight is exp(-16.7), about 0.00000559% of the center, avoiding a visible truncated shoulder. CPU-precomputed adjacent coefficient pairs become weighted bilinear
samples, requiring only five to nine fetches per axis for the configured radii 3–8. Source
and destination dimensions match for each Gaussian pass, so those pairs exactly cover the
discrete kernel. Spreading a fixed tap set over larger gaps is deliberately avoided: it
produced alternating holes in bright-source profiles at radii 6–8. Each axis uses its own
source dimension, including odd and non-square images. Texture
sampling is linear and clamp-to-edge. Every selected scale has an explicit support radius,
neutral RGB tint and energy weight:

| Level (half resolution is 0) | Support radius (level texels) | Tint | Relative energy |
| --- | --- | --- | --- |
| 0 | 3 | (1,1,1) | .25 |
| 1 | 4 | (1,1,1) | .22 |
| 2 | 5 | (1,1,1) | .19 |
| 3 | 6 | (1,1,1) | .15 |
| 4 | 7 | (1,1,1) | .11 |
| 5 | 8 | (1,1,1) | .08 |

Weights are normalized over the actual allocated levels, including tiny images whose chain
ends at 1×1. Descending reconstruction adds each filtered scale exactly once with its normalized
tint/energy weight. Bilinear upsampling only resamples the already filtered coarse sum; there is
no extra tent kernel or recursive 50/50 mixing. Constant-field gain therefore equals extraction
strength independently of scale count. The radii and relative weights are an authored profile,
not a reproduction of another engine's defaults or a diffraction/FFT lens simulation.

Two RGBA16F images per level suffice: horizontal scratch becomes the reconstruction destination
after all Gaussian filtering completes. Input and output texture objects are distinct in every
draw; no texture-array feedback, copies, texture views or barriers are needed. Storage is rebuilt
only on size/level changes, and the shared postprocess owner withdraws publication on resize,
shader reload and world teardown. Three to six scales are configurable; five is the default.

For L allocated levels and A total level texels, cost is 4L fullscreen draws, 4A fragment outputs
and 16A bytes of logical color storage. Sampling work is four taps per reduction, five to nine
fetches per filter axis (covering seven to seventeen texels), and one/two per reconstruction (plus one exposure fetch per extraction fragment in automatic
mode). These count shader samples, not physical memory traffic or measured bandwidth. At 1920×1080,
the default five levels occupy 10.54 MiB and issue 20 draws; six levels occupy 10.55 MiB and issue
24 draws. GPU timing and visual validation are recorded separately from these analytical counts.

The solar disk contributes through its actual attenuated, coverage-blended scene radiance.
HDR solar outGlow.r stays zero; no display-derived marker or extra solar/Mie multiplier enters
bloom. Geometry, water, cloud and transparency occlusion already affect the sampled scene. Material
emission enters that scene once. UI and the later overlay stages are excluded. Bloom is added
after ambient visibility has been applied in lighting and before the single final exposure, tone-map, transfer and dither boundary.
The same HDR publication can feed a future HDR-monitor output transform without changing bloom.
Bloom and final light-shaft images remain separate: native shaft quality can select quarter
resolution or disable shafts entirely, whereas bloom reconstructs at half resolution. Sharing
those allocations would add conditional layer ownership or copies for only one saved final
sampler; the final interface already fits the baseline sampler budget.

The dense-kernel change passed 65 distinct focused checks with no remaining failures or skips.
After matching the steeper coefficient profile, 39/39 bloom/solar/graph regressions pass,
including explicit boundary-weight checks at radii 3–8 and unchanged energy/centroid/no-gap
checks. Receipt: `artifacts/SceneHdrRuntime/gaussian-bloom-kernel-weights-focused.trx`. Coverage includes independent
dense Gaussian impulse/tap expectations, luminance and exposure selection, threshold bypass/continuity,
colored HDR preservation, actual 3–6 scale graphs, odd/1×1 dimensions, resize/retirement, solar
coverage and occlusion with zero red bloom marker, native settings, scene/UI ordering, both LumOn
modes and owned final composition. Production and test catalogs compile 434 and 501 shader
variants respectively. The earlier packaged-binary smoke passed 15/15. Subsequent moving-highlight
coverage reproduced gaps in 11 of 24 radius/phase cases with the stretched kernel; all 24 now
pass against a dense reference with tight mass, centroid and no-trough checks. Paired-sample
coefficient tolerances account for the validation device's measured 1/256 bilinear interpolation
precision. Receipts under `artifacts/SceneHdrRuntime`: `gaussian-bloom-trough-repro.trx`,
`gaussian-bloom-dense-focused.trx` and `gaussian-bloom-dense-precision.trx`; the last receipt
supersedes precision-only failures in the focused run.

Before the steeper Gaussian falloff adjustment, a warmed complete-graph measurement at 1280×720, five scales and 20 draws, with a constant HDR
source reports 0.1321 ms minimum, 1.2186 ms median and 3.1580 ms maximum over five samples
after two warmups. This is a synthetic headless measurement, not a before/after benchmark, live
frame cost or physical bandwidth measurement. Independent source review found no confirmed
resource-feedback, lifetime, normalization or exposure defect.

User-run halo appearance, temporal stability and matched-scene performance acceptance remain
pending. Numerical tests and analytical sample counts do not establish those live results.

## Light shafts

The independently authored light-shaft implementation separates distance occlusion from HDR
shaft bloom. Early extraction reconstructs positive view depth from the current projection and
opaque depth, then smooths visibility over a 128-metre interval. Two or three normalized radial
passes increase their reach toward the projected atmospheric sun. Publication converts visibility
to an occlusion amount: zero is neutral. A full-resolution output gives shared aerial consumers
an unambiguous screen-coordinate mapping. Only aerial in-scattering is multiplied by visibility;
extinction, direct/indirect surface radiance, emission, ordinary bloom and underwater transport
are not darkened by this mask. The physical sky itself is not multiplied by a foreground mask.

After complete scene composition and camera metering, a separate extraction reads HDR scene RGB.
Rec.709 luminance determines the exposure-relative excess above the bloom threshold. One RGB
scale bounds exposed peak radiance before filtering, including the shaft strength. Storage stays
unexposed. The atmospheric sun supplies projection, daylight gating and normalized tint, without
multiplying scene radiance by solar irradiance again. Solar visibility from glow green, sky depth,
an aspect-correct source aperture and a smooth image-edge fade restrict the source. Four balanced
samples cover the reduced pixel footprint. The result is filtered with normalized radial passes
and published separately for additive HDR final composition. There is no unblurred solar source
added by this effect; ordinary bloom and directional shaft bloom are distinct bounded glare lobes,
not replacement solar lighting or compensation for missing bloom. Their configurable strengths
are artistic contributions, not an energy-conserving lens simulation.

The fully featured composite uses 13 active fragment samplers after surface-array consolidation
and ambient-visibility integration, within the OpenGL 3.3 minimum of 16 fragment samplers.
GLSL source remains version 330. Unsupported linking is an explicit readiness failure,
not a legacy rendering fallback.

Native quality 1 uses quarter-resolution filtering, two passes and at most 16 taps per pass;
quality 2 uses half resolution, three passes and at most 32 taps; quality 3 uses half resolution,
three passes and at most 64 taps. The configured sample limit can further bound those counts.
Filter reaches are 0.12/0.36 or 0.04/0.12/0.36 of the vector toward the sun. UV radial interpolation
is aspect-independent; only circular source support requires aspect correction. Outside-screen
taps contribute zero bloom and clear visibility, preventing clamp streaks or artificial border
occluders. Behind-camera, below-horizon and underwater light publish neutral occlusion/zero bloom;
a 15-percent screen-edge margin fades the projected source continuously.

Occlusion uses opaque depth, so translucent geometry and clouds without depth writes do not
cast occlusion into aerial scattering. Shaft bloom uses the composed scene and visibility mask,
which can carry their attenuation. Offscreen occluders and volumetric shadowing remain outside
this screen-space approximation. Refraction samples the screen mask at its receiving pixel; it
is not a bent-ray volumetric shadow solution. No temporal history is allocated: the current pass
has no validated motion/reprojection contract. This explicit spatial-only path avoids stale
history on camera cuts, teleportation, resizing, reload and world transitions.

## Resources, settings and limits

Bloom owns half-resolution and progressively reduced RGBA16F images, plus separate upsample
images to prevent feedback. Light shafts own two reduced-resolution ping-pong images and one bloom output for the late path;
the early path owns two reduced-resolution ping-pong images and one full-resolution occlusion output. Ambient visibility owns the bounded hierarchy and spatial targets described above.
Luma owns full-resolution RGBA16F storage. Sized formats match the vec4 shader outputs,
removing dependence on unsized engine RGB AO storage. Sampler contracts receive typed textures; external
upstream engine handles are wrapped by BorrowedTexture without allocating or owning native storage.
Named input and output references are retained for each framebuffer publication; render
submissions do not look up texture handles. GpuResourceCollection retires private framebuffers,
attachments and borrowed wrappers while leaving engine storage alive.
The wrapper captures the existing target, dimensions, format and contiguous allocated mip
levels; shader contracts enforce the required sampler target. Screen publication, shader reload and world
exit retire PSOs before images/views. Stable frames allocate no GPU resources. Quality and
size changes recreate only affected storage.

Engine glare allocations remain because the non-scene menu path still consumes them. They
are no longer promoted to HDR or validated as scene requirements. Only primary scene color remains required floating-point engine storage; native luma and SSAO
allocations are neither promoted nor consumed by VGE scene postprocessing. Removing menu allocations requires separately
replacing that display-referred path, not deleting images beneath a remaining consumer.

The base game graphics settings exclusively control bloom, light shafts, SSAO quality and FXAA.
EnginePostprocessInputs reads bloom/fxaa booleans and godRays/ssaoQuality integers through
the client settings API, preserving the engine DoPostProcessingEffects gate.
VGE exposes no duplicate effect switches. The HDR Postprocessing group retains algorithm-specific
bloom strength/threshold/knee/levels and shaft strength/radiance limit/sample count.
Camera exposure controls are described in PBR.CameraExposure.md.

For W by H scene pixels, light-shaft RGBA16F storage costs approximately 8WH + 40WH/d² bytes,
where d is the native quality divisor (4 or 2), before dimension rounding. A bloom
pyramid with scale/scratch storage approaches 16WH/3 bytes (RGBA16F), with exact size
depending on level count and rounded dimensions. Bloom uses four fetches per reduction,
five to nine per Gaussian axis and one/two per additive reconstruction pixel. Each shaft radial pass uses one packed fetch per selected tap;
early extraction uses depth, and late extraction uses four scene/depth/visibility/exposure samples.
These are logical storage/work counts, not measured driver allocation or physical bandwidth.

The independent final shader measured median GPU times of 0.0164 ms at 720p, 0.0338 ms at
1080p and 0.0573 ms at 1440p across five warm samples with uniform 1x1 inputs and edge
smoothing enabled. These synthetic timings do not represent a live frame or physical bandwidth.
Receipt: artifacts/SceneHdrRuntime/owned-final-independent.trx.

The independent final implementation builds successfully (zero errors; 102 existing warnings).
Focused validation passed 67 unique checks. The six runtime cases cover both LumOn modes and
native SSAO qualities 0/1/2 without engine final shaders or legacy intermediate outputs.
Receipts: artifacts/SceneHdrRuntime/owned-final-independent.trx and owned-final-runtime.trx;
build: owned-final-independent-build4.log. These checks cover implemented behavior and ownership,
not the subsequent horizon AO implementation. Real-game halo appearance, shafts through
clouds/transparency, underwater transitions, temporal stability and representative GPU cost
still require user-run visual/performance acceptance. The implementation does not establish
native HDR monitor presentation.

Light-shaft algorithm and atmospheric integration validation passed 131 unique focused checks
across light-shafts.trx, light-shafts-corrected.trx and light-shafts-abstractions.trx
under artifacts/SceneHdrRuntime. The final abstraction-cleanup run passed 20/20 checks, including
all six lifecycle cases, with no failures or skips. Its build completed with zero errors and
102 existing warnings (light-shafts-abstractions-build.log). Aerial tests use typed shader inputs;
aerial, shaft and bloom submissions use PostprocessDraw and GraphicsCommandContext, without raw
uniform writes, raster mutation, draw calls or blanket cache invalidation.

The final PSO-path synthetic measurement for four late shaft passes at 320x180 and 16 radial taps,
using uniform 1x1 source textures, was 0.0297/0.0307/0.0307 ms minimum/median/maximum. Its three
RGBA16F targets contain 1,382,400 bytes. This measures neither the early full-resolution occlusion
path nor live-game frame cost or physical bandwidth. Receipt: light-shafts-abstractions.trx.
Live visual and representative performance acceptance remains deferred until the remaining
postprocessing work is finished, per user direction.

## Texture-array consolidation analysis

Source review: 2026-10-08. This is an allocation and draw-dependency analysis of production
VGE texture owners, not a migration or a new GPU performance measurement. The inventory follows
texture factories, specialized GpuTexture subclasses, framebuffer-owned images and existing
array allocations. Test-only allocations and external engine storage are excluded from owned totals.

**Recommendation:** first consolidate DirectLightingTargets into a three-layer RGBA16F array.
Its producer already writes all three outputs together and its consumer reads them together.
This changes the fully featured PBR composite from 17 sampler inputs to 15. Next consider the
G-buffer's three RGBA16F channels and the two RGBA32F water-transport channels, taking that
same composite to 13 and then 12. These are independent owner-local changes, not a global
screen-texture allocation. The numbers are expected interface reductions, not new linked-program
measurements; rebuild/link and numerical comparison remain implementation acceptance work.

### Inventory conventions

W/H are primary scene dimensions; P/Q are resolved screen-probe grid dimensions; A/B are
material atlas-page dimensions. All listed images are single-sample and have one allocated mip
unless explicitly stated. N means nearest filtering, L means linear; ordinary allocations clamp
at image edges. A typed sampler policy can override texture-object filtering. Sharing an array
saves one sampler only when a consumer can use one compatible sampler policy for the layers;
binding the same array twice with different sampler policies still costs two units.

Screen allocations follow their existing owner's resize/rebuild/reload/world-exit lifetime.
Persistent world/material allocations follow their generation/page/cache lifetimes instead.
Matching dimensions alone does not justify joining those lifetimes. No format conversion,
precision reduction, padding or mandatory allocation of disabled features is assumed below.

### Scene, atmosphere, liquid and postprocessing inventory

| Owner and source | Images and layout | Producer → consumers; lifetime and decision |
| --- | --- | --- |
| [DirectLightingTargets](../VanillaGraphicsExpanded/PBR/DirectLightingTargets.cs) | DirectDiffuse, DirectSpecular, Emissive: W×H RGBA16F, L | Direct-lighting MRT → ordinary and pre-overlay PBR composite. Shared lifetime in standalone and LumOn modes. **First recommended group: 3 layers.** Three attachments/outputs remain necessary. |
| [GBufferTextures](../VanillaGraphicsExpanded/GBuffer/GBufferTextures.cs) and [GBufferManager](../VanillaGraphicsExpanded/GBuffer/GBufferManager.cs) | Normal, Material, Environment: W×H RGBA16F, N; PatchId: W×H RGBA32UI, N | Terrain/receiver MRT → direct lighting, composite and LumOn/debug consumers. **Group the three float channels**; retain integer patch identity separately. Preserve primary attachment slots 4, 5, 7 and integer slot 6. Current injection uses Texture2D attachments and must become layer-aware. |
| [Receiver position](../VanillaGraphicsExpanded/GBuffer/GBufferManager.ReceiverPosition.cs) | Optional fallback position: W×H RGBA16F, N | Replaces unavailable engine position storage for receivers. It can physically match the float G-buffer array but is conditional; do not reserve an unconditional fourth layer or change engine attachment ownership. Revisit only after the three-channel migration. |
| [PBRCompositeRenderer](../VanillaGraphicsExpanded/PBR/PBRCompositeRenderer.cs) | compositeColorTex: W×H RGBA16F | Composite output → scene publication. Keep separate from direct/G-buffer/water inputs because the composite samples them while writing this image. No sampler reduction from merging an output with its inputs. |
| [SceneColorParticleTargets](../VanillaGraphicsExpanded/PBR/SceneColor/SceneColorParticleTargets.cs) | Radiance, VisibleRadiance: W×H RGBA16F; ReceiverDepth: W×H R32F; BeforeDepth/AfterDepth: W×H matching primary DepthComponent32 or DepthComponent32F; optional SsaoNormal/SsaoPosition: W×H RGBA16F. N | Particle capture → visibility resolve → later scene lighting. Radiance is read to produce VisibleRadiance: keep separate. Optional normal/position are co-produced and can share a two-layer array, but SSAO handoff/blits need layer-aware destinations and no proven high-pressure consumer benefit. Depth snapshots could share a depth array (two depth-only FBOs, not color MRT); modest resolve binding benefit only, with matching native format and snapshot lifetime. Keep depth-as-color ReceiverDepth separate. |
| [WaterVolumeRenderer](../VanillaGraphicsExpanded/PBR/Liquids/WaterVolumeRenderer.cs) | Optical depth and illumination/source: W×H RGBA32F, N | Signed additive boundary integration MRT → composite. **Recommended two-layer array**, retaining independent outputs, blend state and clears. Not RGBA16F: signed accumulation precision is intentional. |
| [WaterRefractionScene](../VanillaGraphicsExpanded/PBR/Liquids/WaterRefractionScene.cs) | SourceColor: W×H RGBA16F; SourceDepth: W×H R32F; optional reducedColor: ceil(W/2)×ceil(H/2) RGBA16F; reducedDepth: same reduced size RGBA32F. Sampling policies remain per consumer | Pre-overlay capture and final opaque publication have distinct instances; reduction → liquid refraction. Color/depth formats mismatch. Do not merge pre-overlay and final publications: final composite reads the former while writing the latter. compositeColor and final SourceColor are co-produced compatible outputs, but have different publication consumers/lifetimes and give no reduction to the 17-input composite. Keep separate. |
| [AtmosphereTextureSet](../VanillaGraphicsExpanded/PBR/Atmosphere/AtmosphereTextureSet.cs) | Sky: a×2b RGBA16F; aerial Radiance: a×2b×d RGBA16F 3D; Attenuation: a×b×d RGBA16F 3D. L, repeat azimuth/clamp other axes; disabled aerial uses reduced neutral dimensions | Atmosphere publication uploads → sky, direct/forward lighting, composite, liquids and LumOn. **Exclude from a mechanical 2D-array conversion**: 3D distance interpolation and packed angular lobes are part of lookup semantics, and the dimensions differ. Combining them needs a separate LUT-layout/interpolation redesign, not just new bindings. |
| [CameraExposureTargets](../VanillaGraphicsExpanded/PBR/CameraExposure/CameraExposureTargets.cs) | Histogram: 64×1 RG32F; two EV histories: 1×1 R32F, N | Meter → adapt(previous EV) → current EV/final and glare thresholds. Keep histogram and histories separate. Histories match physically but only one is sampled per adaptation draw, so one array adds feedback handling without sampler savings. Auto-exposure owns reset/cut/enable lifetime. |
| [BloomRenderer](../VanillaGraphicsExpanded/PBR/Postprocessing/BloomRenderer.cs) | Two RGBA16F images per Gaussian scale; start ceil half-size, clamp each dimension to 1; 3–6 levels | Extract/downsample → horizontal/vertical Gaussian → weighted additive reconstruction → final. Different scales cannot be different layers without padding. Same-size scale/scratch images alternate read/write roles and remain separate objects to avoid feedback. |
| [LightShaftRenderer](../VanillaGraphicsExpanded/PBR/Postprocessing/LightShaftRenderer.cs) | Two RGBA16F reduced ping-pong images per early/late instance; early output W×H RGBA16F; late output ceil(W/d)×ceil(H/d) RGBA16F, d=4 or 2 | Depth/radiance extraction → radial passes → early aerial occlusion or late final glare. Keep each ping-pong pair separate by default: each pass samples one while writing the other, and currently uses only one working-image sampler. Grouping them saves allocations, not sampler inputs, and adds feedback requirements. Early/late scratch reuse is a separate lifetime optimization, not array consolidation. |
| [RetainedPostprocessRenderer](../VanillaGraphicsExpanded/PBR/Postprocessing/RetainedPostprocessRenderer.cs), [PostprocessPipeline](../VanillaGraphicsExpanded/PBR/Postprocessing/PostprocessPipeline.cs) | Full-size RGBA16F luma/scene; persistent black glare 1×1 RGBA16F | Completed scene → luma → final. Black is a disabled-effect identity. Horizon AO has a separate pre-lighting owner for filter/visibility allocations and borrows the shared depth hierarchy, as documented above. |

The primary color/depth/glow, native SSAO attachments, terrain color atlases, shadow maps,
OIT engine images and native menu postprocess images remain **external storage**, even where
VGE promotes a format or borrows an attachment. They are not candidates for owner-local array
migration. BorrowedTexture and framebuffer discovery describe existing storage; they do not
make it VGE-owned. See [HDR/display contract](PBR.SharedDisplay.md),
[water refraction](PBR.WaterRefraction.md), [water transport](PBR.WaterMedium.md),
[atmosphere publication](PBR.Atmosphere.Publication.md) and
[aerial lookup](PBR.Atmosphere.AerialPerspective.md).

### LumOn and material inventory

| Owner and source | Images and layout | Producer → consumers; decision |
| --- | --- | --- |
| [LumOnTargets](../VanillaGraphicsExpanded/LumOn/LumOnTargets.cs): anchors | Position/normal: P×Q RGBA16F, N | Anchor MRT → tracing, importance mask, temporal/filter/gather/debug. **Two-layer candidate, 2→1 inputs where both are read.** Keep separate from SH coefficients: SH projection reads anchor position while writing coefficients. |
| LumOnTargets: trace scheduling | ProbeTraceMask: P×Q RG32F, N; ProbePisEnergy: P×Q R32F, N | Importance selection MRT → trace/diagnostics. Different formats; retain. |
| LumOnTargets: directional atlases | Trace/current/history/filtered radiance: 8P×8Q RGBA16F, N; corresponding four metadata images: same size RG32F, N | Trace → temporal with history → spatial filter → gather/projection; current/history swap roles. Same-format groups exist, but temporal/filter draws read and write members of the proposed group. **Defer**, retain ping-pong allocations. Radiance and metadata cannot share one array without changing representation. |
| LumOnTargets: SH9 | Seven coefficient images: P×Q RGBA16F, N | Project filtered atlas through seven MRT outputs → SH9 gather. **Strong seven-layer candidate: 7→1 coefficient samplers, saving six in gather.** Still seven output attachments. Applies to SH9 gather mode; no benefit to the directional-atlas gather branch. Existing owner allocates this family with the screen target set. |
| LumOnTargets: outputs | IndirectHalf: resolved half dimensions RGBA16F; IndirectFull, SurfaceAlbedo: W×H RGBA16F (albedo L, indirect default N); Velocity: W×H RGBA32F N | Gather → upsample → composite; surface capture feeds GI; velocity/depth feed temporal/tracing. Keep separate across dependencies/resolutions. Do not merge IndirectFull into always-present direct lighting: standalone PBR must not allocate or require LumOn resources. The shared Rendering depth-hierarchy owner supplies HZB; these targets do not allocate it. |
| [LumOnPmjJitterTexture](../VanillaGraphicsExpanded/LumOn/LumOnPmjJitterTexture.cs) | CycleLength×1 RG16 normalized, N | CPU sequence upload → tracing; cycle/seed lifetime. Unique format/size; retain. |
| [World-probe resources](../VanillaGraphicsExpanded/LumOn/WorldProbes/Gpu/LumOnWorldProbeClipmapGpuResources.cs) | For resolution r, levels l, tile t: radiance r²t×rlt RGBA16F; visibility r²×rl RGBA16F; distance same scalar size RG16F; metadata RG32F; debug RGBA16 normalized. N | Scheduled probe updates/clear → tracing/gather/debug; generation lifetime. Radiance size differs and scalar formats differ. No lossless compatible group in the normal layout; do not pad/promote formats merely to consolidate. |
| [SurfaceAtlasTextures](../VanillaGraphicsExpanded/LumOn/Scene/SurfaceAtlasTextures.cs) | Existing arrays of physical width×height×layers: Depth R16F, Material RGBA8, Indirect/Direct/two Outgoing RGBA16F; N | Capture/relight compute → surface-hit lighting; outgoing publication double-buffered. Four radiance arrays match storage, but updates read published lighting while writing pending/direct/indirect data. **Defer regrouping**; image load/store access and barriers must be audited per dispatch, independently of framebuffer feedback rules. Any layer-bank layout multiplies layer count and must respect MaxArrayTextureLayers and physical-pool budgeting. |
| [Page tables](../VanillaGraphicsExpanded/LumOn/Scene/LumonScenePageTableGpuResources.cs) and [feedback owner](../VanillaGraphicsExpanded/LumOn/Scene/LumonSceneFeedbackUpdateRenderer.cs) | Page table and usage stamp: existing virtual-page-width×height×chunk-slots R32UI arrays, N; generation: chunk-slots×1 R32UI N | CPU residency uploads and compute stamp/compact → trace/feedback. Table/stamp could be separate layer banks; compaction reads both, saving one sampler there. Different clear/update/publication contracts and doubled layer count make this lower priority. Generation is a different shape. |
| [TraceGeometryGpuScene](../VanillaGraphicsExpanded/LumOn/Scene/Geometry/TraceGeometryGpuScene.cs) | Geometry/Legacy: resolution³ R32UI; Light: resolution³ RGBA8; Readiness: slots³ R8UI; Faces: 16384×1 RGBA32UI; Materials: 256×768 RGBA8; Surfaces: 256×256 RGBA32UI; LightColors: 64×1 RGBA16F; BlockLevels/SunLevels: 33×1 R16F. N | CPU scene publication → voxel/surface tracing and relighting. Retain 3D grids/addressing and differently shaped tables. **BlockLevels/SunLevels can become two layers**, saving one sampler in surface-lighting consumers that read both, with only 132 logical bytes total. Low priority, no composite saving. |
| [MaterialAtlasTextureStore](../VanillaGraphicsExpanded/PBR/Materials/MaterialAtlasTextureStore.cs) | Per-page material params and optional normal/depth: A×B RGBA16F, N | Build/upload → terrain/liquid/material consumers. Compatible two-layer group when both enabled; saves one sampler in consumers reading both with matching policy. Page rebuild, optional normal/depth and independently replaced params must become coherent layer publication. Keep engine color atlas external. Grouping pages only helps consumers accessing multiple pages in one draw; current page selection alone does not save a sampler. |
| [Displacement maps](../VanillaGraphicsExpanded/PBR/Materials/MaterialAtlasTextureStore.Displacement.cs), [water-medium maps](../VanillaGraphicsExpanded/PBR/Materials/MaterialAtlasTextureStore.WaterMedium.cs), [neutral relief](../VanillaGraphicsExpanded/PBR/Materials/TerrainReliefBindings.cs) | Per-page indices: A×B R32F N; record tables: count-dependent RGBA32F N; two neutral 1×1 RGBA32F images | CPU table build → tessellation/liquid consumers. Index maps physically match but optional populations and consumer sets differ: defer without a shared consumer benefit. Record dimensions differ; do not pad. Neutral objects are negligible, have different semantic contents and should not drive architecture. |
| [Material tile scratch](../VanillaGraphicsExpanded/PBR/Materials/MaterialAtlasNormalDepthGpuBuilder.TileResources.cs), [multigrid](../VanillaGraphicsExpanded/PBR/Materials/MaterialAtlasNormalDepthGpuBuilder.Multigrid.cs) | Tile-sized L/Base/D0/G1–G4/D/Div/H/Hn/Tmp R32F N; G is RG32F N. Per multigrid level h/hTmp/b/residual R32F N, progressively smaller | Bake derivatives/divergence/solve → normal-depth atlas publication. Compatible sizes within each level, but iterative solver reads/writes scratch members. Offline/build lifetime, no frame-composite benefit; retain ping-pong and exclude from initial migration. |

GpuTexture allocation subclasses and direct GL texture-generation sites were checked as a second
coverage pass. GpuBufferView/GpuBufferTexture and GpuTextureView are storage views, not additional
2D image allocations; they do not belong in an image-array packing total. Renderbuffers are not
sampled textures. General-purpose factories are counted through their production owners above.
See [lighting modes](PBR.LightingModes.md), [screen-probe core](LumOn.01-Core-Architecture.md),
[SH/gather](LumOn.06-Gather-Upsample.md), [surface lighting](LumOn.SurfaceCache.LightingContract.md),
[trace geometry](LumOn.TraceSceneGeometryContract.md) and [material ownership](MaterialAtlas.Architecture.md).

### Draw dependencies and sampler savings

| Proposed group | Producer attachment layout → later readers | Expected savings and storage |
| --- | --- | --- |
| Direct lighting | Output locations 0/1/2 attach array layers 0/1/2; input G-buffer remains separate. Composite reads array while writing composite/refraction targets | Composite 17→15; 3 texture objects→1; 24WH bytes unchanged (47.46 MiB at 1920×1080). No extra copy or fetch reduction. |
| G-buffer float channels | Primary slots 4/5/7 attach layers 0/1/2; keep 6 as RGBA32UI. Direct-lighting and composite draws target separate outputs | Another two fewer samplers in composite (15→13) and two in direct lighting where all three are consumed. 24WH bytes unchanged. Raw Texture2D attachment/binding consumers need migration, not ID aliases. |
| Water transport | Output 0/1 attach RGBA32F layers 0/1; composite later reads both | Another one fewer composite sampler (13→12). 32WH bytes unchanged (63.28 MiB at 1080p). Conditional water lifetime remains conditional. |
| SH9 coefficients | Seven output locations attach seven layers; projection reads directional atlas and separate anchors | Six fewer coefficient samplers in SH gather; 56PQ bytes unchanged. Output/draw-buffer count stays seven. No benefit in standalone mode or non-SH gather. |
| Probe anchors | Two anchor outputs attach two layers; trace/PIS/gather later read both | One fewer sampler per consumer using both; 16PQ bytes unchanged. Cannot append SH outputs to this same storage without a feedback strategy. |
| Material params + normal/depth | Upload/build named layers; consumers read only after publication | One fewer sampler where both inputs are active; 16AB bytes unchanged when enabled together. Reserving the optional layer when disabled wastes 8AB bytes per page. |
| Final bloom + shaft outputs | Separate final effect writes into distinct layers, then final composition samples one glare array | Conditional 2→1 glare inputs, final interface 5→4; **not** a fix for the earlier 17-input composite. Current bloom and shaft ceil-half dimensions match at native shaft quality ≥2, including odd sizes. Quality 1 differs. Await Gaussian layout; do not resample solely to force grouping. |

Counts refer to replacing distinct sampler uniforms with one sampler2DArray, not an array of
sampler2D uniforms. Renumber all remaining units densely: removing sampler uniforms but leaving
a binding at unit 16 does not establish a usable 16-unit fragment interface. Different shader
variants may optimize optional samplers away; count active linked inputs and validate unit indices
for standalone/LumOn, pre-overlay and all quality variants after implementation.

Safe recommended graph: terrain writes G-buffer array → direct lighting reads it and writes
lighting array → composite reads both plus water array and writes separate color/refraction
images. The active draw framebuffer must not expose sampled storage in an unsafe feedback
configuration. An attachment retained by an inactive FBO is harmless. Preserve all clear values,
output slots, indexed blending, integer output types and existing pass ordering.

A shared allocation does not remove layers' logical texel storage, samples, shader fetches or
MRT output bandwidth. It reduces object/binding count at consumers. CPU/GPU timing benefits are
unmeasured. First recommended groups need no staging copies: allocate arrays at the owning
rebuild boundary and render into their layers directly. Copying old textures into an array every
frame would defeat much of the benefit. Atomic replacement can transiently retain old+new storage
until the existing disposal boundary, as with other resource rebuilds.

### Existing abstraction support and actual gaps

- [Texture3D](../VanillaGraphicsExpanded/Rendering/Texture3D.cs) and
  [DynamicTexture3D](../VanillaGraphicsExpanded/Rendering/DynamicTexture3D.cs) already accept
  TextureTarget.Texture2DArray. Reuse those GpuTexture owners; no new borrowed wrapper,
  handle lookup, texture manager or catch-all postprocess resource class is needed.
- [GpuFramebufferAttachment.FromTexture](../VanillaGraphicsExpanded/Rendering/GpuFramebufferAttachment.cs)
  accepts mip and layer and calls FramebufferTextureLayer. GpuFramebuffer.Create accepts these
  attachments. Build one borrowed attachment per layer, owned backing array once through
  GpuResourceCollection. Existing MRT helpers taking DynamicTexture2D[] are convenience overloads,
  not a reason to add another framebuffer system. Non-layered attachments allow normal vertex/
  fragment shaders to write multiple layers through MRT; no geometry shader is required.
- ShaderTextureTarget.Texture2DArray, typed GpuTexture properties, generated sampler binding,
  state-cache targets, uploads and array-image bindings already exist in LumOn. Change the
  consumer contract, GLSL sampler type and layer coordinate together. Use explicit sampler
  policy and preserve texelFetch/filtering behavior. Existing properties typed DynamicTexture2D
  cannot describe an array and need their owning API adjusted.
- PSOs/render passes already express output formats, attachment slots and draw routing. Rebuild
  their target associations using existing lifecycle notifications. Selected layer attachments
  explicitly reject independent Resize: resize/recreate the backing array once and republish
  every layer attachment/FBO together. Do not call the old per-texture framebuffer Resize path
  on a collection of layers. Retire submissions before attachments before storage.
- [GpuTexture](../VanillaGraphicsExpanded/Rendering/GpuTexture.cs) currently allocates with
  TexImage2D/TexImage3D. [GpuTextureView](../VanillaGraphicsExpanded/Rendering/GpuTextureView.cs)
  exists but requires immutable storage. It derives from GpuResource, not GpuTexture;
  ShaderPreparedSubmission's typed sampler overloads accept GpuTexture, so views are not a
  drop-in typed sampler input. A view-based migration would need immutable allocation/lifetime
  support and a deliberate typed view-binding contract. Neither is needed for the first groups.
- GpuSupport already owns maximum texture size, array layers, color attachments, draw buffers
  and sampler capabilities. Validate through that owner. No TextureBarrier call was found in
  current rendering sources; do not quietly assume feedback synchronization is implemented.

Array storage/layer attachment and sampler2DArray are available within the GLSL 330/OpenGL 3.3
feature set, independently of VGE's other runtime requirements. Array layers share dimensions,
format and mip layout; they do not filter into adjacent layers. See the
[Khronos array specification](https://registry.khronos.org/OpenGL/extensions/EXT/EXT_texture_array.txt).
Texture views require immutable storage and OpenGL 4.3/ARB_texture_view; see
[ARB_texture_view](https://registry.khronos.org/OpenGL/extensions/ARB/ARB_texture_view.txt).

For ping-pong candidates, default to separate allocations. A restricted input view excluding
output layers can isolate subresources, but needs the storage/binding work above. Alternatively,
OpenGL 4.5/ARB_texture_barrier permits disjoint texel reads/writes and defines TextureBarrier
between dependent draws in feedback arrangements. It is not safe to infer that different array
layers alone suffice under older rules, nor does a generic MemoryBarrier replace TextureBarrier
for framebuffer feedback. Ordinary rendering to one object followed by sampling it while drawing
to another needs no new explicit barrier. Compute image writes retain their own access/barrier
requirements. See [ARB_texture_barrier](https://registry.khronos.org/OpenGL/extensions/ARB/ARB_texture_barrier.txt).

### Recommended migration order and acceptance

1. DirectLightingTargets, both composite variants and diagnostic readers. Use three layers,
   preserve three MRT outputs, compact sampler units, and compare all output channels in both
   standalone and LumOn modes. This alone addresses the reported 17-sampler composite.
2. Water optical/source pair and G-buffer float channels as independent owner-local changes.
   Water has the smaller interface change; G-buffer has broader raw engine attachment/debug
   consumers. Their combined endpoint is 12 composite inputs; neither is required before the
   first change can ship. Keep engine inputs and publication ownership intact.
3. SH9 coefficients, then anchors as separate arrays. Verify SH and directional gather choices,
   LumOn disabled/startup/switching, unchanged probe grids and per-output values.
4. Material pair only after measuring relevant consumer pressure; preserve optional normal/depth,
   page replacements and bake uploads. Small light-level tables are an optional later cleanup.
5. Revisit final glare grouping during Gaussian bloom design and AO grouping during horizon-AO
   design. Preserve native enable/quality independence and odd-resolution rounding. Do not expand
   quarter-resolution shafts, tiny neutral textures or AO histories merely to match bloom storage.
   Keep temporal/filter working sets separate unless a measured benefit justifies views/barriers.

Implementation validation should use existing typed program/GPU abstractions to check layer
routing, exact channel preservation, no feedback, active sampler counts/indices, settings changes,
odd dimensions, resize, reload, disposal and publication. Do not replace distinct per-layer
semantics with shared clears. Benchmark matched scenes before claiming faster rendering;
allocation/binding savings above are analytical. The original inventory was source-only;
implementation and validation of the selected groups are recorded below.

### Implemented array ownership

The production owners now allocate the five selected groups directly as 2D texture arrays:

| Owner | Array layers | Framebuffer output slots |
| --- | --- | --- |
| DirectLightingTargets.Radiance | Diffuse, specular, emissive | 0, 1, 2 |
| GBufferTextures.Surface | Normal, material, environment | Primary 4, 5, 7 |
| WaterVolumeRenderer transport | Optical transport, scattering source | 0, 1 |
| LumOnTargets.ProbeAnchors | Position, normal | 0, 1 |
| LumOnTargets.ProbeSh9 | Seven packed coefficient vectors | 0 through 6 |

Patch identity retains its separate integer texture. Probe anchors and SH9 coefficients remain
separate allocations because projection reads anchors while writing coefficients. All grouped
images retain their dimensions, formats and sampling policy; there are no per-frame packing copies.
Owners retire and recreate whole arrays at their existing rebuild boundaries. Layer attachments
borrow those owners and cannot resize independently. Temporal/history textures remain separate.

Array consolidation reduced the composite contract to twelve densely numbered sampler units
(0 through 11). Ambient visibility subsequently adds unit 12, for thirteen current inputs.
SH9 gather uses one coefficient sampler and one anchor sampler. Debug views and tracing/gather
consumers use array samplers with explicit layer selection. Together these groups replace
seventeen texture objects with five; framebuffer output counts remain unchanged. These are interface/allocation-count reductions, not claims of reduced texel
storage, fetch count or measured GPU time. Both shader catalogs compile (434 production and
501 test variants), and the managed build passes. Focused GPU validation confirms twelve active composite samplers, numerical HDR/water
composition, both probe gather paths, grouped owner replacement/disposal, fixture isolation
and direct water-layer rasterization. Across the focused runs, 320 distinct cases pass and
three preexisting cases are skipped; no failures remain. At that array-consolidation receipt, linked composite units were exactly
0 through 11; the ambient-visibility tests cover the additional unit 12. The validation device exposes 32 fragment units, 2048 array layers, and eight
color attachments/draw buffers. SH9 gather binds its coefficient array at unit 0 and its
separate anchor array at unit 1.

The final production build reports zero warnings and zero errors; its 30-case smoke run passes.
Independent production review found no lifecycle, feedback or sampler-policy defect.

Receipts: `artifacts/SceneHdrRuntime/grouped-arrays-focused.trx`,
`grouped-arrays-production-smoke.trx`, and subsequent targeted
`grouped-arrays-*` build/test receipts. Intermediate receipts retain the fixture migration
failures; the passing aggregate uses the latest result for each case.

Matched-workload direct-lighting fixtures report zero numerical difference against their
reference and 0.339–0.384 ms repeated CPU submission at 31×19 and 32×24. These are synthetic
submission measurements, not a before/after array benchmark or evidence of a rendering speedup.
Water owner resize/retirement has source review coverage; shader producers and consumers are
GPU-tested, but the tests do not directly instantiate WaterVolumeRenderer. User-run visual
acceptance and live GPU performance remain deferred.
