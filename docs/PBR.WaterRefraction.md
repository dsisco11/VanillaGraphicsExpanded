# Water refraction

`Water Refraction` is a persisted ConfigLib graphics option at `WaterRefractionEnabled`. It defaults to false. The existing configuration loader supplies the missing leaf in older settings documents. Each opaque/liquid invocation reads the current setting; disabling withdraws publication, retires snapshot storage and skips ray traversal. LumOn changes do not introduce a separate resource dependency.

## Opaque publication and resource ownership

`PBRCompositeRenderer` remains at Opaque order 11, after direct/indirect lighting and water-boundary capture and before OIT. With refraction enabled, its existing composite draw writes three attachments: the ordinary transported composite, unattenuated scene-linear opaque radiance in RGBA16F, and matching hardware depth in R32F. The extra color/depth outputs come from the same invocation and projection. Radiance is captured before water absorption, in-scattering, atmospheric transport and display conversion. Its alpha marks physical opaque receivers; sky and first-person depth proxies are invalid.

`WaterRefractionScene` owns the two extra textures and the MRT framebuffer, borrowing the existing composite texture. It publishes only after successful composition and display resolve. The next opaque invocation invalidates publication before checking readiness. Resize, attachment replacement, leave-world and disposal retire both images together. Shader reload reuses the existing shader library and writes a new source on the next successful opaque invocation. Liquid rendering never samples its active OIT attachment. The order-11 MRT publication needs no extra copy or fullscreen draw; the pre-overlay capture below adds separate work. The optional MRT adds 12 bytes of storage and nominal output writes per pixel, approximately 23.7 MiB at 1920 × 1080. This is a format-derived lower bound, not a bandwidth or GPU timing measurement.

The installed engine submits local first-person hands and held items inside `EntityPlayerShapeRenderer.DoRender3DOpaque`, called by `SystemRenderEntities` at Opaque order 0.4. Its hand projection and visibility depth therefore precede the order-11 publication. Marking these pixels invalid cannot recover the world color or depth they already replaced. With refraction enabled, the void prefix `WaterRefractionCaptureHook` observes the local `RenderMode.FirstPerson` non-shadow opaque invocation before its projection change. `WaterRefractionCapture` evaluates the existing direct-lighting and composite algorithms into isolated targets, retaining unattenuated world radiance and matching world depth. The original engine method then executes in its original order, with its original arguments. There is no skipped, queued or replayed engine draw.

At the ordinary order-11 composite, only refraction outputs at negative first-person normal markers use that retained pair; other pixels retain final opaque scene coverage. Primary color, depth and first-person shading follow their existing paths. Capture reuses `GlStateCache.BindFramebufferScope` to restore independent read/draw framebuffer bindings. The existing legacy fixed-function scope optionally preserves viewport for fullscreen draws that change it. Each draw uses the existing `GpuProgram.UseScope` to restore shader ownership. The binding scope adopts actual driver bindings without invalidating unrelated cached state. `GpuFramebufferBlitter` owns reusable scratch FBOs borrowing color attachment zero, so original read/draw routing is never modified. The blitter is constructed with its source and destination and configures scratch attachments once. It subscribes to `GpuFramebuffer.AttachmentsChanged`, which fires after attachment updates, completed resize and retirement; notifications mark its setup dirty for the next copy. Ordinary copies perform no attachment queries. The caller retains and disposes the blitter with its target allocation; disposal unsubscribes both targets. `GpuFramebuffer` owns no blit-operation resources; scratch FBOs do not own borrowed textures or renderbuffers. LumOn surface-albedo capture owns its blitter through `LumOnBufferManager`. `GBufferManager.PrimaryFramebuffer` is a persistent non-owning representation of the augmented engine primary FBO; G-buffer setup refreshes it and notifies dependents after the existing window-rebuild callback, including equal-size rebuilds. Depth/stencil blits bind the original targets without changing color routing. Default-framebuffer blits retain the existing default-buffer selection. Blits never change or capture viewport. Fixed-function state uses the existing scope and pipeline descriptions. The direct-lighting and composite renderers select owned or borrowed framebuffer targets through framebuffer APIs rather than issuing raw GL calls. Frame start invalidates previous publication. Resize disposes the final refraction pair, invalidates the borrowed pre-overlay publication, and resizes the composite scratch in place; the next capture rebuilds mismatched isolated storage. Disable and world/disposal boundaries reclaim isolated storage. Composite allocation replacement, failure, world leave and disposal share one owned-resource cleanup path. Immersive bodies, remote players and shadows retain their original behavior and do not trigger this capture.

The early capture adds one direct-lighting draw and one composite draw, three RGBA16F lighting targets and an RGBA16F/R32F radiance/depth pair (36 additional bytes per pixel). It no longer writes or borrows the ordinary composite scratch. Current-frame LumOn gather has not run at the capture boundary, so captured pixels use direct lighting, emission and the existing standalone environment response rather than stale screen-space GI. Final unmasked pixels still use the selected PBR mode. World geometry drawn later that was occluded by the overlay's depth cannot be recovered from this snapshot; it records actual coverage at the capture boundary. This remains a screen-space limitation, not permission to reorder base-game renderers.

Pre-overlay capture skips both draws only when the current engine liquid mesh source proves
every pool in the active atlas prefix empty. `LiquidMeshSource` reads the manager's current
pool collection through a cached typed accessor and each pool's `IsEmpty()` contract; it does
not use cached rendered-triangle counts, camera fluid classification or GPU readback. Missing
or incomplete resources retain capture. Nonempty pools retain capture even if offscreen,
occluded, nontransmitting or in a mini-dimension. This deliberately does not promise culling
capture whenever a view appears dry while liquid geometry remains loaded elsewhere.

The check runs at the actual first-person overwrite boundary. Skipping invalidates the
pre-overlay publication before returning, without allocating, drawing or retiring the existing
scratch targets. A subsequent populated frame captures fresh data and reuses compatible
storage; resize, disable and world disposal keep their existing retirement behavior. Empty
pools imply no liquid interface even for an underwater camera; the independent camera-medium
and final opaque composition paths are not gated. Third-person, remote-entity and shadow
restrictions remain in the existing overlay predicate.

Installed-engine IL inspection places both `AddTesselatedChunk` call sites in
`ChunkTesselatorManager.OnBeforeFrame`, registered at Before order 0.99. The main loop runs
Before ahead of Opaque and OIT. Center/edge geometry uses the same dimension-aware pool
insertion path, background tessellation queues results, and runtime atlas expansion creates
empty managers. Thus the inspected engine does not populate liquid pools between this capture
decision and submission. This contract does not cover external mods directly inserting meshes
later in the frame. Evidence: `artifacts/WaterLagAnalysis/capture-demand-stage-il.log`,
`capture-demand-upload-il.log` and `capture-demand-pools-il.log`.

Verification passed all six focused cases, with no failures or skips: installed pool construction,
live insertion/removal, a hidden pool in a non-default dimension, and atlas changes; actual capture at full/half
resolution with LumOn on/off; and the first-person/third-person/shadow policy. Empty transitions
perform zero capture draws, withdraw publication and retain the same valid textures; returning
geometry republishes fresh data. Existing reload, resize, settings and world-lifecycle assertions
also pass. The changed production/test C# assemblies were rebuilt successfully against unchanged,
previously validated shader artifacts after unrelated shader-cache replacement access errors
blocked aggregate builds. This is not a fresh shader-build receipt. Evidence:
`artifacts/WaterLagAnalysis/capture-demand-csharp-build.log`, `capture-demand-test-build.log`
and `capture-demand-tests.log/.trx`. No live frame-time improvement has been measured.

Pre-overlay composition retains its own `PBRCompositeShaderProgram` under
`pbr_composite_pre_overlay`, with LumOn, PBR GI composition and short-range AO disabled.
Ordinary composition retains `pbr_composite` and adopts the engine generation's lighting mode
and current composite options before preparation. Alternating these passes does not change
either owner's structural options. Both share the existing offline shader contract and build system,
with independent executables and frame inputs managed by the normal registry reload/disposal
path. The deferred `PrepareFrame` readiness check prepares both owners without activating
full-scene HDR. This removes recurring executable replacement; live timing remains unmeasured.

The retained capture selects `VGE_COMPOSITE_PRE_OVERLAY_ONLY` at declaration. Its fragment
variant exposes only receiver color at location 1 and hardware depth at location 2. The
ordinary color output at location 0, sky-color preservation, water-volume/fog/aerial transport
and restoration from another overlay capture are compiled out. Physical receiver lighting,
standalone environment response and finite/half-float eligibility validation remain unchanged;
sky and invalid first-person proxies publish zero color and sentinel depth one.

`WaterRefractionScene.BeginCapture` allocates only the receiver pair. Its framebuffer has no
attachment zero, draw routing `[None, ColorAttachment1, ColorAttachment2]`, and read attachment
one. The existing framebuffer creation owner supports the leading unused output slot; native
routing stays inside that owner and existing pipeline/binding scopes remain in effect.
Ordinary publication still borrows the composite image at zero and writes all three outputs.

The former capture-time composite color had no consumer: capture returned before display
resolve, and ordinary composition overwrote that image before the display shader read it.
Capture now neither prepares ordinary/display targets nor depends on display-shader readiness.
Allocation failure withdraws capture publication without drawing into ordinary color. Initial
ordinary allocation or replacement preserves the independent current-frame capture; actual
screen resize and world/disposal boundaries still invalidate publication. Compatible captures
reuse their pair, and reload retains the separately selected owners. Removing the extra
RGBA16F write avoids eight nominal output bytes per full-resolution pixel; ordinary storage
is still required later, so this is not an eight-byte-per-pixel persistent-memory saving.

Matched optimized inspection retains the baseline opcode sequence for all eight ordinary
variants. The retained environment-only capture changes from 1143 to 284 static instructions
and from 22 to nine image-reading instructions. Its active fragment outputs are locations 1/2;
water-volume, atmospheric and overlay-restore samplers are absent. These establish removed
output/work, not measured bandwidth or frame-time improvement. The offline contract admits
16 variants after adding the Boolean capture option; selection is fixed on the capture owner.

Fresh serial shader/Debug compilation passed after the generator rejected an undersized
variant budget and the declaration was corrected. The focused run passed 66 of 68 tests;
two older overlay assertions expected unsanitized RGB/depth from an invalid receiver.
Running the saved baseline binaries reproduced both failures. Correcting those expectations
to zero color/alpha and sentinel depth one, rebuilding tests and rerunning the nine overlay
checks passed. All 68 distinct selected cases therefore have passing evidence; no clean
whole-suite rerun is claimed.

The checks exercise real direct/capture/ordinary draws at full/half resolution with LumOn
on/off, stable executable identities, reload, resize, failure publication and world cleanup.
First capture allocates no ordinary color image; later captures preserve sentinel values in
that image until ordinary composition overwrites it. Framebuffer checks verify absent slot zero,
two images and sparse read/draw routing. Eight numerical cases compare capture receiver pairs
exactly with ordinary pre-transport outputs, including nonzero environment lighting, atmospheric
and fog inputs, physical/first-person positions, and a following sky draw that clears old data.
Shared framebuffer creation checks also pass. Receipts, the baseline reproduction and optimized
modules are retained in `artifacts/WaterLagAnalysis/capture-target-*`. Live performance is
unmeasured.

## Background resolution and bilateral receivers

`WaterRefractionScene.BackgroundScale` selects full (`1`) or half (`2`) background storage.
The persisted `WaterRefractionBackgroundScale` control selects quality: `1` is half size and `2`
is full size (default). The composite converts this choice to the owner's divisor (`2` and `1`,
respectively); ConfigLib displays both choices from lower to higher quality.
The pre-overlay capture always remains full size. The ordinary composite restores its pixels
at full size, completes the existing scene handoff, and then optionally reduces the restored
receiver pair before publication. A changed divisor immediately withdraws publication. Resize,
source replacement, disable and world teardown retire associated reduction storage; unchanged
frames reuse their textures and framebuffers. Quality selection does not own these resources.

`water_refraction_reduce.fsh` selects the farthest valid physical opaque texel in each bounded
2x2 footprint. It excludes sky, invalid depth, nonfinite radiance and unsupported alpha metadata,
and copies the selected HDR radiance without averaging. The reduced RGBA32F depth image stores
hardware depth, original source UV and validity; RGBA16F color keeps the receiver metadata.
Odd dimensions round up, and incomplete edge footprints read only existing source pixels.
Depth is fetched first. A candidate must be strictly farther than the current validated
selection and below `.999999` before its color is read. The selection starts at zero, so
these ordered comparisons also reject nonpositive, nonfinite and sky depths. Equal depths
retain the first valid texel in row-major order. Surviving candidates pass the shared color
publication validator before changing the selection: invalid color cannot replace
a valid receiver or prevent a later, nearer valid candidate from being considered.
The complete pair validator uses that same color predicate after its depth checks; reduction
does not repeat depth validation already established by its ordered selection guard.
Retaining the original UV avoids reconstructing a selected silhouette at the reduced cell center.
The reducer has no water-interface geometry: a footprint containing only foreground can retain
that texel, which the subsequent interface test rejects. Farthest selection favors background
coverage and can discard thin nearer submerged surfaces; full resolution remains the baseline.

Matched optimized SPIR-V retains two static fetch sites, with the depth-selection branch now
dominating the color fetch. Per complete footprint, source-level color reads fall from four
to zero for all-invalid depth and to one for descending or equal valid depths; ascending
valid depths still require four. Depth reads and outputs are unchanged. Total static reducer
instructions fall from 167 to 154. All sixteen current composite variants retain their instruction
totals; helper inlining changes ordering, so their opcode sequences are not claimed identical.

The initial reorder still called the complete pair validator and retained redundant depth
checks in optimized code. It increased the ascending workload's median GPU time; splitting
out the shared color predicate removed those checks before finalization. The final bounded
baseline/current/current/baseline comparison uses an RTX 4090, NVIDIA 591.86, 512x512 RGBA16F/R32F
sources and 256x256 RGBA16F/RGBA32F outputs. Queries cover 128 warmed draws without preparation
or uploads; five warmup and ten measured batches per run yield twenty samples per version.

