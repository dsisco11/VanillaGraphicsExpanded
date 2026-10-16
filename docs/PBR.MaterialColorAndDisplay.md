# Material color and display boundaries

Opaque terrain and topsoil now publish unlit, linear material RGB before the direct and LumOn
passes. The scene-linear composite remains RGBA16F. A separate handoff writes the engine's
primary target before OIT and final grading. Primary and bloom blur storage use RGBA16F.
Runtime scene rendering preserves linear radiance through this handoff; owned final composition
performs display conversion. Missing mandatory resources report rendering errors. The runtime
ownership and UI/offscreen isolation contract is [PBR.SharedDisplay.md](PBR.SharedDisplay.md).

Animated entities, standard/instanced meshes, transparent terrain and late held items are covered
by the subsequent [entity and late-surface integration](PBR.EntityAndLateCoverage.md). The
terrain-specific capture and display contracts described here continue to apply.

## Material capture

`PbrTerrainColorPatches` clears only the RGB component of the vertex `rgba` lighting varying.
It retains alpha, vertex deformation, fog metadata, glow and point-light side outputs. The
fragment shader still performs the engine's atlas selection, climate/season/frost color mapping,
topsoil layer blending and material effects. Capture occurs before the `murkiness` declaration
that begins the forward fog/reflection section in both installed terrain shaders.

The captured RGB is decoded with the exact sRGB transfer function used by the CPU's
representative-albedo LUT. Original alpha tests/discards and alpha output remain intact.
After the forward section, RGB is replaced with that unlit linear value. Consequently vanilla
vertex light, reflections and underwater color attenuation no longer become BRDF albedo.
Normal-view output is preserved. Roughness, metallic, emissive, normal and height textures are
data and receive no sRGB conversion. Direct shading remains responsible for sun visibility,
view-space point lights and emission; these formulas have not changed.

Color mapping and layered blending retain the engine's authored color-space behavior; the
result is decoded once for lighting. This does not redefine the engine's texture filtering or
implement linear-space atlas filtering. Primary attachment zero temporarily carries material
color; the scene-color allocation adapter now supplies RGBA16F storage for it.

The capture patch checks its engine-source boundary and reports unsupported layouts instead of
silently assuming the capture succeeded. It adds no per-pixel texture samples. Its preservation
tests use the installed terrain sources, rather than an invented replacement main function.
Restoration uses the AST's `main.InnerEnd("body")` boundary and does not depend on the final
output statement or an `outGlow` assignment. TinyAst 0.11.3 replaces the 0.11.2 dependency whose
binder duplicated block delimiters when recognizing directives inside a body. The former
final-statement workaround and test normalization for those duplicate delimiters are removed.

## Lighting and display

Source evaluation now uses calibrated atmosphere lux/nits, shared block E/pi, explicitly adapted
engine point candela and a 32-nit emission reference. Shared direct diffuse response divides by pi
for sun and punctual sources alike. This source conversion preserves material capture, Fresnel/
metallic partition and shadow ownership. Cache/probe and atmosphere lighting now use fixed-scale
persistent storage, decoded at their declared consumers. The connected transient pipeline uses
one frozen frame scale P for lighting storage and decodes it before physical evaluation or display.
Camera/configuration compatibility converts legacy saved multiplier stops to explicit camera EV100
fields without changing effective gain, using PBR.CameraExposure.md's identity-based migration
and defaults without a version marker. Source/material fields retain their existing
data and receive the fixed calibrated shader interpretation; no engine asset rewrite is required.
Remaining qualification is tracked in
`PBR.PhotometricLighting.todo`; this intermediate pipeline is not a release candidate.

Direct diffuse/specular, emission and gathered indirect lighting remain unexposed linear
signals, stored with P in transient targets. Surface Cache and probe images store their physical quantities times 1/16; material
coefficients remain data. `pbr_composite` adds lighting contributions and converts authored fog
through the 32-nit compatibility reference before blending. It does not clamp radiance to one or
apply a display transform.

