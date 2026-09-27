# Physical sky and shared atmospheric lighting

## Model and units

`AtmosphereModel` integrates RGB single scattering in a spherical atmosphere. Geometry uses
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
Beer-Lambert extinction, directional scattering and planet occlusion. It implements only single
scattering, not that reference's multiple-scattering solution. Rayleigh and aerosol scattering,
aerosol absorption, and ozone absorption contribute separately. The sun's direct transmission
is zero when its ray intersects the planet; there is no below-horizon sunlight floor.

View integration uses 24 segments and solar optical-depth integration uses 12. Quadratic segment
spacing resolves the dense near-ground layer. Cloud coverage maps to aerosol multiplier 1–8;
this is a bounded haze proxy for weather, not cloud-volume transport or geometric cloud shadows.
Volumetric clouds remain a separate task.

## Publication and rendering

`AtmosphereLookup` builds a 32 x 24 lat-long radiance table, evaluating at most 32 directions per
update after initialization. The initial table completes synchronously before the first scene draw;
subsequent rebuilds take 24 updates. Sun direction is quantized to 1/256 component increments,
altitude to 25 metres, and cloud coverage to 0.05. Stationary unchanged inputs do no integration.
An admitted rebuild finishes even if inputs change, preventing update starvation. It then admits
the newest input state. Camera rotation/bobbing do not affect lookup direction; altitude changes
below the key threshold do not invalidate it.

Publication contains one immutable sky table, direct solar irradiance, upward Lambertian sky
response (hemisphere irradiance divided by pi), horizon-average radiance, and local extinction.
The render service uploads the table before publishing its lighting snapshot. All consumers keep
the previous complete snapshot while a refresh is in progress. Shaders always receive valid atmospheric
inputs and have no atmosphere-readiness branches. At 60 FPS, a rebuild spans about 0.4 seconds. This is a
bounded CPU approach with low-resolution interpolation, not a full-resolution fragment ray march.

The installed sky shader samples this table and uses the existing unit-exposure Reinhard/sRGB
display conversion once. The engine's night/fog alpha calculation is retained, as are subsequent
underwater/night-vision effects. Stars remain the separate engine night-sky draw before the dome;
sun and moon remain engine textured draws afterward. Moonlight is not a second atmospheric light
source in this implementation. At night only residual solar twilight is integrated; no artificial
ambient floor is added. Celestial texture photometry and adaptive night exposure are not calibrated
by this model.

## Shared lighting contract

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

On this machine, five warmed full refreshes took 21.446–24.665 ms (median 22.902 ms), approximately
0.954 ms per bounded 32-direction update. Two full refreshes preceded measurement as warmups.
These are CPU harness observations, not GPU timings or a guarantee of in-game frame cost. Work is
spread across 24 render updates after initialization and stops when the quantized input key is unchanged.
The first scene frame pays one complete lookup (approximately 23 ms in this earlier measurement)
before uploading and exposing the atmospheric resources.

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