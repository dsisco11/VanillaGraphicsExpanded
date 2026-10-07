# Atmospheric lighting publication and sky ownership

## Status and objective

Implemented ownership contract following the installed-engine consumer inspection. Runtime uses
an independently registered fullscreen sky renderer and pre-blend atmospheric compatibility publication.
Visual acceptance and migration of legacy cloud lookup content remain open.

Extend the existing atmospheric subsystem to own environmental lighting independently of the
sky draw. The sky renderer consumes published state; it does not update engine lighting globals,
advance frame counters, calculate ambient lighting, or reproduce `calcSunColor`.

## Existing machinery

`AtmosphereModel`, `AtmosphereLookup`, and `AtmosphereBackend` already calculate atmospheric
transport with GPU computation and asynchronous CPU fallback. `AtmosphereLighting` carries
direct solar irradiance, diffuse environment response (irradiance divided by pi), horizontal
sky radiance, local extinction, and sky/aerial lookup data in relative scene-linear units.
`AtmosphereModSystem` publishes matching textures and lighting before scene rendering.
Direct lighting, composite, liquids and LumOn already consume this publication.

Keep these owners and their atomic publication. Do not create a second atmospheric integrator
or run transport in a draw callback. The model is RGB, solar-driven, and approximate; physical
lunar illumination and cloud-volume transport are not supplied by the current model.

The implemented compatibility ambient reference is `Environment + Solar / pi`, representing a
white sun-facing Lambertian surface, with no additional visibility or incidence factor. Apply
the shared unit-exposure peak-preserving shoulder and sRGB transfer for legacy color consumers.
Fog uses the same transfer on `Horizon`. Compatibility daylight is clamped Rec.709-weighted
luminance of that encoded ambient reference; it is an artistic visibility scalar, not physical
irradiance or photometric luminance. No nighttime floor is added. Physical VGE consumers still
read the original snapshot. This explicit approximation needs user-run twilight/night acceptance.

## Verified engine consumers and ordering

Inspection of installed `VintagestoryLib` and API assemblies found:

| State | Consumers and meaning | Replacement owner |
| --- | --- | --- |
| `SkyDaylight` | Chunk opaque/AfterOIT and sun/moon callbacks, plus the old sky; a legacy brightness/visibility scalar, not irradiance | Atmospheric compatibility publication |
| `DitherSeed` | Generic `ShaderProgramBase.Use` publication | Shared frame/display state |
| `ClientMain.frameSeed` | Sky advances it; sun/moon reads it | Shared frame/display state |
| `SkyTextureId`, `GlowTextureId` | Generic shader activation publishes legacy lookup handles | Legacy resource compatibility adapter |
| `SunsetMod` | Generic shader activation publishes the calendar's artistic lookup offset | Legacy compatibility adapter while lookup consumers remain |
| `Sunglow.AmbientColor` RGB and weight | Ambient modifier blending | Atmospheric compatibility publication |
| `Sunglow.FogColor` RGB and weight | Ambient modifier blending | Atmospheric compatibility publication |
| `FogColorSky` | Constructed and mutated by the sky helper; no external field reader found in inspected assemblies | Compatibility only if a consumer requires it |

The night-sky renderer maintains its own frame seed; do not accidentally advance or replace it.
Installed `clouds.fsh` and `cloudmap.fsh` still call `getSkyGlowAt`, and `chunkopaque.fsh`
contains a legacy `getSkyColorAt` path. Shader patching can alter reachable uses; verify the
effective installed variants before declaring any legacy input obsolete.

`AmbientManager.UpdateAmbient` runs at `Before`, order 0. Atmosphere publication currently runs
at `Before`, order -0.5. Publish atmospheric ambient modifiers there, before blending. The
engine sky callback runs at `Opaque`, order 0.2; its existing `calcSunColor` updates occur after
the current frame's ambient blend. Suppress those writes when VGE assumes ownership, otherwise
they overwrite the new publication. Do not manually run the ambient blend a second time.

## Publication contract

Keep `AtmosphereModSystem` as lifecycle/scheduling composition. Put environmental derivation
and engine adaptation in separate atmosphere-domain files with explicit input/output contracts.
Move the `AtmosphereLighting` record to its own file when extending it, keeping lookup integration
separate from snapshot representation.

Publish compatibility values only from the same completed generation as visible sky and lighting
textures. On pending work or failed upload, retain the previous complete generation. A frame
counter still advances once per enabled frame even when atmospheric computation or sky drawing is unavailable.
Startup uses the neutral black atmospheric snapshot until the first completed result. Update failures
retain the last successfully uploaded snapshot and report the failure without changing sky ownership.

Engine ambient RGB is a combined sunlight tint/brightness multiplier: installed
`fogandlight.vsh` multiplies it by voxel sunlight brightness. Ambient blending linearly mixes
ordered modifiers, then scales ambient by scene brightness and fog by scene and fog brightness;
it does not apply a color transfer. Therefore do not assign diffuse-only `Environment` directly.
Derive a documented compatibility response from `Solar` and `Environment`, accounting for the
consumer's existing sunlight factor and avoiding double lighting in patched PBR paths. Derive
fog approximation from `Horizon` or a documented directional sky sample.
Never reinterpret irradiance as display RGB. The engine adapter must
document the consumer's expected color convention and use the shared color conversion where
needed. Keep HDR physical values untouched for VGE consumers. A global fog color is only a
legacy approximation; aerial radiance and transmittance remain the physical transport inputs.

