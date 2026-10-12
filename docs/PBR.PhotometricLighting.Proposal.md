# Photometric lighting and block-light calibration

Status: proposed; not implemented or approved. This document proposes the lighting contract
and its acceptance criteria. It does not authorize runtime changes or constitute an implementation checklist.

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

The relevant current owners are:

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

Propose one metre per world block as the explicit spatial convention, subject to checking
engine coordinates at the binding boundary. Atmosphere distances remain kilometres internally,
with explicit conversion at shared spatial boundaries. Material reflectance, roughness,
metallicity, transmission, visibility and coverage remain dimensionless.

Keep incident illumination distinct from outgoing luminance. For a diffuse reference receiver,
`L = reflectance * E / pi`. An isotropic point light uses `I = lumens / (4*pi)` and
`E = I * max(NdotL,0) / distanceMetres²`, before visibility and documented near-source/range
regularization. These relationships provide measurable reference cases; source authoring and
GPU buffers must not interchange their quantities merely because both contain RGB.
See [Filament's punctual-light equations](https://google.github.io/filament/main/filament.html#lighting/punctuallights).

## Sunlight and atmosphere

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
brightness must integrate back to the same direct solar reference. Aerial in-scattering uses
the same amplitude; attenuation remains dimensionless.

## Propagated block light

Retain the engine's light generation, propagation, attenuation and enclosure behavior without
patching its lighting logic or rewriting its light data. Perform the block-light unit conversion
entirely in VGE shader code, using one shared GLSL helper consumed by terrain and other
applicable surface routes. VGE supplies the calibrated reference illuminance through its
shader bindings; the CPU does not convert or replace the engine's propagated field.
The adapter defines a reference illuminance in lux and a nonnegative monotonic response curve
for the legacy field. Start with an identity curve as the candidate; adopt a nonlinear curve
only if reference measurements demonstrate a need.

The adapter separates the field's brightness and chromaticity without manufacturing light from
zero. For the identity candidate, converted RGB illuminance is `legacyBlockRgb * referenceLux`.
This is a calibration mapping of an engine field, not a declaration that its original values
were physical measurements. The field's assumed encoding must be documented
from engine behavior; applying the texture sRGB function is not an established conversion.
The shader's integrated environment input receives the resulting `E/pi`, not raw 0–1 block RGB.
The identity candidate is evaluated by the shared helper as:

```glsl
vec3 blockIlluminance = max(legacyBlockRgb, vec3(0.0)) * blockLightReferenceLux;
vec3 blockEnvironment = blockIlluminance / PI;
```

Terrain uses this helper through VGE's existing shader injection at environment publication
in `PbrTerrainColorPatches`; other applicable surface routes use the same conversion before
material response. Updating these VGE shader injections is within scope. Adding patches to
base-game CPU lighting or propagation methods is outside scope. Convert each field contribution
once, keeping atmospheric illumination separate so it is not scaled by the block-light reference.

Determine the block reference from a controlled fixture with a nominated lamp, documented
placement, receiver orientation, measured engine field and specified target illumination.
Fit it at fixed exposure and verify additional distances and enclosed scenes. Do not tune the
reference by comparing automatically exposed screenshots. Lamp defaults remain a calibration
decision supported by these receipts; this proposal does not invent real torch output from
the existing light levels.

The aggregate engine field loses individual source identity and direction. Consequently this
adapter cannot recover each source's lumens, inverse-square falloff, additive overlap or proper
directional specular response. These limitations must remain explicit even after its receiver
signal is expressed in lux. Preserve engine overlap behavior and test it; do not claim that
the adapter implements physical source superposition.

Source-based replacement of voxel block lighting, new propagation, area-light transport and
source discovery are outside this proposal. The calibrated adapter establishes coherent
brightness while retaining a bounded compatibility approximation.

## Point lights, emission and receiver response

Provide an explicit lumens contract for VGE-authored punctual lights. For engine-provided lights,
keep compatibility conversion separate: engine color/intensity/range payloads must not simply
be relabeled as lumens. Reconcile held lights with the existing engine field and dynamic arrays
so the same source is not accidentally counted twice. Preserve source positions and document
near-source regularization and any finite influence window in metres.

Apply the same BRDF normalization to sunlight and punctual lights in deferred and forward
shading. Retire the special omission of pi for legacy point-light diffuse once their inputs are
converted. Preserve transmission, energy partitioning and shadow ownership while checking
their units against the incident-light contract.

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
Prefer a fixed documented storage scale for unexposed luminance if measured dynamic range permits
it. A stored value then decodes to physical luminance using the declared scale; exposure metering
and diagnostics must account for it. Do not choose that scale from sunlit surfaces alone: the
solar disk, bright emitters, dim interiors and night lighting impose substantially different limits.

Audit every floating-point texture and intermediate, including R11G11B10 and half-float targets,
at the proposed physical range. If one fixed scale cannot preserve bright and dark signals,
require an explicit pre-exposure design before implementation proceeds. That design must cover
the frame's common storage scale, temporal rescaling, persistent probe/cache storage, sky LUTs,
water inputs and metering. Per-pass arbitrary clamps or independent exposure scales are not
acceptable remedies. This is a storage decision gate, not authorization for an unrelated HDR
attachment redesign.

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

Version any new configuration and material mappings. Existing saves and third-party sources
must receive documented compatibility defaults. Exposure settings need an explicit migration
because scene-relative old values may no longer produce the same display brightness. The goal
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
- Metering and final display preserve source ratios; changing exposure does not change light
  output or feed generated bloom and shafts back into metering.

Quantitative tolerances must be stated before implementation qualification, with separate
allowances for analytical arithmetic, texture quantization and approximate propagation/transport.
Reference scenes use fixed exposure, neutral grading and isolated sources, then combined sources.
Broader in-game acceptance checks sunlight versus lamps, indoor/outdoor transitions, night
visibility, transparent/held surfaces, water and LumOn on/off with automatic exposure restored.
Automated receipts establish numerical and lifecycle behavior; user-run inspection establishes
whether the chosen game lighting and adaptation are acceptable.

## Decisions required before implementation planning

Approval would adopt the physical-unit contract, a shared calibrated engine-field adapter and
the stated scope and acceptance boundaries. Exact lamp/emissive compatibility references and
the block response curve require reference measurements. The proposed solar reference and
metre-per-block convention must be confirmed. HDR range receipts must resolve fixed scaling
versus a separately specified pre-exposure design. These decisions belong in the approved design
before a dependency-ordered implementation checklist is produced.