| Depth pattern | Baseline median (ms) | Current median (ms) |
| --- | ---: | ---: |
| Invalid | 0.123904 | 0.118784 |
| Descending | 0.126976 | 0.120832 |
| Ascending | 0.130048 | 0.125952 |
| Mixed invalid/background | 0.128000 | 0.120832 |

Ascending ranges touch at 0.128 ms; the other three measured ranges separate. These small
results apply only to the warmed fixture, not live frame time or arbitrary material coverage.
Full samples/ranges are in `artifacts/WaterLagAnalysis/reduction-candidate-gpu-*`; the earlier
guard-only attempt is retained in `reduction-gpu-*`.
The final supported shader/Debug build passed with zero warnings/errors, and all nine focused
GPU cases passed without skips, including odd edges, malformed pairs, half-float limits,
publication lifecycle and both composite publication paths. All 24 explicit receiver cases
match the original shader exactly, including color, depth and provenance. The reducer and
sixteen composite optimized binaries match the measured candidates byte-for-byte. Final
receipts are retained in `artifacts/WaterLagAnalysis/reduction-final-*`.

`liquids/receiver.glsl` supplies the shared four-tap receiver filter. Spatial bilinear weights
are eligible only for finite physical receivers more than 0.5 mm behind the oriented local interface.
The numerical separation guard replaces the old 2 cm exclusion, retaining centimetre-deep
receivers while still rejecting on-plane and foreground data. UV displacement scales with
physical receiver separation; there is no separate shallow-water displacement ramp.
The eligible tap with greatest spatial weight anchors the represented depth layer. Other taps
must differ in axial depth by at most the larger of a 5 cm floor, ordinary footprint support
capped at 2% of axial depth, and a grazing-interface allowance capped at 8% of axial depth.
With `p = 2 * abs(inverseProjectionXYScale) / backgroundDimensions`, `z = abs(anchorZ)` and
`r = anchorPosition / z`, ordinary support is `min(0.02*z, z*max(p.x,p.y))`; grazing support is
`min(0.08*z, 1.5*z*dot(abs(interfaceNormal.xy),p)/max(0.1,abs(dot(interfaceNormal,r))))`.
The latter estimates axial variation of submerged geometry parallel to the local interface;
it prevents a represented shallow-view floor becoming depth stairs at half size. The 1.5
multiplier supplies bounded tolerance for local slope variation, while the denominator floor
and depth cap bound grazing growth. It is not a receiver-normal reconstruction or proof of
hidden topology. A steep receiver exceeding this axial gate can retain all four taps only
when they are coplanar within reconstruction precision and two independent source neighbors,
outside the footprint along its anchor's X and Y directions, confirm the same plane. This
distinguishes a continuous wall from the fictitious plane that two separate depth columns can
form inside a four-tap footprint. Missing or inconsistent evidence keeps the ordinary gate.
Verification may use opaque geometry above the water interface at a shoreline, but those
extra samples never supply receiver radiance, interpolation weights or intersection coverage.
Color and
reconstructed position use exactly the same surviving weights and normalization. Unsupported taps
never contribute color; no surviving taps reports unavailable coverage. This replaces the
previous nearest lookup and blanket adjacent-foreground veto. Small unresolved geometric
features within the chosen tolerance remain an approximation of the represented depth field.
Projection still uses the full camera matrix; lookup offsets and bounds use background dimensions.
The sampler is independent of traversal and is the common receiver entry point for future tiers.

The producer encodes eligibility in the existing depth image: `1` is unavailable, while a
finite value strictly between zero and `.999999` promises physical coverage, alpha at least
`.5`, and finite RGBA representable in RGBA16F. `receiver_publication.glsl` validates the pair,
including the absolute 65504 half-float limit before storage. The composite applies this after
restoring any pre-overlay pair; sky, unsupported overlays and invalid radiance publish zero
color and sentinel depth. Reduction validates the pair before selecting its source and retains
the existing original-UV and validity metadata. Formats, attachment sizes and draw count do
not change. This moves repeated eligibility work to the producer, rather than allocating a
separate validity image or trusting hardware depth from an unvalidated source.

Ray probes and independent continuity neighbors read only depth. Support retains geometry,
spatial weights and integer source texel coordinates instead of four RGB values. A confirmed
triangle fetches its three colors after geometric acceptance. UV selection chooses its geometry
first, then uses that triangle result or fetches at most four positive-weight colors for the
selected spatial fallback. Reconstruction retains tap order and sum-then-divide normalization;
there is no geometry refilter. Final color reads still fail closed on invalid alpha or nonfinite
RGBA if a caller violates the coherent publication contract. Such malformed pairs are not
supported geometry inputs and cannot rely on per-tap rejection during the earlier search.
Replacing four `vec3` colors with four `ivec2` coordinates removes four scalar payload elements
relative to the preceding implementation; actual register allocation still depends on compilation.

The earlier 322-test receipt in `artifacts/WaterLagAnalysis/receiver-radiance-tests.log/.trx`
validated deferred interpolation while still reading eligibility color during search. It does
not validate this revised publication contract. The revised implementation passed a fresh
isolated Debug build and 300/300 focused GPU checks, with zero failures or skips, recorded in
`artifacts/WaterLagAnalysis/depth-search-build.log` and `depth-search-tests.log/.trx`.
Actual composite draws verify ordinary and restored-overlay validity in RGBA16F/R32F targets;
reduction tests check the 65504/65520 storage boundary. Receiver tests check malformed pairs,
full/half reconstruction, ray/UV selection and moving shallow continuity. Instrumented cases
verify zero color reads during unsuccessful search and three for selected triangles.
The matched compiler comparison uses the same pinned compiler and `-O` for both saved
baseline and current sources (`artifacts/WaterLagAnalysis/depth-search-optimized.csv`).
Across 32 liquid variants, static instructions increase from 54,634 to 58,564, module bytes
from 985,920 to 1,045,160, and static image-fetch instructions from 136 to 204. Across eight
composite variants, instructions increase from 8,747 to 9,251 while image-fetch instructions
remain 56. These are aggregate module counts, not instructions or fetches executed per pixel:
moving color fetches into final reconstruction reduces repeated search reads but expands
compiled final-selection paths, and publication adds validity checks. Register allocation,
GPU timing and net in-game performance remain unmeasured.

The existing shader library owns the reduction executable and reload lifecycle. Reduction uses
the composite pipeline description, framebuffer/binding restoration and program `UseScope`.
Its source images are absent from the active reduction framebuffer. Preparation failure or a
thrown reduction leaves publication unavailable, allowing ordinary liquid fallback. No display
conversion or water-volume attenuation occurs in reduction or receiver filtering.

Storage and work counts are format-derived bounds, not measured GPU costs. Full final snapshots
retain 12 bytes per full pixel. Half backgrounds add 24 bytes per reduced pixel to that restoration
storage, about 11.9 MiB extra at 1920x1080 (about 35.6 MiB total final snapshot storage).
Existing pre-overlay storage remains independently required. Half publication adds one fullscreen
draw, at most four full-source depth reads, up to four surviving color reads and two output
writes per reduced pixel. Each receiver
evaluation reads four depth taps, plus at most two depth-only continuity neighbors on a steep
coplanar footprint. Only selected radiance adds three triangle-color reads or up to four spatial
color reads. Six depth taps correspond to 24 bytes at full resolution or 96 bytes at half
resolution; selected color adds 24 or 32 bytes. These are nominal format counts excluding caches
and compression, not measured bandwidth. Reduced working sets do not establish a net speed or
memory improvement; timing remains user-run acceptance work.

Traceability: coherent capture/reduction and lifecycle follow the Water quality and receiver
contract below and `PBR.Liquids.md` ownership boundary; filtering follows the bilateral and
local-interface requirements in `PBR.WaterRefraction.todo`; linear radiance follows
`PBR.MaterialColorAndDisplay.md` and `PBR.WaterMedium.md`. Draw state retains the existing
compatibility boundary allowed by `Rendering.AuthoritativePipelineState.todo` and its proposal.
`WaterRefractionReductionTests` independently selects eligible source pixels and checks exact
associated radiance, depth and original UV, including odd edges. `WaterReceiverFilterTests`
checks shared eligible weights, finite/metadata rejection, unrelated layers, clipped/smooth
slopes and an independently authored grazing floor at both resolutions. The production liquid
tests consume actual reduced sources, including the fixed-world shallow camera that exposed
the insufficient initial slope bound. `WaterRefractionCaptureStateTests` executes capture and
the registered final composite in both lighting modes, comparing restored RGB/depth before and
after reduction. `WaterRefractionResolutionTests` checks target reuse, failed or missing reduction,
resize, disable and shader-library invalidation/repreparation. Existing owner world-leave and
mode regressions cover their established lifecycle boundaries. Quality/settings controls use the
existing preparation boundary; the snapshot owner does not depend on algorithm quality selection.

Completed validation on 2026-10-04: fresh build and **117/117 tests passed, zero skips**, in
`artifacts/PbrColor/water-receiver-final-regressions.log`. This includes 79 focused receiver,
reduction, production liquid, restoration and lifetime checks plus 38 adjacent composite,
boundary, transparency, sun and mode regressions. The offline shader catalog verifies 416
variants. Second source/document review and independent completion audit found no remaining
scoped requirements. Reload evidence exercises actual shader-library redeclaration and
preparation, rather than the complete engine reload event. Headless results establish numerical
and ownership behavior; live appearance and GPU timings remain unmeasured.

## UV distortion

Quality `0` uses `liquids/pixel_normal_refraction.glsl`: UE-style pixel-normal offset.
It compares the unperturbed mesh normal with the continuous wave normal in view space,
both oriented toward the eye. Matching normals produce no offset, including oblique flat
water. The UV offset is their XY difference multiplied by the projection focal scales,
a resolution-independent strength of `0.02`, and axial receiver separation ramped from
zero to full strength over `0.3` metres. This strength is VGE calibration, not UE's
resolution-dependent distortion constant. Increasing depth beyond that ramp does not
increase displacement. Candidate UVs use the existing 0.55-texel safe edge clamp and
bilateral eligibility checks; unavailable candidates retain the validated seed.
Snell direction remains available for scattering and total internal reflection, but does
not select the background UV. Absorption and scattering retain the selected receiver's
approximate water path. Full and half background resolution remain supported.

`LiquidRenderer` selects persisted quality before shader preparation. The default remains
`3`; `refraction_selection.glsl` maps `1`, `2`, `3` to geometric x2/x4/x8 budgets.
Their approximate fallback remains `liquids/uv_distortion.glsl`, described below; it uses
the continuous wave normal and IOR 1.333 rather than pixel-normal offset.

The higher-quality fallback uses IOR-based projection.
Let `s` be the displaced view-space interface, `n` its oriented unit normal, and `d` the Snell
direction from `normalize(s)`. A filtered lookup at `project(s)` supplies the straight receiver
`b` and a local receiver normal `m`. Its water-normal separation is
`h = max(0, -dot(b-s,n))` metres. With `c = max(0.1, -dot(d,n))`, the conservative
default estimate is `min(32,h/c)`. When `abs(dot(d,m)) > 0.00001` and
`dot(b-s,m)/dot(d,m)` is positive, that receiver-plane proposal supplies the estimate,
capped at 32 metres. Sparse support uses an axial receiver plane. Nonplanar support can
guide this approximate projection but cannot establish a ray hit.
Projection uses the full camera matrix and homogeneous division, so FOV, aspect and oblique
interfaces do not rely on a fixed pixel scale. Distorted UV is the projection of
`s + d*estimate`. Displacement naturally approaches zero as physical separation decreases;
the former 2-to-25-centimetre ramp is removed because switching from a geometric hit to
that suppressed fallback could abruptly erase shallow-water distortion. The cosine floor is
dimensionless and bounds grazing behavior. A normally viewed flat interface has no offset;
oblique flat water bends according to Snell, and wave normals alter the direction and offset.
A pixel-normal offset would require separate calibration to retain this depth/FOV behavior.

The seed and candidate both use the shared bilateral filter, including full or half-size
original-UV reconstruction. A candidate with compatible source triangles reweights geometry
and HDR radiance at the desired projected endpoint's camera-ray intersection inside those
actual triangles. Curved quads use each source triangle's plane independently; this does not
establish a Snell-ray intersection. At half size, if the original-source quad does not cover
the query, violated edges in its actual camera projection select a neighboring footprint.
Overlapping corners reuse cached values; at most three missing corners are fetched and
independently checked for submerged eligibility and layer compatibility or precise planar
continuation. The new actual triangles must cover the query. No bounding-box filling or
barycentric extrapolation is accepted. Unavailable coverage retains the eligible bilinear
approximation. Nonfinite projections and projections behind the eye are rejected.
Approximate distorted endpoints are clamped to the captured image with a 0.55-texel
inset at the actual refraction texture resolution; a one-texel dimension uses its center.
This continues edge samples when the estimated destination leaves the screen, rather than
abruptly reverting to the undistorted seed. It can stretch edge content and does not recover
offscreen geometry. Source-triangle projections, initial seed eligibility, and geometric
ray-hit validation retain their exact coverage checks. An unsupported distorted candidate
retains the validated seed radiance and geometry. If the seed has no support, no thickness is
invented and the existing straight-through liquid transport remains active. The filter can
renormalize supported taps at an edge without importing out-of-bounds or foreground color.
Valid selection has confidence one; it has no aesthetic edge/range fade. This does not supply
hidden geometry, and a disappearing represented receiver can still change the selected result.

Both distorted selection and its validated seed fallback report `VGE_WATER_RECEIVER_UV`, never
`RAY`. For the selected receiver `r`, above-water submerged length is the approximate
`min(32,max(0,-dot(r-s,n))/c)` metres. It estimates the ray distance from water-normal separation; it is
not a ray intersection, exact bathymetry or multi-interface transport. Underwater length is
`length(s)`: the camera-to-interface segment is water and the sampled exit segment is air.
The retained Snell direction feeds the shared refracted-scattering convention, with the
water-side eye direction used underwater. Total internal reflection returns no transmitted
receiver before any background lookup. Fresnel and HDR/legacy adaptation remain in the existing
shared liquid consumer; no second display conversion or opaque attenuation is introduced.

