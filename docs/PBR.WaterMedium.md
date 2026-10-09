# Water absorption and single scattering

The owned liquid shader evaluates explicit homogeneous medium coefficients rather than inferring extinction or scattering from water texture tint. Material definitions own the medium, while the existing material atlas owner publishes a current-generation tile index image and compact coefficient table. An oriented boundary capture now supplies per-channel transport to the scene-linear opaque composite. The parent task remains open for the coverage and acceptance requirements below.

## Units and measured reference

Each game block is one cubic metre; a distance of one block is one metre. Absorption and scattering coefficients have SI units m^-1. Density, Henyey-Greenstein anisotropy, material transmission strength, IOR, Fresnel reflectance and roughness are dimensionless. The existing water interface retains IOR 1.333 independently of medium density.

The clear-water absorption reference is Pope and Fry, *Applied Optics* 36(33), 8710–8723 (1997), [Table 3 and experimental conditions](https://www.researchgate.net/publication/5588568_Absorption_spectrum_380-700_nm_of_pure_water_II_Integrating_cavity_measurements), [DOI](https://doi.org/10.1364/AO.36.008710). Their integrating-cavity measurements used purified Type I water at 22 °C, with temperature variation no greater than 1 °C. The selected samples are:

| Channel | Effective wavelength | Absorption | Reported standard deviation |
| --- | --- | --- | --- |
| R | 650 nm | 0.340 m^-1 | 0.003 m^-1 |
| G | 550 nm | 0.0565 m^-1 | 0.0011 m^-1 |
| B | 450 nm | 0.00922 m^-1 | 0.0005 m^-1 |

This is a three-wavelength diagonal RGB approximation, fitted exactly at those samples. It is not an integration over display primaries, illuminant spectra or camera spectral response. No temperature/salinity correction is applied. Clear-water scattering defaults to zero; this makes no measured claim about pure-water molecular scattering. Additional absorption and scattering describe separately authored dissolved/particulate water properties.

## Material authoring

`waterMedium` is accepted in shared defaults and individual material definitions. Members inherit independently; explicit zeros remain zeros. Coefficient arrays must contain exactly three finite nonnegative values, each at most 100 m^-1. Density is in [0, 100] and anisotropy in [-0.95, 0.95]. Invalid members inherit with a source diagnostic. The schema mirrors these bounds.

```json
{
  "roughness": 0.08,
  "transmission": 1,
  "waterMedium": {
    "additionalAbsorptionPerMetre": [0.02, 0.01, 0.005],
    "scatteringPerMetre": [0.05, 0.05, 0.05],
    "density": 1,
    "anisotropy": 0.7
  }
}
```

These extra coefficients are an authoring example, not a measured natural-water preset. Effective absorption is `(clearWaterAbsorption + additionalAbsorption) * density`; effective scattering is `scattering * density`. Zero density disables medium transport, while leaving the liquid interface response intact. Lava and nontransmitting/full-alpha liquids retain their previous body-lighting response.

## Evaluation and ownership

`includes/liquids/medium.glsl` accepts a submerged path length in metres. It evaluates `T = exp(-(absorption + scattering) * length)` and the analytic integral of a constant single-scattering source. A short optical-depth series avoids cancellation and preserves the zero-extinction limit. The bounded Henyey-Greenstein function uses the cosine between incoming and outgoing photon directions.

For a homogeneous surface path, `VgeWaterEvaluatePath` returns the clamped metre length,
RGB extinction, optical depth and transmission together. Receiver transport and ordinary
fallback each evaluate their own path once; they do not share results across different lengths.
The fallback uses the same transmission for interface coverage and scattering integration.
The source integral reuses transmission in `(1-T)/extinction`, retaining the existing
second-order series below optical depth `.001` and its zero-extinction limit. When no
effective scattering channel is positive, it returns zero before evaluating the integral;
absorption still attenuates the background. Source radiance and scattering retain their
nonnegative clamps. Transmission-only consumers retain the standalone helper.
The integral evaluates the three RGB channels explicitly, avoiding dynamic vector indexing
and temporary arrays while allowing each channel to select its own thin-path branch.

Before this change, optimized surface binaries retained a vector exponential for transmission
and a scalar exponential inside each receiver/fallback RGB integration loop. Local extinction
and length clamps were already shared by the compiler; those are not claimed as removed work.
Signed boundary capture accumulates optical depth rather than evaluating this exponential,
so its accumulation and heterogeneous-source approximation are unchanged.

Matched optimized inspection removes two scalar exponential sites from every surface variant
(eight total `Exp` sites become six). These were inside RGB integration loops, so each could
execute for up to three thick channels on its selected path. Explicit RGB integration and
zero-scattering guards increase total static instructions by 46; fewer exponential sites alone
do not establish lower GPU cost. All 24 capture variants retain their previous opcode sequences.

An initial shared-transmission implementation retained dynamic RGB indexing and regressed UV
timings in the focused fixture. The explicit RGB version removes that observed regression.
A warmed baseline/current/current/baseline comparison uses equally optimized SPIR-V, the
existing binary-override validation and `GpuTimerQuery` on an NVIDIA RTX 4090 (driver 591.86),
512x512 targets, two warmup batches
and five measured batches per run. Each query covers 16 draws, excluding preparation and uploads.
The following medians combine ten samples per version/workload; units are milliseconds per batch.

| Receiver pattern | UV baseline / shared | x8 baseline / shared |
| --- | ---: | ---: |
| Valid | 1.619968 / 1.581568 | 3.539968 / 3.432448 |
| Invalid | 0.614400 / 0.568320 | 4.709888 / 4.709376 |
| Spatial checker | 1.691136 / 1.635328 | 10.217984 / 9.913856 |

Most ranges overlap; only UV invalid ranges separate in this run (0.607232–0.623616 versus
0.566272–0.573440 ms). These are small workload-specific results, not a measured game-frame
or volume-capture improvement. Thirty-six matched full-MRT comparisons retain exact alpha
and exact values in outputs 0/1/2; the largest remaining color difference is `3.5762787e-7`.
Receipts and sample ranges are in `artifacts/WaterLagAnalysis/shared-medium-candidate-*`;
`shared-medium-*` without `candidate` retains the initial implementation's comparison.

The final supported serial shader/Debug build passed with zero warnings/errors. All 113
focused GPU cases passed without skips, covering analytic thin/thick/threshold paths,
zero extinction/scattering, defensive coefficient/source/length clamps, underwater ordering,
refraction quality/resolution, boundary capture and liquid compatibility. All eight optimized
surface binaries from this build match the measured explicit-RGB candidates byte-for-byte.
Final receipts: `shared-medium-final-build.log`, `shared-medium-final-tests.log/.trx` and
`shared-medium-final-*` optimized/provenance artifacts in the same evidence directory.

The current liquid consumer supplies shadow-visible solar irradiance, bounded surface-local environment/block lighting and distance-attenuated engine point lights. Environment/block source is isotropic; direct sources use the phase function. Sunlight does not enter this source when the receiver's existing shadow/sky visibility is zero. This is a constant local illumination approximation, not light transport integrated along the volume. Dynamic lights remain unshadowed as in the existing surface path; terrain rejection along their paths remains necessary.

Exactly zero anisotropy uses the constant `1/(4*pi)`, including media with nonzero scattering.
Nonzero anisotropy retains the bounded Henyey-Greenstein expression, even arbitrarily close
to zero; the existing `[-.95,.95]` clamp is unchanged. The direction overload skips the cosine
for isotropic scattering. Surface lighting also skips the refracted outgoing-direction transform
when it serves only that phase evaluation, while retaining the view, solar and point-light
directions used by reflection. Boundary lighting skips its phase-only world-space eye transform
and solar normalization. Boundary orientation, solar/shadow illumination, signed accumulation,
and the separate zero-scattering gate remain unchanged.

Optimized inspection confirms that the isotropic branch bypasses the angular formula and
phase-only directions. The shared nonzero helper avoids nested zero tests that the compiler
retained in an initial implementation. Relative to the previous binaries, the eight surface
variants add 43 static instructions and eight boundary variants add seven; the sixteen other
capture variants are unchanged. This is a dynamic-work reduction, not smaller shader code.

Matched optimized draws on an RTX 4090 (driver 591.86) use 512x512 targets, the existing timer,
and baseline/current/current/baseline ordering. Both materials have nonzero scattering; the
checker alternates isotropic and anisotropic records and verifies both responses occur.
Boundary medians are identical at 0.295936 ms per 16 draws across all three material patterns.
Surface timings remain inconclusive: short batches appeared slower, but a bounded repeat with
128 draws, five warmup batches and ten measured batches per run reversed that trend. Its
combined medians (20 samples per version, milliseconds per 128 draws) are:

| Medium pattern | UV baseline / current | x8 baseline / current |
| --- | ---: | ---: |
| Isotropic | 27.291648 / 20.153856 | 50.464768 / 42.229248 |
| Anisotropic | 27.564032 / 20.686848 | 64.713728 / 42.242048 |
| Spatial checker | 27.912192 / 20.643328 | 60.040704 / 44.796928 |

Ranges overlap broadly: x8 isotropic spans 25.616384–74.984448 ms before and
38.582272–58.620928 ms after. Neither attempt establishes a stable surface speedup or regression,
and neither measures live frame performance. Both attempts and full ranges are retained in
`artifacts/WaterLagAnalysis/isotropic-gpu-*` and `isotropic-stable-gpu-*`.
Forty-two matched full-MRT output pairs retain exact alpha; maximum scaled differences are
`5.94e-7` for boundary lighting and `2.37e-7` for surfaces. Analytic checks cover exact zero,
signed `.7`, signed `1e-7` and out-of-range anisotropy at five angles. Only the clamped forward
peak needs a `5e-5` relative tolerance, independently reproduced by float32 denominator rounding;
exact isotropic equality and the distinction from tiny nonzero anisotropy remain strict.
The final supported serial shader/Debug build passed with zero errors (101 existing warnings),
and all 16 focused GPU cases passed without skips. The 32 optimized final binaries match the
measured candidates byte-for-byte. Across 672 production output vectors covering sun and point
lights, above/below water, UV/x8 and enabled/disabled refraction, alpha is exact and maximum
scaled difference is `2.34e-7`. Build, tests, binary identity and output receipts are retained as
`artifacts/WaterLagAnalysis/isotropic-final-*`.

For accepted refracted receivers, solar and point-light phase evaluation uses the outgoing photon
direction within water. Above water this is the reverse refracted interface-to-receiver ray,
transformed from view to world space. Underwater it is the interface-to-camera direction; the
refracted exit segment is air. Straight-through fallback retains its existing camera-ray direction.
The receiver sampler supplies geometry; `liquids/transport.glsl` evaluates RGB transport without
display conversion, and the liquid output adapter converts only after linear confidence blending.

Medium metadata is rebuilt from resolved material plans on synchronous, asynchronous and cached restoration paths. Original material UVs select coefficients independently of animated tint UVs. BRDF texture overrides retain the target material's medium. Two RGBA32F texels store each record; a nearest-filtered R32F index image stores exact one-based indices, with zero selecting clear water. Liquid shader inputs borrow the owning Texture2D objects for material and medium tables; atmosphere volumes use DynamicTexture3D and boundary capture uses DynamicTexture2D. Generated submission validates these objects before binding. Engine-owned terrain, depth and shadow resources retain texture-ID bindings. Metadata is retired on atlas removal, resize and disposal. It is independent of normal/depth atlas availability and LumOn. A transmitting atlas adds four bytes per atlas pixel plus 32 bytes per tile record, rounded to table texture dimensions.

`WaterVolumeRenderer` submits the existing liquid meshes double-sided before opaque composition, using the completed liquid-depth wave snapshot. Its retained `pbr_water_volume` shader owner stays in capture mode 3 independently of the mode-0 liquid surface owner, avoiding per-frame executable replacement. Entry boundaries add and exit boundaries subtract the distance remaining to the opaque receiver. Two additive RGBA32F targets hold signed RGB optical depth, submerged length, scattering-source integral and boundary count. This handles visible sloped interfaces and multiple disjoint intervals without using the old depth cap. A recognized underwater camera adds its initial medium over the full receiver distance; the actual fluid block and resolved material distinguish water from other submerged liquids. Camera lighting supplies bounded diffuse environment/block illumination.

Boundary capture evaluates source illumination only when at least one effective scattering
coefficient is positive. The material record already includes density, so clear water and
zero-density media skip the world-space eye transform, solar-direction normalization, shadow
visibility, phase function and diffuse source calculation. Their source RGB is zero, while
signed optical depth, remaining water length and boundary count are accumulated normally.
No small-coefficient threshold is used: any positive channel retains the existing lighting.
The predicate is evaluated per fragment, so mixed materials retain their individual response.

Surface lighting has a different sharing boundary: its solar visibility and point-light
calculations also supply reflection and non-water body lighting. Those evaluations remain
unchanged, as does the camera-medium contribution. Skipping the volume source is not a
reason to suppress the surface reflection or a submerged camera's absorption.

Frame capture follows the selected liquid contract: boundary mode stages zero point-light and
fog-sphere counts and omits those array copies, since neither stage consumes them. The shared
vertex executable still evaluates climate/season colormap coordinates, so all 40 colormap
rectangles remain current in boundary mode. Camera projection/inverse and viewport/depth-plane
inputs come from the shared VgeFrameUBO. Shadow transforms,
animation, atlas metadata and solar/environment inputs keep their common capture path; wave
and draw transforms retain their existing caller-owned staging. Surface capture refreshes its
active point-light/fog prefixes and counts, including after a mode change. The production
volume and surface programs retain independent frame storage. Submission still uploads the
complete 4560-byte liquid effect block; this optimization reduces CPU preparation, not upload bandwidth.
The capture owner caches its mode classification against the existing immutable requested
settings snapshot. Real settings changes refresh it before capture; unchanged batches and
shader reload retain the same valid classification. This avoids the generated option getter's
per-call validation/allocation overhead without changing that shared accessor API.

Optimized-stage inspection confirms that volume fragments do not access frame counts, light
positions/colors or fog arrays, and the shared vertex stage accesses only the colormap array
among these candidates. The unchanged frame declaration and shader binaries retain full uploads.
Focused validation passed 25/25 checks after C# builds, reusing the previously validated shader
artifacts. Actual GPU buffer readback covers independent owners, counts changing from 100 lights/
three spheres to one and zero, changed contents, quality/reload and boundary-to-surface recapture.
Colormaps, animation and the matching projection inverse remain current throughout.

A baseline/current/current/baseline CPU comparison measures the actual capture method through
the same controlled engine API adapter, with 8192 warmup calls and five 2048-call samples per
run. Median microseconds per call (ten samples per version) are:

| Lights / fog spheres | Surface baseline / current | Boundary baseline / current |
| --- | ---: | ---: |
| 0 / 0 | 6.269 / 6.309 | 9.079 / 9.150 |
| 4 / 1 | 7.216 / 7.240 | 7.080 / 6.341 |
| 100 / 3 | 22.781 / 23.289 | 22.245 / 6.375 |

The populated boundary workloads have separated sample ranges; surface and empty-boundary
ranges overlap. Cross-case timing differences are not an engine workload model. Warm allocation
remains 320 bytes per call in both versions, including the adapter's dispatch overhead. The
initial uncached getter version added 520 bytes per call and was replaced before completion.
These results establish reduced CPU copying for populated volume inputs, not lower GPU upload
cost or a game-frame improvement. Stage inspection, build/test receipts, ABBA logs and complete
sample ranges are under `artifacts/WaterLagAnalysis/frame-input-*`.

The zero-scattering gate passed a fresh isolated Debug build and 12/12 focused boundary and
transport checks. Production boundary draws cover clear water, a positive red-only scattering
coefficient with solar/environment illumination, zero effective density and different entry/exit
materials. Signed-interval and underwater transport checks retain their numerical references.
Optimized SPIR-V places shadow visibility, phase and source lighting inside the positive-scattering
branch, merging zero before the unchanged output writes; static instructions increase from 534
to 543. This establishes skipped source work, not an automatic reduction in GPU duration.

A controlled warmed ABBA comparison used equally optimized baseline/current binaries, existing
`GpuTimerQuery`, active shadow sampling and 16 draws at 512x512 per sample. Preparation and uploads
were outside the query. On an NVIDIA RTX 4090, driver 591.86, the mean query times were:

| Medium pattern | Baseline | With gate |
| --- | ---: | ---: |
| Clear | 0.294502 ms | 0.293990 ms |
| Scattering | 0.294195 ms | 0.294400 ms |
| Spatial checker | 0.294400 ms | 0.294605 ms |

These results show no meaningful timing separation. Driver program caching was bypassed and
binary overrides were checked. The checker contained both clear and scattering pixels; source
output sums and zero-pixel counts matched between binaries. This small headless workload does
not establish a production speedup or the cost of divergence in a game scene. Evidence is in
`artifacts/WaterLagAnalysis/clear-scattering-*`: build/test logs, matched timing CSV and ABBA
logs, baseline/current provenance runs and optimized disassemblies. Each ABBA run passed all
ten boundary cases, and the two subsequent provenance checks each passed their selected case.

The opaque composite evaluates RGB transmittance before its existing display resolve, with atmospheric transport restricted to aggregate air length. No extra exposure resolve is introduced. Successful opaque publication disables bulk transport in the later liquid OIT surface pass. Capture failure retains the original liquid fallback. Screen storage costs 32 bytes per pixel (about 63.3 MiB at 1920 × 1080), plus an extra liquid mesh submission; GPU cost has not been measured.

## Remaining rendering requirements

Sky transport is accepted only when captured boundaries establish a finite exit from water. The far plane bounds capture without becoming an assumed water depth; unresolved sky paths retain their existing background. Invalid finite/range/winding checks reject transport and retain the existing composite fog behavior. Net winding checks do not prove intermediate topology or mesh closure. Camera classification deliberately uses the existing engine underwater threshold and fluid-block lookup. No additional camera-contact texture or probe pass is allocated. This classification does not follow animated waves or tessellated liquid corner heights; exact animated contact belongs to the waterline work. Shorelines, mini-dimensions, camera crossings and actual production mesh closure require further verification.

Opaque background transmission now remains RGB, including HDR values, with LumOn enabled or disabled. [Optional water refraction](PBR.WaterRefraction.md) evaluates the medium along accepted bent opaque paths from an unattenuated radiance snapshot and replaces the original OIT background. Engine bucket OIT still uses scalar interface revealage on fallback paths; transparent receivers, late scene consumers, held objects and multiple refracted intervals require coordinated coverage. Signed extinction is exact for valid disjoint straight intervals, but scattering uses a constant extinction-weighted source approximation rather than ordered heterogeneous transport. Boundary lighting samples local sunlight visibility and diffuse illumination; volume-wide solar/local-light occlusion is not implemented.

The current change does not replace vanilla underwater fog on other scene consumers, nor validate atmospheric haze ownership for a new underwater pass. Those consumers must be coordinated before bulk medium fog is enabled. User-run visual acceptance and GPU time/sampling/bandwidth measurement remain outstanding; no game was launched.

## Validation

Focused subagent validation covers production SPIR-V, homogeneous analytic transport, measured-reference transmittance, monotonic absorption, thin paths, zero density, zero incident light, directional phase values, authoring inheritance/invalid inputs, atlas coefficients and generation retirement. Analytic fixtures load typed offline-built shader programs through the shared component owner and fullscreen renderer. Boundary tests exercise sloped interfaces, two disjoint intervals, foreground rejection, finite sky intervals, unresolved sky fallback and underwater exits using framebuffer and indexed-mesh abstractions. Composite tests check CPU parameter layout and rendered RGB/HDR transmission with LumOn on/off, zero density and non-water camera fallback. Existing liquid/material/atmosphere suites provide regression coverage. Source review confirmed that the rendering requirements above remain unresolved.

The medium-foundation run passed 59/59 tests without skips (`artifacts/PbrColor/water-medium-focused.log`). The current incremental volume build succeeded with zero warnings/errors, and the shader catalog verified 163 stages / 401 variants. Naming the water gate's Boolean predicates fixes the reproduced disabled-capture fog regression on the tested GPU path. All 16 HDR tests pass; the broader focused suite passes 264 tests with five explicit measurement tests skipped and zero failures. Current receipts: `artifacts/PbrColor/water-volume-named-condition-build.log`, `artifacts/PbrColor/water-volume-named-condition-hdr.log` and `artifacts/PbrColor/water-volume-named-condition-focused.log`.

```powershell
$env:NUGET_PACKAGES = 'C:/Users/Sisco/.nuget/packages'
dotnet build VanillaGraphicsExpanded.Tests/VanillaGraphicsExpanded.Tests.csproj -c Debug --no-restore
dotnet test VanillaGraphicsExpanded.Tests/VanillaGraphicsExpanded.Tests.csproj -c Debug --no-build --no-restore --filter 'FullyQualifiedName~Liquid|FullyQualifiedName~WaterMedium|FullyQualifiedName~WaterBoundary|FullyQualifiedName~WaterVolume|FullyQualifiedName~PbrComposite|FullyQualifiedName~Atmosphere|FullyQualifiedName~MaterialTransmission|FullyQualifiedName~BRDFProperties|FullyQualifiedName~PbrMaterialRegistry'
```

After replacing test-side shader helper calls and raw rendering/binding calls with typed binary program owners and existing rendering abstractions, the affected subset passed 28/28 tests. The broader suite again passed 264 tests with five measurement skips and zero failures. The offline shader build verified 163 stages / 401 variants with 106 warnings and zero errors; a serial retry avoided an intermittent shader-cache publication failure. Receipts: `artifacts/PbrColor/water-test-abstractions-build.log`, `artifacts/PbrColor/water-test-abstractions-subset.log` and `artifacts/PbrColor/water-test-abstractions-focused.log`.

The shared fullscreen helper also uses owned VAO/VBO/EBO wrappers, indexed submission and framebuffer clearing; analytic tests read through their texture owner. Full GPU validation recorded 2,914 passes, five skips and one failure in `SurfaceLightingSpatialRuntimeTests.MixedConsumersFollowReplacementPolicy(sh9: true)`. The same failure reproduces with the original fullscreen helper, so this abstraction migration did not introduce it. This is not a clean full-suite pass. Receipt: `artifacts/PbrColor/water-shared-abstractions-gpu-detailed.log`; final affected-test receipt: `artifacts/PbrColor/water-shared-abstractions-subset.log`.

After removing the unfinished camera-contact probe and retaining engine classification, the offline SPIR-V build succeeded with 106 warnings and zero errors in 56.86 seconds. Focused water/composite validation passed 35/35 tests without skips in three seconds. No camera-contact sampler, additional contact framebuffer or probe shader variant remains. Receipts: `artifacts/PbrColor/water-engine-classification-build.log` and `artifacts/PbrColor/water-engine-classification-focused.log`.

Concrete texture input validation: the offline build passed with 106 warnings and zero errors; focused liquid/water/composite/atmosphere tests passed 243 tests with five measurement skips, and the shader generator suite passed 153 tests including subclass publication with implicit texture-ID conversion. Receipts: `artifacts/PbrColor/water-typed-textures-build.log`, `artifacts/PbrColor/water-typed-textures-focused.log` and `artifacts/PbrColor/water-typed-textures-generator.log`.
