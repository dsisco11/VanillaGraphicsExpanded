# Photometric lighting and block-light calibration

Status: the user approved pre-exposed frame lighting, a bounded visible solar disk and fixed-scale
persistent lighting and a narrow finite-angular solar specular refinement on 2026-10-11.
Shared quantity, shader conversion and admitted storage support are implemented. Atmosphere amplitude,
block-field publication, punctual evaluation, emission and shared direct diffuse normalization now use
the declared source quantities. Connected transport and integrated scene storage are implemented;
physical camera ranges, configuration migration and postprocess containment have focused
qualification. Full integrated and user-run acceptance remain pending. Source-derived calibration and numerical qualification
of the supported design domain are recorded below and in `PBR.PhotometricLighting.todo`.
These prerequisite receipts do not establish completed runtime migration, integrated GPU
precision, live appearance or performance.

The retained support lives in `LightingCalibration`, the shared frame calibration binding,
`pbr_photometric`, `LightingStorage` and component/history generation metadata. The existing
camera owner provides a frozen GPU scale publication, and atmosphere texture ownership supports
fixed-scale encoding with matching metadata. Producer conversion is an intermediate implementation,
not a releasable complete photometric pipeline until integrated qualification and appearance checks
are accepted. Scene/cache writes and connected display consumers have coordinated scale adoption.
The approved bounded signed-water estimator is integrated using a
global bound from the same published source convention; physical source adapters follow with
their producers. These support contracts have focused CPU/GPU and lifecycle checks, rather
than complete-scene photometric acceptance.

## Problem and intended result

VGE combines lighting inputs whose relative calibration is not defined. Terrain publishes the
engine's propagated block-light RGB directly beside atmosphere environment lighting. Direct
sunlight uses the atmosphere's relative solar reference, while engine point lights retain a
different diffuse normalization. Emission is an unqualified material multiplier. Exposure can
make the image viewable, but cannot repair incorrect ratios between these contributions.

The proposed result is a renderer in which source output, incident illumination and outgoing
surface brightness have declared photometric meanings. A lamp should retain its authored output
when moved between an indoor and outdoor scene. Sunlight, scattered sky lighting, propagated
block lighting and LumOn should remain comparable before exposure and display conversion.

This is an RGB approximation with physical units, not a spectral renderer or a claim that the
engine's existing voxel light propagation becomes physically accurate.

## Current implementation and capture evidence

The following owner inventory records the pre-conversion behavior that motivated this proposal.
The source implementation notes above and below describe the retained conversions; storage and
complete scene integration remain open.

| Owner | Current behavior relevant to this proposal |
| --- | --- |
| `PbrTerrainColorPatches` | Publishes block RGB plus atmosphere environment multiplied by propagated sky visibility. |
| `AtmosphereModel` and `AtmosphereLookup` | Use relative solar RGB; derive direct solar irradiance, sky radiance and integrated sky response together. |
| `pbr_direct_lighting` and `pbr_forward_surface` | Normalize solar diffuse by pi while retaining the legacy point-light convention. |
| `pbr_environment` | Applies material response to an integrated environment signal. |
| `lumon_pbr` and LumOn capture/gather owners | Transport and consume indirect signals whose precise stored quantities need reconciliation. |
| Camera exposure and owned final composition | Meter scene-relative luminance and apply an exposure multiplier before the existing display transform. |

The `ao-not-visible.rdc` investigation found working AO sampling and attenuation in the inspected
standalone composite. The checked attenuated pixels survived into final scene input. A
terrain-convention decomposition estimated that block light supplied about 96% of environment
input luminance in that frame; captured automatic exposure was +1.1206 stops. These observations
support investigating lighting calibration, not removing AO or using stronger AO to compensate.
The evidence is retained locally under `artifacts/AoNotVisible/findings.md`; those ignored
artifacts are diagnostic receipts, not a portable prerequisite for this proposal.

Current runtime contracts remain documented in [material and display boundaries](PBR.MaterialColorAndDisplay.md),
[lighting modes](PBR.LightingModes.md), [camera exposure](PBR.CameraExposure.md),
[shared display ownership](PBR.SharedDisplay.md) and [postprocessing](PBR.Postprocessing.md).
This proposal describes intended changes; it does not rewrite those documents as if implemented.

## Unit and color contract

Adopt the following authoring and evaluation quantities:

| Quantity | Unit | Meaning |
| --- | --- | --- |
| Lamp or point-source output | Lumens (lm) | Total visible-light output, before distance and receiver orientation. |
| Directional sunlight | Lux (lx) | Illuminance on a surface perpendicular to the incoming direction. |
| Evaluated point-source intensity | Candela (cd) | Directional output used to calculate illumination at a receiver. |
| Sky, emission and outgoing surface signal | Nits (cd/m²) | Luminance associated with a direction or an emitting surface. |
| Propagated block-light receiver approximation | Lux (lx) | Calibrated incident illumination represented by the engine field. |