The sampler performs no iterative ray evaluations and at most two filtered UV lookups:
one seed and one candidate. Each reads four depth taps, with at most two
additional depth-only continuity checks on a steep footprint. A reduced candidate can additionally
fetch at most three missing neighbors to correct original-source coverage, giving at most
15 depth reads plus at most four selected-color reads in total (16 total without that correction,
12 without either extra check). A missing seed cannot meet the continuity preconditions and uses
at most four depth reads and no color; TIR uses zero. The small fixed filter
loops are sampling work, not ray traversal. No new screen targets or draw calls are required.
Quality and background resolution specialize fragment binaries through the existing offline
shader-option pipeline. These are source bounds, not GPU timings or speedup measurements.

Traceability: the UV tasks in `PBR.WaterRefraction.todo` govern displacement, shallow-depth scaling,
bounds, provenance and bounded work; the Water quality and receiver contract and Background
resolution and bilateral receivers sections govern coherent geometry/radiance and fallback.
`PBR.WaterMedium.md` governs SI units, RGB extinction and photon directions; `PBR.Liquids.md`
and `PBR.LiquidRenderer.Proposal.md` retain the owned shader, waves and six-output OIT contract.
`PBR.MaterialColorAndDisplay.md`, `PBR.SharedDisplay.md` and `PBR.OutputDithering.md` govern
linear composition before the existing output adapter. The authoritative pipeline plan/proposal
retain existing binding and state owners; this sampler introduces no new submission boundary.
Full-scene HDR activation, live appearance and measured GPU costs remain
separate work.

The original UV implementation's build and validation passed 156/156 checks with zero skips
(`artifacts/PbrColor/water-uv-final-regressions.log`); that offline catalog verified 417
variants. This historical receipt predates the shallow receiver corrections described under
Shallow receiver continuity; it does not validate their changed equations or fetch bounds.
`WaterUvRefractionTests` then contributed 25 cases: independently projected FOV/aspect
displacement and associated HDR radiance at full/half size, authored flat/opposite local wave
slopes, thin-depth suppression, foreground/invalid candidate fallback, unsupported seeds,
underwater/TIR, near-edge bounds and the 32 m path cap. Its lookup counter executes the bounded
seed/candidate work; it does not count filter taps as ray steps. These local wave-normal
references do not establish animated in-game wave appearance.

The production liquid suite then passed 44 cases, including 14 new UV cases for HDR/legacy output,
full/half backgrounds, tilted/rotated interfaces, unavailable inputs, entry/exit/TIR and independently
predicted approximate-path solar/point scattering. Legacy UV gradient checks retain the single
display mapping. The other 87 checks cover the existing ray diagnostics, shared filter/reducer,
publication/capture/lifecycle, transport/overlay and adjacent composition/boundary/liquid behavior.
The initial 3 cm suppression fixture incorrectly assumed all taps survived its strongly tilted
eligibility plane; the final isolation case keeps them physically eligible and directly checks UV.
Hard foreground rejection remains separately tested. No production eligibility tolerance was
weakened. Second source/document review covered formulas, provenance, fallback, TIR, shared
composition, default selection and unchanged state/resource owners. The independent completion
audit found no remaining scoped requirements. Headless numerical evidence
does not establish live appearance or GPU cost; no game was launched.

## Optics and traversal

The liquid frame owner computes the inverse projection on the CPU when it stages the
camera projection. Both column-major matrices are published together through the existing
frame UBO; singular projections are rejected before replacing either matrix. The inverse is
appended at byte offset 4640, making the block 4704 bytes while preserving earlier offsets.
Ray traversal, UV distortion and its fallback use this supplied inverse for receiver
reconstruction rather than inverting the projection per fragment. Diagnostic shaders receive
the same projection/inverse pair through typed fixture inputs. This changes where the matrix
is evaluated, not receiver validation, traversal budgets or background-resolution semantics.

Validation on 2026-10-04 passed a fresh isolated Debug build and 312/312 GPU regressions
with zero skips, including asymmetric camera reconstruction, changed projection publication,
full/half receivers and shallow continuity. Disassembly found no `MatrixInverse` instructions
in all 32 liquid fragment variants, their shared vertex binary, or the two refraction diagnostic
binaries. Receipts are `artifacts/WaterLagAnalysis/inverse-projection-build.log`,
`inverse-projection-tests.log/.trx` and `inverse-projection-spirv.json`. These checks establish
correctness and removed shader work; they do not measure live GPU improvement or Release cost.

The liquid shader uses its continuous animated water normal and the existing water IOR of 1.333. This is the current water material optical model, not a new configurable IOR property. Air entry uses an eta ratio of 1/1.333; underwater exit uses 1.333. Existing dielectric Fresnel handles total internal reflection. Reflection keeps the existing direct/environment response; scene reflections remain a separate task.

Tracing starts at the displaced interface in view space. The stable quality IDs `1`, `2`, `3`
select total ceilings of two, four and eight receiver-depth evaluations. The seed at distance
zero counts, as does every changed ray-position lookup. There is no separate refinement loop
or final revalidation outside the ceiling. Each evaluation uses the shared four-tap filter;
zero spatial-weight neighbors may supply local geometry but never alter the evaluated color
or depth weights. The local compatible layer guides sampling rather than distributing a few
samples blindly across the former quadratic 32 m march.

`VgeRefractionSupport` retains the filtered position, spatial weights, four cached source
positions and integer color texel coordinates. Three noncollinear compatible taps estimate a local plane normal and
authorize only their actual triangle. A fourth compatible coplanar tap can authorize the second
triangle; a missing corner never becomes a filled rectangle. For plane normal `m`, receiver
position `b`, interface `s` and Snell direction `d`,
the next proposed distance is `dot(b-s,m)/dot(d,m)`. A nearly parallel denominator is unavailable,
not an infinite path. With insufficient geometry the proposal uses an axial plane; that is a
search estimate, not proof of an intersection. This equation supports positive view-Z rays.

A corrected intersection can reuse cached taps only when it lies inside an actual retained source
triangle (normally `(0,1,2)` or `(1,3,2)`). Barycentric weights apply to both geometry and HDR radiance
at that corrected hit. A reduced texel's original source position defines the triangle;
its cell center and the source positions' bounding rectangle cannot fill unsupported corners.
Sparse/nonplanar support or failed triangle containment cannot authorize a geometric hit,
even when axial depth matches. Further probes can find usable geometry within the remaining budget;
otherwise validated UV selection retains its explicit approximate provenance. This avoids pairing
a ray position with a different position's color on slopes. Layer-filter tolerances remain separate
from plane-consistency tolerance. Homogeneous reconstruction propagates a conservative device-depth
perturbation `8 * 1.1920929e-7` through its axial derivative. The plane-consistency allowance is bounded
between 0.5 mm and 2 cm, without reusing the former 15 cm residual threshold.

The extent is at most 32 m and is analytically clipped against homogeneous screen inequalities
and the eye plane, without depth reads. Unsupported samples do not immediately abort a
recoverable search: bounded recovery probes begin at 0.125 m, then grow within the available
range and the same total budget. No bracket or interpolation across an unsupported gap
can establish a hit. Later coverage must independently prove its own local intersection.
Partial edge footprints are validated by the filter; the old two-pixel/half-texel guard is gone.
The physical interface test, metadata checks and associated-color filtering remain required.

After evaluating a probe, traversal stops if its next clamped ray distance is exactly unchanged.
This removes repeated endpoint lookups when support is unavailable or a represented patch
cannot establish a hit. The current endpoint is always evaluated first; valid patch hits and
plane proposals that move back to another position remain eligible. Equality compares ray
distance, not texture coordinates or texel footprints, and adds no movement tolerance.
The existing proposal-selection tolerance is unchanged. Exhausted progress returns the ordinary
unavailable ray result, letting the existing selector use the original validated seed for UV
fallback. It never promotes the last unsupported patch to a hit or changes the x2/x4/x8 ceilings.

Matched optimized inspection changes only the six ray-marching surface variants, adding 12
static instructions for the equality/exit bookkeeping. Both UV-only variants and all 24 capture
variants retain identical opcode sequences. This is a reduction in repeated dynamic sampling,
not a claim that total static instruction count or measured GPU frame time decreases.

Executed full/half-resolution edge fixtures cover unavailable support and supported patches
whose intersection cannot be accepted. Their short visible paths now use two probes (seed and
endpoint): x8 decreases from eight probes/18 depth reads to two/six, x4 from four/ten to two/six,
and x2 remains two/six. Selected validity, position and HDR radiance match independently seeded
UV selection. A different represented plane at the maximum extent still produces its valid ray
hit on the second probe. Positive extents below `.0005` metres retain two distinct probes even
within the same texture footprint; those partial-edge footprints use four total depth reads.
An entirely unavailable 32-metre path visits distances 0, .125, .5, 2, 8 and the endpoint once;
x8 therefore stops at six probes while lower tiers retain their ceilings. The invalid-seed
recovery fixture finds support at two metres and jumps to the endpoint, completing in five.

The serial shader/Debug build passed. The focused run passed 36/36 cases without skips, including
TIR, cached patch coverage, seed reuse, full/half tier reference comparisons, moving x2 ray/UV
transitions and range exhaustion. A subsequent test-only build and two-case run passed the
additional tiny-extent checks; all 38 distinct cases have passing evidence, not a single 38-case
run. The six-case baseline reproduction, optimized comparison and final receipts are in
`artifacts/WaterLagAnalysis/endpoint-*`. Live GPU/frame-time improvement remains unmeasured.

Every supported geometric result has confidence one. Unconditional interface-edge, receiver-edge,
range and residual fades are removed. `refraction_selection.glsl` prefers that result and otherwise
invokes the bounded validated UV sampler, keeping provenance `UV` rather than `RAY`. Only failure
of both samplers retains ordinary straight-through transport. TIR invokes neither fallback lookup
nor transmission. Shared HDR transport and its output adapter consume the selected result once;
there is no extra original background or display conversion. Changes in represented geometry can
still change receiver selection: screen-space data cannot establish arbitrary offscreen/occluded
receivers, missing transparent layers, hidden topology or all subpixel thin geometry. No temporal
history or universal receiver/motion-continuity guarantee is introduced.

The ray sampler returns the validity and support of its distance-zero interface lookup along
with its result. UV fallback consumes that original support and the already computed Snell
direction; it does not project/filter the seed again. Later recovery probes never overwrite
the retained seed. An invalid seed still permits ray recovery, but an unsuccessful ray with
an invalid seed returns unavailable coverage without UV reads. TIR and failure to project the
interface likewise perform no fallback reads. Standalone UV quality continues to evaluate its
own seed independently, using the same subsequent candidate-selection implementation.

The handoff retains one existing `VgeRefractionSupport` plus a validity flag across traversal;
it adds no texture, UBO, persistent state or new payload type. It extends the seed's lifetime
and copies valid initial support once, so saved samples alone do not prove reduced register
pressure or GPU duration. Matched compilation of all 32 liquid variants before and after
the handoff, using the same compiler and `-O`, reduced full-resolution ray modules from
5,859 to 5,219 static instructions and half-resolution ray modules from 7,100 to 6,380.
Standalone UV modules gained ten instructions with unchanged static texture-fetch counts.
The comparison is recorded in `artifacts/WaterLagAnalysis/seed-reuse-optimized.csv`;
hardware register allocation and live GPU savings remain unmeasured.

Fresh isolated Debug validation covers 71 distinct GPU cases. The initial run passed 68 and
failed three half-resolution edge assertions: those seeds contain two in-bounds depth taps,
not four. After correcting only that expected count, all six new comparison cases passed in
a focused rerun; this was not a second full 71-case run. The comparisons render raw tracing,
combined selection and independently seeded UV against identical full/half backgrounds at
x2/x4/x8. Geometry, HDR radiance and transport agree; counters verify the removed seed reads,
including partial edges. Invalid-initial/valid-later and valid-initial/invalid-later cases check
the last probe explicitly, and initial offscreen projection performs zero reads. Existing
standalone UV, TIR, exact-budget exhaustion and shallow moving x2 transitions also passed.
Receipts: `artifacts/WaterLagAnalysis/seed-reuse-tests.log/.trx`,
`seed-reuse-corrected-tests.log/.trx`, `seed-reuse-build.log` and `seed-reuse-test-build.log`.

Maximum depth-fetch bounds, separately from ray evaluations, are 48/24/12 for x8/x4/x2,
including up to two depth-only steep-continuity checks per evaluation. A successful ray adds
three selected-color reads. Exhausted rays fetch no color before UV fallback. A valid cached
seed allows fallback to add at most nine depth reads (one four-tap candidate, two continuity
neighbors and three reduced-footprint recovery neighbors) plus four selected-color reads;
an invalid cached seed adds none. Reuse removes the original seed's four depth reads and up
to two continuity reads, with fewer saved reads at partial screen-edge footprints. UV-only
uses at most 19; TIR uses zero. These bounds assume the coherent producer-validated pair.
They are source work bounds, not measured GPU timings or proof of a faster frame.

Traceability: bounded traversal, confidence-loss reproductions and reference comparisons follow
the traversal tasks in `PBR.WaterRefraction.todo` and its Water quality and receiver contract.
The shared filter/reducer contract above controls metadata, local geometry, foreground exclusion
and full/half source provenance. `PBR.WaterMedium.md` controls metre units, RGB medium transport
and photon directions; `PBR.Liquids.md` and `PBR.LiquidRenderer.Proposal.md` control wave normals,
engine mesh/OIT ownership and shader preparation. `PBR.MaterialColorAndDisplay.md`,
`PBR.SharedDisplay.md` and `PBR.OutputDithering.md` retain linear composition and the existing
HDR/legacy adapter. The approved authoritative pipeline plan/proposal retain state, resources and
binding ownership; traversal changes no draw, target, capture or shader-activation boundary.

