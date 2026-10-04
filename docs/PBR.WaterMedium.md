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

The current liquid consumer supplies shadow-visible solar irradiance, bounded surface-local environment/block lighting and distance-attenuated engine point lights. Environment/block source is isotropic; direct sources use the phase function. Sunlight does not enter this source when the receiver's existing shadow/sky visibility is zero. This is a constant local illumination approximation, not light transport integrated along the volume. Dynamic lights remain unshadowed as in the existing surface path; terrain rejection along their paths remains necessary.

For accepted refracted receivers, solar and point-light phase evaluation uses the outgoing photon
direction within water. Above water this is the reverse refracted interface-to-receiver ray,
transformed from view to world space. Underwater it is the interface-to-camera direction; the
refracted exit segment is air. Straight-through fallback retains its existing camera-ray direction.
The receiver sampler supplies geometry; `liquids/transport.glsl` evaluates RGB transport without
display conversion, and the liquid output adapter converts only after linear confidence blending.

Medium metadata is rebuilt from resolved material plans on synchronous, asynchronous and cached restoration paths. Original material UVs select coefficients independently of animated tint UVs. BRDF texture overrides retain the target material's medium. Two RGBA32F texels store each record; a nearest-filtered R32F index image stores exact one-based indices, with zero selecting clear water. Liquid shader inputs borrow the owning Texture2D objects for material and medium tables; atmosphere volumes use DynamicTexture3D and boundary capture uses DynamicTexture2D. Generated submission validates these objects before binding. Engine-owned terrain, depth and shadow resources retain texture-ID bindings. Metadata is retired on atlas removal, resize and disposal. It is independent of normal/depth atlas availability and LumOn. A transmitting atlas adds four bytes per atlas pixel plus 32 bytes per tile record, rounded to table texture dimensions.

`WaterVolumeRenderer` submits the existing liquid meshes double-sided before opaque composition, using the completed liquid-depth wave snapshot. Its retained `pbr_water_volume` shader owner stays in capture mode 3 independently of the mode-0 liquid surface owner, avoiding per-frame executable replacement. Entry boundaries add and exit boundaries subtract the distance remaining to the opaque receiver. Two additive RGBA32F targets hold signed RGB optical depth, submerged length, scattering-source integral and boundary count. This handles visible sloped interfaces and multiple disjoint intervals without using the old depth cap. A recognized underwater camera adds its initial medium over the full receiver distance; the actual fluid block and resolved material distinguish water from other submerged liquids. Camera lighting supplies bounded diffuse environment/block illumination.

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
