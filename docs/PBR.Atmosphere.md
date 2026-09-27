# Physical sky and shared atmospheric lighting

## Model and units

`AtmosphereModel` integrates RGB single scattering plus a LUT-based isotropic multiple-scattering
approximation in a spherical atmosphere. Geometry uses
kilometres: ground radius 6360 km, atmosphere top 6460 km; one game block is interpreted as one
metre above the world's sea level. Underground viewpoints clamp to the ground atmosphere;
altitudes above 99 km clamp below the atmosphere top. This is a local Earth-like model, not a
mapping of the game's terrain onto a spherical planet.

Rayleigh coefficients are (0.005802, 0.013558, 0.033100) km^-1 with an 8 km scale height.
Aerosol scattering/extinction are 0.003996/0.004440 km^-1 with a 1.2 km scale height and
Henyey-Greenstein asymmetry 0.76. Ozone absorption is (0.000650, 0.001881, 0.000085) km^-1,
with triangular density centred at 25 km and zero below 10/above 40 km. RGB solar irradiance
is (1.474, 1.8504, 1.91198) in relative scene-linear irradiance units. These are three-band
approximations, not a spectral or photometric calibration of the engine.

The implementation follows the radiative-transfer decomposition described by
[Bruneton's atmospheric scattering reference](https://ebruneton.github.io/precomputed_atmospheric_scattering/atmosphere/functions.glsl.html):
Beer-Lambert extinction, directional scattering and planet occlusion. The multiple-scattering
approximation below is distinct from Bruneton's higher-dimensional solution. Rayleigh and aerosol scattering,
aerosol absorption, and ozone absorption contribute separately. The sun's direct transmission
uses finite-disk horizon coverage and transmission at the visible segment centroid; it becomes
zero once the whole disk is occluded. There is no below-horizon sunlight floor.

View integration uses 24 segments and solar optical-depth integration uses 12. Quadratic segment
spacing resolves the dense near-ground layer. Cloud coverage maps to aerosol multiplier 1–8;
this is a bounded haze proxy for weather, not cloud-volume transport or geometric cloud shadows.
Volumetric clouds remain a separate task.

## Multiple scattering

`AtmosphereMultipleScattering` builds an immutable table, initially 32 solar-cosine by 16 altitude samples, using
128 equal-solid-angle sphere directions and 24 quadratically spaced ray segments per direction.
Squared altitude coordinates concentrate resolution near the ground. The table stores incident
radiance normalized by extraterrestrial solar irradiance. The shared quality level scales both
dimensions linearly; its RGB payload ranges from 6 KiB to 96 KiB.

| Quality | Sky LUT | Multiple-scattering LUT | Angular rays | Ray steps | Solar steps |
| --- | --- | --- | ---: | ---: | ---: |
| 0 | 32x24 | 32x16 | 128 | 24 | 12 |
| 1 | 64x48 | 64x32 | 256 | 48 | 24 |
| 2 | 96x72 | 96x48 | 384 | 72 | 36 |
| 3 | 128x96 | 128x64 | 512 | 96 | 48 |

`AtmosphereScatteringBudget` scales both LUT dimensions and integration counts using the base
count multiplied by `(quality + 1)`, preserving the baseline at quality zero. Quality
therefore improves interpolation and angular, ray and solar-transmittance integration accuracy.
These budgets compound: the nested solar-integration work bound scales as
`(quality + 1)^5` (up to 1024 times the default at quality 3), while table storage scales as `(quality + 1)^2`.
This is an operation-count bound, not a measured time prediction; high settings can have very long
worker latency even with SIMD acceleration. Builds remain asynchronous, retain the previous displayed
LUT and check cancellation between batched ray steps and ground evaluations.
These integration budgets apply to source-table construction; the sky-view single-scattering kernel
retains its existing 24 view and 12 sunlight steps.

Linear-sampling validation passed all 15 focused quality tests in
`artifacts/AtmosphereTensor/atmosphere-linear-quality.trx`, covering clamping, all four integration
budgets on compact tables, cache replacement and asynchronous quality changes.

Linear-dimension validation subsequently passed 85 focused atmosphere tests with three optional
measurements skipped in `artifacts/AtmosphereTensor/atmosphere-linear-dimensions.trx`. Sky and
multiple-scattering size expectations, quality clamps and maximum-dimension rejection use the new
1x/2x/3x/4x policy; integration counts retain their linear scaling.

Earlier integration-budget validation passed 85 focused Release tests, with three optional measurements skipped,
in `artifacts/AtmosphereTensor/atmosphere-scattering-budgets-final.trx`. It checks policy/clamping,
all quality integration budgets on compact 2x2 tables, solar-step refinement and bounds, cancellation,
and full quality-1 cache replacement and asynchronous publication. Full quality-2/3 source-table
construction was deliberately not run under the expanded budgets; their complete build latency is unmeasured.

The earlier dimension-only shared-quality validation passed 77 focused Release atmosphere tests, with three optional measurement
tests skipped, in `artifacts/AtmosphereTensor/atmosphere-scattering-quality.trx`. This constructs all
four source-table sizes at normal quadrature, verifies sun-motion reuse and quality-only invalidation,
and covers asynchronous quality admission, maximum-size cancellation and oversized-table rejection.

The design follows the isotropic approximation in
[Hillaire's atmosphere technique](https://sebh.github.io/publications/egsr2020.pdf).
Each ray integrates two quantities: direct illumination scattered with an isotropic angular distribution,
and the response to a unit isotropic incident field. Their sphere averages are a source S and feedback F.
The table stores the geometric-series closure S/(1-F), separately for each color channel. Analytic
constant-medium segment integration keeps scattering feedback bounded by extinction rather than
using an unbounded source-times-distance estimate. A 1e-5 denominator floor guards roundoff.

The planet boundary is a uniform Lambertian ground. Its former fixed albedo of 0.1 is now the
bare-ground fallback; production estimates regional snow reflectance from averaged seasonal
temperatures and their warming/cooling trend, without scanning terrain. See
[PBR.Atmosphere.Seasons.md](PBR.Atmosphere.Seasons.md) for assumptions, quantization and engine inputs.
Direct solar ground reflection
contributes to S and reflection of isotropic illumination contributes to F. This is a global atmospheric
boundary assumption, not a sample of game terrain or Surface Cache lighting. The visible sky integration
does not separately draw that ground reflection, and direct solar irradiance remains unchanged.

At each view sample the batch integrator looks up the source using local altitude and the sun's cosine
relative to the local planetary normal. It multiplies by the local scattering coefficient and view
transmittance, then adds it to the original directional single-scattering contribution. It does not apply
the direct-sun angular distribution or planet-shadow mask to this indirect source again. Solar RGB is
applied once at the end. Shared environment and horizon lighting integrate this same completed sky LUT.

The worker retains one medium table and reuses it across observer and sun-direction changes.
Its cache identity includes aerosol, quantized ground reflectance and both table dimensions; quality changes rebuild it before the
matching sky is published. Aerosol uses the admitted 0.05 cloud-coverage bucket consistently for all transport, so
sub-bucket cloud jitter during sun movement does not rebuild the medium table. Aerosol changes
rebuild it before computing a matching sky; cancellation is checked between
table cells, batched ray steps and view batches. Only complete sky/lighting snapshots reach render-thread publication.
This remains an RGB isotropic approximation with finite angular/spatial resolution, not a spectral or
fully directional multiple-scattering solver. It does not add moonlight, clouds or terrain occlusion.

Validation: 70 correctness cases passed across
`artifacts/AtmosphereMultipleScattering/multiple-scattering-final.trx` (69) and
`measurements-final.trx` (one additional weather-coherence case). Three opt-in measurement/diagnostic
cases were skipped in the ordinary run. The refined reference uses 256 directions and 96 ray steps,
covering aerosol 0.1/1/8, altitude 0/2/25/99 km and twilight/day solar angles. Comparisons use
`1e-4 + 0.15 * reference` absolute-plus-relative tolerance per channel; this is a numerical convergence
bound for the approximation, not a claim of that accuracy against physical measurements. The initial
32-direction table failed high-altitude twilight, prompting the 128-direction budget. Tests also
cover passive feedback bounds with white ground, darkness at night, ground reflection, cancellation,
shared sky/environment contributions and unchanged direct solar/extinction. Independent review found
no remaining normalization or worker/publication ownership defects. User-run visual acceptance remains open.

Historical scalar worker-side timings in `measurements-final.trx` on .NET 10.0.12 with tiering disabled and no CPU
affinity restriction were: source-table build median 361.069 ms (308.668–373.677), cached-medium
32x24 sky 1.922 ms (1.894–2.367), and cached-medium 256x192 sky 128.315 ms (125.216–131.579).
These measurements used the default 32x16 source table, including for the larger sky workload;
they predate shared quality scaling. Startup, weather and quality changes now incur background
precomputation; sun/observer changes reuse it. Matched 128-direction ABBA samples measured single scattering at 0.303/0.321 ms
and multiple scattering at 0.328/0.333 ms, both with zero warmed managed allocations. These are CPU
harness observations, not guaranteed frame costs. That CPU implementation added no GPU source-table
pass. The compute backend described below now provides an alternative; these historical results do not measure it.

## GPU computation

Supported devices compute the source table, sky and matching lighting integrals using three
production SPIR-V compute shaders. Unsupported devices retain the CPU implementation below.
The GPU owner bounds source work to 64 cells per completed batch, caches the medium across sun and
observer changes, and uses fenced asynchronous readback before coherent publication. Shader reloads
discard pending programs/work while retaining the displayed snapshot; world teardown discards both.
Capability limits, memory, dispatch ordering, numerical tests and measurements are detailed in
[PBR.Atmosphere.GpuComputation.md](PBR.Atmosphere.GpuComputation.md).

## CPU integration

Multiple-scattering table construction batches up to 128 angular rays through
`AtmosphereModel.MultipleScatteringTransferBatch`. `TensorPrimitives` evaluates square roots,
density exponentials, weighted solar-density columns and RGB segment/solar attenuation. It selects
the supported SIMD width; no register-width-specific kernels are required. Ray geometry and
visibility remain scalar, as does the ground boundary evaluated once per ray. The analytic segment
integral reuses tensor attenuation and retains the scalar thin-segment limit. Direction order and
angular reduction order are preserved, while linear extinction is applied after solar-density summation.

The table builder precomputes its invariant angular directions once. Each batch rents one bounded
scratch array and returns it in `finally`; maximum requested scratch is 62848 floats at the supported
96-solar-step reference limit (the shared pool may round this up). Stack scratch is bounded by
128-ray batches and the 1024-direction construction limit. The scalar transport and internal
`useScalarReference` table option remain available for numerical validation and matched measurements;
production table construction uses the tensor path. Neither option changes the physical model or quality budgets.

Tensor validation passed 94 focused atmosphere tests (four opt-in measurements skipped), and all
nine new parity/cancellation cases passed with `DOTNET_EnableHWIntrinsic=0`; receipts are under
`artifacts/AtmosphereMultipleScatteringTensor`, including `portable.trx`. Comparisons cover batch tails,
clear/hazy media, ground reflectance 0/0.1/1, twilight/night, altitude extremes and complete-table
closure at every integration budget, with tolerance `1e-5 + 1e-4 * abs(reference)` per channel.
Independent review found no scratch-bound, lifetime, normalization or publication defects.

Matched complete-table measurements in
`artifacts/AtmosphereMultipleScatteringTensor/measurement.json` used .NET 10.0.12, an Intel
Family 6 Model 151 CPU, affinity mask 4, disabled tiered compilation, warmup and three ABBA blocks.
The default 32x16 table measured scalar median 965.00 ms (925.03–1041.47) versus tensor 137.95 ms
(124.50–156.80), about 7x faster in this run. Both allocated 6200 managed bytes for the returned
table and owner; pooled working storage added no warmed allocations. Absolute timings differ from
earlier sessions, so these same-run comparisons supersede cross-run speedup estimates.

On bounded 2x2 tables using quality 0/1/2/3 integration counts, scalar medians were
7.25/50.93/171.84/399.05 ms and tensor medians 1.15/6.61/20.15/44.28 ms; each path allocated
104 bytes per returned table. These higher-quality results measure integration budgets, not full
production-sized high-quality tables or render-frame cost. No GPU workload changed.

The lookup evaluates directions in batches of at most 128 using `AtmosphereModel.RadianceBatch`.
`TensorPrimitives.Sqrt`, `Divide`, `Clamp` and `Exp` process the altitude/density samples and RGB
transmission spans. The library selects the available SIMD width and handles tails; there are no
register-width-specific implementations. Position arithmetic, visibility decisions and per-ray sample
order follow the scalar model. Sunlight integrates the three weighted medium-density columns before
applying their linear RGB extinction coefficients once per ray. Occluded sunlight paths are excluded
from the compacted sample arrays.

Scratch arrays are rented from `ArrayPool<float>` and returned in `finally`; stack scratch is bounded
by the batch capacity. Every CPU fallback rebuild computes the entire LUT in one background task;
128 is the SIMD batch capacity, not a per-frame limit. The 24 view samples and 12 sunlight samples
are unchanged. The original scalar `Radiance` remains a numerical reference, while the lookup uses
the batch path. SIMD exponentials can round differently, so comparison tests use a small numerical
tolerance rather than requiring bit-identical scalar output.

Tensor validation passed 53 atmosphere tests (the opt-in measurement test was skipped) in
`artifacts/AtmosphereTensor/atmosphere-tensor-columns.trx`; all six batch reference cases also
passed with hardware intrinsics disabled. Coverage includes partial batches, the 128-direction
boundary, horizon/night conditions, altitude and aerosol extremes, and unchanged LUT publication.
The comparison bound is `1e-5 + 1e-4 * abs(reference)` per channel.

Matched Release measurements on an i9-12900K / .NET 10.0.12 x64 used one pinned logical CPU,
disabled tiered compilation, three seconds of warmup per workload and four ABBA blocks of 40
128-direction calls. The independent confirmation report
`artifacts/AtmosphereTensor/measurement-columns-confirm.json` recorded these median CPU costs:

| Sun | Scalar / 128 directions | Tensor / 128 directions |
| --- | ---: | ---: |
| Noon | 2.558 ms | 0.320 ms |
| Horizon | 2.535 ms | 0.315 ms |
| Night | 0.137 ms | 0.076 ms |

Both paths allocated zero managed bytes after warmup. Absolute scalar timings varied substantially
between runs despite unchanged source; these are matched observations, not a promised speedup.
The default-tiered-JIT run also improved daylight integration but had a nighttime timing outlier.
These measurements cover the CPU integration kernel, not full LUT publication or in-game frame time.

## Publication and rendering

`AtmosphereBackend` selects GPU compute when the cached capabilities and resource limits support it.
Otherwise `AtmosphereLookup` builds the configurable radiance table (default 32 x 24), with uniform
azimuth and horizon-focused elevation sampling shared with GPU generation and engine lookup.
See [PBR.Atmosphere.HorizonSampling.md](PBR.Atmosphere.HorizonSampling.md) for the altitude-aware
mapping, pole/seam behavior and corrected illumination weights.
`AtmosphereComputation` admits one `Task.Run` build at a time. These serial tasks reuse one worker-owned
lookup and compute the entire table, including shared lighting integrals, without yielding across render frames.
The render callback captures value inputs and polls completion without waiting. Only that callback
uploads textures and publishes lighting; background code never accesses engine or GPU objects.
Until the first result arrives, consumers receive a valid 1x1 black sky with zero lighting and extinction.
Subsequent builds retain the previous complete LUT. Initialization can therefore briefly show a dark
sky rather than blocking the first scene frame.
Sun direction is quantized to 1/65536 component increments to resolve finite-disk horizon transitions,
altitude to 25 metres, and cloud coverage to 0.05. Stationary unchanged inputs do no integration.

`Atmosphere.SkyLutQuality` is persisted in VGE config and exposed through ConfigLib as one quality
selector. Levels 0–3 map to 32x24 (default), 64x48, 96x72 and 128x96. Both dimensions are derived
by multiplying their base dimensions by `(quality + 1)`. Separate width/height settings are removed
without migration. Larger tables increase background integration time. Changes during a build are
coalesced into the latest inputs captured after it finishes. Admitted work finishes and is published
before another build starts, preventing continuously changing weather from starving publication.
Quality changes follow the same asynchronous policy; they no longer require an immediate render-thread rebuild.
Completed snapshots carry their own dimensions;
the GPU owner uploads a replacement texture before publishing its ID and matching lighting, then
disposes the old texture. Same-size refreshes reuse the existing allocation. No shader reload is needed.

Quality validation: all 47 focused atmosphere tests passed in
`artifacts/PbrColor/atmosphere-quality.trx`. Checks include all four levels and the full 256x192 table,
config defaults/null restoration, clamping and quality-only serialization, immediate resize superseding pending work with the latest inputs,
incremental weather refresh and partial final batches, immutable previous snapshots, and GPU
allocation/content/reuse/retirement through the production owner.
This is headless validation, not live appearance acceptance.
World teardown cancels CPU work between SIMD batches and retires pending GPU resources without waiting;
an old-world owner cannot publish into a new world. Task failures are observed even when their owner is discarded.
Camera rotation/bobbing do not affect lookup direction; altitude changes
below the key threshold do not invalidate it.

Publication contains one immutable sky table, direct solar irradiance, upward Lambertian sky
response (hemisphere irradiance divided by pi), horizon-average radiance, and local extinction.
The render service uploads the table before publishing its lighting snapshot. All consumers keep
the previous complete snapshot while a refresh is in progress. Shaders always receive valid atmospheric
inputs and have no atmosphere-readiness branches. CPU rebuild latency depends on worker scheduling
and computation; GPU source-table work is bounded across render updates. Both backends use
low-resolution interpolation rather than a full-resolution fragment ray march.

The installed sky shader samples this table and uses the existing unit-exposure Reinhard/sRGB
display conversion once. The engine's night/fog alpha calculation is retained, as are subsequent
underwater/night-vision effects. Stars remain the separate engine night-sky draw before the dome;
the sun reuses the engine quad with atmospheric disk shading, while the moon retains its textured
draw afterward. See [solar disk integration](PBR.Atmosphere.SolarDisk.md). Moonlight is not a second atmospheric light
source in this implementation. At night only residual solar twilight is integrated; no artificial
ambient floor is added. Lunar texture photometry and adaptive night exposure are not calibrated
by this model.

## Shared lighting contract

Asynchronous rebuild validation: `artifacts/AtmosphereAsync/atmosphere-async.trx` passed 59 focused
Release tests, with one opt-in benchmark skipped and no failures. Tests cover full builds by default,
completion-only publication, stable-input reuse, latest-input/resolution admission, cancellation,
disposal and the GPU transition from a 1x1 placeholder to a computed LUT. The production shader
catalog rebuilt successfully. No live game verification was performed.

- Deferred direct lighting receives attenuated solar RGB; existing shadow visibility and point lights
  remain unchanged. Physical sunlight uses normalized Lambert diffuse (`1/pi`) in both forward and
  deferred shading; the existing helper's legacy point-light calibration remains unchanged.
- Terrain and entity capture use the atmosphere's sky response times local engine sky availability.
  Forward PBR uses the same response and solar RGB. Block illumination remains independently supplied.
- LumOn frame/world-probe inputs receive the same sky response. Screen-probe sky misses use its
  hemispherical approximation, replacing the former fabricated solar halo. These probe paths do not
  yet sample the angular sky table; their lighting is a coarse angular approximation of the new sky.
- Surface Cache direct refresh replaces white sunlight with the same sky irradiance and oriented
  solar term, weighted by the existing local sunlight-availability estimate. Existing bounded refresh
  converges resident pages without global clearing. Visibility remains the existing voxel-light
  approximation rather than new directional shadow rays. The unused legacy relight shader is not
  part of the production `SurfaceLightingDispatch` path.

Attachment 7 alpha now records local sky visibility. RGB retains the existing environment contract.
Forward and deferred composition use the same local extinction and horizon source for a homogeneous
Beer-Lambert aerial-perspective approximation based on receiver distance in metres. Sky visibility
gates its in-scattered source so sealed interiors gain no new sky haze. Engine fog is retained as a
separate artistic/local effect after this term; atmosphere is applied once before display conversion.
This approximation does not integrate changing altitude along long receiver rays or trace shadowed
participating media. It is not a volumetric fog solution.

## Validation

The first integration receipts passed 98 distinct cases across
`artifacts/PbrColor/atmosphere-integration-final.trx` (92), `atmosphere-published.trx` (16, six new),
and `atmosphere-sky-final.trx` (two overlapping installed-sky cases). They cover physical trends,
analytic vertical transmission, finite output, bounded/immutable cache publication, changing inputs,
installed engine shader linking, actual published forward and Surface Cache lighting, interior
visibility, HDR/fog boundaries and point-light preservation. The shader catalog rebuilt 153 stages
and 387 variants. Solar-unit normalization received a subsequent focused validation recorded below.

The final `artifacts/PbrColor/atmosphere-solar-units.trx` receipt passed 67 cases covering deferred
solar irradiance normalization, forward point-light/emission preservation, and installed shader
linking. Across these four receipts, 111 distinct tests passed. Physical solar diffuse uses irradiance
divided by pi; point lights and emission retain their existing calibration. Solar normalization is now unconditional; the former light0.w mode flag is reserved padding, always written as zero.

The original 32-sample-budget measurement on this machine recorded five warmed full refreshes at
21.446–24.665 ms (median 22.902 ms), approximately
0.954 ms per bounded 32-direction update. Two full refreshes preceded measurement as warmups.
These are CPU harness observations, not GPU timings or a guarantee of in-game frame cost. Work is
now performed in full background builds and stops when the quantized input key is unchanged.
These historical synchronous timings do not describe the current render-thread cost.

Review corrected boundary-layer undersampling, GLSL declaration order and macro expansion, the
sky's alpha preservation, exact-zenith azimuth handling, and the physical/legacy solar-unit boundary.
The tested vertical solar transmission agrees with the analytic density integral within 1% for
aerosol multipliers 1 and 8. This does not certify all angular quadrature errors or LUT resolution.

Source/IL evidence for sky direction, camera translation removal and celestial draw order is under
`artifacts/PbrColor`. No game process was launched and no live appearance claim is made.

### Synchronous initialization validation

After removing shader readiness branches, 100 focused tests passed (zero failures or skips) in
`artifacts/PbrColor/atmosphere-initialization.trx`. Coverage includes complete initial publication,
unchanged-input reuse, bounded subsequent refresh, installed shaders without the readiness uniform,
and forward, terrain, composite and Surface Cache lighting. All 153 stages / 387 shader variants
rebuilt successfully. The render ordering review confirmed initialization precedes scene lighting
consumers and publication follows texture upload. No live game verification was performed.

## Engine shader binding ownership

Atmospheric engine bindings use an explicit pass-name allowlist: `sky` owns the sky texture;
`chunkopaque` and `chunktopsoil` own environment lighting; `standard`, `entityanimated`,
`instanced` and `chunktransparent` own forward atmospheric lighting. Other shader families
receive no atmospheric bindings even if they declare similarly named uniforms.

Compilation clears previous metadata, then successful linking resolves only the family's allowed
uniforms against the engine's active interface. Optimized-out inputs are omitted. Draws read the
cached flags without `HasUniform` discovery. Metadata uses weak program-object keys, so replacement
programs and recycled GL identifiers cannot inherit another program's interface. Recompilation,
including a failed replacement, discards the previous interface. VGE-owned programs retain their
existing typed contracts.
Validation: 14 focused binding tests passed in `artifacts/PbrColor/atmosphere-bindings-final.trx`,
covering family ownership, unrelated-program rejection, optimized-out inputs, cached reads and
successful/failed recompilation. Installed engine IL in
`artifacts/PbrColor/ShaderCompileBindings.il.txt` confirms uniform locations are populated before
`Compile` returns. `artifacts/basegame-ModSystemFpHands.il` records first-person item/hand registration
as `standard`/`entityanimated`, so both inherit the explicit family contract.