The material atlas owner admits finite binary16 parameters before replacing or uploading an
image. It retains the maximum neutral emission decoded from admitted coefficients,
including binary16 rounding and accepted pending uploads, through the matching atlas lifetime.
Cached restores and asynchronous artifacts use that same page admission. Queued transfers retain
the concrete texture owner and discard retired destinations before uploading; reused numeric
texture names cannot redirect an old job into a replacement image. A lower replacement cannot
erase the bound of compatible cached lighting. Invalid replacement data leaves the previous
texture and bound available; page retirement removes both. The compact Surface Cache material
LUT retains its own decoded emission bound and C-encoded emission payload.

`pbr_display_resolve` copies linear radiance on an HDR frame, including sky, and composites
the captured premultiplied particle layer over the transported background once. On the retained
legacy route it performs the opaque display boundary instead. The retained legacy conversion
uses the following baseline policy:

1. Fixed unit exposure, common to LumOn-enabled and standalone composition (see [PBR.LightingModes.md](PBR.LightingModes.md)).
2. Nonnegative RGB-ratio-preserving mapping: `c / (1 + max(c.r, c.g, c.b))`.
3. Exact linear-to-sRGB encoding.

This is an explicit baseline display policy, not adaptive exposure or a calibrated filmic
transform. It preserves highlight ordering above one instead of clipping all highlights during
a blit. All channels share a shoulder denominator to retain linear RGB ratios.
See [PBR.SharedDisplay.md](PBR.SharedDisplay.md) for the sky/sun/surface contract.

On the legacy route, sky pixels already used the shared display helper and bypass this resolve.
Both composition and resolve use `lumonIsSky`, including its exact depth threshold. On HDR
frames the owned final shader adds scene and effects, applies camera exposure and samples
the scene-linear grading/filmic display lookup described in [PBR.Postprocessing.md](PBR.Postprocessing.md).
The legacy shoulder is not applied again to that HDR scene. The renderer uses a distinct
source texture and disables depth testing/writes for the fullscreen draws, restoring fixed
function state afterwards. The new GPU scope is `PBR.DisplayResolve`.

## Remaining ownership

Solar surface specular reflection uses `includes/pbr_finite_disk_brdf.glsl` in deferred, forward and
liquid surface paths. The finite emitter uses the existing visible disk angular radius. A
representative direction within that extent determines half-vector alignment, while a
view-dependent energy factor compensates the larger reflection footprint. Material roughness,
center-direction diffuse and masking, solar irradiance and shadow ownership retain their
existing meaning. Punctual lights continue to use the directional Cook-Torrance response.
Deferred and forward sunlight call `addFiniteDiskDirectLight` explicitly; `addDirectLight` retains
the punctual response. Angular radius describes source extent and does not select the light type.
This finite-source approximation does not establish completed photometric calibration or
change the disk's display policy, water volume scattering or transmission models.
Focused installed/GPU validation passes 142 tests, including independent cone-boundary and
normal-incidence references, grazing/minimum-roughness precision and existing surface/shadow/liquid
contracts. Production Release compilation passes. Live appearance and performance remain unmeasured.

This document owns opaque material capture and its color boundary. Entity, forward-surface
and late-item coverage is documented in [PBR.EntityAndLateCoverage.md](PBR.EntityAndLateCoverage.md).
Runtime scene-HDR ownership is implemented as specified in `PBR.SharedDisplay.md`; owned sky
visual acceptance is recorded in `PBR.BaselineShading.todo`. Water transport acceptance is tracked in
`PBR.WaterRefraction.todo`. Engine-authored fog and effect colors are decoded for
linear composition but do not constitute calibrated physical radiance.

## Shared HDR handoff work

`PBR/SceneColor/SceneColorAllocation` selects RGBA16F for primary color and the existing bloom
blur targets at the engine's allocation calls. Glow, revealage, depth and SSAO data keep their
formats. Engine-owned texture names, framebuffer publication and retirement are unchanged.
Luma and god-ray storage were already RGBA16F in the inspected installation. This removes an
intermediate storage limit; it does not by itself change the scene's color convention.

