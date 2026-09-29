# Thin foliage sunlight transmission

Materials can opt into colored backlighting with `"transmission": 0.35` in their material definition. The scalar inherits from defaults; explicit zero disables it. Values are bounded to [0, 1], with nonfinite values treated as zero. The built-in `plant` material uses 0.35; opaque grass-covered terrain remains non-transmitting.

## Shading

`pbr_foliage_transmission.glsl` is shared by deferred direct lighting and forward surface shading. It adds albedo-tinted diffuse sunlight on the opposite side of the visible surface normal. A broad forward-scattering lobe strengthens the effect when viewing toward the sun. Metallic suppresses transmission, and the existing geometric/two-sided normal correction remains responsible for leaf orientation.

Transmission uses solar irradiance, sky visibility and actual shadow-map visibility. It does not inherit the ordinary diffuse shadow floor. It contributes neither specular reflection nor emission. Point lights and indirect lighting retain their current behavior.

This is a thin-surface approximation inspired by foliage shading, not an exact Unreal shader port. It adds no thickness tracing, screen-space blur, new geometry, colored cast shadows or multiple scattering through leaf layers. Existing shadow-map bias and resolution still affect apparent leaf self-shadowing.

## Material storage

The material atlas now stores RGBA16F: roughness, metallic, emissive, transmission. RGB procedural generation, RGB disk caches and override-image alpha masks retain their contracts. Every upload combines cached/generated RGB with current material transmission; changing strength does not require baking a new RGB tile.

The existing RGBA16F G-buffer material attachment stores the same channels. Its former alpha duplicated metallic-derived reflectivity; LumOn reflectivity readers now read metallic directly. No G-buffer attachment or sampler was added. The material atlas payload grows from six to eight bytes per texel.

## Validation and acceptance

Focused tests cover inheritance, explicit zero, bounded values, RGB preservation, real deferred backlighting, metallic suppression and GPU evaluation of shadow/view/surface directions. Installed forward shader variants are compiled as part of focused validation.

Validation on 2026-09-29: the broad material/PBR suite passed 191 of 192 tests; the separate atlas smoke suite passed both tests. Shader compilation succeeded. The remaining failure is `MaterialAtlasCacheKeyStabilityTests` line 109: disabling normal maps leaves `RequiresNormalDepthAtlas` true while relief remains enabled, so the expected key change does not occur. The cache-key implementation is unchanged here. Executing the test on pristine HEAD was blocked by its missing `includes/debug/compute_composite_split.glsl`; a preexisting failure was therefore not confirmed by baseline execution. Logs: `artifacts/foliage-transmission-validation.log` and `artifacts/foliage-transmission-atlas-validation.log`.

User-run visual acceptance remains open: inspect leaves and crossed plants with sun in front, behind and hidden by terrain, compare opaque stone and grass-covered ground, and check both forward and deferred routes. Tune the initial 0.35 strength against those scenes; compilation and numerical tests do not establish visual acceptance.