This separation follows established physical-lighting practice; lumens are not a universal
buffer unit. See [Filament's lighting units and punctual-light evaluation](https://google.github.io/filament/main/filament.html#lighting/units).

Use linear Rec.709/sRGB primaries and the existing luminance weights
`Y(rgb) = 0.2126*r + 0.7152*g + 0.0722*b`. For physical RGB signals, Y must recover the
declared scalar photometric quantity. Normalize authored source chromaticity to unit Y before
multiplying by intensity. Authored display colors are decoded once before normalization;
engine brightness fields require their own adapter and are not automatically sRGB-decoded.
Zero-intensity sources produce zero without division by a zero color luminance.

Use one metre per world block as the verified spatial convention at the engine binding boundary.
Atmosphere distances remain kilometres internally,
with explicit conversion at shared spatial boundaries. Material reflectance, roughness,
metallicity, transmission, visibility and coverage remain dimensionless.

Keep incident illumination distinct from outgoing luminance. For a diffuse reference receiver,
`L = reflectance * E / pi`. An isotropic point light uses `I = lumens / (4*pi)` and
`E = I * max(NdotL,0) / distanceMetres²`, before visibility and documented near-source/range
regularization. These relationships provide measurable reference cases; source authoring and
GPU buffers must not interchange their quantities merely because both contain RGB.
See [Filament's punctual-light equations](https://google.github.io/filament/main/filament.html#lighting/punctuallights).

## Sunlight and atmosphere

The retained producer uses `AtmosphereModel.SolarAmplitude`, derived once from its zenith
transmission at 0.001 km and aerosol one. The GPU parameter owner submits the same amplitude;
unit-solar multiple-scattering tables retain their dimensionless convention. Direct solar RGB is
lux, sky/disk/aerial RGB is nits and integrated environment RGB is E/pi. Sun motion and weather
change transport, without refitting source power. Lookup encoding and full scene storage adoption
retain their separate integration obligations.

Retain the existing atmosphere model and its relationships between extinction, direct solar
illumination, sky scattering, the solar disk and aerial transport. Replace its arbitrary
amplitude with one declared photometric reference, applied consistently to every derived signal.

Propose a reproducible calibration reference of 100,000 lux of direct sunlight at perpendicular
incidence, with a zenith sun, altitude 0.001 km and aerosol setting 1. This is a chosen VGE
reference condition, not a claim that every real clear-sky condition has that illuminance.
Derive the model's solar amplitude from that reference once; do not renormalize sunlight as the
sun moves or weather changes. The model's resulting extinction and horizon visibility continue
to change illumination normally. Reference chromaticity and environmental settings must be
recorded with the calibration.

Sky lookup samples represent RGB luminance. Their cosine-weighted integral represents
illuminance. Where an existing environment consumer expects sky irradiance divided by pi,
publish the corresponding `E/pi` signal explicitly and retain that meaning in its name and
contract. Direct solar illumination is not added again into this scattered-sky term. Solar-disk
brightness before the display-storage cap must integrate back to the same direct solar reference.
The visible disk's capped output is a documented rendering approximation, not a replacement solar
amplitude. Aerial in-scattering uses the same amplitude; attenuation remains dimensionless.

## Propagated block light

`EngineLightingCalibration` fits the nominated torch against the active engine tables when its
decoder inputs change. It fits terrain's truncation after multiplication by 255.999 and the
accessor/dynamic truncation after multiplication by 255 independently. It publishes matching block
reference lux, neutral field floors, point candela
per payload unit and terrain/accessor sky endpoints through the shared frame owner. Terrain and
forward publishers convert each block input once before material response. Surface Cache direct
seeding reads its existing normalized RGBA geometry field, with nearest-byte accessor endpoints;
its differently decoded level/32 palette route no longer supplies block illumination. Standard/
entity/held meshes use accessor endpoints; transparent terrain uses terrain endpoints. Instanced
sky availability decodes its per-instance accessor and per-vertex terrain factors separately before
multiplication. Raw RGB products retain the engine's existing supplied-field compatibility semantics.
No CPU
light generation, field rewriting or source-resolved propagation is added.

Retain the engine's light generation, propagation, attenuation and enclosure behavior without
patching its lighting logic or rewriting its light data. Perform the block-light unit conversion
entirely in VGE shader code, using one shared GLSL helper consumed by terrain and other
applicable surface routes. VGE supplies the calibrated reference illuminance through its
shader bindings; the CPU does not convert or replace the engine's propagated field.
The adapter defines a reference illuminance in lux and a nonnegative monotonic response for
the legacy field. Installed source evidence establishes a nonzero level-zero floor, so raw
identity scaling is rejected. Remove that compatibility floor before applying the linear lux
scale; this is a shader interpretation change, not a propagation change.

The adapter separates the field's brightness and chromaticity without manufacturing light from
zero. Let `peak=max(legacyBlockRgb.r,legacyBlockRgb.g,legacyBlockRgb.b)` and let `floorPeak`
be the active engine decoder's level-zero peak. The corrected field is zero when `peak<=floorPeak`;
otherwise `correctedRgb=legacyBlockRgb*(peak-floorPeak)/peak`. This retains chromaticity and has
a monotonic nonnegative response. Converted RGB illuminance is `correctedRgb * referenceLux`.
This is a calibration mapping of an engine field, not a declaration that its original values
were physical measurements. The field's assumed encoding must be documented
from engine behavior; applying the texture sRGB function is not an established conversion.
The shader's integrated environment input receives the resulting `E/pi`, not raw 0–1 block RGB.
The floor-corrected mapping is evaluated by the shared helper as:

```glsl
vec3 rawBlock = max(legacyBlockRgb, vec3(0.0));
float peak = max(rawBlock.r, max(rawBlock.g, rawBlock.b));
vec3 correctedBlock = peak > blockLightFloorPeak
    ? rawBlock * ((peak - blockLightFloorPeak) / peak) : vec3(0.0);
vec3 blockIlluminance = correctedBlock * blockLightReferenceLux;
vec3 blockEnvironment = blockIlluminance / PI;
```

Terrain uses this helper through VGE's existing shader injection at environment publication
in `PbrTerrainColorPatches`; other applicable surface routes use the same conversion before
material response. Updating these VGE shader injections is within scope. Adding patches to
base-game CPU lighting or propagation methods is outside scope. Convert each field contribution
once, keeping atmospheric illumination separate so it is not scaled by the block-light reference.

Derive the block reference from the nominated lamp asset, the engine's level propagation and
brightness/color conversion, a documented reference receiver and the specified illumination.
The user's stated propagation rule decreases the encoded light level linearly with distance
through flood fill; preserve that rule rather than fitting inverse-square attenuation to it.
Verify the precise distance/occlusion and interpolation behavior against engine code before
claiming a receiver RGB. A linear encoded-level decrease does not imply that the brightness
table's decoded RGB also decreases linearly.

Use a reproducible source-derived fixture to calculate the shader multiplier and check additional
distances, enclosure and overlap. A controlled game capture is validation of that derivation,
not a mandatory prerequisite when the relevant inputs and transformations are established from
source. Request live measurements only for unresolved behavior that source evidence cannot
establish. Do not tune the reference by comparing automatically exposed screenshots or invent
real torch output from the existing light levels.

The aggregate engine field loses individual source identity and direction. Consequently this
adapter cannot recover each source's lumens, inverse-square falloff, additive overlap or proper
directional specular response. These limitations must remain explicit even after its receiver
signal is expressed in lux. Preserve engine overlap behavior and test it; do not claim that
the adapter implements physical source superposition.

Source-based replacement of voxel block lighting, new propagation, general area-light transport
and source discovery are outside this proposal. The approved solar-only finite-angular specular
response below is a narrow exception; it does not introduce general area emitters or area shadows.
The calibrated adapter establishes coherent brightness while retaining a bounded compatibility
approximation.

## Point lights, emission and receiver response

`PunctualLight` authors linear chromaticity and lumens, while the retained light list's explicit
authored capture converts isotropic flux to candela. Its positive payload W carries influence radius;
engine captures retain their original RGB and W zero. `VgePointIlluminance` adapts the latter using
the active reference, retains one-metre inverse-square saturation and fades smoothly from 80% of
`min(25,length(rawPayload))` to that radius. All direct diffuse sources share BRDF division by pi.
Direct/forward emission multiplies authored base RGB and coefficient by the 32-nit reference;
the cache retains its neutral emission and exclusive block-versus-emission policy.
Standard held-source collection retains innate plus separately admitted hands, rather than adding
the combined player payload again. Custom registered emitters can still overlap the voxel field;
this compatibility adapter does not infer identities or subtract fabricated source contributions.

Provide an explicit lumens contract for VGE-authored punctual lights. For engine-provided lights,
keep compatibility conversion separate: engine color/intensity/range payloads must not simply
be relabeled as lumens. Reconcile held lights with the existing engine field and dynamic arrays
so the same source is not accidentally counted twice. Preserve source positions and document
near-source regularization and any finite influence window in metres.

Apply the same BRDF normalization to sunlight and punctual lights in deferred and forward
shading. Retire the special omission of pi for legacy point-light diffuse once their inputs are
converted. Preserve transmission, energy partitioning and shadow ownership while checking
their units against the incident-light contract.

### Finite-angular solar specular response

The user approved this narrow refinement on 2026-10-11. Evaluate solar surface specular reflection
using the existing visible disk angular radius, `AtmosphereSolarDisk.AngularRadius=0.021377339`
radians, rather than treating the solar source as an infinitesimal direction. Share the response
between deferred and forward surface receivers and the separate `pbr_liquid.glsl` solar-reflection
call. Keep punctual lights punctual. Retain the calibrated direct solar irradiance, diffuse
response, existing shadow/sky visibility and atmospheric publication owner;
do not introduce additional solar power or change volume-scattering/transmission models.
Select the solar response explicitly at sunlight call sites. Angular radius describes emitter
extent and must not identify a light as solar; other emitter types can also have finite extent.

Select and qualify an energy-normalized finite-angular approximation before relying on its
output bound. Use the physical disk radiance from the matching atmosphere generation, independently
of its display-storage cap. The response must remain finite at grazing angles and minimum
roughness, retain material/Fresnel energy partition, and approach the directional response as
the angular radius tends to zero where that limit is finite. Establish the actual approximation's
range analytically and check its angular/material behavior numerically. A theorem for an ideal
passive reciprocal reflector alone does not certify the existing approximate BRDF or a newly
chosen approximation. The selected response's supported precision envelope remains a prerequisite;
this approval does not accept the rejected global storage guard.

The user subsequently approved implementing representative-direction evaluation with matching
view-dependent energy compensation. `includes/pbr_finite_disk_brdf.glsl` selects the mirror direction
when it lies inside the solar cone; otherwise it refines the half-vector alignment on the cone's
boundary. Effective source extent fades with `1-roughness^2`. The original GGX distribution uses
the selected half-vector, with an energy factor `aSquared/extendedASquared` determined by
roughness, source extent and view/half-vector alignment. Center-direction masking, diffuse
lighting and visibility retain their existing ownership. A zero source radius returns the
existing directional BRDF. This replaces the rejected simple NDF-width candidate, not the
material's authored roughness. Numerical/GPU qualification of this implementation and its
photometric storage envelope are separate obligations; no universal physical radiance bound
or integrated photometric migration is claimed by implementation alone.

### Emission and integrated lighting

Define emission as an explicit RGB luminance signal. Existing emissive material coefficients
remain dimensionless authoring data and need a documented reference-nits conversion. Retain
their authored appearance conventions through an explicit compatibility mapping, rather than
silently assigning each current coefficient a physical measurement. Glow/bloom eligibility
remains separate from emitted brightness.

Propagated block light remains an indirect/environment approximation and receives existing
indirect AO treatment. Direct sunlight and evaluated point lights continue to use their own
visibility rather than indiscriminate AO multiplication. Standalone and LumOn routes share
source calibration even where their transport approximations differ.

## LumOn and other scene contributors

Declare the quantity of each LumOn capture, probe, gather and composite boundary. Cached outgoing
surface luminance and an incident irradiance estimate are different signals. A consumer that
expects an integrated Lambertian-equivalent illumination must receive an explicit conversion;
it must not multiply already material-weighted outgoing lighting by the receiver response again
without a justified transport step. Trace the actual consumers before changing representation.

Remove hidden calibration factors or identify them as artistic multipliers. Physical defaults
use neutral multipliers, and diagnostics show when artistic controls depart from the calibrated
baseline. LumOn must neither re-add standalone environment illumination already represented by
its transport nor drop calibrated block illumination on receivers it cannot cover. Preserve the
current forward-receiver fallback policy until an equivalent transport replacement exists.

Water reflection/refraction, underwater in-scattering, transparent/late receivers, particles,
fog, celestial contributions and glare all need declared compatibility at the scene boundary.
Engine-authored effect colors may remain approximations, but their conversion must be explicit
and must not inject an unscaled display-space value into physical HDR lighting. UI and offscreen
display content remain outside scene-lighting and camera-exposure conversion.

## HDR storage and camera response

The public lighting contract uses physical units independently of the GPU storage representation.
The approved representation uses one pre-exposure multiplier for the current frame's transient
lighting, a fixed multiplier for persistent lighting, and an explicit visible solar-disk cap.
Retain current texture formats except the user-approved RGBA32F storage for the three engine
OIT accumulation layers. This changes stored magnitudes, not authored light output or
the lighting model; it does not authorize an unrelated HDR attachment redesign.

### Frame lighting and exposure

Let `P` be the finite positive frame pre-exposure multiplier. Store transient lighting as
`stored = P * physical`; decode as `physical = stored / P`. Apply that convention to each declared
light-bearing quantity, including environment `E/pi`, direct outgoing lighting, scene/OIT color,
water scattering source and generated glare. Coverage, attenuation, optical depth, visibility,
material data, confidence and history counts are not scaled.

The existing camera-exposure owner freezes `P` before the first scene draw, after the existing
current-frame light-list publication. Automatic mode starts from the previous successfully
published exposure multiplier; manual mode starts from the current bounded manual exposure plus
compensation. Startup, mode changes and resource retirement use a finite bounded manual seed
until automatic publication exists. A camera cut resets adaptation through
the existing owner; the frame's frozen `P` never changes midway through rendering. Qualify the
first frame and abrupt bright transitions rather than claiming a previous-frame scale anticipates
them. Keep exposure buffers on the GPU; no CPU exposure readback or per-frame allocation is needed.

The original transient-range investigation rejected the
candidate global rule `P=min(max(cameraMultiplier,1/16),16000/B)` using scene-wide worst-case
lighting bounds for the existing point-direction solar GGX response: it protects bright specular
storage by erasing calibrated indoor torch lighting
in the current packed scene format, even after tightening the indirect-light bound. Do not
implement that rejected rule or treat its passing arithmetic assertions as design acceptance.
Previous camera exposure alone also does not prove finite storage on startup or abrupt bright
transitions. Resolve both range and calibrated-lamp precision before selecting a safeguard.
The approved finite-angular solar refinement permits reevaluating a frame-wide safeguard with
its own certified response bound; the rejection above applies to the tested original response
and bound, rather than prohibiting all source-informed scale selection. The revised selection
rule, producer-admission equations and supported arithmetic domain are specified below.
Runtime enforcement remains part of the coherent conversion work.

Source bounds may still inform that qualification. With normalized geometry, bounded
reflectance/Fresnel, roughness >=0.04 and
`NdotH` clamped to [0,1], the direct specular contribution per unit incident RGB is bounded by
`1/(4*pi*roughnessMin^4*kMin) < 262144`, where `kMin=(1.04)^2/8`.
Include diffuse/transmission allowances, the point list's summed candela with its <=1 near-source
attenuation, local environment and emission when evaluating any proposed bound. Validate material/normal domains at
their existing boundaries; the current unclamped GGX dot is not proof of that future contract.
The 16,000 stored-unit primary budget is a tested headroom candidate, not an accepted global
source scale. Qualify weighted OIT accumulation and multi-source/glare sums separately;
their intermediates can exceed their final normalized color. Unsupported bounds must be reported,
not silently replaced by arbitrary source-intensity reductions.

#### Selected scale rule and supported arithmetic domain

For the approved finite-angular solar response and bounded water estimator, select the
source-informed rule `P=min(max(cameraMultiplier,1/16),16000/B)` within the following admitted
domain. `B` is a conservative maximum RGB component of the assembled non-disk scene bound,
including producer arithmetic allowances. If B is zero, use the desired camera/storage-floor
scale without division. This supersedes the earlier rejection only for the revised bounded
response and a valid assembled B; it does not rehabilitate the original punctual-sun budget.

Derive B from matching published source data rather than fixed experimental ceilings. Include
the current light list's summed converted candela, actual finite GI intensity/tint component
gains, calibrated block and emission limits, atmospheric lookup reconstruction bounds and the
nonrecursive water/air composition paths. Persistent data must pass its own fixed-scale
admission before entering this budget. For each convex temporal component, take the maximum
of its current physical bound and the retained originating physical bound, then sum the
components used by the current frame. A maximum of two whole-frame bounds is insufficient:
new direct light and bright retained indirect light can appear together. Incompatible history
is withdrawn through its existing validity owner; ordinary compatible history is included.

Round B upward when publishing Float32 metadata and P downward when selecting the scale.
Require positive finite P and finite scale ratios/rescaled intermediates, not just a finite
normalized result. Derive the lower supported P from retaining the existing calibrated warm
torch RGB fixture, without introducing a new minimum signal or a universal appearance-error
criterion. Under the recorded analytical allowance and nearest format conversion, packed
storage requires `P>=7.1479435064e-6`; binary16 alone requires `P>=2.2337323458e-7`.
The packed requirement governs the current pipeline. It permits a nominal B up to about
2.2384 billion nits, subject to conservative metadata/scale rounding at the boundary.
This is a representation domain, not an authored source-power ceiling. It applies to the
nominated pure 18% Lambert fixture; other BRDFs, visibility and materials can legitimately
produce darker signals. Single-store tolerances and later integrated precision checks remain.

If an input, persistent admission, assembled B or scale handoff is unsupported, report it at
the existing publication/rendering boundary. Retain a prior completed atmosphere generation
where that owner permits it, or reject the incompatible publication/draw through the existing
failure contract. Do not clamp lamp power, fabricate valid metadata or force P upward past the
range guard. Bound calculation uses existing source/configuration/history publication; it
does not discover lights, meter pixels or read camera exposure back to the CPU.

The 20-check arithmetic receipt covers strict nearest-even retention boundaries, outward bound
and downward scale rounding, zero B, exposure seeds, component history under source removal,
nonunit GI tint, custom candela sums, unsupported inputs and the four conditional water-frame
fixtures. All four fixtures fit the supported domain. It qualifies this selection rule given
valid producer bounds, not every producer admission or an integrated GPU implementation.
Receipt: `artifacts/PhotometricLightingPrerequisites/frame-scale-domain-qualification.json`.

The assembled budget has the following conservative scalar form. Let `S` be maximum solar
lux RGB, `D` maximum uncapped disk radiance RGB, `J` the maximum component of the summed
converted point candela, `E` maximum environment E/pi, and `K` maximum converted block lux.
Let `G` be the admitted mode-specific indirect bound: standalone `E+K/pi`; LumOn the maximum
of its gained/tinted SH gather bound and the block fallback, preserving mode exclusivity.
Use actual emission bound `F` and actual remaining additive compatibility bound `R`.

```
surface = 0.9*D + (66560 + 1/pi)*J + 2*S/pi + G + F
waterReflection = 2*D + 200000*J + E
boundaryScattering = 64*S + (K + pi*E)/(4*pi)
surfaceScattering = (2/0.0722) * (64*(S + J) + (K + pi*E)/(4*pi))
airSky = 3*A
B = surface + waterReflection + boundaryScattering + surfaceScattering + airSky + R
```

Before summing, replace each retained component by the maximum of its new and originating
physical bounds. Add outward producer arithmetic allowances. This counts one visible sky,
two nonrecursive air contributions and the existing surface-medium composition; additional
composition paths require their own bound. `A` can use the current publication's tighter
reconstruction bound. Independently admitted smooth/Mie components at fixed C=1/16 give
the representation bound `A=3598265.112997` nits, including the recorded single-store allowance;
this is not a claimed all-weather physical maximum.

Nine arithmetic checks qualify this equation for four conditional standalone/LumOn day/night
fixtures. Their B values range from 478.93 to 881.82 million nits; downward-rounded P ranges
from 3.34078e-5 to 1.81443e-5, retaining the nominated torch RGB and storing B below 16,000.
The fixtures use F=32 and R=32; neither establishes a universal bound for other materials or
legacy effects. Producer admission, matching history, the stated transport topology and later
GPU arithmetic validation remain explicit premises.
Receipt: `artifacts/PhotometricLightingPrerequisites/assembled-frame-budget-qualification.json`.

Retain the frozen scale publication through postprocessing even when metering publishes a new
exposure. Histogram input is `stored / P`, so it measures physical scene luminance. Final display
uses `stored * exp2(currentEV) / P` before the existing tone curve. Bloom and shaft thresholds
evaluate that same exposure-relative brightness and return output in the frame's stored units.
This preserves one effective camera exposure, rather than applying pre-exposure again at display.

Transient history carries the scale under which its RGB was written. Before blending an old
stored sample with current lighting, multiply it by `P_current / P_history`; validity and other
metadata are unchanged. Invalid or unavailable scale metadata makes that history incompatible.
Settings/reload/resize/world transitions use existing retirement and publication boundaries.
Generated bindings and capability checks must cover the frozen-scale and display-exposure reads.
Scale correction alone does not qualify range: a retained history can contain brighter physical
lighting than the current source budget. Include its originating physical bound when budgeting
the blend, or withdraw incompatible history through the existing validity owner. Qualify the
rescaled intermediate before filtering/blending, including abrupt source removal; matching
`P_current/P_history` identities are insufficient evidence of finite storage.

### Persistent lighting and atmospheric lookups

Persistent Surface Cache, world-probe and probe-lighting storage uses a fixed multiplier
`C = exp2(-cacheStorageEV)`, initially `cacheStorageEV = 4` (`C = 1/16`). Cached incident lux,
outgoing nits and packed emission each retain their declared quantity while using that multiplier.
Cache producers encode once; tracing/shading consumers decode once before physical evaluation,
or use the equivalent `P/C` conversion when publishing transient stored lighting. Blending cache
history does not depend on camera exposure, and ordinary exposure changes do not invalidate it.
Changing `C` retires incompatible cache generations through their existing owners. The four-stop
default is selected within the admission domain below, not a guarantee that every material or
source range fits.

Atmosphere computations and immutable CPU lighting snapshots remain physical FP32. Long-lived sky
and aerial luminance textures use a fixed generation scale, initially the same `1/16` candidate;
their publication includes that scale alongside the existing matching snapshot/textures. Consumers
decode or convert to `P` explicitly. Attenuation is unchanged. A pending atmosphere result retains
the previous complete generation and its scale; changing camera exposure does not rebuild LUTs.
The separately evaluated solar disk is not baked into these lookup textures. Confirm lookup and
cache range/precision at their separate admission boundaries.

The selected persistent representation admits finite nonnegative scalar signals through
`65504/C=1,048,064` in their declared physical quantity. Validate before conversion and before
publication, including intermediate products; defensive storage saturation must not disguise
failed admission or establish validity (see "Defensive storage clamping").
The current jointly projected SH9 route admits nonnegative directional inputs through
131,072 nits per component. Its conservative signed coefficient bound including the recorded
arithmetic/storage allowances is 1,039,487.351, below the scalar ceiling. Signed coefficients
retain their signs. This directional limit belongs to the probe/SH representation, not to the
atmosphere integrator or every source. Other SH evaluators require their own contract.

Compatible convex histories preserve admitted bounds. Recursive cache lighting still needs
a precommit range check: convex temporal weighting is not a proof of multi-bounce convergence.
Unsupported cache/probe updates retain or withdraw data through their existing validity and
publication owners; they must not clamp source power or mark overflowed data valid. Atmosphere
smooth/Mie components separately pass the fixed-C packing check, while their reconstructed
angular bound comes from actual completed-generation arrays and phase bounds. The ten-check
receipt verifies endpoints, overflow rejection, signed coefficients, current source hashes
and convex-history admission. It qualifies the representation domain, not every authored
source or atmosphere configuration. Receipt:
`artifacts/PhotometricLightingPrerequisites/persistent-admission-qualification.json`.

Admission remains directional through the existing owners:

| Producer boundary | Required bound and failure rule |
| --- | --- |
| Atmosphere completed snapshot/texture upload | Check finite physical Solar/Environment and every packed smooth/Mie component; publish scale and reconstructed directional bounds with the same generation. Failed generation retains the previous complete publication. |
| Engine block adapter | Derive floor, calibration and maximum from the active decoder/table; convex field interpolation cannot exceed that decoder maximum. Invalid calibration is rejected before publication; field conversion remains shader-only. |
| Point-light publication | Use the actual converted component sums for the existing list, including custom payloads. Invalid or unsupported input fails the current publication rather than borrowing a stale list or reducing lamp output. |
| Materials and artistic gains | Use actual finite emission and intensity/tint component bounds, with the declared normalized geometry/material domain. Changed source/configuration bounds participate in the same frame/calibration generation. |
| Cache, directional probes and histories | Validate the appropriate fixed-C or directional domain before commit; include retained originating component bounds. Failed estimates do not become valid black samples or age retained history. |
| Water capture/composition | Use the shared U and finite source/optical-depth accumulation bounds; invalid metadata uses the existing transport validity/fallback path. Reflection/refraction snapshots retain their nonrecursive source and matching frame scale. |
| Other scene effects and OIT | Declare compatibility luminance and actual component bounds at the existing scene adapter before weighting/blending; include additive contributions in B. Check alpha, depth/weight and accumulation domains separately; UI/offscreen content remains excluded. |

These are implementation obligations of the selected domain, not claims that the legacy
runtime already enforces them. No new source discovery, light registry or global validation
framework is required. Existing parameter preparation, snapshot packing, shader evaluation
and publication/validity boundaries perform the checks.

Water reflection/refraction snapshots inherit the current frame's `P`. Cross-frame reuse requires
their own scale metadata or existing invalidation. Camera-medium inputs remain raw engine data
until shader calibration; signed optical depth remains unscaled. UI and offscreen display routes
do not participate in these scene-storage conversions.

### Defensive storage clamping

Use pre-exposure and conservative source/history bounds as the primary range control, with
targeted shader clamps as a final storage safeguard. This amendment was approved on 2026-10-12.
It supplements the selected storage contract; it does not change source calibration, frame-scale
selection, fixed persistent scaling or the existing admission/failure rules.

At each selected lighting write, apply the storage scale and contain the result before narrowing,
packing or writing the destination. Document the actual destination format, channel semantics,
clamp range and any remaining blending headroom. Nonnegative binary16 lighting uses [0,65504];
signed binary16 lighting coefficients use [-65504,65504]. Packed float and FP32 targets require
their own representable limits and arithmetic allowances; do not apply the half-float ceiling
universally. Alpha, depth, optical depth, weights and validity metadata retain their own contracts.
Retain the separate visible-disk cap and its reserved glare/composition headroom.

Shared shader helpers must explicitly handle exceptional values rather than depending on min/max
NaN behavior. Positive infinity maps to the upper storage endpoint; negative infinity maps to
zero for nonnegative lighting or the lower endpoint for signed lighting. NaN uses the existing
owner's invalid-sample/fallback path; a neutral finite payload, if needed for a write, must not
turn an invalid cache/history publication into valid black lighting. Source inputs and metadata
remain subject to admission before shading. A finite contained output does not prove that its
original calculation was valid. Keep intermediate products, sums and rescaling safe independently.

Audit and protect atmosphere lookup packing, cache/probe encoding, reflection/refraction,
temporal history and scene-composition writes, then exposure/glare/postprocessing outputs where
their formats and arithmetic require it. Place protection at documented boundaries rather than
after every BRDF operation. Existing publication owners continue rejecting unsupported persistent
updates; clamps contain exceptional payloads without authorizing their reuse as valid history.
Use existing diagnostics or focused test instrumentation to expose unexpected clipping, with no
per-frame CPU readbacks or new diagnostic manager. Supported ordinary lighting must remain
unchanged by these clamps; intentional visible-disk clipping remains separately documented.

A fragment clamp cannot guarantee that additive blending or weighted OIT accumulation stays
finite. Qualify accumulated sums, weights and resolve arithmetic separately using the existing
bounds and approved RGBA32F accumulation. Reserve headroom before blending; a clamp after resolve
cannot repair overflow that already occurred. This amendment authorizes no further target-format
changes, new passes or transparency-algorithm replacement.

Validation must exercise destination limits, over-range finite values, both infinities, NaN,
signed coefficients, scale/history transitions and combined accumulation. Verify finite stored
payloads, preserved validity/fallback behavior and unchanged ordinary supported values. Unexpected
clipping remains a reported range defect even when containment prevents visible infinity artifacts.

### Visible solar-disk protection

Compute physical disk luminance from the calibrated atmosphere without clamping its solar input.
At the visible disk's scene-output boundary, cap `P * diskLuminance` componentwise at 64,000.
Reserve 16,000 stored units for bounded non-disk contributions. The effective disk-write ceiling
is at most 48,000 and decreases when glare strengths require further headroom, using the formula
in the fresh qualification receipts below. This avoids needing framebuffer reads
to discover remaining headroom. It is a documented headroom refinement of the 64,000 maximum,
not a change to physical disk luminance. Prove the non-disk budget in combined-output fixtures.
The cap is in stored units and stays below the finite limits of both half and packed scene formats.
Qualify disk blending with the background and other contributions. This protection belongs to the disk draw, not to
direct solar illuminance, diffuse sky illumination, the atmosphere amplitude or all light sources.
Diagnostics distinguish uncapped physical disk luminance from the capped scene contribution.

The existing atmosphere binding owner computes the visible disk's maximum storage scale on
the CPU from that same physical RGB and the captured glare strengths. Evaluate the cap and
cap-to-peak ratio in double precision, round the cap down to a float, and reserve one downward
float step in the scale for multiplication rounding. Publish that limit as a solar-draw uniform;
the fragment shader selects `min(P, diskMaximumScale)` before multiplying physical disk RGB.
Invalid disk or glare inputs produce an unavailable (zero) scale. This calculation preserves
the existing shader language versions and requires no `precise` qualifier or GPU readback.

This intentional high-end approximation does not promise <=1% physical-value precision for every
simultaneously visible dark value. The earlier 0.001-nit interval is an illustrative stress test,
not an accepted minimum or a release gate. Retain analytical and per-format quantization bounds;
qualify accumulated transport, temporal reuse, metering and exposed output separately across
daylight, lamps, interiors and night. Do not excuse a visible or transport-relevant loss merely
because the final tone curve hides the disk's cap. Reject nonfinite inputs and report unsupported
source ranges rather than introducing undocumented per-pass clamps.

Keep metering after scene composition and before generated glare/display conversion. Rebase
histogram bounds and exposure limits for the declared scene scale. Preserve the current meaning
of exposure stops as log2 of the display multiplier; distinguish physical calibration from user
exposure compensation. A photographic EV100 authoring model may be considered separately and
must not be claimed merely because the lighting has physical units.

The existing global tone curve can remain initially. Fixed-exposure validation must demonstrate
correct source ratios before automatic adaptation is used. Glare thresholds must state whether
they operate in physical or exposed units. Camera exposure must not feed back into authored
light power, AO, material values or physical propagation.

## Ownership and compatibility

Keep block-field conversion in the shared VGE GLSL helper; lighting owners supply its calibration
parameters through the existing binding system. CPU bindings, generated shader interfaces and diagnostics should identify quantities
and units. Shader compilation, reload, resize and world teardown retain existing ownership;
do not add per-frame source discovery or resource reallocation to establish calibration.

Existing saves and third-party sources receive documented compatibility defaults through the
existing owners. The user approved retaining saved camera stops without automatic conversion or
a configuration version marker; new or missing settings use exposure limits -20/+2. The
maximum limits dark-scene amplification to four times; explicit saved values remain authored settings.
Saved values may require user adjustment because the physical scene scale changes brightness. The goal
is coherent lighting, not exact preservation of the previously uncalibrated screenshots.
Calibration or storage changes invalidate dependent cached lighting and histories through
their existing owners; they must not leave differently scaled old and new lighting mixed.

## Validation and acceptance

Automated validation must cover the following independent properties:

- Scalar intensity and normalized RGB luminance agree; invalid/zero source inputs remain finite.
- A diffuse reference receiver follows `reflectance * E/pi`, with the same normalization in
  sunlight, point-light, deferred and forward routes.
- A synthetic isotropic 1,000-lumen point source produces the expected illuminance at one and
  two metres outside any regularized region; doubling distance quarters its illumination.
- The atmosphere meets its chosen reference condition and preserves consistent sun, sky,
  solar-disk and aerial-scattering amplitude across solar elevation and weather settings.
- Block-field conversion is monotonic, zero-preserving and exposure-independent, with recorded
  reference-fixture agreement and documented nonphysical overlap/falloff limitations.
- Terrain and forward routes use the shared shader conversion exactly once, with unchanged
  engine light data and no new patches to base-game lighting generation or propagation logic.
- Emission, LumOn transport, scene composition and indirect AO use each conversion/material
  response once; deterministic source-isolation cases cover both lighting modes.
- Storage and temporal data remain finite and sufficiently precise for the accepted daylight,
  solar-disk, lamp, interior and night range, including resize/reload and calibration changes.
- Defensive storage clamps satisfy the format, exceptional-value, validity and accumulation
  requirements in "Defensive storage clamping"; ordinary supported lighting does not clip.
- Metering and final display preserve source ratios; changing exposure does not change light
  output or feed generated bloom and shafts back into metering.

Quantitative tolerances must be stated before implementation qualification, with separate
allowances for analytical arithmetic, texture quantization and approximate propagation/transport.
Reference scenes use fixed exposure, neutral grading and isolated sources, then combined sources.
Broader in-game acceptance checks sunlight versus lamps, indoor/outdoor transitions, night
visibility, transparent/held surfaces, water and LumOn on/off with automatic exposure restored.
Automated receipts establish numerical and lifecycle behavior; user-run inspection establishes
whether the chosen game lighting and adaptation are acceptable.

## Prerequisite source reconciliation

The following inventory was checked against current repository sources on 2026-10-11.
It describes current behavior and the intended quantity boundary, not a completed conversion.
Source paths below are relative to `VanillaGraphicsExpanded/`; shader paths are relative to
`assets/vanillagraphicsexpanded/shaders/`.

| Boundary and owning source | Current signal and normalization | Required physical boundary |
| --- | --- | --- |
| `PBR/PbrTerrainColorPatches.cs`, `PBR/PbrSurfaceShaderPatches.cs` | Raw nonnegative engine block RGB plus atmospheric `Environment` times local sky visibility; generic paths divide sky by 0.35 before `VgeLocalEnvironment` multiplies it back. | Shared shader converts block RGB to lux, then `E/pi`; atmospheric `E/pi` remains separate. Remove the cancelling legacy sky factors during migration. |
| `PBR/Atmosphere/AtmosphereModel.cs`, `AtmosphereLookup.cs` and `AtmosphereSolarDisk.cs` | Relative solar irradiance; sky integration publishes `E/pi`; directional sky/disk/aerial samples are relative radiance. Disk uses `Solar/(visible*pi*sin(radius)^2)`. | One reference amplitude scales direct lux and every derived luminance signal together; attenuation and visibility retain no intensity scale. |
| `Rendering/VgeLightsRenderer.cs`, `VgeLightsUniformBuffer.cs` | Copies engine view-space positions and `PointLightColors3` unchanged, capacity 100. No separate range is stored in this buffer. | Engine payload adapter must recover its brightness/color/range convention before assigning source lumens/candela. Bindings preserve engine data. |
| `pbr_direct_lighting.fsh`, `includes/pbr_forward_surface.glsl` | Solar diffuse divided by pi; point diffuse not divided by pi. Point attenuation is `min(1/distanceSquared,1)` with direction epsilon 0.0001 and no finite shader cutoff. | Shared normalized receiver response after source conversion; document the one-metre saturation region separately from the direction epsilon. |
| Direct emission in those same shaders | `baseColor * max(emissiveCoefficient,0)`; generic meshes obtain the coefficient from glow level. | Existing coefficient remains dimensionless; explicit reference nits supplies luminance. Glow eligibility stays separate. |
| `lumonscene_surface_lighting.csh`, `LumOn/Scene/SurfaceLightingDispatch.cs` | Direct cache seed is `32*blockLevel*lightColor + skyLevel*(pi*Environment + Solar*NdotL)`; missing atmosphere uses `Environment=32/pi`. Indirect uses `pi*mean(outgoingHitRadiance)`; combination is `(direct+indirect)*albedo/pi + emission`. | Direct/indirect cache RGB are incident lux; published outgoing RGB are nits. Preserve the estimator and apply hit and receiving albedos at their respective bounce, once each. Retire hidden 32-unit calibration and missing-atmosphere intensity fallback coherently. |
| `LumOn/Scene/LumonScenePbrSurfaceLutRegistry.cs` | Cache emission is neutral `32*Emissive`, binary16 packed in the immutable surface LUT. `SurfaceLightingMaterialEmission` selects aggregate block light or material emission globally; enabling emission excludes all block light. | Declare a separate legacy neutral-emission adapter and preserve this exclusive accounting policy until an approved replacement exists. Do not enable both aggregate and source emission without accounting for overlap. |
| `includes/lumon_surface_lighting.glsl`, `lumon_near_field_hit_lighting.glsl`; world-probe GPU trace/commit | Read the cache's outgoing surface RGB without another hit-material multiply; world-probe atlas stores directional outgoing radiance. CPU completion keeps the same signal. | Directional nits; no albedo/pi at an already shaded hit. CPU legacy evaluator is a separate engine-relative approximation, not a physical source. |
| `lumon_probe_atlas_trace.fsh` and temporal/filter/project stages | Hit radiance passes through into directional atlases; emission-only fallback uses baseColor*coefficient*GI boost (and tint), while supported cache hits replace it. Sky misses use `ambientColor*SkyMissWeight`, a coarse hemispherical response. | Nits throughout directional storage. Map environment `E/pi` to the explicitly declared constant-radiance sky approximation; make boost/tint/sky weight artistic controls, with neutral physical defaults and no repeated tint. |
| `includes/lumon_sh9.glsl`, `lumon_probe_sh9_gather.fsh` | Cosine SH convolution divides irradiance by pi, then gather applies intensity/tint. | Gather is incident `E/pi`, not already shaded receiver output: receiving albedo in `lumon_pbr` is warranted. Keep signed SH coefficients distinct from nonnegative evaluated light. |
| `lumon_probe_atlas_gather.fsh` | Cosine-weighted average `sum(L*cos)/sum(cos)` without octahedral solid-angle weights; equals constant input L, but is an approximate angular integral for varying L. | Declare approximate `E/pi`; do not insert another pi merely because the shader calls the value irradiance. Angular estimator error needs its own fixture, not an arbitrary global intensity correction. |
| `includes/lumon_worldprobe.glsl`, `includes/lumon_worldprobe_gather.glsl` | World diffuse sampling returns `pi*(avgBlock + skyTint*skyVisibility)`; fallback currently copies it directly into the screen gather's `E/pi` slot. Directional world sampling instead returns radiance without pi. | Keep world diffuse evaluation as incident lux, then divide by pi exactly once at the gather fallback boundary. Keep directional sampling in nits. Constant-radiance fixtures must agree between screen and world replacement before artistic intensity/tint. |
| `includes/lumon_pbr.glsl`, `pbr_composite.fsh` | Applies receiver material response and indirect AO to gathered illumination once. LumOn excludes standalone environment; missing publication yields zero GI. Composite receives unit intensity/tint because gather already applies configuration. | Nits after material response. Preserve environment exclusivity, readiness policy and indirect-only AO. Forward receivers retain their own local block fallback. |
| `PBR/Liquids/LiquidShaderProgram.FrameInputs.cs`, liquid lighting and `WaterVolumeRenderer.cs` | Liquid uses solar, sky response, raw block and engine point inputs; camera-medium source currently combines CPU-sampled raw block RGB and `Environment`, times scattering/(4*pi). Signed volume source/optical depth accumulate separately. | Convert raw block in shader, including the camera-medium route: CPU may publish the raw field but must not perform the physical conversion. Declare incident irradiance versus isotropic radiance before phase-function integration. Optical depth is dimensionless; source per length is luminance/metre. |
| `WaterRefractionScene`, `pbr_composite`, liquid reflection/refraction | Unexposed outgoing RGB copied into pre-transport receiver snapshots and read along bent paths; world receiver validity and depth remain metadata. | Compatible stored luminance at every sample/blend. No new material weighting, display decode or exposure on copied lighting. |
| `PBR/SceneColor/SceneColorLegacyPatches.cs`, particle capture, owned sky effects and fog | Authored scene RGB is decoded to linear before blending; stars/moon/clouds/particles/fog/night vision have no physical amplitude. Alpha/glow/revealage remain data. | Explicit compatibility reference nits per existing effect policy, applied once at scene publication; no claim of physical lunar/cloud transport. Offscreen/UI never receive this conversion. |
| `PBR/CameraExposure`, owned bloom/shafts/luma/final | Meter unexposed completed scene; gather glare afterwards; retain unexposed RGB until one final `exp2(EV)` and display conversion. Bloom/shaft thresholds use exposed brightness; shaft solar tint is normalized rather than multiplied into scene light again. | Metering decodes storage to the declared luminance; display scale accounts for storage exactly once. Retain ordering, artistic glare controls and scene-relative stops. |

The source inventory supersedes historical descriptions where they conflict. In particular,
`PBR.LightingModes.md` retains the old 0.35 ambient approximation, `PBR.EntityAndLateCoverage.md`
retains a pre-HDR transparency description, and `PBR.MaterialColorAndDisplay.md` calls the
composite RGBA16F. Current source and `PBR.Postprocessing.md#packed-hdr-composite-color` use
R11F_G11F_B10F for composite RGB. These are documentation reconciliation items for the
physical migration, not permission to restore old behavior. The controlling cache quantities
are in [Surface Cache lighting](LumOn.SurfaceCache.LightingContract.md); current atmospheric
publication, aerial and water boundaries are in [publication](PBR.Atmosphere.Publication.md),
[aerial transport](PBR.Atmosphere.AerialPerspective.md) and [water units](PBR.WaterMedium.md).

### Confirmed spatial and color decisions

Retain one metre per world block. `AtmosphereModSystem.Update` converts player altitude above
sea level with `0.001`, and the established water contract evaluates block distances directly
against inverse-metre coefficients. Engine terrain vertices plus chunk origin are transformed
into view positions without another world-length scale; shared point positions are view-space
coordinates. This confirms a renderer convention, not a measurement of a real-world game object.

Retain the proposed 100,000-lux zenith solar reference, altitude 0.001 km, aerosol 1.
Normalize only the common amplitude of the existing solar RGB `(1.474,1.8504,1.91198)` against
the reference model result; preserve the model's relative chromaticity/extinction. The reference
excludes extra artistic boosts. Existing ground-albedo/multiple-scattering settings must be
recorded for sky/aerial fixtures, even though they do not change the direct extinction reference.
The game disk radius remains `0.021377339` radians; do not silently substitute the real sun's
angular radius when evaluating storage or integrated disk illumination.

Authored display source colors are decoded once, negative components rejected/clamped under
the existing owner policy, and normalized by Rec.709 Y. Zero intensity or zero valid color
produces zero. Nonfinite scalar/color inputs must be rejected with an existing diagnostic
before publication; do not divide by zero or retain NaN/Inf in a supposedly valid source.
The aggregate engine field instead preserves its engine HSV/brightness conversion. Current
`TraceGeometryLightDecoder` uses actual engine block/sun level tables and byte-normalized HSV
RGB; the Surface Cache legacy scalar table is `level/32`, not that same decoder. Their raw
values must be compared in the fixture before sharing calibration parameters. No blanket
texture-sRGB decode is justified by these field encodings.

### Reference fixture and tolerances

The user selected the normal torch as the baseline. Use `game:torch-basic-lit-up`, installed
survival torch HSV `[4,4,14]`. Record exact installed asset/mod versions, the source-derived
propagation/brightness/color rules, source/receiver coordinates and calculated raw input RGB
before selecting the shader's reference lux. Label derived values separately from live observations.

The external reference is [Medina-Alcaide et al. (2021), Table 1](https://journals.plos.org/plosone/article?id=10.1371/journal.pone.0250497#pone-0250497-t001)
and [S4 Appendix, experiment 5](https://journals.plos.org/plosone/article/file?id=10.1371/journal.pone.0250497.s004&type=supplementary):
a dry juniper/birch torch with deer marrow and no added resin. Mean measured illumination
was 21.94 lux at 0.4 m. Table 1 reports 3.51 cd, consistent with `21.94*0.4^2`; the main
text's 14.04 cd conflicts with that table and is not used. Isotropic-equivalent output is
`4*pi*3.5104 = 44.11 lm`. This is a single experimental construction, not a population
average or directly measured total flux. The five-torch mean mixes fuels and is not a
wood-and-fat-specific reference. Table/appendix were visually inspected; raw local copies
are diagnostic receipts under `artifacts/PhotometricLightingPrerequisites/`.

Adopt 44 lm as the documented nominal compatibility candidate, giving 3.5014 lux at one
metre under the declared isotropic approximation. Retain engine warm chromaticity; the paper
does not establish that RGB or a source-flame luminance. Its reflected-surface luminance
must not be used as emissive flame nits. This anchors a stationary mean without adding
combustion/flicker simulation. The value remains subject to the source-derived engine-field fixture;
it does not by itself determine the shader multiplier or calibrate every engine emitter.

For an optional controlled live validation, use a sealed room with one lamp, no held/emitting items,
no other emitters and receiver sky
visibility zero. Record the torch cell and actual flame reference position; put matte neutral
receivers facing it at documented one-, two- and four-block separations. Receiver distances
are relative to that nominated position; the aggregate field has no true point-source origin.
Disable automatic exposure, set manual exposure/compensation to zero and grading to neutral;
disable glare during measurements. Keep these settings unchanged between captures. Measure
the raw vertex/field payload and interpolation at the reference receiver, not encoded final
pixels. A RenderDoc mesh/input inspection or the existing engine field accessor can establish
raw field values. Synthetic or source-derived propagated levels are not observed live evidence;
they may establish the calibration when their engine transformations and settings are proven.

For floor-corrected response, if the corrected reference field has luminance `Y_B > 0`, choose
`referenceLux = targetReceiverLux/Y_B`. Then `E_rgb=correctedRgb*referenceLux` and integrated
environment input is `E_rgb/pi`. This recovers the chosen scalar target without changing
engine propagation. Additional distance, enclosure, colored-lamp and two-lamp overlap cases
check monotonicity, zero preservation and unchanged engine behavior; they do not fit an
inverse-square curve or assert physical superposition of the aggregate field. Record them
with LumOn disabled first, then compare source publication in both modes and held/placed
representations. Held-source collection currently replaces the combined player emission
with innate plus separately admitted hands; this avoids that specific combined-array duplicate,
but does not prove the game field and array cannot overlap in every receiver route.

Fresh installed-owner inspection distinguishes the standard routes: player-held emission is
collected into dynamic position/color arrays, while the block accessor reads stored chunk light
seeded by placed blocks. The audit covers five installed assemblies and six narrow checks;
no feed of standard held emission into the propagated field was found in that direct-call
inventory. VGE attachment updates only move already admitted dynamic entries. This supports
separate accounting for the inspected standard held and placed sources, without proving global
disjointness for custom registered lights, collectible overrides or indirect/modded paths.
Preserve those compatibility limitations rather than inventing source-resolved subtraction.
Receipt: `artifacts/PhotometricLightingPrerequisites/EngineFieldReceipt/held-source-routing-receipt.json`.

Qualification tolerances are separated before downstream implementation. The numerical
fixture tolerance measures implementation agreement with the chosen nominal value; it
does not claim 1% accuracy for a fluctuating real torch or the isotropic approximation.

- Float analytical conversion: absolute error <= `max(1e-6,1e-5*abs(reference))` in the declared
  quantity; unit-normalized color Y uses absolute `1e-5`. Numerical zero must remain zero.
- One binary16 round trip: <= 0.05% relative for normal values, plus half a subnormal quantum
  in decoded units; round-to-nearest is the numerical reference, not a promise about every GPU.
- One R11/RG channel round trip: <= 0.8% relative for normal values; B10 <= 1.6%, plus half
  the relevant subnormal quantum in decoded units. A driver-specific GPU allowance must be
  justified by actual conversion behavior. Do not apply a one-store bound to an entire chain.
- Engine fixture mapping: scalar reference <= 1% relative plus documented byte/interpolation
  quantization; zero field remains zero. Source-derived fixtures check exact encoded levels and
  interpolation rules; repeated same-scene captures can additionally establish sampling
  reproducibility. Non-reference falloff/overlap are compatibility checks, not analytical
  inverse-square tests. LumOn angular/Monte Carlo errors require independent integration and
  variance receipts; they cannot be excused by the engine fixture's tolerance.

Independent diffuse fixture: pure Lambert reflectance 0.18, incident 1,000 lux, perpendicular
normal and full visibility gives `L=180/pi=57.2957795` nits. A 1,000-lumen isotropic point gives
79.5774715 lux at one metre and 19.8943679 lux at two metres, outside any regularized region.
Use the analytical point case with regularization disabled or a radius below one metre; it
does not validate the current engine shader's saturated inner region. A 60-degree incidence
halves illumination; zero visibility produces zero reflected light. Production Fresnel/metal
fixtures include their separately computed dimensionless energy partition instead of comparing
a dielectric BRDF against a pure Lambert value.

Independent angular diagnostics compare constant, linear and quadratic directional fields at
three receiver normals against analytical E/pi and equal-solid-angle sphere quadrature at
128x256 and 256x512 directions. Across these nine smooth, fully valid scalar fixtures, maximum
SH9 relative error is 1.42%; the legacy unweighted octahedral gather reaches 5.46% at stride one
and 6.50% at stride two. Constant SH response is 1.000001476 from the rounded basis. These
are measured approximation errors, not new acceptance tolerances or all-field error bounds.
They exclude texture quantization, probe interpolation, confidence, occlusion, tint and GPU
arithmetic. Receipt: `artifacts/PhotometricLightingPrerequisites/angular-reference-results.json`.
An independent hemisphere-moment and quadrature-convergence recheck passes 18 assertions;
the finest sphere grid differs from the analytical reference by at most 0.0152% across these
fixtures. Receipt: `artifacts/PhotometricLightingPrerequisites/angular-reference-validation.json`.

A separate controlled cosine-sampling diagnostic uses the field `1+0.75*cos(theta)`, 64 rays
per batch and a selected retained-history count cap of four. Its analytical E/pi mean is 1.5;
independent-batch variance is 0.00048828125, and saturated history variance is that value divided
by nine (blend weight 1/5). Across 32 deterministic seeds, 256 updates each and 64 warmup
updates, measured variances are 0.000504229 and 0.0000551032 respectively. This isolates the
sampler/history formula with fully resolved illumination; it does not measure production hash
correlation, missing geometry, recursive cache feedback or storage error. The history cap is
a diagnostic setting, not a new runtime default. Receipt:
`artifacts/PhotometricLightingPrerequisites/angular-variance-results.json`.

These approximation diagnostics do not replace or invalidate the separate all-direction
discrete-table storage certificate below. A reconstruction range bound and agreement with
the exact illumination integral answer different questions.

### Storage inventory and open qualification

| Current owner/storage | Quantity to encode; data excluded from light scaling |
| --- | --- |
| Atmosphere CPU snapshots/compute output FP32; sky and aerial display textures RGBA16F | Sky/background/Mie and aerial source luminance; attenuation texture, alpha and extinction are dimensionless or inverse length. Common amplitude must reach CPU/GPU/publication together. |
| G-buffer Surface RGBA16F array | Environment `E/pi` RGB only. Normal/material layers and sky visibility alpha are dimensionless data. |
| DirectLightingTargets RGBA16F, three layers | Outgoing diffuse/specular/emission and transmission payloads in the otherwise spare alpha channels; alpha here contains lighting, unlike coverage alpha. |
| Composite R11F_G11F_B10F; engine primary/owned scene, particles and retained luma RGBA16F | Outgoing luminance, with premultiplied coverage where applicable; coverage/glow/perceptual luma remain their existing metadata. |
| Engine OIT three-layer accumulation RGBA32F through the approved allocation hook | Weighted premultiplied outgoing luminance and weighted alpha, normalized at resolve; separate revealage remains unchanged. |
| Pre-overlay/refraction color and reduced color RGBA16F | Unattenuated outgoing luminance; alpha validity excluded. R32F depth and RGBA32F reduced geometry are distance/eligibility data. |
| Surface Cache Direct/Indirect/Outgoing RGBA16F; packed binary16 emission | Direct/indirect lux, outgoing/emission nits, with explicit quantity conversions. Validity/count metadata excluded. Neutral emission's packed coefficient must not overflow before shading. |
| Screen-probe directional atlases and SH9 RGBA16F; gather/upsample RGBA16F | Directional luminance, signed SH coefficients, gathered `E/pi`; hit-distance/confidence/SH reliability excluded. |
| World-probe radiance RGBA16F and CPU results FP32 | Directional luminance; distance, sky sign/visibility, scheduling metadata excluded. |
| Water boundary transport RGBA32F | Signed optical depth excluded; scattering-source integral uses luminance. CPU camera-source parameters remain raw until shader conversion. |
| Owned bloom, shaft and disabled black images RGBA16F | Unexposed glare luminance; occlusion masks remain dimensionless. Camera exposure history SSBO is FP32 stops, not stored light. |
| TraceGeometry LightColors RGBA16F, Block/SunLevels R16F; material tables and atlases | Raw engine compatibility input and dimensionless coefficients, not physical radiance storage. Do not scale these globally or alter propagated engine data. |

The inventory includes all current light-bearing texture families; geometric/history metadata
and offline material-bake scratch are intentionally outside intensity conversion. Intermediates
can exceed final diffuse luminance: incident cache lux, signed SH projection coefficients,
low-roughness specular peaks and additive multi-source/glare sums require explicit range bounds.
Disk-only range checks cannot qualify these families.

The approved representation is the pre-exposed frame/fixed persistent storage contract above.
The following fixed-scale analysis explains the rejected requirement of retaining the complete
uncapped disk and an arbitrary dark interval at <=1% simultaneously. It does not qualify the
approved representation or require that discarded precision criterion. The illustrative dark
reference 0.001 nit is not an accepted minimum or an added night floor.
Fresh source-linked scalar receipts give relative reference solar RGB
`(1.3883028,1.6108252,1.4638687)`, Y `1.5529066682`; a common amplitude of `64395.3703382`
therefore supplies 100,000 lux. The disk is 69,664,100.29 nits in Y, RGB approximately
`(62,279,895.94,72,262,351.15,65,669,818.16)`. Bare nits overflow half/packed scene storage.

For `stored=physical/S` (`k=1/S`), retaining that colored disk requires `S>=1103.175`
in half and `S>=1111.319` across packed channels. Every overflow-safe scale puts the
continuous dark interval `[0.001,0.002]` nits in subnormal storage. Its grid spacing is
`delta=S*q`, with `q=2^-24` for half, `2^-20` for RG and `2^-19` for B. A rounding
midpoint in that interval has relative error at least `delta/(2*0.002)`, so <=1% requires
`S<=671.089` in half, `<=41.943` in RG and `<=20.972` in B, contradicting the disk bounds.
If no midpoint lies in the interval, both endpoints round to one constant, which cannot
meet their disjoint 1% output ranges. Thus no fixed scale qualifies this illustrative
continuous interval and disk together. A coincidentally exact individual dark sample is
insufficient to qualify the interval. At `S=2048`, 0.001 nit decodes to 0.0009765625
in half, 0.001953125 in packed RG and zero in packed B. Normal-range single-store worst
rounding bounds are 0.048828125%, 0.78125% and 1.5625% respectively.

Receipt: `artifacts/PhotometricLightingPrerequisites/storage-receipt.json`; reproduced with
`dotnet run --project artifacts/PhotometricLightingPrerequisites/StorageReceipt/StorageReceipt.csproj -p:UseSharedCompilation=false --verbosity quiet`.
The temporary console links current unmodified scalar atmosphere/sky-mapping/disk/lighting
sources and supplies only unused lookup-dimension defaults. It independently rounds the
unsigned packed formats and uses .NET binary16 for half. Successful build/run is a fresh
CPU/source receipt, not GPU storage, complete atmosphere or live game qualification.
A separate exhaustive code-point/binary-search recheck passed 148 assertions, including
the continuous-interval proof, analytical diffuse/point fixtures and source SHA256 identities:
`storage-independent-validation.json`, reproduced with
`node artifacts/PhotometricLightingPrerequisites/validate-storage-receipt.js`.
The source-linked reference may inform the amplitude; downstream CPU/GPU integration must
still meet the physical reference and weather/elevation fixtures. The illustrative envelope
fails the old uncapped fixed-scale stress test; its minimum and accumulated-pass tolerance were
never accepted. The approved visible-disk cap is explicit and does not claim to pass that test.

Fresh installed IL also establishes engine point payloads. The HSV `AddPointLight` overload
uses `BlockLightLevels[brightness]`, byte HSV conversion without sRGB decoding, and multiplies
the resulting reordered RGB by the original brightness byte. The `Vec3f` overload directly
stores reordered supplied color. Neither supplies lumens or a separate range value. Installed
`fogandlight.vsh` infers a range from color-vector magnitude and limits it to 25; current PBR
uses its own inverse-square saturation instead. A compatibility adapter must state which
meaning it preserves; it cannot assume those two overloads supply identical physical power.
Receipts: `engine-light-il.txt` and `engine-light-util-il.txt` in the same artifact directory;
`VintagestoryLib.dll` SHA256 `E08F22B493B92FEAF0AAEB79D22437EA0F7EFC38AA7F72A04A47F98BC0E40DF0`,
`VintagestoryAPI.dll` SHA256 `034283E7E9D98EAE45EE63005576FD89BADC3C995B531CC4C3FE46F3EB2D3296`.

The user approved the bounded pre-exposed approach described in "HDR storage and camera response"
on 2026-10-11. That supersedes the earlier unresolved fixed-scale-versus-pre-exposure choice.
Numerical qualification must now test that selected representation, including its explicit disk
approximation, fixed cache/lookup range, scale transitions and accumulated consumer error.
The 148-assertion fixed-format receipt remains useful arithmetic evidence; it is not a passing
receipt for this newly selected representation. No runtime conversion existed when those
prerequisite receipts were collected; the source conversions described above now supersede that state.

### Fresh source-derived calibration and storage receipts

The installed-source fixtures in `artifacts/PhotometricLightingPrerequisites/EngineFieldReceipt`
establish the normal torch's HSV `[4,4,14]`, open-air face-neighbour propagation with a one-level
decrement, decoded byte colors, overlap behavior and corner interpolation. Adjacent air receives
level 13, not 14. The reference is an unsmoothed single-air-cell field value; an arbitrary wall
pixel may interpolate several cells and must not be described as the same measurement.
Level 13 decodes to `(145,99,71)/255`; level zero retains `(10,10,10)/255`.

Use the floor-corrected shader response specified above with floor `10/255`. Fitting the nominal
44 lm isotropic-equivalent target at one metre gives `referenceLux=8.982904808459303`.
The resulting scalar illuminance is 3.501408748 lux at level 13, 3.272320849 at level 12 and
2.811659594 at level 10; level zero gives zero. These are source-derived compatibility results,
not live measurements or inverse-square block propagation. Derive parameters from the active
world's brightness tables through existing bindings; refresh them on table/calibration changes.
Do not rewrite field values or scan sources each frame.

The torch's actual dynamic-light payload is `(154,105,76)/255*14`. Select
`pointReferenceCandela=0.562774738350764` per raw RGB payload unit. Keep the one-metre
inverse-square saturation region and use the existing engine-derived influence radius
`min(25,length(rawPayload))` metres, with a smooth fade from 80% of that radius to the edge.
The reference radius is 11.051125119 metres. The same nominal source gives 3.501408748 lux at
one metre and 0.875352187 lux at two metres, before the outer fade. This adapter preserves the
payload's compatibility convention; it does not recover independently authored lamp flux.

Select 32 nits for a white material with emissive coefficient one as an explicit compatibility
anchor from existing cache authoring. This is not a measured flame luminance. Retain the direct
material's base-color modulation and the cache's neutral-emission convention and exclusive
aggregate-block-versus-emission policy. For cache block illumination, reuse its existing normalized
light RGBA data and the shared conversion, rather than the legacy level/32 palette approximation.

Fresh delegated numerical receipts cover 806,905 bounded-storage assertions, 2,103 calibration
and safety assertions, and 313 postprocessing assertions. They establish arithmetic and source
contracts; they do not establish runtime publication, GPU precision or live appearance.
The fixed `1/16` lookup scale fits the sampled physical atmosphere peak of 64,394.7601 nits.
A proposed bounded persistent directional range of 131,072 nits gives a conservative signed SH
coefficient bound of 1,038,963.903, fitting binary16 at that scale. These are explicit design
ranges, not universal guarantees for every source/material.

Glare needs additional headroom: with supported bloom/shaft strengths at most two, use
`T=64000/max(1,bloomStrength,shaftStrength)` and a disk-write ceiling
`min(48000,max(0,T-16000))`. Default strengths permit 48,000; maximum strength two permits
16,000. The tested positive normalized filters then keep each individual target at most 64,000.
The final combined scene and glare are added in FP32 before tone conversion.

The original storage audit identified two qualification issues. The engine's unmodified
transparent accumulation uses RGBA16F and sums weighted RGB before dividing by accumulated alpha.
The primary-light budget alone cannot protect that original format
against sufficiently many overlapping layers; normalization after accumulation cannot repair
overflow. Near-plane depth also needs an explicit weight-domain bound. Do not silently pick an
arbitrary layer limit or change attachment formats to declare this resolved.

The preliminary 120-case CPU accumulation receipt makes the failure concrete: at stored RGB
16,000, coverage one and supplied depth zero, four layers remain finite but eight overflow.
FP32 accumulation remains finite through 1,024 tested layers, with worst relative error 0.00205%
across those fixtures. The user approved a narrow RGBA32F exception on 2026-10-11; these CPU
fixtures alone do not establish GPU behavior or performance. The active engine allocation is the three-layer
`TexImage3D` in `SystemRenderOITLayers.BeforeOIT.rebuild`; changing its unused initial 2D
accumulation image would not fix the active buckets. Widening the three buckets adds 24 bytes
per pixel: about 47.46 MiB at 1920x1080 or 108.42 MiB at the inspected 3440x1377 size.
Use the existing allocation/publication lifecycle for this approved exception; it does not
require a new transparency algorithm. Memory bandwidth/performance remain unmeasured.
`EngineOitAccumulationFormatHook` implements the exception at that original call. Focused
installed/GPU tests verify exactly one changed IL format operand, actual three-layer RGBA32F
allocation, resize, clearing, array sampling, normalized eight-layer HDR output and engine
retirement. These tests exercise the real bucket attachments with diagnostic draw/resolve
shaders; they do not establish full production transparency appearance or live performance.
Receipts are under `artifacts/PhotometricLightingPrerequisites/OitFormatTests/`.

A five-check source-derived real-arithmetic range receipt also covers valid negative supplied
depth. The installed weight is `exp(-depth/3000)` and the logarithm uses `depth/30+1`; valid
real depth greater than -30 metres gives weight less than `exp(0.01)`, so use the conservative
weight allowance 1.011 rather than assuming weight at most one. Bell weights are at most one.
With nonnegative scene components at most 64,000 and coverage in [0,1], unnormalized bucket
RGB is at most `N*1.011*64000`. Even the counter-representability domain N<=2^64 is far below
FP32 overflow in real arithmetic; this is not a selected layer-quality limit. Validate the
actual rounded logarithm input, source domains and blend arithmetic during integration.
Real depth greater than -30 does not itself prevent its rounded logarithm input becoming zero.
This establishes a design range model, not GPU accumulation precision or appearance acceptance.
Receipt: `artifacts/PhotometricLightingPrerequisites/oit-real-range-qualification.json`.

An algorithmic alternative is sorted premultiplied-alpha composition, or a bounded per-pixel
sample list that merges excess layers with color/transmittance composition. Ordinary alpha
layers use `C=alpha*source+(1-alpha)*background`, so repeated equally bright layers do not
build the unnormalized brightness sum used by current buckets. Additive materials and oversized
individual inputs still require range qualification. A bounded sample-list implementation needs
ordered per-pixel writes, additional sample storage, insertion/merge logic and a new resolve;
this would replace the current transparency algorithm and is outside the approved narrow
conversion work. Its behavior does not establish safety of the existing weighted accumulator.
The separate [bounded depth-sorted transparency proposal](PBR.Transparency.Proposal.md) records
that possible later refactor. A revisit task is in `PBR.BaselineShading.todo`; its implementation
is not part of this approved storage exception.

Installed standard-perspective near clip lies in `[0.025,0.1]` metres. Its clipped implicit depth
therefore bounds the depth weight by `exp(0.1/3000)=1.0000333338888951`, rather than exactly one.
Explicit-depth effect and custom-projection routes need their own finite depth domain. Receipts:
`oit-accumulation-qualification.json`, `oit-accumulation-summary.json` and
`EngineFieldReceipt/projection-il.txt` in the same prerequisite artifact directory.

Furthermore, SH reconstruction and configured GI gain must enter any proposed frame safety bound, rather
than only raw cache component maxima. A conservative nine-term reconstruction bound is
5,898,256.261 nits before artistic gain. Including it prevents the tested overflow but causes
material dark-signal loss: a 0.001-nit packed blue signal loses about 29% at gain one and becomes
zero at gain sixteen in the tested night case. This is not an accepted dark precision envelope.
Finite configuration validation and a tighter justified bound or explicit supported precision
contract remain necessary. This coarse independent-coefficient estimate is superseded for the
current scalar SH9 path by the joint projection certificate below. The receipts are `bounded-storage-validation.json`,
`calibration-safety-validation.json`, `postprocess-storage-validation.json` and
`EngineFieldReceipt/oit-storage-receipt.json` under the artifact directory.

The current 64-cell projection table permits a substantially tighter joint bound. A sphere-cover
certificate evaluates the positive cell kernels at 8,192 directions and adds a global Lipschitz
cover allowance, then separately accounts for coefficient quantization and FP32 arithmetic.
Its all-direction upper bound is `1.274332993*M + 9.36161e-7` in E/pi for nonnegative directional
components bounded by M. Select the rounded design bound `1.28*M + 9.36161e-7` for this scalar
packed evaluator. It requires the inspected table, normalized directions and jointly projected
inputs; it does not apply to arbitrary independently clipped coefficients or other SH helpers.
Use the gathered E/pi bound in frame qualification without separately adding the raw incident
cache ceiling that is not directly consumed by scene composition. The seven-assertion receipt
is `joint-sh-bound-validation.json`; enforcing supported directional input ranges remains future
integration work, not current runtime behavior.

The 630-assertion `dark-display-visibility-validation.json` passes the stated single-store
quantization tolerances, including their absolute subnormal allowance. This does not prove
appearance or acceptable source retention. With the tighter bound and neutral GI gain, the
rejected global-scale candidate chooses daytime P approximately `5.88399574e-7`. The nominated
torch on an 18% Lambert receiver produces `0.200615944` nits, but its RGB rounds to zero in
packed scene storage. Unlike the illustrative 0.001-nit stress sample, this is part of the actual
planned calibration fixture. Thus this safeguard fails a material lighting requirement despite
passing format arithmetic. Qualification must preserve that lamp contribution while handling
ordinary low-roughness solar highlights; neither a looser verifier nor a new nighttime floor
resolves the failure.

The follow-up warm-color fixture confirms that merely widening packed scene RGB to binary16
would not rescue that rejected scale. The calibrated block-lit 18% receiver's RGB is
`(0.2724789882,0.1860373782,0.1334207460)` nits. At the candidate daytime P, half-float channels
round to three, two and one subnormal steps; decoded Y becomes `0.216821676` instead of
`0.200615944`, an 8.078% error with a color shift. The dynamic torch reference has the same
decoded result. The four targeted checks in `warm-reference-storage-validation.json` confirm
these arithmetic results; they do not qualify the candidate for release.

The approved refinement is a finite-angular solar reflection response using the existing visible
disk radius. The current single-direction sun approximation can produce solar specular values
far above the actual disk radiance. For nonnegative passive reciprocal reflection with
per-channel reflectance at most one, integration over a finite disk gives reflected radiance
at most the incident disk's maximum radiance. This is a property of that proposed physical
response, not a proof about the current approximate BRDF. User approval on 2026-10-11 makes this
a solar-only exception to the general area-light-transport exclusion; no new lamp discovery,
area-emitter system or shadow system is proposed. The subsequent approved representative-direction
implementation supplies the concrete response; the later geometry-coupled certificate and
selected frame-domain receipts below qualify its prerequisite storage bound.

The 77 conditional numerical checks in `finite-disc-response-validation.json` evaluate that
option at the reference disk maximum of 72,262,351.1498 nits. A 16,000-unit budget gives
P approximately `0.0002214154`, about 376 times the rejected global scale. The warm block
reference survives with packed luminance error 0.566% and half error 0.009%; the warm dynamic
reference gives 0.256% and 0.038%, respectively. Stated illustrative point, environment and GI
allowances also preserve these samples. This is not a qualification of every world/light list:
use the matching current atmosphere generation and actual admitted source bounds. Packed blue
still has up to 3.15% error in the reference sample, consistent with subnormal storage; no
universal color-precision or appearance acceptance is claimed. Qualifying the selected
finite-angular response remains required before using any such physical radiance bound.

A follow-up candidate broadens only the solar GGX normal distribution with
`aSquared=min(1,roughness^4+sin(angularRadius)^2/4)`, keeping the existing Schlick visibility,
Fresnel and punctual response. A 4,097-interval real-arithmetic certificate bounds its specular
response by 2.092589 times the maximum physical disk radiance; 2.1 includes the stated arithmetic
allowance. The 89-assertion receipt `widened-ggx-solar-validation.json` also tests storage and
12 finite-disk reference comparisons. This is a bounded candidate, not the selected response.
The reference torch remains nonzero, but packed Y error is about 1.18% with neutral GI and
1.25% with one reference point light. More importantly, converged reference comparisons show
20.2% excess at roughness 0.3 and an 80-degree view, and 61.4% at roughness 0.1 and a 60-degree
view. Minimum-roughness quadrature changes by 21–37% when refined, so those samples cannot
establish reference accuracy. A normalized broadened NDF alone therefore does not qualify the
solar response: preserve the approved material-response intent rather than selecting this
candidate solely because its storage bound is convenient. No runtime approximation is adopted.

The subsequently approved representative-direction implementation is a different approximation.
Its final four-iteration source passes 182 CPU arithmetic/domain checks. Across 24 cone-boundary
comparisons, its maximum half-vector alignment gap against the independent dense search is
`2.89e-8`; extreme grazing and cone-boundary cases remain finite. A conservative analytical
specular bound is `5.954371*maximumDiskRgb`, rounded to `6*maximumDiskRgb`, under the stated
normalized geometry, bounded Fresnel and roughness domains. This is not the ideal passive
reflector's one-times-disk bound. Example neutral-GI torch packed luminance errors are 0.249%
without a reference point and 0.537% with one; gain-16 examples reach 1.93%, and packed blue
quantization remains significant. These examples do not qualify every admitted light list.
Converged normal-view disk integration comparisons differ by -17.7% at roughness 0.04 and
-42.2% at roughness 0.1. Record those as approximation differences, not observed in-game
appearance or a claim of exact integration. Receipt: `representative-solar-validation.json`.
An independent centered normal-view investigation reproduces the roughness-0.1 difference:
integrated unit-disk-radiance response `0.5332623191` versus approximation `0.3083958212`.
Polar adaptive integration and transformed adaptive/composite integration agree. The mixed
roughness/source-extent term in the compensation denominator is the primary cause; removing
only that term diagnostically gives `0.5392154563`, 1.12% above the reference. Switching
single-scattering masking changes the reference by about 0.0013%; dielectric F0=0.04 retains
the same relative discrepancy. The disk irradiance normalization and mirror-direction branch
are consistent. These attribution checks do not qualify removing the term away from the
centered normal-view case, reproduce a complete alternative pipeline, or establish live appearance.
No runtime equation was changed. Receipt:
`artifacts/PhotometricLightingPrerequisites/finite-disk-discrepancy-results.json`.
The subsequent geometry-coupled interval certificate tightens the retained opaque finite-disk
response bound from six to `0.9*maximumDiskRgb`, without changing the runtime equation. A
separate tighter certificate reaches 0.81; the frame experiment retains 0.9 for margin.
Coverage-normalized water reflection is bounded separately: twice maximum disk RGB for the
finite disk, and 200,000 times incident RGB for punctual sources. The opaque punctual bound is
66,560 times incident RGB. These are real-arithmetic certificates over the recorded geometry,
roughness and Fresnel domains, not formal GPU floating-point/compiler error guarantees.
Receipts: `artifacts/PhotometricLightingPrerequisites/finite-disk-range-coupling-results.json`
and `artifacts/PhotometricLightingPrerequisites/water-punctual-range-coupling-results.json`.

The assembled conditional frame experiment passes 45,238 CPU arithmetic assertions across
both lighting modes, 0–100 maximum installed-table point lights, GI gains 1 and 16, exposure
extremes and scale identities. It includes global pre-transmittance solar RGB, water reflection,
coverage-normalized scattering, admitted atmosphere/environment contributions and postprocessing.
All channels of the calibrated warm torch on an 18% receiver survive in the worst daylight
100-point/GI-16 fixture. Packed luminance error is 7.35% standalone and 7.10% with LumOn;
half-float error is 0.56% and 0.34%, respectively. These pass the existing single-store subnormal
absolute tolerances, not an accumulated transport, color-accuracy or appearance criterion.
Receipt: `artifacts/PhotometricLightingPrerequisites/complete-frame-coupled-water-supplement.json`.

This does not qualify every current producer. The atmosphere/environment admission bounds,
GI tint range, water/air transport episode limits and temporal-history provenance still require
owner reconciliation. Maximum installed-table point intensity does not bound arbitrary finite
custom RGB payloads. These original complete-frame fixtures remain conditional, and GPU arithmetic and
accumulated dark-light precision remain unqualified. No source clamp, new format or runtime
storage safeguard is selected by these receipts alone. The subsequent selected scale rule
defines its arithmetic domain; full producer admission and integrated precision remain separate.

Source review also establishes that the GI gain ceiling is not a complete artistic-control
bound: `LumOnSettingsConfig.Sanitize` caps intensity at 16 but validates only the length of
`IndirectTint`, whose components are passed into gather. The qualification fixture's unit tint
is therefore conditional. Include the actual finite nonnegative component gains at the existing
configuration/publication boundary when deriving the budget; report unsupported inputs rather
than treating unit tint as a current runtime guarantee.

The signed water-boundary path invalidates an additional assumption of the assembled fixture.
It samples lighting independently at each boundary, then adds/subtracts
`scattering * source * remainingDistance`. Valid submerged length and optical depth do not
prove this source integral is bounded by the homogeneous source maximum. With a receiver at
100 m, entry at 1 m, exit at 2 m, density 1, scattering 1 m^-1 and the clear-water absorption
reference, entry source 1 and exit source 0 produce accepted length 1 m and terminal count zero.
The resulting blue scattering is 62.3394 despite both sampled sources being at most 1;
the matched homogeneous control is 0.62969. At receiver distance 1,000 m the former becomes
629.061. This is a source-faithful CPU witness, not an observed game scene or a newly introduced
transport defect: the water contract already declares heterogeneous scattering approximate.
It does show that the two homogeneous scattering episodes in the frame fixture do not cover
current boundary capture. Budget signed source accumulation using its actual source, coefficient,
remaining-distance and contribution bounds before normalization; do not infer it from accepted
length or terminal winding alone. Receipt:
`artifacts/PhotometricLightingPrerequisites/water-signed-boundary-range-results.json`.
For nonnegative admitted sources, a conservative per-channel bound is the sum of positive entry
terms plus the initial camera-medium term: the nonnegative optical-depth integral factor is at
most one. A coarser bound needs a proved positive-contribution count and ray extent, including
projection ray scaling. The signed net boundary count cannot supply that count. Any such bound
must pass the calibrated-lamp precision checks before replacing the homogeneous experiment.

A narrow follow-up reuses the unchanged format helpers and all other frame-budget terms,
replacing one homogeneous boundary allowance with this signed interval. It passes 99 arithmetic
and source-identity assertions across four selected lighting-mode/day-night rows and ten variants.
The primary boundary-source envelope excludes point arrays, as the capture shader does. At a
100 m daylight receiver, packed torch luminance errors are 6.28% standalone and 6.10% with
LumOn, with all channels retained. At 1,000 m, packed blue rounds to zero in both modes and
luminance error reaches 55.75% and 55.82%; half-float retains all channels but has 1.08% and
1.12% luminance error. These remain conditional conservative-budget experiments, not observed
scene errors or proof that independent source maxima occur together. Passing single-store
subnormal absolute tolerances therefore cannot establish the calibrated-lamp precision envelope.
Do not select this revised global guard as qualified storage: the signed-source bound and dark
precision requirement remain unresolved. Receipt:
`artifacts/PhotometricLightingPrerequisites/complete-frame-signed-water-supplement.json`.

### Approved bounded water-source estimator

Retain the signed boundary capture, optical-depth integration, receiver validity, material
coefficients, transmission and existing resource owners. Refine only the scattering estimate.
For each channel, let `U` bound every contributing phase-weighted local illumination source,
`tau` be the accepted nonnegative optical depth, and `Q` the signed source integral. Evaluate
`min(max(Q,0),U*tau)*h(tau)` using the existing thin/thick integral factor `h`.
For an exact homogeneous interval, `Q=scattering*source*length <= U*tau`, so this leaves its
response unchanged. With nonnegative coefficients and scattering no greater than extinction,
the exact heterogeneous transport also satisfies `S <= U*(1-exp(-tau))`. The bound therefore
removes an estimator overshoot rather than changing authored lamp power or medium coefficients.
The existing thin-path series approximation still requires its arithmetic allowance.

Derive `U` from the same completed atmosphere generation's solar/environment bounds and the
converted block-field maximum, including the maximum admitted angular phase response. Include
the initial camera-medium source. Boundary mode consumes no point array. Receiver-local lighting
alone cannot bound independently sampled boundaries. Missing, nonfinite or incompatible bound
metadata must withdraw the affected transport through its existing validity/fallback path.
Validate products and the rescaled intermediate, not just the normalized output.

Use the following componentwise source convention. Let `S` be the published solar illuminance,
`Ebar` the published diffuse environment E/pi, `D=pi*Ebar` its incident-lux adapter, and `B` the
shader-converted block illuminance. Boundary source is
`S*skyAvailability*shadowVisibility*phase + (B+D*skyAvailability)/(4*pi)`;
camera-medium source is `(B+D*skyAvailability)/(4*pi)` with no assumed direct solar visibility.
Scattering coefficients multiply these local sources separately. The diffuse term retains an
explicit isotropic compatibility surrogate; it is not an exact angular sky integral. Multiplying
the existing E/pi environment input by pi is a planned unit correction, not unchanged legacy
amplitude. Raw camera block data still receives its physical conversion in the shader.

Select the qualification bound `U=64*max(S,0)+(Bmax+pi*max(Ebar,0))/(4*pi)` before source-expression
rounding allowances. `Bmax` comes from the admitted active decoder range and calibration, not
receiver-local field samples. The bound covers every material anisotropy in [-0.95,0.95], so
publication needs no material scan or light discovery. Matching source/calibration generations,
visibility in [0,1], nonnegative finite components and outward-rounded publication are required.
Validate source-expression addition/product rounding at implementation; do not publish a rounded
value below the admitted source maximum. The initial camera source uses the same bound.

The narrow source receipt passes 273 CPU checks. Its nine-interval FP32 phase certificate has
largest upper bound 63.27969, below 64, assuming round-to-nearest basic operations and square-root
relative error at most 2^-24. This is an explicit qualification model, not an unconditional GLSL
intrinsic/compiler guarantee. Actual source publication and GPU arithmetic remain later checks.
Two complete atmosphere fixtures yield maximum U about 6.6393 million and 6.4177 million nits;
these are derived snapshot bounds, not a fixed admission ceiling. Single-boundary and U*tau
products must be finite; accumulated signed capture also needs its positive-contribution bound.
Finite receiver distance alone is not sufficient. Four conditional frame substitutions retain
torch RGB, with daytime packed luminance errors 5.415% standalone and 0.488% with LumOn.
Other experimental frame admissions are retained in those rows, so they do not qualify the
whole-frame safeguard. Receipt:
`artifacts/PhotometricLightingPrerequisites/water-source-upper-qualification.json`.

This requires a narrow change in `includes/liquids/boundary_transport.glsl` and source-bound
publication through existing composite/frame bindings. It requires no additional target, draw,
GPU readback, light discovery or ordered heterogeneous integrator. The user approved this narrow
refinement on 2026-10-12. It is the explicit exception to retaining the heterogeneous estimator;
it does not authorize other water-transport changes. Shader integration and GPU qualification
must cover homogeneous invariance, thin/zero paths, camera-source cases, disjoint media,
endpoint mismatches, source-bound compatibility and dark-light storage.

The proposal-only experiment passes 310 CPU assertions (271 estimator checks and 39 inherited
source/format checks). It preserves homogeneous arithmetic and exact zero optical depth;
ordered positive-media reference cases support the passive-transport bound. The two boundary
mismatch witnesses decrease from 62.3394/629.061 to 0.635497 under unit source bound, independently
of receiver distance. Replacing only the boundary allowance in the conditional frame fixture
retains all torch channels: daytime half-float luminance error is 0.259% standalone and 0.048%
with LumOn; packed error is 5.547% and 0.614%. These are conditional single-store results, not
general lamp accuracy, accumulated transport or appearance acceptance. The bound must cover
every contributing boundary and camera source, and finite products remain an explicit domain
requirement. This receipt supports the approved design, not completed runtime integration:
`artifacts/PhotometricLightingPrerequisites/passive-boundary-envelope-proposal-results.json`.

### Atmosphere publication range evidence

Atmosphere publication review adds 43 CPU extraction/source-identity assertions over seven
existing complete quality-zero lookup fixtures. Their largest packed smooth/Mie component is
64,394.76 nits, representable at the candidate `C=1/16`. However, positive-band maxima and the
maximum Mie phase factor yield a conservative haze reconstruction bound of 160,123.86 nits.
That bound cannot certify the experimental 131,072-nit admission; it does not demonstrate an
actual violation (the original haze sky texel directions peak at 12,669.80 nits). Neither that
ceiling nor the million-lux medium-input ceiling is proved across the accepted atmosphere domain.

Use the existing completed snapshot and packing/upload owners to derive matching-generation
source bounds: smooth-band maxima plus maximum phase factor times unweighted-Mie maxima,
with stored rounding/interpolation allowances, and the actual solar/environment components.
The existing fenced GPU readback already supplies the complete arrays; this needs no additional
GPU dispatch or readback. Publish bounds with the same completed snapshot/textures and retain
the old complete publication on failure. Fixed `C=1/16` still needs a separate packed-component
range check against 1,048,064 physical units before half-float conversion; finite FP32 values
alone do not establish representability. The seven fixtures are sampled evidence, not a global
domain qualification. Receipt:
`artifacts/PhotometricLightingPrerequisites/atmosphere-publication-range-qualification.json`.

The earlier engine-field receipts establish decoder-specific zero-sky floors: terrain's byte-normalized value is
`7/255`, whereas the world accessor supplies `0.03`. Convert encoded sky availability separately
from block illuminance as `clamp((raw-floor)/(full-floor),0,1)` with active-table, decoder-specific
endpoints. This makes encoded level zero contribute zero physical sky availability and full level
contribute one, preserving monotonic interpolation without altering propagation. It is a
dimensionless shader adapter, not a sunlight intensity multiplier.

The 45-assertion `world-fallback-units-validation.json` verifies constant-input world diffuse
evaluation gives pi times the incident-radiance average. Dividing by pi at the shared gather
fallback yields the same E/pi convention as the screen path. Directional sampling remains in
nits; this is planned normalization with CPU/source evidence, not an implemented shader fix.

### Remaining evidence and decisions

#### Additive compatibility contributors

Select the existing neutral-emission reference, 32 nits per unit linear RGB, for the remaining
engine-authored scene-color approximations. This reuses the documented compatibility anchor,
not a new claim of measured cloud, lunar or particle radiometry. At the existing scene adapter,
straight authored RGB `c` becomes `32*VgeSrgbToLinear(c)` before coverage, weighting or integration,
then receives frame P once. Physical atmosphere, solar, BRDF and transport results bypass this
adapter. Already prelit engine particles/clouds remain explicitly approximate; do not reinterpret
their aggregate RGB as lux or add their embedded engine lighting again. UI/offscreen draws bypass it.

For a finite nonnegative maximum authored component u, the adapter bound is `32*d(u)`, where
`d(u)=u/12.92` below 0.04045 and `((u+0.055)/1.055)^2.4` otherwise. Values above one are supported
only with their actual bound; decoding does not clamp them. Publish bounds from the existing
texture/color/configuration owner, including expression multipliers, rather than sampling rendered
pixels. Missing or nonfinite bounds fail admission. This requires explicit per-adapter arithmetic
in the existing owners, not a general interval-analysis framework or new source registry.

| Contributor and inspected boundary | Required bound before frame composition |
| --- | --- |
| Quad particles and aurora, installed `particlesquad.fsh` / `aurora.fsh` | Their final straight OIT color is already clamped to [0,1]; at most 32 nits per component after conversion. Keep the existing clamp, alpha and glow policy. |
| Cube particles, `SceneColorLegacyPatches` and isolated particle capture | Bound the actual fog/shadow/underwater-modified straight result before premultiplication. Unit texture color alone is insufficient. The existing straight-alpha layer is convex when alpha is in [0,1]. |
| Ordinary clouds, installed `clouds.fsh` | Include both sky-glow multipliers, perception, fog interpolation, additive `plightrgb` and the 0.5-times-night-vision peak before decoding. Do not assume `rgbaCloud` bounds final color. Bound final RGB through these actual published inputs. |
| Volumetric clouds, installed `cloudvolumetric.fsh::traverse` | Convert each cloud-color sample before integration. With sample bound r, v in [0,1], alpha in [0,1] and at most 200 steps, integrated RGB is at most 200*r per invocation. Its direct `OITaccumulate` route bypasses ordinary OIT's RGB premultiplication; normalized color is not automatically bounded by r. Include the resolve divisor and number of submissions, or prove a tighter sample RGB/alpha ratio bound. |
| Stars, moon and late lines/decals/highlights/diagnostics, `SceneColorLegacyPatches` | Bound the final authored expression, including moon phase, nonlinear brightness and underwater effects, before decoding. PBR-owned held/forward surfaces bypass this compatibility conversion. |
| Owned sky tint and night vision, `pbr_sky.fsh` | Convert only the authored tint/night terms with the same 32-nit reference. Tint interpolation is convex for admitted murk in [0,1]; night vision adds `32*d(0.5*strength)` at its peak. Perception may alter physical sky RGB and needs its explicit multiplier/additive bound, not another authored-color decode of atmospheric radiance. |
| Composite and sphere fog | Convert authored fog RGB before its existing convex interpolation. Use its actual decoded bound; preserve separately evaluated physical aerial/water transport. |

Let r_i be each adapter's resulting physical bound after its internal expression/integration.
For ordinary positive weighted OIT and straight-alpha composition, normalized RGB is bounded by
the maximum contributing straight-color bound; additional alpha composition is convex. Sum bounds
for genuinely additive draws. A conservative R may sum all per-invocation output bounds, including
overlay counts and separately normalized OIT buckets; tighter convex bounds are optional. For the
volumetric direct accumulator, use its actual numerator bound divided by the resolve's admitted
positive denominator. Installed `transparentcompose.fsh::unproject` returns black below weighted
alpha 0.0001 and divides by alpha otherwise; this is a cutoff, not a clamped divisor. A
conservative nonzero-output bound is therefore numerator/0.0001. A zero-alpha colored sample cannot be treated as a
bounded straight color without that calculation. Nonfinite intermediates or an unsupported assembled
B reject the publication/draw; RGBA32F does not remove this normalization requirement.

The installed native volumetric map has a tighter source contract: `cloudmap.fsh` writes
`colour.a=1` and changes only RGB afterward, into its owner's RGBA32F map. The existing
volumetric renderer submits one fullscreen mesh. For that admitted map, traversal integrates
RGB and coverage with the same positive weights, so `k.rgb<=r*k.a` for sample RGB bound r.
The direct bucket multiplier preserves this ratio; positive normalized bucket output is
therefore bounded by r. This proof requires the matching native map generation, finite
nonnegative opacity, unit sample alpha and its actual RGB expression bound. It does not
qualify arbitrary replacement cloud-color textures or remove the general numerator/divisor
admission above. Owner inspection receipts are `artifacts/phase4-cloud-owner-il.txt` and
`artifacts/phase4-cloud-construction-il.txt`; installed arithmetic is covered by focused GPU tests.

These rules settle quantities and the conservative bound construction. Exact engine-expression
input bounds are constructed from the published inputs as follows, rather than assuming every
engine value is in [0,1]. Texture maxima include active replacements and filtering; normalized
byte textures have maximum one. Vertex colors and gains use actual finite component bounds.
Admission requires finite intermediate expressions and the declared coverage/fog domain.

- A convex fog/underwater mix is bounded by the maximum of its two endpoint bounds. For an
  extrapolating mix with factor interval [a,b], use
  `max(abs(1-a),abs(1-b))*U + max(abs(a),abs(b))*V` instead; do not silently clamp the factor.
  Sphere-fog interpolation is explicitly clamped in `fogspheres.ash`, so repeated spheres
  retain the maximum endpoint-color bound.
- In installed `fogandlight.fsh::applyPsychedelicEffect`, f is in [5,6], each cosine component
  is bounded by one and `df>=0.09`. Thus `abs(fcol)<=0.01+7*(0.135^1.2+0.01)<0.72`, or below
  1.44 for the subtracted cloud branch. Use conservative 1.6 times actual nonnegative strength
  for the additive authored-RGB bound. The separate palette tangent must remain finite before
  cosine evaluation; unsupported nonfinite arithmetic is not admitted as a valid color.
- Cloud point RGB in `clouds.vsh::getPointLightRgbvl` is a nonnegative weighted mean (or a
  subunit weighted sum) of the actual point colors after its red-above-10 divide-by-100 rule.
  Its bound is the maximum component of those adjusted colors, not raw summed candela and
  not an assumed unit RGB. The brightness denominator is `max(1,bPointBrightSum)`.
- For ordinary clouds let C bound the published `rgbaCloud.rgb`, H bound the glow/fog texture
  mixture and alpha be in [0,1] after removing display-noise addition. Then the pre-underwater
  authored bound is `max(Q+1.6*max(1,3*Q)*psychedelicStrength,FogRgbMax)`
  where `Q=C*max(1,1.2*H)`, followed by
  `+ CloudPointRgbMax + 0.5*nightVisionStrength`. The second sky-glow multiplier is at most one.
  C is an absolute component bound, including actual dayLight, cloudBrightness,
  globalCloudBrightness and signed undulation. Native perception uses `min(1,sum(RGB))`
  without a lower clamp: a negative vertex result can amplify its palette term, hence the
  `max(1,3*Q)` allowance. Do not use the nonnegative-input allowance for these signed vertices. The
  inspected 3D gnoise is bounded by 3.6 (unit hash components, three-component corner dot,
  convex interpolation and final factor 1.2); combine this with the actual cloud mesh radius,
  positive cloudsLength and undulatingModeness to bound the vertex undulation multiplier.
- Cube-particle `applyLight` normalizes nonnegative RGB to mean bMax. Each component is at most
  `3*bMax`; the dynamic branch's glow and contrast multipliers give `3.9375*bMax`. Multiply by
  actual particle color and the normal/shadow multiplier, then apply fog/underwater bounds.
  bMax includes actual sun/block brightness, MINBRIGHT, glow and point strength. The installed
  point strength is bounded by `1+(25/15)*max(25,abs(1-sunlightIntensity))`, plus 0.45 times
  night vision in its alpha/brightness channel. Scene-only particle/decal shader normalization
  scales source colors and weights before restoring brightness; an all-zero source returns
  neutral RGB. The intermediate weighted denominator has a normal positive guard, so dark
  ambient is admitted without an artificial illumination floor. Offscreen arithmetic remains
  native. Invalid raw point-color range divisions still fail admission; finite storage fallback
  alone does not certify unsupported sources.
- Night-sky cube sampling, with display noise removed, is bounded by
  `max(0,2*(CubeTextureMax-0.03))+0.5*nightVisionStrength`, followed by underwater mixing.
  Moon texture/color and initial fog bounds first pass through the phase multiplier, at most
  alpha+0.05. The subsequent sky-fog mix can restore bright fog: take T as the maximum of that
  phase-scaled bound and the actual fog RGB bound before nonlinear brightness.
  If its nonlinear brightness branch is selected, RGB bound T becomes
  `T*(3*T/2.8)^1.4`; subsequently take the maximum with the actual sky-texture/glow bound and
  apply underwater mixing. Lines/decals/highlights use their actual final color/gain bounds,
  not the celestial bound or an emission-texture assumption.

Night-sky alpha is not implicitly unit coverage: installed `nightsky.fsh` yields 1.1 at zero
dayLight. Preserve its authored expression and budget its actual blend factor. For an overlay
with source bound U and alpha interval [a,b], bound the result by
`max(abs(a),abs(b))*U + max(abs(1-a),abs(1-b))*BackgroundBound`; apply this recurrence in the
existing draw order. To retain the assembled `B=Base+R` notation, include both the additive terms
and any excess background multiplier in R. Do not claim convex blending or introduce a new
opacity clamp for extrapolating legacy overlays. Ordinary bounded OIT coverage remains separate.

For owned physical sky, apply the perception approximation to RGB expressed in units of the
32-nit compatibility reference and restore that reference afterward; do not sRGB-decode the
physical atmosphere again. This makes its additive allowance at most `32*1.6*strength` nits
and preserves a single reference-unit interpretation for the existing artistic operation.

All these quantities reuse existing published textures, colors, gains, mesh limits and light
arrays. No rendered-pixel readback is required. The supported domain is the resulting finite
assembled B and storage admissions, not universal support for arbitrary mods or infinite gains.
Their owner-specific implementation and GPU checks remain downstream work; the equations and
failure policy are the prerequisite contract. The R=32 experiment does not replace this inventory.

The installed volumetric-cloud main also applies `exp(log(k+1)+n)-1` after traversal, including
to alpha. That display-noise transform is outside the recurrence bound and can produce alpha
above one. On scene HDR draws, bypass it under the existing
[PBR.OutputDithering.md](PBR.OutputDithering.md) contract: float scene data retains radiance and
coverage, while final encoded output owns dithering. Preserve the offscreen/display route.
This is removal of a misplaced display operation, not a new cloud transport or opacity clamp.
With that selected boundary and admitted inputs, one unit-color cloud invocation has the loose
normalized bound `200*32/0.0001=64000000` nits. Include each possible overlapping invocation in
R, or publish a proven tighter RGB/alpha bound. Many submissions can therefore fail the frame
retention domain even when raw RGB fits; the design must report that incompatibility.

Imported sky-color helpers also add `NoiseFromPixelPosition` to glow/color, including alpha.
Bypass those display-noise calls on scene HDR cloud/celestial draws under the same output
dithering contract. Otherwise unit glow alpha and the ordinary cloud second-multiplier bound
do not hold. Preserve their offscreen/display behavior. The native volumetric map already
replaces this helper with zero; its bounded RGB expression still requires owner publication.

The source-linked nine-check receipt confirms the traversal bound, cutoff equality, zero-alpha
case and convex three-bucket resolve for admitted alpha. It also reproduces the original noise
transform's alpha-one input becoming 1.011396 at the conservative positive 1920-pixel noise bound.
The selected HDR bypass removes that operation, while retaining final display dithering.
This qualifies the design arithmetic, not actual texture values, submission counts or GPU code.
Receipt: `artifacts/PhotometricLightingPrerequisites/additive-cloud-bound-qualification.json`.

Fifteen source-linked checks independently recheck these input formulas against ten installed
shader sources, including the corrected moon fog order and night-sky alpha interval. The
conditional largest prior frame fixture plus one unit-color cloud invocation and the star
allowance totals 945.82 million nits, below the nominated retention-domain ceiling. This is a
design fixture, not an observed scene or proof that every replacement asset fits. Actual source
values, compatible generations and overlap counts must enter the selected equations during
implementation. Unsupported values follow the declared failure policy; no unspecified universal
legacy-color maximum remains in the design.
Receipt: `artifacts/PhotometricLightingPrerequisites/additive-input-formulas-qualification.json`.

The unit and owner audit provides the dependency boundaries. Transparent accumulation,
persistent admission, the selected frame-scale rule and the nominated torch-retention domain
now have the receipts recorded above. The assembled budget is explicit and passes its four
conditional fixtures. The additive-contributor section specifies compatibility unit adapters,
source-expression bounds, extrapolating alpha and cloud normalization; the fixture's R=32
cannot stand in for those producers.
Integrated GPU arithmetic and consumer behavior remain implementation work.
Live fixture measurements are needed only where
source evidence cannot resolve the mapping; later appearance acceptance remains user-run.
Historical AO capture decomposition does not establish these calibration quantities.
Current runtime documents retain legacy units until coherent implementation; their historical
32-unit mappings are inputs to migration, not physical calibration constants.

## Decisions required before downstream implementation

The requested prerequisite work retains the physical-unit contract, shader-only engine-field
adapter and stated scope. Spatial/solar conventions and the nominal torch reference are now
recorded above, and the storage approach is approved. Source-derived compatibility mappings
and conditional storage qualification are recorded. Use the additive-contributor unit/bound
inventory during migration; do not replace actual producer bounds with the 32-nit experimental
allowance. Reuse the
existing camera, atmosphere, cache and lifecycle owners; no general lighting or exposure framework
is required by the selected approach.

## Calibrated nighttime celestial lighting

Planned stars/galaxy replacement: [PBR.StarsAndGalaxy.todo](PBR.StarsAndGalaxy.todo)
defines the replacement source contract. On activation it supersedes the combined
uniform moonless background and the stars' compatibility-image policy below. It uses
a procedural HDR galaxy, instanced stars and separately budgeted airglow, retaining
the 0.0008-lux orientation-averaged reference total. The selected component split is
an artistic calibration, not a measured breakdown. Existing lunar transport and shadow
ownership remain unchanged. The following describes the currently implemented model
until that complete replacement is integrated; do not add both background models.
Owned visible drawing now replaces the legacy stars/galaxy images and removes the
uniform stellar half from visible atmosphere, retaining airglow. Receiver environment
and finite-path background remain the combined approximation until the planned
source-derived lighting integration; owned drawing alone does not complete that work.

The nighttime extension reuses the atmospheric transport backend, medium table and atomic
publication. Sun and moon are evaluated sequentially against one admitted medium generation;
only a complete pair may replace the visible sky and surface-lighting snapshot. Lunar phase
changes can reuse completed directional transport. CPU fallback follows the same composition.
Aerial attenuation remains dimensionless and is not added twice.

The lunar reference is **0.1 lux direct normal illuminance at clear zenith**, at the existing
0.001-km observer and aerosol-one calibration point. This is a selected representative value,
not the maximum possible terrestrial moonlight. The source uses the existing RGB solar spectral
approximation for reflected sunlight, scaled by 0.1/100,000; atmospheric extinction and tint
are therefore applied once. A lunar reflectance spectrum is not separately simulated.
[Kyba et al., How bright is moonlight?](https://academic.oup.com/astrogeo/article/58/1/1.31/2938119)
discuss typical full-moon illumination around 0.05–0.1 lux and approximately 0.3 lux under rare
ideal conditions. The calibration is not derived from native calendar brightness.

The installed calendar phase coordinate has a period of eight and full moon at four. Convert
it to phase angle alpha = abs(phase - 4) * 45 degrees. Use the normalized magnitude-fit response
10^(-0.4 * (0.026 * alpha + 4e-9 * alpha^4)), subtracting its new-moon endpoint and renormalizing
so new moon is exactly zero and full moon exactly one. This excludes an independent earthshine
source and keeps the wrapped transition continuous. The empirical phase model is approximate;
see [MoonShine, Poon et al.](https://besjournals.onlinelibrary.wiley.com/doi/10.1111/2041-210X.14299).
Native MoonPhaseBrightness interpolates an artistic table, including -0.1 at new moon, and is
not used as illuminance. Normalize the actual native MoonPosition; do not assume opposition.

Use an equivalent lunar disk radius matching the installed moon sprite's opaque footprint:
atan(64 * 0.023100002 * (1.1 + e) / 50), where
 e = clamp((dot(sunDirection, moonDirection) - 0.99) * 40, 0, 0.2).
The opaque full-moon image spans approximately half the billboard width; its transparent halo
is excluded. The apparent emitter is deliberately larger than the terrestrial moon, as drawn
by the game. Keep physical integrated illuminance independent of this artistic angular size.
This radius supplies finite-disk direct shading and horizon coverage. It exceeds the retained
solar radius, so the existing solar specular-response bound remains conservative. Native moon
sprite display brightness is still a compatibility representation, not a separately calibrated
physical disk-radiance image.

The unresolved moonless background reference is **0.0008 lux horizontal illuminance**, represented
by a neutral uniform upper hemisphere (radiance E/pi). This combines unresolved starlight and
airglow rather than simulating either in detail. The reference is from the
[NPS night-sky measurement guide](https://www.nps.gov/subjects/nightskies/NSQmetrics.html).
Use the same sampled background for sky radiance and environment integration. Its finite-path
in-scattering approximation is backgroundRadiance * (1 - transmittance). A documented cloud-cover
proxy reduces its power by 1 - 0.9 * coverage; this is not a physical cloud-volume solution.
The contribution exists alongside daylight and becomes negligible there without a day/night
brightness toggle. Individually rendered stars are compatibility images; they do not add a
second calibrated incident-light contribution. No receiver emission or minimum brightness is added.

Sky and aerial textures retain their existing formats and storage scale, with three elevation
bands: combined smooth lighting, solar Mie transport and lunar Mie transport. Each concentrated
band is reconstructed with its own source direction. The uploaded generation's conservative
reconstruction bound includes both bands. Two-band standalone fixtures and neutral startup
publications retain their existing interpretation. Solar, lunar, background, attenuation and
source identities are published together; faint-light precision requires actual-format checks.
Scene bounds include the selected lunar emitter's equivalent radiance,
Lunar / (visibleFraction * pi * sin(radius)^2), for finite-disk specular response as well as
the actual solar raster radiance. A zero solar disk at night must not remove the lunar
reflection bound. This bound does not change the moon sprite's compatibility brightness.

The sun owns the single direct celestial path while any part of its disk remains visible.
After it disappears, an illuminated visible moon takes over. During overlap, the unselected
moon's direct term is omitted, while both sources' scattered sky contributions remain. This
bounded approximation avoids an unshadowed secondary direct light or another shadow pass.
When neither emitter supplies direct light, only the available diffuse environment remains.

Reuse the installed shadow owner's near/far targets, passes, projection and matrix publication.
Its native camera already chooses between sun and moon, but its artistic selection and phase
gating do not define the physical source. Narrow getter substitutions in the four inspected
shadow-owner callbacks feed the selected completed emitter to the camera, cascade depth extent
and elevation gate. Retain native fog suppression, intensity fading and render order. Phase
modulates source energy once, rather than additionally weakening lunar shadow contrast. Existing
shadow visibility bindings remain valid for terrain, forward receivers and liquids. No new
shadow map, cascade, resolution increase or extra celestial shadow rendering is introduced.

The selected direct direction, radius and illuminance are shared by deferred/forward receivers,
liquids, water source admission, LumOn lighting and shafts. Combined environment lighting remains
subject to receiver sky availability, interior occlusion and ambient AO. Visible solar disk
rendering retains the actual solar source rather than the selected celestial source. Fixed-
exposure numerical checks precede user-run moonlit/moonless, phase, horizon, interior, weather,
LumOn and automatic-exposure appearance acceptance.