Use a VGE-owned ambient modifier and remove/neutralize only the obsolete sky contribution.
Preserve weather, underwater, night vision and other ambient modifiers. Reset or restore ownership
on world teardown or replacement disposal; drawing failures do not relinquish publication.
Never clear the ambient modifier collection wholesale.

`SkyDaylight` needs an explicit dimensionless compatibility mapping and independently specified
star-visibility behavior. Do not silently copy the engine's daylight-minus-moonlight formula or
introduce a night irradiance floor. Night appearance must be validated against the solar-only
model's limits before claiming equivalent lunar illumination.

Do not bind the VGE two-lobe sky LUT to vanilla sky/glow samplers: their coordinates, channels
and interpretation differ. Retain valid legacy handles during migration, then migrate actual
remaining consumers to physical atmospheric inputs. `SunsetMod` is not a physical-model input.

## Draw ownership

The owned sky `IRenderer` runs at Opaque order 0.2. It owns a procedural fullscreen triangle through an empty VAO,
camera-relative ray transforms, pipeline state and drawing, and reads the completed atmospheric
publication. The fragment shader reconstructs viewing rays and samples the existing atmospheric
LUT. Neither a dome mesh nor the engine mesh generator is used. Legacy spatial effects use a
virtual 250-unit ray position. Avoid implicit dependence on matrices prepared by the old callback.

The prefix suppresses the entire base sky frame callback for the lifetime of the registered VGE
sky renderer. Its construction/asset lifecycle remains for legacy lookup consumers. There is no
per-frame ownership flag or readiness-based vanilla fallback. Disposing the replacement releases
lifecycle ownership; missing textures, shader failures, reload and world resets do not.

The renderer prepares its own drawing resources. An unavailable resource skips its draw, while
atmospheric and frame publication continue independently. Draw/preparation exceptions are reported
and disable owned drawing until reload/reset; vanilla remains suppressed. Atmospheric update
failures retain the last complete snapshot. Foreign ambient-slot replacements are preserved without
reactivating the vanilla sky callback. Shared daylight/fog fields still update even if the ambient
color slot is missing or owned by another mod.

## Validation and remaining acceptance

The fullscreen replacement passed the SPIR-V-enabled Debug build and 203 focused atmosphere,
frame-input and procedural-geometry tests, with five opt-in measurement skips and no failures.
The suite exercises per-pixel camera rays, virtual-depth fog, and lifetime recovery without engine
mesh uploads or deletions. See [fullscreen submission](PBR.Atmosphere.md#owned-fullscreen-submission)
for receipts and the remaining live visual/performance acceptance.

Historical persistent-ownership validation before fullscreen conversion passed: normal Debug build and 265 regression tests, with five
explicit opt-in measurement skips and zero failures. Coverage includes suppression before the first
snapshot, missing depth resources, mesh preparation exceptions, reset/reload recovery, disposal,
and fresh daylight publication while preserving a foreign ambient-color slot. A stale incremental
assembly initially failed the new daylight assertion; a forced rebuild and matching production/test
DLL hashes preceded the clean run. Receipts: `artifacts/PbrColor/sky-no-fallback-build.log`,
`sky-no-fallback-tests.log`, and `sky-no-fallback.trx`. No game was launched.

Historical per-frame ownership validation passed: Debug build with zero errors (115 existing warnings;
none in the changed ownership code/tests), and 266 regression tests passed with five explicit
opt-in measurement skips and no GPU-availability skips. The earlier focused run passed 38/38;
these overlap the broad suite. Coverage includes SSAO on/off native draw/state restoration,
prefix installation, camera-relative input packing, modifier order/restoration and foreign
ownership, frame seed wrapping/resource publication, and reload/resize/world retirement.
Upload/deletion integration is checked through the engine API with fixture-owned native storage;
this is not an in-game allocation or appearance measurement. Receipts:
`artifacts/PbrColor/atmosphere-ownership-build.log`, `atmosphere-ownership-regression.log/.trx`,
and `atmosphere-ownership-focused.log/.trx`.

The following remain acceptance and coverage requirements for further consumer migration:

- Resolve engine ambient RGB conventions and effective legacy cloud/chunk consumers.
- Specify and test the physical-to-legacy brightness mapping, twilight opacity and night policy.
- Verify publication precedes ambient blending and all sky/celestial consumers.
- Test unchanged, pending and failed atmospheric generations without mixed lighting/textures.
- Test exactly-once frame progression and retained independent night-sky behavior.
- Test teardown/reload, missing resources and continued vanilla suppression without stale modifiers.
- Validate owned rendering, celestial order and state restoration with focused GPU tests.
- Obtain user-run day/twilight/night, weather and underwater visual checks; do not launch the game.

Source evidence: `AtmosphereModSystem.cs`, `AtmosphereLookup.cs`, installed shader includes,
and inspection receipts under `artifacts/PbrColor/atmosphere-global-consumers.txt` and
`artifacts/PbrColor/hdr-installed-sky-il.txt`. These receipts describe the inspected installation,
not every engine version or third-party mod.