Accepted above-water paths apply the authored water absorption and constant-source in-scattering over the refracted interface-to-receiver length. Atmospheric transport applies only on the camera-to-interface air segment. Underwater exits apply atmospheric transport on the outgoing air segment and medium transport on the camera-to-interface segment. The existing engine camera classification does not follow animated water contact; that belongs to the waterline task. The local lighting and homogeneous medium approximations are unchanged from [water transport](PBR.WaterMedium.md).

## Shared receiver and optical evaluation

`liquids/transport.glsl` defines `VgeWaterReceiver`: validity, sampling provenance, unattenuated
linear radiance, view-space position and refracted direction, submerged length in metres, and
confidence. The samplers report unavailable, ray-traced or approximate UV results. Confidence is
independent of validity and provenance.
`liquids/refraction.glsl` and `liquids/uv_distortion.glsl` select receivers without evaluating lighting or display conversion.

For accepted above-water rays, scattering compares the incoming photon direction (`-L`) with
`transpose(mat3(modelViewMatrix)) * -refractedDirectionVS`. The trace travels from camera toward
the receiver; outgoing scattered photons travel in reverse. For an underwater exit, outgoing
water photons instead point from the interface toward the camera (`-surfaceVS` transformed to
world space). The refracted segment is air. Solar and point-light scattering share this rule;
isotropic environment lighting is unchanged. Unavailable/disabled refraction retains the
straight-through source direction.

`VgeWaterTransport` evaluates RGB extinction and integrated source radiance independently of
receiver selection. Accepted paths consume the unattenuated snapshot, so opaque water-volume
attenuation is not applied twice. The straight-through path retains its existing `WasComposed`
guard. `VgeWaterCompose` combines linear premultiplied contributions and coverage before the
output adapter. HDR output keeps radiance above one; legacy output applies the shared display
operator and dither once to the resulting straight color. This fixes the previous interpolation
of separately tone-mapped contributions without activating full-scene HDR.