`VgeSceneOutput`, the liquid frame's `SceneLinear` field and the opaque handoff shader's
`SceneLinear` input support linear radiance without per-draw display conversion. Runtime binding
owners select HDR for scene contributors and retain UI/offscreen isolation. Surface programs
use `vge_pbrRoute`; authored engine effects use the separate scene-color binding described in
`PBR.SharedDisplay.md`. The experimental coordinator and global shader-use hook were removed.
Sky-specific HDR patches and vanilla sky G-buffer injection are removed. The VGE-owned
`pbr_sky` program samples the existing atmospheric lookup, retains spatial sky effects and
always emits scene-linear radiance at runtime. It writes color/glow only; background metadata
comes from framebuffer clearing. Lifecycle and user-run visual acceptance are
documented in [PBR.Atmosphere.Publication.md](PBR.Atmosphere.Publication.md).
The historical producer inventory and current HDR contract are in
[PBR.WaterRefraction.md](PBR.WaterRefraction.md#hdr-producer-and-consumer-contract).

## Microfacet energy model

The default surface model uses height-correlated Smith GGX visibility, with material
roughness in [0.04, 1] and Schlick Fresnel with unit grazing reflectance. Punctual and
finite-disk responses use the same masking model. Finite-emitter footprint normalization
remains a separate approximation; the energy lookup does not resolve its previously
observed discrepancy against numerical disk integration.

The immutable directional-albedo lookup stores E and Ef for that selected single-scattering
model. Receivers evaluate `W = 1 + F0 * (1 - E) / E` and
`R = W * (E * F0 + Ef * (1 - F0))`. Direct specular is multiplied by W; diffuse reserves
the complementary fraction 1-R. This is an energy-compensation approximation, rather
than explicit microfacet bounce tracing. Its view-dependent multiplier does not preserve
exact BRDF reciprocity. Metals have no diffuse term; F0 and reflected fractions remain
bounded to [0, 1]. Invalid lookup inputs fail closed.

The source asset is a 128-by-128 Float32 E/Ef table, generated offline with 16,384
visible-normal samples per node and embedded in the assembly. Runtime receivers sample
an RG16F 2D texture with hardware bilinear filtering, clamp-to-edge and no mipmaps.
Coordinates retain `roughness = 0.04 + 0.96*u*u`, `NdotV = v^4`; endpoint coordinates
map to texel centers, independently of resolution. Native and owned programs share
texture unit 32 through the existing texture/sampler infrastructure. No new fullscreen
pass or per-frame CPU BRDF integration is introduced.
Normal Debug and Release builds run `tools/MicrofacetEnergy` only when
`PBR/Materials/Energy/ggx-energy.bin` is missing, before resource discovery. Existing
assets are preserved regardless of timestamps or generator changes; deliberate regeneration
requires removing the asset or running the tool explicitly. Generation first writes an
intermediate file and publishes it after successful completion. Design-time builds skip it.
The generator precomputes the shared disk sequence and fixed hemisphere terms once,
then integrates independent directions using portable `Vector<double>` lanes alongside
the existing material-row parallelism. Unsupported CPUs and leftover sample directions
use the scalar path. Accumulation stays double precision; the serialized Float32 table
is byte-identical to the qualified source in tested four-lane, two-lane, scalar and Debug runs.
Three interleaved fresh-process Release runs per variant on the development machine gave
median wall times of 1.286 seconds originally, 0.769 seconds with precomputation alone,
and 0.304 seconds with SIMD (including startup/write, excluding compilation). These are
generator timings, not game performance. Numerical corner/odd-count checks cover 180
cases across the three execution modes. Receipts: `artifacts/MicrofacetEnergy/GeneratorOptimization`.

The general `ShadingQuality` setting is independent of sky and water quality. Levels
0/1/2/3 select 16/32/64/128 square texels, using 1/4/16/64 KiB of GPU texture payload.
The default is 3; unsupported values normalize to 3. This setting currently controls
only material-energy lookup density and remains available to other shading features.
Lower levels resample the validated source on the same warped axes; all levels round
nodes to half precision before upload. Changes publish a complete replacement on the
next material lookup binding; unchanged settings reuse the current texture. CPU diffuse
cache material calculations retain the immutable full-precision source, so quality
changes do not recalibrate cached lighting or require material recapture.

Independent endpoint and off-grid numerical checks of the selected artifact found maximum
relative E error 0.03246% and maximum absolute Ef error 0.000281. These are sampled
qualification results, not an exhaustive error proof. Detailed inputs and convergence
receipts are in `artifacts/MicrofacetEnergy/SelectedLookupAndStorage.md`.

Across the same 360 independent high-sample reference cases, ideal filtering of the
half-rounded texture gives the following sampled errors. The original 0.5% relative-E
and 0.0005 absolute-Ef targets remain satisfied at the default level. Reduced levels
deliberately relax accuracy, rather than meeting the default qualification targets.

| Shading quality | Size | Texture payload | Maximum relative E error | Maximum absolute Ef error |
|---|---|---|---|---|
| 0 | 16x16 | 1 KiB | 2.271% | 0.015148 |
| 1 | 32x32 | 4 KiB | 0.464% | 0.003395 |
| 2 | 64x64 | 16 KiB | 0.165% | 0.000969 |
| 3 (default) | 128x128 | 64 KiB | 0.052% | 0.000354 |

These CPU results isolate density and half-storage effects; hardware filtering precision
is qualified separately. Receipt: `artifacts/MicrofacetEnergy/RG16FQualityQualification.md`.
A separate 193x193 offset-grid comparison against the full-resolution source exposes
larger reduced-tier errors than the integration corpus: the 32x32 tier reaches 0.660%
relative E and 0.005306 absolute Ef before hardware output rounding. Both corpora are
retained in the receipt; none of these sampled maxima is a continuous-domain guarantee.
An actual 360-point RG16F lookup on an RTX 4090 measured maximum relative E error
0.07937% and absolute Ef error 0.0005511 against the independent integration corpus.
The latter exceeds the ideal lookup's 0.0005 target: hardware filtering can round its
result to binary16 in addition to stored-node rounding. GPU checks retain the ideal
lookup targets and add a separately derived per-component allowance for output half-ULP,
local filter-weight precision and Float32 arithmetic. They do not claim total hardware
Ef error is below 0.0005 or fit a blanket tolerance to the observed image. Default
maximum sampled W error is 0.07931% and R error is 0.0005511; these are sampled checks,
not universal continuous-domain error bounds. Data: `artifacts/MicrofacetEnergy/hardware-texture-quality.json`.

The shared environment response interprets its E/pi input as directionless incident
radiance: specular uses input*R and diffuse uses input*(1-R)*(1-metallic)*baseColor.
It approximates isotropic lighting, not a prefiltered directional reflection environment.
Diffuse-only cache material adapters reserve the projected-view-average specular fraction;
they do not bake W into cached outgoing radiance. The receiver evaluates its own material
response once after incident lighting is gathered.

The approved storage policy uses camera-driven pre-exposure and format-specific scene
containment rather than preserving every hypothetical specular peak. Source-envelope
estimates no longer reduce P or certify an unclipped image; separate faint pixels retain
their camera-selected encoding scale. Finite outliers can clip at scene boundaries.
Persistent incident/diffuse formats and invalid cache/history rejection are unchanged.
See [the approved highlight policy](PBR.MicrofacetStorage.Evaluation.md#approved-highlight-policy).
GPU correctness checks, measured cost, and user-run appearance acceptance remain separate
requirements. The user accepted the implementation's in-game appearance on 2026-10-15;
the subsequent RG16F/quality refactor is qualified by the numerical and lifecycle checks below.
The normal debug launcher builds and loads this implementation by default, with no
opt-in flag or separate test package.

Before the texture refactor, an opt-in SSBO microbenchmark on an RTX 4090 at 256x256 found one evaluation at
the timer noise floor (about 0.002 ms). A synthetic loop with 100 varying-material
evaluations measured 0.0348 ms without the lookup and 0.0696 ms with compensation.
This deliberately prevents material lookup hoisting and is not the production per-frame
cost or a bandwidth measurement. Receipt: `artifacts/MicrofacetEnergy/gpu-cost.json`.
Those historical SSBO timings and four-node Float32 request estimates do not describe
the RG16F texture implementation. Its shader issues one filtered lookup per evaluation;
physical external-memory traffic and live frame cost remain unmeasured.
For the RG16F implementation, the same opt-in 256x256 RTX 4090 workload measured
0.002048 ms without compensation and 0.002560 ms with the texture for one evaluation
(near the timer noise floor). At 100 varying-material evaluations it measured 0.034816 ms
and 0.058368 ms respectively. The texture payload is 64 KiB at the default quality.
These are warm synthetic shader timings, not a matched live frame or a physical-memory
bandwidth measurement. Receipt: `artifacts/MicrofacetEnergy/gpu-texture-cost.json`.
Counting four contributing RG16F texels gives a nominal 16-byte request footprint per
lookup before cache reuse/coalescing. The 100-evaluation workload therefore represents
100 MiB of source-level requests, or about 1.80 TB/s effective request throughput for
the complete timed shader. This includes BRDF arithmetic and hardware filtering and
must not be interpreted as measured external-memory traffic or isolated memory latency.
The core run passed 236 tests and a subsequent focused run passed 60, with overlap;
do not add these counts as unique coverage. The nine initially blocked registered-consumer
cases now use the actual camera-owned freeze. A 52-test runtime batch passes after restoring
the local-cache handoff's missing C decode, including independent directional inputs and
full-image material assertions for both gather conventions. Source calibration and packed
precision are explicit in the fixture references. The user subsequently accepted appearance;
live performance remains unmeasured.
The final focused Release selection passes 390/390 with zero skipped, and both shader
catalogs pass (450 production and 528 fixture variants). The archived Release ZIP's DLL,
embedded lookup and generated shader identities match that tested build; build receipts are in
`artifacts/MicrofacetEnergy/CandidateValidation.md`.
The texture/quality follow-up validates 448 distinct Debug cases: 446 passed in
`texture-pipeline-final.trx`; two older native-hook fixtures required the existing native
camera-world adapter and then passed in `texture-native-hook-final.trx` (3/3, including
one already passing energy-binding control). The original route, snapshot and numerical
assertions remain. This coverage includes all four quality levels, actual engine-hook
rebinding after foreign sampler state and quality replacement, full lighting paths,
360 independent hardware-reference cases and the opt-in cost query. Both production
450 and fixture 528 shader variants compile. No game was launched for these checks.
The normal Debug build passed and its DLL matches the tested production assembly;
the embedded source asset, 325 copied shader sources and all 450 compiled shader/digest
entries were verified current. Receipts: `artifacts/MicrofacetEnergy/TextureLookupValidation.md`
and `texture-default-debug-receipt.json` in the same directory. The user's recorded visual
acceptance and these refactor checks complete the material-energy task.

## Validation

Focused validation covers installed-source capture placement and alpha/body preservation,
HDR composite sums, fog color conversion, RGBA8 resolve, sky-depth classification and registered
renderer integration. Existing pre-display numerical assertions observe the retained RGBA16F
intermediate; primary-output assertions observe the separate resolve. No game was launched and
no live appearance or performance result is claimed.

The driver also compiles and executes transformed multiline opaque/topsoil fixtures with the
production color helper, checking decoded RGB and preserved alpha. Installed-source tests check
the real statements and matching lexical scope. This is not a full compilation of every engine
include/define combination.

Validation receipts (subagent-run):

- ShaderBuildTool Release succeeded: 153 stages and 387 variants.
- `artifacts/PbrColor/pbr-color-boundaries.trx`: 38 passed; two initial terrain AST failures
  prompted the scope correction and are superseded below. Passing coverage includes direct
  lighting/shadows, registered composition, display grading isolation and lighting restoration.
- `artifacts/PbrColor/pbr-color-boundaries-final.trx`: 12 passed; the same two terrain failures
  are superseded below. This run covers the additional HDR emission case, display transfer,
  linear fog and registered display output after the framebuffer state-cache correction.
- `artifacts/PbrColor/pbr-terrain-production.trx`: final terrain checks **12/12 passed**,
  including the complete production patch chain and actual transformed GLSL execution.
- These initial receipts predate the TinyAst 0.11.3 update. Current preservation tests compare
  exact source text, including comments and whitespace, without normalizing extra block wrappers.
  Separate assertions retain capture/use scope coverage.