Each homogeneous water path shares one evaluated transmission between attenuation and source
integration. The straight-through fallback also reuses that transmission for coverage. Different
receiver/fallback lengths remain separate evaluations; underwater transport still uses only
the submerged camera segment. The thin-depth series and zero-scattering early return are
described in [water medium evaluation](PBR.WaterMedium.md#evaluation-and-ownership).

The surface evaluates ordinary fallback transport only when the receiver is unavailable or
its confidence is below full replacement. A valid receiver with confidence at least one skips
the fallback depth lookup, thickness, extinction and scattering integration. The unused fallback
inputs are initialized to zero radiance and unit alpha;
the existing clamped premultiplied composition then returns the complete refracted contribution.
Confidence is not changed. Partial-confidence receivers retain both contributions, while disabled
refraction, rejected coverage and total internal reflection retain the ordinary path.

Source illumination uses the same positive-effective-scattering rule as boundary capture.
Solar and point-light phase terms are evaluated only for required fallback/refracted sources;
the refracted photon-direction transform is likewise unnecessary for zero scattering or exactly
isotropic scattering. Isotropic sources use `1/(4*pi)` without forming a phase cosine;
every nonzero anisotropy retains its directional response (see `PBR.WaterMedium.md`). Shared
shadow visibility, solar and point-light reflection remain outside these gates. Non-water body
lighting, glow, sphere fog, preview alpha and six-target OIT output retain their existing paths.
The underwater outgoing air segment remains separate from the submerged camera segment.

Above water, the surface-to-camera atmospheric segment is evaluated once after linear
premultiplied confidence composition, before display adaptation. Both candidate contributions
use the same displacement, sky visibility, atmospheric parameters and sun direction. For
transmission `T`, atmospheric scattering `S`, fallback color/alpha `F,a` and receiver color
and confidence `R,w`, the combined alpha is `A = (1-w)*a + w`. Applying aerial perspective to
the composed straight color yields `T*((1-w)*a*F + w*R)/A + S`. This equals composing
`T*F+S` and `T*R+S`: their scattering weights sum to `A`, rather than adding `S` twice.
The surface's fallback alpha is at least `.001`, so composition's normalization floor does
not alter this equivalence. Alpha itself is unchanged.

Full-confidence receivers already omitted their discarded fallback aerial evaluation;
unavailable receivers already evaluated only fallback. Those existing endpoints still need
one shared camera evaluation. Partial confidence now also uses one evaluation rather than two.
Non-water liquids use that same final camera segment. An underwater receiver's outgoing air
segment remains evaluated before submerged transport, with no atmospheric evaluation along
the underwater camera segment. Sphere fog, preview transparency and OIT remain after the
surface's display adapter in their existing order.

Matched optimized compilation covers all 32 liquid fragment variants. Each of the eight
surface variants removes 100 static instructions and three image-sampling instructions
(19 to 16), corresponding to one aerial lookup site and its coordinate calculations.
The other 24 capture variants are unchanged. Current production confidence endpoints already
evaluate only one camera segment, so these counts do not establish additional dynamic savings
for those pixels. Partial-confidence composition avoids a second lookup; no new GPU timing
or live frame-time improvement is claimed. The comparison is recorded in
`artifacts/WaterLagAnalysis/shared-aerial-optimized.csv` and its matched disassemblies.

The shared-camera change passed a fresh serial shader/Debug build. All 100 existing production
regressions passed. The expanded optical-helper test initially rejected a byte-array upload to
a float texture before drawing; an explicit float-array fixture correction and test rebuild
then passed that test. It checks 36 combinations of confidence (0/.25/.5/1), fallback alpha
(.001/.25/1) and sky visibility (0/.4/1), comparing shared and separately evaluated transport
against independent CPU expectations. It also verifies air-before-water ordering for exits.

Matched optimized baseline/current production runs each passed 22 selected cases. Nonzero aerial
textures, sky visibility 0/.4/1 and refraction enabled/disabled cover UV/ray receivers, rejection,
underwater/TIR, non-water liquids and directional scattering. Their 792 six-target center
readback comparisons differ by at most 1.0001e-5 absolute and 1.2346e-7 scaled. These are focused
headless checks, not live visual acceptance or a full-frame comparison. Build, initial and
corrected tests, output runs and comparison CSV are retained under
`artifacts/WaterLagAnalysis/shared-aerial-*`.

The fallback-work change passed a fresh serial shader/Debug build and 101/101 focused tests
with zero failures or skips. Coverage includes all refraction tiers, full/half receivers,
disabled/rejected refraction, underwater/TIR, directional scattering, non-water compatibility,
transparency and sun reflection. Existing optical-helper tests retain partial-confidence
composition checks; current production selectors publish only zero or one confidence.
Matched optimized builds cover all 32 liquid fragment variants. The eight surface variants
retain the conditional fallback depth, transport, aerial and source-phase work and each gain
66 static instructions; the other 24 capture variants are unchanged. Static image-sampling
counts are unchanged. This demonstrates conditional execution, not
a smaller shader or measured register/occupancy improvement.

Four warmed ABBA runs passed two production cases each, comparing UV and x8 at full resolution
on an RTX 4090 (driver 591.86). Each sample times 16 draws at 512x512, excluding preparation
and uploads. Nonzero medium scattering, solar/environment/point lighting and aerial transport
remain active. Valid, invalid and 8-pixel checker receiver regions produce 120 samples.
The checker includes both replaced and fallback pixels: 161344/262144 replaced for UV and
199228/262144 for x8. Across 36 attachment comparisons, revealage and glow are bit-identical;
weighted color differs by at most 1.1921e-6 absolute (1.1236e-6 scaled).

| Receiver workload | UV baseline / changed (ms) | x8 baseline / changed (ms) |
| --- | ---: | ---: |
| Valid | 2.2062 / 2.1893 | 4.3402 / 4.0847 |
| Invalid | 0.6339 / 0.6513 | 6.1773 / 4.7974 |
| Checker | 2.1171 / 2.0828 | 14.7528 / 13.0181 |

These are medians per 16 draws, with broad overlapping ranges. For example, x8 checker ranges
are 12.58–16.62 ms before and 8.78–17.51 ms after. They do not establish a reliable speedup,
production cost or isolated divergence penalty. Live frame-time benefit remains unmeasured.
Receipts are `artifacts/WaterLagAnalysis/fallback-shading-*`: build/test logs, optimized and
timing CSVs, ABBA logs/TRX, and full-attachment output comparisons. An initial measurement
attempt rejected the missing default-x8 binary override before timing that case; the corrected
fixture explicitly maps both default and variant assets, and all four subsequent runs passed.

The controlling contracts are the receiver/provenance and composition requirements in
`PBR.WaterRefraction.todo`, the units and photon-direction convention in `PBR.WaterMedium.md`,
and the six-target OIT/engine ownership contract in `PBR.Liquids.md`. Display adaptation follows
`PBR.MaterialColorAndDisplay.md`, `PBR.SharedDisplay.md` and `PBR.OutputDithering.md`.
Focused validation covers production liquid output and independent transport/direction references.
Traversal diagnostics retain the original authored scenes; bounded-algorithm expectations now
require full confidence for supported coverage and classify UV fallback separately.

Fresh build and focused validation passed 40/40 tests with zero skips in
`artifacts/PbrColor/water-transport-integration-tests.log`. The production liquid cases cover
HDR and legacy output, highlights above one, Snell-selected receivers, linear confidence blending,
TIR, disabled/unavailable refraction, camera rotations and nonzero solar/point-light scattering.
The directional cases independently predict Snell, Henyey-Greenstein and Beer-Lambert results
per RGB channel. A typed precompiled helper fixture checks entry/exit photon directions and
world transforms, colored transport and confidence endpoints. The 12 receiver diagnostic cases
then used a precompiled fixture importing production traversal, preserving the original rejection,
sample-count and confidence checks. No runtime compilation of VGE-owned GLSL is needed.
These receipts establish the scoped water contract, not whole-scene HDR activation or live visuals.
The separate regression run passed 55/55 with zero skips in
`artifacts/PbrColor/water-transport-regressions.log`: liquid transparency/sun/program behavior,
signed water boundaries and capture, RGB water-volume composition in both lighting modes,
refraction publication/overlay handling and resource lifecycle. Both runs used fresh builds.

## OIT composition and limitations

A fully trusted refracted source includes the background, medium transport and Fresnel-weighted interface response. It emits opacity one, causing overall multiplicative OIT revealage to become zero, so the engine compositor does not add the original background again. During fallback transitions, blend premultiplied scene-linear radiance and opacity together: `alpha = mix(fallbackAlpha, 1, confidence)` and `source = mix(fallbackColor * fallbackAlpha, refractedColor, confidence)`. The straight linear color is `source / alpha`; the selected HDR/legacy output adapter runs only after this blend; the original background contributes only `(1-confidence)*(1-fallbackAlpha)`. This keeps the complete result continuous without blending the original background twice. The bucket weights and six output attachments remain unchanged. Failed traces retain the existing scalar straight-through transmission and already-composed opaque water transport. Flow animation, shadows, lava/body lighting, emission, local fog spheres and preview transparency continue through the liquid path.

The snapshot represents opaque receivers only. Accepted paths approximate one water interval; other water boundaries, overlapping liquids and transparent objects are absent from it. Multiple accepted liquid layers retain engine bucket color averaging, rather than ordered optical transport. A transparent object behind water can contribute separately through OIT and is not refracted by this algorithm. Preview transparency deliberately mixes the completed liquid result with the original background. These limitations require live evaluation at overlaps and shorelines; this implementation does not claim complete transparent scene transport or the later scene-linear HDR pipeline.

## Water quality and receiver contract

The following contract governs the work in [PBR.WaterRefraction.todo](PBR.WaterRefraction.todo).
The fields and default/validation behavior are implemented. The distinct x8/x4/x2 algorithms
are implemented with total receiver-depth ceilings, as described under Optics and traversal.
The ConfigLib Water Settings section exposes enable/disable, quality `0` (pixel-normal offset),
`1` (ray march x2), `2` (ray march x4), `3` (ray march x8, default), and background resolution
`1` (half) or `2` (full, default).
Both menus display lower values before higher values. Numeric allowed values retain
integer persistence without named-mapping serialization. The original root enable property is
retained, so saved choices do not require migration to a new nested configuration object.

All three controls are client-side and grouped between the Water Settings separator and the
existing master section. Changes use the established ConfigLib event and sanitization paths,
without a global shader reload or restart. Algorithm/resolution changes select a precompiled
fragment generation through the existing preparation boundary before liquid submission.
Each composite invocation snapshots enable/divisor; pre-overlay
capture always remains full size, and final publication uses the selected divisor. The liquid owner
stages quality and rejects a published pair whose resolution differs from the current setting,
so a change between opaque publication and OIT retains safe fallback until the next publication.
Quality changes leave receiver allocations intact. Disable retains the existing capture/final
retirement gates. These controls affect refraction; they do not disable water geometry or medium lighting.

`RefractionQuality` declares structural option `VGE_WATER_REFRACTION_QUALITY`, with finite
domain `0..3` and default `3`. `RefractionBackgroundScale` declares
`VGE_WATER_BACKGROUND_RESOLUTION`, with domain `1,2` and default `2`, matching persisted
resolution choices rather than the snapshot owner's inverse divisor. Both are fragment-only
`ShaderUse` entries. `CanTakeOwnership` batches both selections with `ConfigureOptions` before
`EnsureReady`; the ordinary OIT callback uses that existing readiness owner before binding inputs.
Unchanged batches reuse their immutable requested settings and projected load plan through the
existing settings editor. It copies values only when a normalized selection changes, without
skipping callbacks, validation or readiness. A failed preparation remains suppressed until
inputs or assets change; an unchanged batch does not erase that failure or a reload request.
In a warmed 4096-batch CPU allocation comparison, the two liquid selections decreased from
11,336 to 1,320 bytes per batch (88.4%). This excludes caller-delegate construction and warmup,
but includes the configuration call's own overhead; it is not an allocation-free or game-FPS claim.
See [shader settings publication](ShaderAuthoring.md) for atomic and inactive-option semantics.
No quality uniform is declared or uploaded. Quality zero compiles the UV-only selector; higher
qualities compile fixed total loop ceilings of 2/4/8 with validated UV fallback. Receiver metadata
decoding compiles for the selected full/half convention instead of checking dimensions per tap.
Standalone diagnostic fixtures retain dynamic inputs to compare algorithms using the shared code.

The existing structural option system uses an unconditional finite matrix: four qualities by two
resolutions by four capture modes, for 32 fragment variants and one shared vertex binary.
Capture modes do not require extra vertex variants. Some capture modes do not execute refraction,
but the current option contract cannot conditionally omit structural options; their source is still
optimized through the existing build pipeline. No new generator or shader-cache system is introduced.
Release builds use the existing performance optimization policy; distinct compiled variants and
removed runtime selection do not by themselves establish GPU speedup.

Volume capture and surface rendering retain separate `LiquidShaderProgram` owners in the
existing shader registry. `pbr_water_volume` keeps capture mode 3, while `pbr_liquid` keeps
surface mode 0 and the selected refraction quality/resolution. They share the existing offline
shader contract and binaries but own independent executables and frame inputs. Alternating
passes therefore binds existing programs instead of replacing the executable twice per frame;
normal asset reload and registry disposal manage both owners.

Optimized name-stripped SPIR-V can expose separate vertex and fragment block entries at the
same fixed binding. The existing prepared-binding owner accepts equal buffer extents with
disjoint, known stage ownership and retains one diagnostic representative. Duplicate entries with
same-stage collisions, unknown ownership or unequal extents remain rejected. Buffer submission uses
the fixed slot, with no extra uploads or runtime name lookup; compiler optimization remains enabled.

Precompiled-option validation on 2026-10-04 passed **1213/1213 checks in Debug and 1213/1213
in optimized Release, with zero failures or skips**, in 21.8927 and 10.5383 seconds respectively.
Both builds verified the current 445-binary catalog. Coverage includes all eight quality/resolution
selections on a retained liquid owner, shared vertex selection, capture-mode transitions, removed
quality uniform, 66 numerical liquid-water cases, wave/depth programs and prepared-binding
rejection/restoration. Receipts: `artifacts/PbrColor/water-precompiled-options-debug-final.log`
and `artifacts/PbrColor/water-precompiled-options-release-final.log`. These overlapping suites
supersede the earlier Release shared-block rejection; isolated outputs avoided replacing the
running client's mod or shader cache. No live appearance or GPU performance result is claimed.

Settings validation: fresh subagent build and 117/117 focused checks passed with zero skips
(`artifacts/PbrColor/water-settings-tests.log`). This includes twelve water configuration cases,
sparse startup defaults preserving the old enable flag, ConfigLib event application, numeric
round-trip/sanitization, grouped control definitions, actual capture/composite scale selection
in both lighting modes, receiver lifecycle, and the existing liquid/UV algorithm checks.
Direct liquid-owner quality assignment is source-reviewed; the production shader selector is
exercised by the liquid suite. Read-only inspection of installed ConfigLib 1.10.12 confirms
numeric allowed values pass through selection and `JsonObjectPath.Set` without named-mapping
string serialization (`artifacts/PbrColor/water-settings-configlib-il.log`). No user configuration
file was modified and no live GUI save/reopen or game appearance acceptance is claimed.

The lower-to-higher ordering correction passed a fresh isolated build and 117/117 checks,
zero skips (`artifacts/PbrColor/water-settings-ordering-tests.log`). The shader build verified
417 variants. Background choice `1` selects half size and `2` selects full size; missing/invalid
values default to `2`. The capture tests exercise the conversion to the owner's inverse divisor.
The running client locked the normal mod DLL/shared shader cache, so verification used isolated
assembly and SPIR-V output directories; it did not replace the running client's mod.

| Persisted property | Values and default | Contract |
| --- | --- | --- |
| `WaterRefractionEnabled` | Existing Boolean, default `false` | Preserve existing saved values. Off is independent of quality. |
| `WaterRefractionQuality` | Integer `0` = pixel-normal offset, `1` = x2, `2` = x4, `3` = ray march x8 (default) | Present the UI in lowest-to-highest order, labelled Water Quality. Values are stable identifiers, not loop counts. |
| `WaterRefractionBackgroundScale` | Integer resolution choice `1` = half width and height, `2` = full size (default) | Independent of quality; all four qualities support both resolutions. Convert to the inverse size divisor at receiver ownership boundaries. |

Missing leaves use these defaults through the existing `ConfigModSystem` load/default-materialization
and `VgeConfig.Sanitize` paths. Preserve the current enable flag when migrating old documents.
Unknown integer quality values reset to `3`; unsupported resolution choices reset to `2`, rather than silently
turning the feature off. Wrong JSON types follow the existing loader's error/recovery policy.
Runtime changes are adopted as one
frame-consistent settings snapshot; a resolution change invalidates publication before replacing
the pair. A quality-only change does not reallocate the background. Disable withdraws publication,
reclaims refraction-only final and pre-overlay storage, and skips traversal, distortion and reduction.
Shared water-volume, atmosphere and HDR scene resources remain independently owned and active.

The x8/x4/x2 limits count **all ray-position receiver-depth evaluations**, including refinement and
any final revalidation. Cached results can be reused, but the old five refinements and final lookup
cannot be added outside that ceiling. Historical tracing performed up to 38 receiver evaluations;
the replacement performs at most 8/4/2, each validating four depth taps and
conditionally validating two additional depth neighbors (up to six fetches).
The former adjacent-depth loop and hidden refinements are removed. Texture taps are not ray steps.
Record those taps and bounded UV fallback
work separately; the UV tier performs no iterative ray traversal. The 32-metre current extent is
a baseline, not a mandate to distribute two new samples across that whole distance blindly.

All tiers use shared bilateral receiver filtering: spatial weights combined with valid metadata,
oriented interface-plane eligibility and compatible depth support. Normalize only surviving taps,
and associate colour with the same receiver support as depth. No support means unavailable data.
Do not average distinct surfaces into a fictitious intersection. Filter tolerances and crossing
estimation are selected against the baseline fixtures during their implementation; requiring the
filter does not license removing physical foreground rejection. Projection uses the full view;
sampling offsets and bounds use the background texture dimensions explicitly.

Half-size storage uses `max(1, ceil(width / 2))` by `max(1, ceil(height / 2))`. Resolve pre-overlay
restoration at full resolution before reducing the final publication. The reduction must select
compatible receiver support and preserve its associated radiance/depth/validity, including odd
edge footprints; simple independent colour/depth averages are prohibited. Full resolution remains
the baseline because reduced storage cannot recover subpixel coverage already discarded. Publish
only a complete pair from one frame/projection/settings generation, using the existing owners and
completed resize/reload boundaries. Extra reduction scratch belongs to `WaterRefractionScene`,
not the liquid mesh renderer. Do not introduce a parallel framebuffer/state ownership system.

Receiver outcomes are explicit: valid marched receiver, valid approximate UV receiver, unavailable
coverage, and no transmitted ray due to total internal reflection. Prefer the valid marched result;
when its coverage is genuinely unavailable, attempt bounded UV distortion with the same receiver
eligibility/filtering. Only use undistorted transport when neither selection is usable. An
approximate UV receiver must not be reported as a geometric ray hit. Valid supported distortion
must not be unconditionally faded solely because the interface is near the screen edge, or because
an arbitrary distance fade begins. Any transition blends complete HDR transport contributions,
not a second copy of the original background. Hidden/offscreen geometry and transparent layers
absent from the snapshot remain unavailable; total internal reflection correctly transmits nothing.

Optical evaluation retains IOR 1.333 and material-owned RGB coefficients in inverse metres. For an
above-water camera, the ray into water points away from the surface toward the receiver, so the
outgoing photon direction used in the water phase function is its negative, transformed to world
axes. For an underwater camera, scattering toward the camera uses the water-side interface-to-eye
direction; the outgoing refracted ray beyond the interface lies in air and must not replace it.
Compare that water-side outgoing direction with incoming light propagation (`-L`) consistently.
Keep isotropic environment terms, bounded local lighting and the existing homogeneous-medium
approximation; this contract does not add a volumetric shadow march or change coefficient units.

## HDR producer and consumer contract

The baseline inspection established the pipeline below, before the conditional HDR changes
described under Shared handoff implementation status. Installed-client IL was exported
to `artifacts/PbrColor/water-hdr-engine-il.txt` using Mono.Cecil assembly reading,
without executing the client. `VintagestoryLib.dll` SHA256 is
`E08F22B493B92FEAF0AAEB79D22437EA0F7EFC38AA7F72A04A47F98BC0E40DF0`.
The installed shader sources are under `G:/Vintagestory/assets/game/shaders`; these identify the
inspected installation and are not new source-code dependencies.

| Boundary / owner | Verified baseline behaviour | Required HDR contract |
| --- | --- | --- |
| Opaque material capture, `GBufferManager` and surface patches | Primary attachment zero contains linear albedo for supported geometry, but already-resolved sky colour for sky pixels; engine primary allocation is RGBA8. | Preserve material/radiance distinction and metadata; provide floating-point scene storage and a compatible engine primary handoff. Format replacement alone is insufficient. |
| `WaterRefractionCapture.BeforeOverlay` / `PBRCompositeRenderer.RenderComposite(capture, isolatedLighting)` | Before local first-person projection, evaluates isolated direct plus standalone environment lighting; early return publishes without a display draw. No current-frame LumOn gather exists yet. | Retain engine order and coherent pre-overlay world colour/depth. Do not invent late coverage or reuse stale GI. Keep unattenuated scene-linear capture. |
| `DirectLightingRenderer` at Opaque 9 / LumOn | Direct and indirect signals are unexposed linear; composition selects one environment/GI policy. | Preserve units, publication checks and single application of light. |
| `WaterVolumeRenderer` at Opaque 10.5 | Additive RGBA32F optical-depth/source capture; `WasComposed` prevents repeated liquid bulk transport. | Retain boundary capture and consume matching-generation volume transport once; change publication acknowledgement to the completed HDR scene write, not an obsolete early display resolve. |
| `pbr_composite.fsh` / Opaque 11 | Captures unattenuated RGBA16F radiance and R32F depth before water/aerial transport; ordinary RGBA16F output carries transported geometry plus display-space sky. | Keep pre-transport refraction source separate from transported HDR background. Migrate sky inputs and remove the mixed colour convention. Preserve invalid sky/first-person receiver marking. |
| `pbr_display_resolve.fsh` / `PBRCompositeRenderer` | Converts geometry to SDR primary before OIT; sky bypasses conversion. | Remove this early conversion only with the complete compatible scene handoff; move display conversion after HDR composition and migrate the sky bypass at the same time. |
| `LiquidRenderer` / `pbr_liquid.fsh` / `includes/pbr_liquid.glsl` | OIT 0.369; local display conversion/dither, display-space confidence blending, then local sphere fog and preview alpha; six bucket outputs. | Emit scene-linear surface/background transport and compatible sphere fog; preserve preview alpha and glow semantics, no early display transform. |
| `PbrSurfaceShaderPatches` / `pbr_forward_surface.glsl` | Standard, instanced, animated and transparent terrain evaluate forward light but resolve to display RGB before OIT/AfterOIT. | Migrate every scene forward route sharing HDR targets, including standard-derived first-person items and entityanimated hands. Keep GUI/offscreen routes separate. |
| Engine `SystemRenderOITLayers.BeforeOIT/AfterOIT` | Rebuild installs RGB8 bucket revealage and RGBA16F three-layer accumulation, six outputs and multiplicative/additive blending; AfterOIT binds bucket sources. | Keep alpha/revealage as dimensionless data; all accumulation producers must supply compatible linear premultiplied RGB. Preserve engine attachment ownership and late bucket setup. |
| `ClientPlatformWindows.MergeTransparentRenderPass` / `transparentcompose.fsh` | Bucket unprojection, overall revealage, straight-alpha result blended over Primary using SRC_ALPHA / ONE_MINUS_SRC_ALPHA. | Merge over transported floating-point HDR background, retaining single background contribution. Floating-point accumulation already exists but currently accumulates display RGB. |
| `ClientMain.MainRenderLoop` / AfterOIT | Opaque -> OIT -> merge -> AfterOIT; late entities use Primary. | Keep AfterOIT scene draws inside the HDR interval; preserve their physical/overlay depth contracts. |
| Sky/solar patches and engine celestial/effect draws | `AtmosphereSkyPatches` and solar shader resolve before blending; stars/moon retain authored display colours. | Retain atmosphere radiance through scene blending; explicitly decode/calibrate legacy authored colours at their scene boundary and move display-only perception effects to the final boundary. |
| `RenderPostprocessingEffects` / `RenderFinalComposition` | Bloom extraction reads primary; final scene input is framebuffer index 10, not directly primary. `final.fsh` performs FXAA, bloom/SSAO/godray combinations and display grading/clamps. | Preserve HDR through all pre-display copies/postprocess intermediates, including the scene sent to final composition. Give bloom/SSAO/godrays explicit linear roles; place one tone-map/output conversion before display grading/UI. |

The common scene migration is owned by the **complete-scene HDR task in
[PBR.BaselineShading.todo](PBR.BaselineShading.todo)**, not duplicated inside the water renderer.
Before full-scene HDR activation, that owner must establish and verify:

1. Floating-point scene target handoff through opaque, merge, AfterOIT and pre-display processing,
   with coherent resize/reload and no read/write feedback. Retain engine mesh and framebuffer
   authority, using existing resource APIs and explicit restoration boundaries.
2. Linear output from all scene contributors sharing those targets, including fallback vanilla
   liquids if VGE ownership is unavailable. Installed OIT source families also include
   `particlesquad`, `particlesquad2d`, `clouds`, `cloudvolumetric`, `aurora` and `blockhighlights`,
   in addition to `chunkliquid`, `chunktransparent` and `entityanimated`; standard/instanced
   variants and custom shaders can acquire OIT includes through preprocessing. Opaque
   `particlescube` and authored celestial/effect draws also need explicit routing. Preserve alpha,
   depth, glow and render order; adapt existing effects rather than replacing their algorithms.
   Third-party scene contributors must honor the HDR target contract or provide an explicit
   color adapter. Their registrations must not switch VGE to a legacy scene pipeline.
3. One final scene display boundary after linear composition/effects, before display grading and
   UI. Update `findbright`, blur/intermediate formats, `final` colour operations and FXAA ordering
   consistently. UI/inventory must not pass through scene exposure. Native HDR output and new bloom
   quality algorithms are separate tasks; ordinary SDR output is sufficient here.
   Move SDR dithering to the final encoded output as specified by [PBR.OutputDithering.md](PBR.OutputDithering.md);
   do not retain per-draw dither in scene-linear radiance or apply an 8-bit amplitude to HDR storage.
4. Executed producer/consumer fixtures proving values above one survive the handoff, linear
   transparency matches numerical references, output conversion occurs once, and unsupported or
   failed mandatory setup reports an error without changing the scene color convention.

Mandatory HDR scene ownership is implemented by the binding owners described in
[PBR.SharedDisplay.md](PBR.SharedDisplay.md). Complete live-frame visual acceptance remains open.
This work does not block
water-specific implementation or verification. Shared water optics consume linear receiver data
and produce linear transport results; an output adapter follows the convention selected for
the owned HDR scene. Missing HDR dependencies do not switch to legacy display output. Controlled producer/consumer fixtures can verify the water HDR
branch without replacing the sky or activating HDR throughout the running scene. Sky replacement
and complete-scene activation remain separate parent tasks. Completing the water contract does not
claim their completion or HDR-monitor support. The approved authoritative-pipeline proposal constrains future
state ownership; its unimplemented APIs are not a prerequisite for these source/diagnostic checks.

### Shared handoff implementation status

The engine allocation adapter now selects RGBA16F for primary scene color and bloom blur
intermediates at the original allocation calls. Primary glow, OIT revealage and depth/SSAO data
retain their formats. Luma and god-ray targets were already RGBA16F. Engine framebuffer ownership,
completed-rebuild publication and retirement remain unchanged. The installed allocation sequence
is checked directly, including primary color-before-glow and both SSAO branches.

PBR forward/liquid output and the opaque handoff have explicit linear branches. In that branch,
`VgeSceneOutput` retains nonnegative unexposed radiance, and local sphere-fog colors are decoded
before interpolation. The sun branch also retains radiance; the VGE-owned sky selects the same prepared frame convention. Legacy OIT adapters decode
straight color before engine premultiplication; volumetric clouds decode the authored color
sample before integration. Postprocessing preserves linear RGB, uses display-derived alpha for
FXAA contrast, and selects one display conversion before final grading. The god-ray glare metric
uses display brightness without clipping its linear RGB. Mandatory HDR preparation is owned by SceneColorPipeline and supplied through existing scene
bindings, typed owned inputs and explicit engine postprocess call sites. The historical shader
branch receipts below do not themselves validate the newer runtime selection; see PBR.SharedDisplay.md.

Fresh subagent-run foundation validation passed 33/33 tests with no skips in
`artifacts/PbrColor/scene-color-foundation-tests.log`: installed allocation/Harmony checks,
headless float storage, engine rebuild regressions, actual opaque handoff in both modes,
shared output math and existing display regressions. This receipt predates the subsequent
legacy/postprocess adapters and does not validate their complete integration.

The subsequent fresh shader regression passed 76/76 with no skips in
`artifacts/PbrColor/scene-color-patches-regression.log`. Installed-source and GPU cases cover
legacy OIT decoding before premultiplication, cloud sample decoding before volume integration,
linear RGB with perceptual luma alpha, HDR god-ray suppression, and HDR scene/bloom/god-ray
composition resolved once before grading and final quantization. Existing final/sky/sun/forward
and liquid highlight regressions also pass. The first run exposed an AST mistake: a preprocessor
directive node did not contain the conditional's statement body. The patch now guards the actual
clamp statement, and the final receipt supersedes the failed run and an intermediate whitespace
assertion failure. These counts are separate receipts, not a summed distinct-test total.

| Shared scene-HDR work | Controlling requirements | Current evidence |
| --- | --- | --- |
| Engine scene storage | Parent common-handoff task; HDR contract items 1 and 4; authoritative state proposal's engine ownership boundary | `SceneColorAllocation`/`SceneColorAllocationHook`; original installed IL, Harmony installation, headless storage and rebuild checks. Native window allocation has not been executed by these fixtures. |
| Selectable producer output and fog | HDR contract item 2; `PBR.MaterialColorAndDisplay.md`; `PBR.Liquids.md` six-output and alpha contract | `VgeSceneOutput`, liquid frame/handoff bindings and producer shader patches; helper, handoff and retained legacy-output checks. Runtime scene output is mandatory HDR; complete live-frame acceptance remains open. |
| Legacy OIT and existing effects | HDR contract items 2 and 3; `PBR.SharedDisplay.md`; `PBR.OutputDithering.md` final-output requirement | `SceneColorLegacyPatches`, `SceneColorPostprocessPatches`, final patch; installed GLSL compilation and independently predicted numerical cases in the 76-test receipt. No new bloom algorithm or HDR monitor output. |
| Ordered opaque particles | HDR contract items 1, 2 and 4; parent common-handoff particle requirement; authoritative state proposal's engine ownership boundary | `SceneColorParticleTargets`, receiver separation shader, draw scope, capture hook and optional opaque handoff layer; focused receipts below. Conditional preparation and activation are wired; complete-frame execution remains outstanding. |

Further installed-source inspection found an additional coupling that activation must resolve:
`SystemRenderParticles.OnRenderFrame3D` enables alpha blending for cube particles at Opaque 0.6,
before direct lighting at 9 and composition at 11. Primary RGB still contains material color at
that point. Merely decoding the particle output or marking the blended pixel as completed radiance
would mix material data with radiance and lose the underlying receiver. Preserve underlying
material/depth and compose particle radiance without reordering or replaying engine submissions.
`SceneColorParticleTargets` now provides the isolated storage for that boundary: particle RGB
blends into owned RGBA16F with accumulated coverage, while depth and glow are borrowed from
primary. Its draw routing leaves underlying material and receiver metadata untouched. Matching
32-bit depth snapshots bracket the original particle invocation; the installed engine requests
`DepthComponent32`, and snapshots retain that exact format. The separation shader restores the
pre-particle material depth where particle visibility survives, or selects a later opaque
receiver and suppresses the particle layer where visibility changed. This comparison uses the
same depth representation without a geometric tolerance. Equal-depth replacement is not
distinguishable from depth alone; the intended engine boundary uses strict `Less` depth testing.
Third-party draws must honor the receiver-depth contract; a different convention needs an explicit adapter.

The opaque handoff shader has an explicitly enabled optional particle layer. It combines
`background * (1 - coverage) + premultipliedParticleRGB` on the linear route after opaque air/water
transport. Decoded legacy particle color already includes its original fog effects and must not
receive that transport again. The retained material background remains the refraction receiver;
this does not add refracted particle layers or ordered transparent transport. Capture resets and
primary attachment notifications invalidate publication; owned-image disposal preserves borrowed
engine images. Depth copies preserve independent framebuffer bindings and scissor state, and
storage clearing leaves indexed blend/write masks unchanged. Scissor preservation is owned by
`StateCache.PreserveScissorState`: the caller must establish known state through the cache before
capture. The copy applies its scissor-disabled pipeline and restores cached state without driver
queries or invalidation. Unknown state is rejected before copying rather than guessed.
The cache-owned scope and particle regressions pass 14/14 focused tests with no skips after a
fresh build (`artifacts/PbrColor/scene-color-particles-cache-scope.log`), including nested and
exceptional restoration, unknown-state rejection, and indexed-state preservation.

Fresh subagent-run validation passed 27/27 with no skips in
`artifacts/PbrColor/scene-color-particles-final.log`, including particle capture/separation,
installed cube-shader output, linear/legacy handoff and existing display regressions. Cases
cover HDR ordered blending, zero/partial/full coverage, later foreground replacement,
zero-alpha depth writers, empty draws/reuse, indexed state restoration, unsupported-depth
rejection, equal-size publication invalidation and borrowed-image survival after retirement.
Copied depth matches actual source readback exactly; nominal fragment depth uses a small
storage-quantization tolerance for the installed fixed-point format. A fresh build succeeded;
the shader catalog contains 170 stages / 408 variants and the final receipt verifies all
408 binaries current. The initial run caught use of the color-texture factory for depth
snapshots; those now use `DepthTexture`. Fixture corrections cover OpenTK enum availability,
engine lighting defaults, depth quantization and deferred disposal. This final receipt
supersedes those failed intermediate runs; it is not added to prior overlapping test counts.

`SceneColorParticleCaptureHook` now brackets the installed `Render(int, float)` call, which follows
standard alpha-blend setup in `OnRenderFrame3D`. Its void prefix never suppresses the original
submission. The draw scope establishes the known full-scene particle boundary through pipeline
descriptions, redirects only its color destination and restores independent framebuffer bindings
and the engine's standard output-zero blend factors. A finalizer withdraws failed captures and
restores routing even when the original method throws. Model, stage, framebuffer and current
shader checks exclude OIT/offscreen submissions; the cube shader's color-convention input is
selected at each recognized submission to prevent stale fallback/offscreen values.

The capture owner is registered at Opaque 8.5, ahead of direct lighting at 9. It resolves the
current particle layer and material depth before direct lighting, LumOn, water-volume integration
and composition consume that depth. Engine visibility depth remains unchanged for OIT visibility
and interface clipping. The opaque handoff receives the separated particle layer after material
transport. Before-stage invalidation withdraws the previous frame, and resize/world/disposal
boundaries retire owned snapshots. `PrepareFrame` preflights resources and the patched cube shader;
SceneColorPipeline prepares it after the Before 8.5 reset and rejects an incomplete handoff.
Complete live-frame visual acceptance remains outstanding.

Shader compatibility now uses the existing executable-capability registry:
`SceneColorConvention` is declared only after the relevant fragment patch succeeds and is
published only after successful engine compilation. It describes the selectable color branch,
not complete HDR readiness. Particle preparation requires that capability on the current
executable as well as its binding uniform; a similarly named uniform alone is insufficient.
The frame coordinator must additionally validate target formats, all contributors, perception
effects and prepared consumers before choosing HDR. This change does not enable a partial frame.
Focused verification passed 8/8 tests with zero skips in
`artifacts/PbrColor/scene-color-capability-tests.log`: executable identity/failure/reload semantics,
final-patch capability declaration and particle preparation in both SSAO modes. The publication
fixture explicitly supplies a capability on its test program; this is not an installed-engine
compilation or complete-frame HDR receipt. The particle rasterization, integration and publication
test files still contain runtime GLSL compilation/raw GL setup and need migration to the prescribed
precompiled test path. Their existing raster coverage has not been removed to hide that gap.

The runtime shader registry now explicitly registers `SceneColorParticleShaderProgram`; packaged
shader compilation alone did not make `GpuShaderPrograms.Get` return it. A registered-owner
lifecycle fixture exposed this omission before activation. The corrected callback/publication
integration passed 27/27 tests with no skips in
`artifacts/PbrColor/scene-color-particle-integration-final.log`. This includes actual registered
shader resolution and Before/world/resize/disable/disposal boundaries, not a live game frame.

Source review also identified required SSAO follow-through: the installed cube shader writes
engine normal/position outputs 2/3. When those engine attachments exist, particle capture now
stores both outputs in separate RGBA16F images. The underlying engine position remains intact
for deferred lighting, including physical first-person receivers. After ordinary opaque
composition, `SceneColorParticleSsaoShaderProgram` restores only pixels where particle depth
still owns visibility; later foreground and untouched pixels retain their existing metadata.
Zero-alpha particles retain their original depth/metadata behavior. The restoration destination
borrows only engine normal/position, avoiding feedback with sampled visibility depth. Both
registered particle programs must be ready before selecting the HDR frame. This additional
SSAO path passed a fresh 31/31 focused GPU/integration tests with zero skips in
`artifacts/PbrColor/scene-color-particle-ssao-tests.log`; shader generation covered 172 stages
and 410 variants. Cases cover selective restoration, zero-alpha depth writes, later foreground,
untouched pixels, borrowed-image lifetime, installed cube SSAO outputs and registered owner
lifecycle with SSAO enabled/disabled. The lifecycle fixture invokes restoration after receiver
resolve; the placement inside `PBRCompositeRenderer` was source-reviewed, not executed by that
fixture. This receipt overlaps the earlier suites and must not be added to their counts.
The SSAO component test was subsequently rewritten to follow `VanillaGraphicsExpanded.Tests/agents.md`:
precompiled production shaders, typed GPU abstractions, shared setup and separate behavioral/lifetime
cases. Its fresh receipt is 7/7 passed with zero skips in
`artifacts/PbrColor/scene-color-particle-ssao-standards.log`. These cases upload independently specified
particle submission results and execute the real depth copies, receiver resolve and metadata restore;
they do not rasterize particles or verify engine scheduling. The earlier synthetic rasterization case
is no longer part of this test file.

`PBRCompositeRenderer.PrepareFrame` now exposes composition preparation before scene submission:
it prepares separate scratch/primary destinations and both the pre-overlay fallback and selected
ordinary composition shader variants, plus display handoff. The normal draw reuses the same
preparation methods. Preparation does not draw, publish refraction or select HDR; lighting,
particles, compatible scene producers and final processing remain the frame owner's other gates.
Registered-owner preparation and related regressions passed 11/11 tests with zero skips in
`artifacts/PbrColor/scene-color-compositor-preflight-tests.log`. Preparation selects both lighting
modes without draws/publication or primary-color changes, preserves independent framebuffer
bindings, and rejects missing/invalid primary metadata. Subsequent ordinary composition, borrowed
target replacement, pre-overlay capture state and color handoff checks also pass. This proves the
compositor preparation boundary, not complete-frame execution.

Installed-engine inspection identifies `ShaderRegistry.shaderPrograms` as the shared array for
file and memory registrations, including third-party programs. `Before` callbacks precede the
engine shadow/opaque/OIT stages. The outer screen renderer calls scene rendering, postprocessing,
AfterPostProcessing, final composition, AfterFinalComposition and the final blit in that order.
The frame decision must therefore cover the engine postprocess/final methods explicitly rather
than infer their role from `CurrentRenderStage` alone. A registry inventory also does not establish
that arbitrary third-party Before callbacks are safe scene contributors.

The former SceneColorShaderInventory gate and its registry-classification tests were removed
when HDR became mandatory. Merely registering a shader cannot change the scene pipeline.
Required owned consumers still validate their resources and patched executable contracts,
and missing dependencies raise errors. UI/offscreen program reuse is classified by the actual
stage/target boundary; third-party scene producers are responsible for HDR-compatible output.

The installed registry always includes `woittest` and `colorgrade`. Field-use inspection identifies
`woittest` as an optional framebuffer-debug OIT producer, so its patch decodes straight authored
RGB at `drawPixel` entry before the unchanged alpha/weight calculation. The alternate `colorgrade`
endpoint resolves linear input before grading and dithers after output only when its color flag
is explicitly enabled. Neither pass receives an unconditional auxiliary exemption. Ordinary terrain
fragment patches now declare `SceneMaterialCapture` after successful source mutation.
Fresh build and 19/19 focused checks passed with zero skips in
`artifacts/PbrColor/scene-color-shader-inventory-final.log`: inventory classification, executable
capability lifetime and installed-source patch placement. These are CPU/source checks, not GPU
execution of the two new engine shader branches or complete-frame HDR evidence.
The particle path allocates a nominal 28 bytes per pixel of owned scratch storage, plus 16 bytes
when SSAO metadata is retained. It adds two depth copies, one receiver-separation draw and an
optional SSAO-restoration draw. These are format/pass counts, not measured GPU costs.

The earlier experimental SceneColorFrame coordinator and global shader-use hook were removed.
Those coordinator-specific receipts are historical. The parent full-scene task now supplies
mandatory SceneColorPipeline preparation, convention binding through the existing PbrDrawRouteHook, typed
owned sky/liquid/composite inputs, and shader activation wrappers only in the two engine
postprocess owners. This activates particle separation after its reset and reports errors
when mandatory preparation fails. There is no legacy scene fallback or registry-wide veto. Current ownership and validation: PBR.SharedDisplay.md.

After this deferral, a fresh build and 55/55 focused tests passed with zero skips in
`artifacts/PbrColor/water-deferred-coordinator-regressions.log`: the 40 water checks, lighting-mode
lifecycle, opaque handoff and final-output regressions. Water transport completion is unchanged.

The opaque handoff tests now use the existing fullscreen framework for pipeline and framebuffer
setup rather than issuing redundant raw GL state calls. All nine existing numerical cases passed
after that cleanup in `artifacts/PbrColor/scene-color-handoff-framework-tests.log`, with no skips.
`SceneColorOutputTests` has also been migrated to a typed offline-built fixture importing the
production color helper; its original HDR, negative-input, alpha and dither assertions are retained.
Fresh build and the migrated output plus nine handoff cases passed 10/10 with no skips in
`artifacts/PbrColor/scene-color-output-handoff-precompiled-tests.log`. The helper now executes
from production-importing SPIR-V through the shared GPU program/fullscreen owners, with no
runtime GLSL compilation or test-local shader lifetime management.

Vanilla engine shader fixtures use GLSL compilation, as explicitly clarified by the user.
`InstalledShaderFixture` reuses the existing installed-source patch helper and
`TerrainShaderTestFixture`, then imports reflected driver locations into the matching engine
shader wrapper. The executable has one owner. No separate exporter, numeric interface contract,
SPIR-V manifest or extra test-build project is required for vanilla shaders.
Reachability review of `particlesquad2d` found that its `oitPass <= 0` direct assignment bypasses
the OIT decoding wrapper, but the installed engine library constructs and renders that owner
only from `GuiCompositeMainMenuLeft`; its pass field retains zero. This is a GUI route, where
the frame binding must remain legacy, not a demonstrated scene-HDR omission. No production
decode was added to that branch. GUI isolation still requires a rendered check.
No complete-frame HDR or live appearance claim follows from these focused checks. The common
handoff still requires complete execution evidence, review and audit.

Decals register at AfterOIT 0.5 and do not share this early material-buffer problem. These are
installed IL observations, not live visual results. Complete execution coverage of readiness,
unknown-contributor rejection and failure/lifecycle handling, plus sky perception effects, remains required.

Current missing-scene coverage, scalar OIT revealage and multiple-liquid bucket averaging are
separate limitations. Moving RGB into HDR fixes colour-space composition, not ordered refraction
through arbitrary transparent layers. Preserve the documented single represented opaque receiver
contract until a separate transport extension is designed and verified.

The contract inventory uses the following controlling sources. These references describe the
requirements consulted, not new claims of runtime verification:

| Work item | Consulted document and requirement | Evidence / verification boundary |
| --- | --- | --- |
| Receiver baseline and sampling decisions | [PBR.BaselineShading.todo](PBR.BaselineShading.todo), refraction item; current Optics and traversal section above | Exact production traversal include, liquid consumer, snapshot/capture owners and focused diagnostic/production GPU fixtures. No live camera-defect reproduction claimed. |
| Medium and liquid compatibility | [PBR.WaterMedium.md](PBR.WaterMedium.md), Evaluation and ownership; [PBR.Liquids.md](PBR.Liquids.md), Captured OIT contract; [PBR.LiquidRenderer.Proposal.md](PBR.LiquidRenderer.Proposal.md), Compatibility and lifecycle | Preserve SI units, RGB transport, one submission owner, six bucket outputs and engine-owned meshes/targets. Shared receiver/transport evaluation and linear confidence composition are implemented and covered by the focused water receipt above. |
| Settings and resource decisions | Parent refraction enable/disable contract and current `ConfigModSystem`, `VgeConfig`, `WaterRefractionScene` / `WaterRefractionCapture` | Stable settings/defaults and publication policy specified above; new settings runtime tests belong to implementation, not this design receipt. |
| HDR dependency and colour boundaries | [PBR.MaterialColorAndDisplay.md](PBR.MaterialColorAndDisplay.md), Lighting and display; [PBR.EntityAndLateCoverage.md](PBR.EntityAndLateCoverage.md), Transparency and display boundary; [PBR.SharedDisplay.md](PBR.SharedDisplay.md), HDR ordering; [PBR.OutputDithering.md](PBR.OutputDithering.md), final-output migration | Source/installed IL inventory defines the full-scene activation contract; water-specific implementation is independently testable and does not claim complete-scene HDR acceptance. |
| Lighting and engine authority | [PBR.LightingModes.md](PBR.LightingModes.md), LumOn composition and lifecycle; [Rendering.AuthoritativePipelineState.todo](Rendering.AuthoritativePipelineState.todo) and its approved proposal, Scope boundaries / Engine integration | Retain mode-generation ownership, engine shader activation and framebuffer restoration. Diagnostic hooks compile away normally; no new submission architecture is introduced. |

## Validation and remaining acceptance

### Settings and integration

Fresh subagent builds passed **1303/1303 checks in Debug and 1303/1303 in optimized Release,
with zero failures or skips**, in 13.5629 and 11.1855 seconds respectively. Both builds verified
179 stages and 445 variants. Receipts: `artifacts/PbrColor/water-settings-integration-debug-final.log`
and `artifacts/PbrColor/water-settings-integration-release-final.log`. These overlapping suites
include earlier checks and are not additive.

The production liquid suite now has 93 cases. All four qualities at full/half resolution execute
HDR entry, underwater exit, total internal reflection and unavailable receiver metadata, with
disabled baselines. Legacy entry also covers every quality/resolution pair. Actual configuration
loading across disposal/reload covers all eight saved quality/resolution combinations, changing
the saved enable flag independently; missing leaves and invalid values retain their existing checks.
Shipped ConfigLib controls and event values cover all four choices. Live GUI save/reopen is unverified.

Representative compatibility checks exercise refraction exclusion for lava/full-alpha liquids,
patterned atlas flow, scene-linear fog and blocked/restored solar scattering. Accepted HDR water
also executes six-target bucket blending, overlapping layers and preview coverage, compared with
independent blend arithmetic and a CPU model of the installed compositor. This does not execute
the engine compositor or establish ordered refraction through transparent receivers. Existing
included suites exercise rendered wave triangulation, pre-overlay restoration, both upstream PBR
composition modes and resource transitions. The liquid shader has no separate LumOn mode axis.

The initial flow fixture omitted its packed UV footprint, so atlas padding selected an adjacent
texel. Correcting that authored metadata passed the unchanged color references and tolerances;
no production shader change was needed. Verification used isolated outputs without replacing the
running client's mod or shader cache. On 2026-10-04 the user initially reported no live checks,
then reported shallow shoreline-wall banding with half-resolution backgrounds and x2 sparkles
at both resolutions, and subsequently confirmed both issues fixed and verified in game.
The user subsequently accepted the result and explicitly requested completion on 2026-10-04.
Further visual-matrix runs, matched-scene GPU measurements, fallback statistics and matched
image comparisons are waived as completion prerequisites. GPU time and actual bandwidth
remain unmeasured; headless execution durations are not production GPU timings.

### Shallow receiver continuity

User-run observations on 2026-10-04 identified half-resolution shoreline-wall banding and
x2 shallow-depth sparkles at both background resolutions. Independent shallow-wall GPU
fixtures reproduced two different mechanisms. The original direct half-resolution filter
jumped by 0.1742 HDR red units at 20 cm and 0.1492 at 3 cm separation during subpixel motion;
full-resolution filtering stayed continuous. The axial layer gate discarded alternate columns
of a steep continuous wall. Independent neighboring-plane checks now retain that surface
without widening across separate depth plateaus.

A smooth curved shallow receiver reproduced x2 ray/UV switching at both resolutions:
adjacent HDR jumps reached 0.07305 at full size and 0.06777 at half size, while UV-only and
x4/x8 stayed below 0.00274. Removing the shallow displacement ramp and using the represented
receiver-plane proposal aligns the fallback with the refracted direction. Reduced-source
coordinate correction and actual triangle interpolation additionally remove the half-size
sample-center bias. The UV result remains approximate; the geometric hit proof, foreground
eligibility and 2/4/8 ray-position ceilings remain unchanged. At most two additional paired
source checks verify steep filter support; reduced UV candidate coverage can additionally
fetch at most three missing neighboring source corners, reusing the overlapping corners.

The corrected focused run passed **254/254 checks, zero skips**. At half size, the curved
x2 receiver's largest adjacent HDR jump fell to 0.001901 while retaining the same 22 UV
fallbacks and two method transitions; the planar 20 cm x2 case fell to 0.001489 with its
three transitions retained. Full-resolution curved x2 fell to 0.006369 with its eight
transitions retained. This verifies continuous fallback without suppressing transitions,
raising the ray budget or accepting unsupported geometric hits. Half-size UV-only curved
motion remained below 0.004901. Receipts: `artifacts/PbrColor/water-shallow-adjacent-build.log`
and `artifacts/PbrColor/water-shallow-adjacent-focused.log`. Earlier reproduction and failed
intermediate receipts remain separate; they are not passing validation.
The final broader Debug and optimized Release runs each passed **329/329 checks, zero skips**,
covering the water suites, liquid transparency, liquid shader program integration and fresh
adjacent-corner foreground/depth-layer rejection. Receipts:
`artifacts/PbrColor/water-shallow-final-debug.log`,
`artifacts/PbrColor/water-shallow-final-release-build.log` and
`artifacts/PbrColor/water-shallow-final-release.log`. The shader catalogs retain 179 stages
and 445 variants. These counts overlap the focused run; durations are not production GPU timings.

The regression fixture uses affine physical-position HDR radiance, 201 subpixel motions,
both resolutions and all four tiers. It checks continuity and geometry/radiance association;
a separate two-depth-column fixture rejects a fictitious four-corner plane. These numerical
checks do not establish live visual acceptance or GPU cost. Separately, on 2026-10-04 the user
confirmed that both reported artifacts are fixed and verified in game. This closes visual
retesting for these two defects. The user's subsequent acceptance and explicit closure
direction waive further matrix checks and GPU measurements as completion prerequisites.

### Bounded traversal and selection

Fresh subagent build and final current-source validation passed **273/273 checks, zero skips**,
in 8.4086 seconds (`artifacts/PbrColor/water-budget-final-current.log`). The offline catalog
verified 417 variants; the final incremental invocation compiled the corrected diagnostic stage
and verified the other 416 from cache. Assembly and SPIR-V output were isolated because the
running client holds normal mod/cache files; no game action or running-mod update was performed.

The executed coverage includes 18 bounded-selection cases, 72 baseline diagnostic cases
(12 authored scenes by three ray ceilings and two resolutions), 66 actual liquid cases,
25 UV cases, and existing filtering, reduction, capture/publication, lifecycle, transport,
settings and adjacent lighting/OIT regressions. Exhausted searches attain exactly 2/4/8
ray evaluations with a separately counted invalid UV seed; TIR performs neither kind of lookup.
Valid edge receivers, adjacent-foreground support, the 1 cm interface separation and 28 m paths
retain confidence one. The synthetic positive-view-Z case remains supported at x8; smaller
budgets can exhaust before its usable coverage. Fixed-world headings/pitches retain production
checks. The diagnostic now reports actual refracted direction, provenance, selected XYZ/radiance
and separate ray/UV counts rather than attributing approximate fallback to a geometric hit.

`WaterRefractionBudgetTests` compares four authored depth fields against an independent CPU
reference using 1024 ray intervals and exact visible-plane intersections. Each field has seven
camera positions per tier/resolution. On the flat full-size floor all three budgets return
seven geometric hits with maximum position error 0.000078 m. Half-size x8 also returns all seven;
x4 uses one UV fallback and x2 uses two, with maximum errors 0.06856/0.06914 m. Those approximate
receivers are checked against an independently projected UV reference plus the reduced source
footprint, rather than being accepted as exact ray intersections. The gap field is recovered at
all seven positions by x8; lower budgets have explicitly reported unavailable/UV selections.

A separate smooth sloped-plane sweep checks actual RAY/UV transitions and associated HDR color:

| Background | RAY / UV selections out of 61 | Method switches | Maximum motion error beyond analytic receiver motion | Maximum HDR RGB motion error |
| --- | --- | --- | --- | --- |
| Full | 48 / 13 | 3 | 0.04550 m | 0.00360 |
| Half | 39 / 22 | 27 | 0.06392 m | 0.00576 |

Both errors are bounded by the independently derived two-texel receiver footprint and its authored
color gradient. These are controlled spatial-sampling bounds, not a claim that every live transition
is invisible. The steeper plane retains all 61 validated UV selections at each resolution while
strictly preserving its plane and geometry/color association. This directly guards against the
reproduced axial-depth-only false hits. Cached-hit tests independently check a textured floor's
corrected-hit color and reject reuse outside an irregular half-source hull. The code uses actual
source triangles; neither a nominal cell nor a bounding rectangle proves that corner's coverage.

The two-pixel thin receiver is missed at one of the seven reference positions by every tier,
at full and half resolution. The selected valid approximate background can consequently differ
by about 6.03 m from the dense reference's thin receiver. The receipt reports misses and fallback
counts rather than hiding this quality limitation. Separate depth-discontinuity checks reject
false geometric crossings; no unrelated layers become a fictitious intermediate ray hit.
Fewer evaluations and valid fallback do not establish closest-hit completeness or improved live
image quality. Headless results do not establish live appearance, camera-crossing acceptance or
GPU timing; those remain in the integration/acceptance list. Second source/document review and
independent completion audit found no remaining scoped traversal requirements.

### Receiver baseline diagnostics

`WaterRefractionDiagnosticTests` executes a precompiled fixture importing the actual
`includes/liquids/refraction.glsl` with opt-in event/sample macros through the existing headless
shader framework. Ordinary shader variants
expand these hooks to nothing. The historical baseline below predates bilateral filtering and bounded
traversal; its budgets, rejection margins and confidence fades describe that baseline only.
The original three diagnostic outputs recorded hit/reason/evaluation count/confidence, last receiver
UV and positive view depth, and submerged length/refracted Z/fallback mixing weight/returned Z.
On rejection, the sample is the last attempted lookup, not a selected valid receiver; a projection
failure before any lookup leaves it zero. The fallback weight is `1-confidence` (one on failure),
not the engine's final revealage or a measurement of visible distortion strength.

The following controlled 128-pixel GPU cases are reproduced in
`artifacts/PbrColor/water-refraction-contract-final.log`:

| Case | Observed result | Implication for subsequent work |
| --- | --- | --- |
| Flat / tilted normal | Accepted in 23 receiver evaluations, confidence 1; UV x changes from 0.503125 to 0.571335. | Established successful baseline; retain supported geometric distortion. |
| Receiver 1 cm behind a flat interface | Interface-plane rejection on the first evaluation (reason 4). | The fixed 2 cm exclusion rejects this physically behind-water sample; shallow-path eligibility needs improvement. |
| One adjacent foreground texel | Entire trace rejected on first evaluation (reason 5), despite valid central depth. | Bilateral support can retain valid taps, provided foreground colour never leaks into them. |
| Edge sweep at columns 1, 3, 6, 12, 32, 64 | Column 1 fails projection before a receiver read. Columns 3 and 6 are accepted but confidence is 0.058087 and 0.409624; columns 12 onward have confidence 1. | Distinguishes hard margin rejection from avoidable attenuation of a valid receiver. At column 3 the receiver UV x is already 0.138667: the interface-edge fade suppresses otherwise supported distortion. |
| Receiver depth 30 m, interface depth 2 m | Accepted length 28.009766 m, 36 evaluations, confidence 0.498169. | Range fade halves a valid contribution without a missing receiver. |
| Positive view-Z ray | Accepted with direction Z +0.115695, 13 evaluations, confidence 1; UV (0.804779, 0.5), sampled positive depth 1.822360 m, returned ray Z -1.850749 m. | Confirms the helper can accept a ray moving toward the camera; this uses a synthetic wide projection and does not establish ordinary in-game camera coverage. |
| Depth step from 20 m to 3 m | Residual rejection (reason 7), 13 evaluations, approximately 0.434958 m mismatch. | Necessary discontinuity guard; broad threshold relaxation would admit unsupported crossings. |
| Grazing normal | Projection rejection (reason 1) after 12 receiver evaluations. | Demonstrated screen-coverage limitation; no hidden receiver is asserted. |
| Sky / invalid receiver metadata | Rejection (reason 2) on first evaluation. | Keep invalid coverage distinct from physical receivers. |
| Receiver beyond traversal range | Exhaustion (reason 9) after 32 evaluations. | No hit is proven; a bounded approximate fallback must retain its own validity rules. |

The diagnostic codes also distinguish homogeneous reconstruction failure (3), total internal
reflection (6) and nonfinite radiance (8); their mere instrumentation is not proof that every
branch was exercised by these new cases. Existing production-SPIR-V `WaterRefractionTests` were
rerun alongside them, including flat/tilted surfaces, underwater exit/TIR, invalid/nonfinite depth,
bottom-edge composition and fixed-world pitches 15/45/75 degrees at headings 0/90 degrees.
Those tests predict the floor texel and composition independently; they do not launch the client.

The fresh focused Debug build/test invocation passed **44/44**, zero skips: 12 diagnostic cases,
20 production refraction cases, nine overlay-composition cases, two lifecycle cases and one
capture-state case. The final fresh build and expanded water/liquid/composite regression passed
**114/114**, zero failures/skips, in 5.09 seconds. These counts overlap and must not be added.
Receipts: `artifacts/PbrColor/water-refraction-contract-final.log` and
`artifacts/PbrColor/water-refraction-contract-regression.log`. The initial shader build compiled
and verified 168 stages / 406 variants; the final incremental receipt verified all 406 binaries
current. Existing compiler/analyzer warnings remain. An initial diagnostic assertion incorrectly
treated receiver depth 30 m as a 30 m water path; the actual interface starts at 2 m. Correcting its
expected 28 m path gives the independently expected half-strength range fade. No production
threshold was changed to obtain a pass. Earlier failed diagnostic logs are superseded by these
fresh receipts, not counted as successful validation. Sandbox restore/compiler-path failures
were resolved through approved escalation before the successful fresh build/test runs.

Run through a build/test subagent with the existing NuGet package location:

```powershell
$env:NUGET_PACKAGES = 'C:/Users/Sisco/.nuget/packages'
dotnet test VanillaGraphicsExpanded.Tests/VanillaGraphicsExpanded.Tests.csproj -c Debug --no-restore --filter 'FullyQualifiedName~WaterRefraction|FullyQualifiedName~WaterMedium|FullyQualifiedName~WaterBoundary|FullyQualifiedName~WaterVolume|FullyQualifiedName~Liquid|FullyQualifiedName~PbrComposite' --logger 'console;verbosity=detailed'
```

Source/contract review separates these executed baseline observations from the planned fixes.
Second review and an independent completion audit found the baseline investigation and design
contract fully supported by the source, linked documents and executed receipts.
These historical baseline receipts do not establish settings UI, reduced-step algorithms, bilateral filtering, downsampling or whole-scene HDR activation
implemented by these diagnostics. Live appearance and production GPU cost remain unmeasured.

### Earlier implementation receipts

Constructor-configured blitting and attachment notifications passed a fresh build and 236/236 regressions. After tightening ordinary primary-load notification suppression, a fresh final build and 34/34 affected tests passed. Functional checks prove that silent external attachment changes are not queried during ordinary copies, wrapper refresh updates the retained copy, repeated setup/load/unload emits no refresh, and an equal-size window-rebuild callback notifies once. Resize completion/no-op suppression, retirement rejection and subscription removal are also covered. Receipts: `artifacts/PbrColor/event-blitter-build.log`, `artifacts/PbrColor/event-blitter-regressions.log` and matching TRX, plus `artifacts/PbrColor/event-blitter-final-refresh.log` and matching TRX. Counts overlap; working-tree whitespace checks and source review passed.

Blit extraction into `GpuFramebufferBlitter` passed a fresh build and 232/232 regressions with zero skips. Coverage includes managed/raw target overloads, original routing and viewport preservation, unchanged-attachment reuse, texture replacement, depth/combined copies, renderbuffers, reset/disposal nonownership, and the production LumOn surface-albedo capture/recreation path. Independent source review confirmed caller-owned lifetime and reset before target disposal. Receipts: `artifacts/PbrColor/framebuffer-blitter-build.log`, `artifacts/PbrColor/framebuffer-blitter-regressions.log` and matching TRX. Counts overlap earlier suites.

Scratch-FBO conversion passed a fresh build and 223/223 regressions with zero skips. Tests verify binding-only exceptional restoration, copied color/depth values, unchanged original MRT routing and viewport, refreshed replacement attachments, borrowed texture/renderbuffer lifetime, and capture integration. Receipts: `artifacts/PbrColor/scratch-blit-regressions.log` and matching TRX. The working-tree whitespace check passed. These tests include the earlier suites; live rendering and GPU cost remain unmeasured.

Before scratch-FBO conversion, the framebuffer scope and API reuse changes passed a fresh build and 189/189 focused regressions with zero skips. Coverage includes actual stale-cache independent read/draw bindings, complete MRT routing and read selectors, viewport restoration on exceptional exits, borrowed framebuffer dimensions/ownership, engine and foreign program restoration through `UseScope`, bidirectional blit routing, real pre-overlay capture draws, resize invalidation, world-leave deletion and subsequent recreation. Receipts: `artifacts/PbrColor/render-boundary-regressions.log` and matching TRX. The working-tree whitespace check passed; live rendering and GPU cost remain unmeasured.

Rendering reuses `GpuFramebuffer.BindWithViewport` and `Clear`; `Wrap` accepts optional viewport dimensions without taking ownership. The additional framebuffer-state scope has been removed. Binding restoration uses the existing `FramebufferScope`, with combined scopes preserving distinct read and draw targets. The primary resolve attaches the engine color texture once to a reusable non-owning target, leaving engine MRT routing untouched. Ordinary frames only bind it; source framebuffer/attachment replacement, resize, and world/disposal boundaries retire the target. The additional framebuffer rendering and shader suspension helpers have been removed. When no engine shader owner is registered, `GpuProgram.UseScope` reads the previous program through `GlStateCache.GetCurrentProgram()` under the existing cache contract. Synchronization with untracked engine program changes remains deferred. Direct lighting and composition retain pipeline descriptions and scoped restoration. The earlier abstraction regression receipt (`artifacts/PbrColor/render-abstraction-regressions.log`, 179/179) predates this API cleanup; fresh validation is recorded below. Test counts overlap and are not additive.

The pre-overlay correction preserves engine submission order and passed a fresh build plus 110/110 related regressions with zero skips. Eight production-composite variants cover available/missing capture, marked/unmarked pixels and both lighting modes; only the refraction pair is restored and visible first-person lighting remains unchanged. The runtime integration fixture executes two real shader draws, checks publication and unchanged primary color/depth, restores an active engine shader owner and independent read/draw framebuffer bindings despite a stale managed cache, and verifies viewport, blending, depth mask, frame invalidation and disable retirement. An independent capture/installed first-person shader regression run passed 84/84; these suites overlap. Evidence: `artifacts/PbrColor/held-refraction-engine-il.txt`, `artifacts/PbrColor/held-refraction-regressions.log` and matching TRX, and `artifacts/PbrColor/water-refraction-capture-regressions.log` and matching TRX. Independent source review and `git diff --check` passed.

The combined run exposed an older numerical fixture's uninitialized aerial texture: seven color comparisons passed alone but failed after capture allocations. Explicit zero initialization changed the exact capture/refraction pair to 21/21 passing and the final combined run to 110/110. Expected optics and tolerances were unchanged. This was a reproduced headless fixture issue; it does not establish live visual acceptance.

Focused subagent validation passed 109/109 liquid, water-medium/boundary, refraction, composite and configuration regression tests with no skips. A subsequent configuration-persistence run passed 23/23 after adding actual saved-option loader restoration assertions. These suites overlap; their counts must not be summed as distinct tests. The production shader catalog verified 168 stages / 406 variants; the final incremental build recompiled 13 variants after the final parameter comments. Build succeeded with existing compiler/analyzer warnings and no errors. Independent source review found no confirmed new implementation defect; `git diff --check` passed.

Tests exercise opaque MRT publication in both shader lighting modes, flat/tilted entry, underwater exit and total internal reflection, foreground/adjacent shoreline/sky/nonfinite/offscreen/exhaustion fallback, an independently predicted Snell-gradient texel and display value, persisted-option restoration and repeated snapshot toggle/resize/attachment replacement. Lifecycle tests vary the lighting configuration but do not execute live engine mode switching. Receipts: `artifacts/PbrColor/water-refraction-regression.log`, `artifacts/PbrColor/water-refraction-tests/water-refraction-regression.trx` and `artifacts/PbrColor/water-refraction-config.log`.

During fixture development, `ReadPixelsRegion` unbound the render target; a later draw initially wrote elsewhere and assertions read stale pixels. Rebinding before each draw resolved the reproduced fixture failure. Temporary diagnostic shader edits were removed before the passing regression run.

The screen-transition correction adds production GPU checks for continuous normalized bottom-row revealage, camera/world transform consistency, and a fixed horizontal water/floor scene viewed at pitches of 15, 45 and 75 degrees and headings of 0 and 90 degrees. The fixed scene's refracted floor texel, Fresnel response and premultiplied display contribution are predicted independently on the CPU. These cases use 128-pixel targets; a 16-pixel target's steep floor depth quantization exceeded the conservative crossing tolerance at 15 degrees. This remains a nearest-depth screen-space approximation. Positive view-Z accepted rays are not covered by these tests, and no live camera-facing defect reproduction is claimed. The shader build succeeded; final focused validation passed 27/27 and related water/liquid/display regressions passed 98/98, with no skips. Receipts: `artifacts/PbrColor/water-refraction-transitions-fixed-world.log` and `artifacts/PbrColor/water-refraction-transitions-regressions.log`, with matching TRX files.

The user verified the shallow shoreline-wall banding and x2 sparkle corrections in game on 2026-10-04, then accepted the result and explicitly requested completion. Further visual-matrix runs and production GPU measurements are waived as completion prerequisites. No game was launched by the agent. Snapshot output bandwidth, traversal cost and full production GPU frame/pass time remain unmeasured; no performance improvement is claimed.
