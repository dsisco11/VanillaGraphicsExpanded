# Atmospheric lighting publication and sky ownership

## Status and objective

Implemented ownership contract following the installed-engine consumer inspection. Runtime uses
an independently registered fullscreen sky renderer and pre-blend atmospheric compatibility publication.
The user accepted the replacement's appearance on 2026-10-11. Migration of legacy cloud lookup content belongs to the separate
volumetric-cloud task; the compatibility adapter retains valid original lookup handles meanwhile.

Extend the existing atmospheric subsystem to own environmental lighting independently of the
sky draw. The sky renderer consumes published state; it does not update engine lighting globals,
advance frame counters, calculate ambient lighting, or reproduce `calcSunColor`.

## Existing machinery

`AtmosphereModel`, `AtmosphereLookup`, and `AtmosphereBackend` already calculate atmospheric
transport with GPU computation and asynchronous CPU fallback. `AtmosphereLighting` carries
direct solar illuminance in lux, diffuse environment response (E/pi), horizontal sky luminance
and sky/aerial luminance in nits. Local extinction remains inverse metres; lookup transmission
is dimensionless. Each directional source uses a declared amplitude, and all products in a
completed publication share the same admitted medium and celestial inputs.
`AtmosphereModSystem` publishes matching textures and lighting before scene rendering.
Direct lighting, composite, liquids and LumOn already consume this publication.

Keep these owners and their atomic publication. Do not create a second atmospheric integrator
or run transport in a draw callback. The RGB model now admits solar and lunar sources through
the same transport backend and retained medium table. A complete publication also carries
separately budgeted airglow and the diffuse integral of the owned stars/galaxy. Cloud-volume
transport remains outside this model.

The compatibility ambient reference is `Environment + DirectIrradiance / pi`, representing a
white surface facing the selected celestial source, with no additional visibility or incidence factor. Apply
the shared unit-exposure peak-preserving shoulder and sRGB transfer for legacy color consumers.
Fog uses the same transfer on `Horizon`. Compatibility daylight is clamped Rec.709-weighted
luminance of that encoded ambient reference; it is an artistic visibility scalar, not physical
irradiance or photometric luminance. No nighttime floor is added. Physical VGE consumers still
read the original snapshot. The replacement's appearance was accepted by the user on 2026-10-11;
this mapping remains an artistic approximation. The lunar/background extension has focused
qualification tracked in PBR.PhotometricLighting.todo; its in-game appearance acceptance is pending.

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

`AtmosphereLighting` carries `LookupStorageScale` and `CalibrationGeneration` with its immutable
CPU snapshot. `AtmosphereTextureSet` supports admitted fixed 1/16 light encoding, validates both
smooth/solar-Mie/lunar-Mie sky and aerial arrays before upload, and commits its scale/identity after every upload
succeeds. Coverage and attenuation remain dimensionless. The atmosphere producer now derives one
amplitude for the declared 100,000-lux solar zenith reference, scales reflected lunar transport
to the 0.1-lux full-moon reference, and supplies physical celestial lux,
sky/disk/aerial nits and environment E/pi. Runtime lookup upload now encodes lighting with fixed
C=1/16, and sky/aerial consumers decode the matching generation once. The uploaded band maxima,
maximum Mie phase, interpolation and narrowing allowances produce `ReconstructionMaximumNits`;
that bound is committed with the same complete set. Invalid or unsupported lighting/extinction
rejects the replacement before upload and retains the previous publication. Complete scene/storage migration remains
tracked in `PBR.PhotometricLighting.todo`. Source conversion alone is not a releasable photometric
pipeline. A camera exposure change does not rebuild these persistent lookups.

Keep `AtmosphereModSystem` as lifecycle/scheduling composition. Put environmental derivation
and engine adaptation in separate atmosphere-domain files with explicit input/output contracts.
Move the `AtmosphereLighting` record to its own file when extending it, keeping lookup integration
separate from snapshot representation.

Publish compatibility values only from the same completed generation as visible sky and lighting
textures. On pending work or failed upload, retain the previous complete generation. A frame
counter still advances once per enabled frame even when atmospheric computation or sky drawing is unavailable.
Startup uses the neutral black atmospheric snapshot until the first completed result. Update failures
retain the last successfully uploaded snapshot and report the failure without changing sky ownership.
Shader reload withdraws pending transport through the existing backend while retaining the completed
lighting snapshot, sky/aerial texture handles and ambient ownership. Completed texture storage does
not depend on the compute executable. World teardown still clears publication and retires storage.

Engine ambient RGB is a combined sunlight tint/brightness multiplier: installed
`fogandlight.vsh` multiplies it by voxel sunlight brightness. Ambient blending linearly mixes
ordered modifiers, then scales ambient by scene brightness and fog by scene and fog brightness;
it does not apply a color transfer. Therefore do not assign diffuse-only `Environment` directly.
Derive a documented compatibility response from `DirectIrradiance` and `Environment`, accounting for the
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
introduce a receiver brightness floor. Night appearance must be validated against the declared
lunar/background source quantities, independently of compatibility daylight.

Do not bind the VGE atmospheric sky LUT to vanilla sky/glow samplers: its coordinates, channels
and interpretation differ. Retain valid legacy handles during migration, then migrate actual
remaining consumers to physical atmospheric inputs. `SunsetMod` is not a physical-model input.

## Draw ownership

The planned [owned stars and galaxy](PBR.StarsAndGalaxy.todo) extends this lifetime
and atomic-publication policy to a load-generated HDR source and instanced stars.
Its source set and derived lighting must publish together when the cached illumination
is activated. VGE independently registers CelestialRenderer at Opaque order 0.19.
NativeNightSkyDrawRegistration unregisters the native night-render callback and both
sun/moon draw callbacks through the public event manager. The native client-system registry and asset lifecycle
remain intact. No Harmony night-draw patch or client-system replacement is used.
The obsolete night-sky compatibility shader transformation is retired.
Historical night-sky counter statements above describe the former separate renderer;
no other inspected consumer reads that renderer's private counter.

The moon has an independent `MoonRenderer` at Opaque order 0.195, after stars/galaxy
and before atmospheric local fog/underwater coverage at 0.2. `Celestial.Moon` is a
tracked additive HDR pass with no depth writes or glow contribution. Its GLSL 330
program reconstructs a procedural phased Lambert disk without engine shader code,
moon textures or native mesh data. Clouds and terrain occlude it in later passes.

The completed atmosphere freezes `MoonPhase` with lunar direction, angular radius
and extinguished `Lunar` illuminance. `MoonDisk` normalizes the projected surface
integral to this RGB illuminance, using the analytic Lambert phase integral for a
fully visible disk and CPU projected-area quadrature for a horizon-clipped disk.
This normalization is cached by immutable publication identity, not recomputed
per frame. The disk receives no second phase multiplier or atmospheric extinction.
It preserves the admitted apparent radius and 0.1-lux full-moon reference. The
procedural surface currently has uniform albedo; it does not model lunar craters.

The CPU-certified radiance maximum joins frozen frame storage admission; the native
moon compatibility allowance is withdrawn while owned lunar rendering is enabled.
Resize, reload, context changes and world teardown use existing graphics owners.
Failure leaves native drawing disabled and retries only at reset/reload boundaries.

The celestial generation owner now prepares and submits its own tiled HDR cube during
world loading, retaining a fenced complete cube, catalog, source quadrature, encoding
and bounds. It uses the existing global sky quality selector and retains completed
resources across failed replacements. The owned galaxy and immutable instanced star
subset use the same source encoding and calendar rotation. External transmission is
generated by the existing atmosphere sky compute dispatch, with the same observer and
medium as sky/aerial transport, and is fenced and published with that complete texture
set. CPU fallback builds and caches it on the existing atmosphere worker. The draw
only borrows the matching texture; it never runs an atmospheric integrator or uploads
its own extinction table. Camera exposure does not trigger atmospheric computation.

This 512-sample RGBA32F table is dimensionless RGB transmission by elevation. Radially
stratified atmospheric density makes observer-to-space extinction azimuth independent;
the horizon-focused sampling therefore requires one dimension. The 2D sky lookup also
represents scattered radiance's azimuth dependence relative to the solar/lunar sources,
with separate smooth and Mie bands. Their sample counts describe different functions.
External transmission follows the existing atmospheric generation cadence, including
directional updates; no independent compute dispatch, fence or synchronous readback is
introduced. The existing asynchronous output readback includes its additional 8 KiB.

The celestial pass runs at Opaque order 0.19, before atmosphere at 0.2 and later disks,
clouds and receivers. Its RGB is additive physical lighting with no independent halo.
The atmosphere contributes its full radiance while retaining local horizon/flat fog and
underwater occlusion of the background. That legacy local fog is distinct from planetary
external extinction. Daylight alpha and the legacy cloud proxy do not additionally fade
the external sources; actual cloud drawing still occludes them later. Exposure and glare
remain under existing postprocessing owners, without a star-specific boost or cutoff.

The atmospheric producer embeds only the separately budgeted 0.0004-lux airglow in visible
sky radiance. No draw-time subtraction or second uniform stellar contribution remains.
`CelestialDiffuseLighting` integrates the quality-independent 64x64-per-face galaxy quadrature
and all 4096 calibrated stars through the completed external-transmission table. Rotation,
cosine and solid-angle weights determine horizontal illuminance; the existing receiver
representation stores E/pi. Its bounded cloud proxy, 1-0.9*coverage, enters once in illumination.
Visible external sources use extinction once and are occluded by actual later cloud drawing,
without applying that proxy again. Airglow is already ground-level and is not extinguished as
an external emitter. Solar/lunar transport and shared shadow ownership are unchanged.

The existing atmosphere backend runs the diffuse sum on a cancellable worker and caches its
immutable result by complete source, rotation, observer/media, solar/lunar inputs and grid.
Rotation-only updates reuse directional transport and combined solar/lunar/airglow arrays.
There is no extra atmospheric ray integration, GPU dispatch, texture, per-frame source rebuild
or synchronous readback. Finite-path celestial scattering uses the documented isotropic
approximation, E/pi*(1-T); it shares the same attenuated RGB and preserves dimensionless T.
Compatibility horizon fog receives that same isotropic celestial term. This is not directional
galaxy scattering or a volumetric airglow/cloud solution.

`CelestialSourceLifetime.Ready` retains a fenced candidate while its atmosphere is pending.
Only successful complete texture upload commits it as the display source, retiring the prior
set. Failed uploads retry the retained immutable candidate; pending/failed builds retain the
last source, rotation and complete lighting together. The draw consumes the published rotation,
not the advancing calendar independently. Reload cancels pending transport while preserving
completed resources; world teardown cancels workers and clears both publications. Before the
first source succeeds, stellar illumination and drawing are zero while solar/lunar and airglow
remain available. Context-invalidated source results cannot commit against a new GPU set.
The atmosphere owner also tracks the existing context-registration generation: namespace
changes withdraw old lighting/texture handles and pending results, then recreate its existing
backend and texture sets. The celestial owner retains its independent recovery scheduling.

Terrain, forward receivers, liquids/reflections, water transport and LumOn consume the same
published Environment and aerial textures through their existing bindings. Sky visibility,
interior occlusion and AO remain receiver operations. Frame bounds use the updated environment
and uploaded aerial reconstruction envelope. LumOn prioritizes bounded direct relighting when
the environment changes, and source replacement changes the surface dependency identity so
dependent probes retire obsolete history. Ordinary celestial motion retains temporal probe
history rather than clearing it every frame. Live appearance and runtime performance acceptance
remain outstanding in the linked stars/galaxy checklist.

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

Current replacement cleanup and reload validation passed the SPIR-V-enabled Debug build
with zero errors and 128 unchanged warnings. The subagent-run regression suite passed 300 tests
with five explicit opt-in measurement skips and no failures. Coverage includes exact source
preservation and native linking of both installed sky SSAO variants, complete lighting/sky/aerial
publication retention on reload, owned sky effects and lifecycle, atmospheric-model regressions,
frame inputs and runtime HDR handoff. All 431 production and 508 test shader variants reused
compiler results; no shader compiler invocations or emission occurred in this build.
Receipts: `artifacts/PbrColor/sky-task-current-repaired-build.log` and
`artifacts/PbrColor/sky-task-current-repaired-focused.log/.trx`.
The validation subagent independently reviewed the cleanup and reload lifetime changes.
The user supplied visual acceptance on 2026-10-11: "yea it looks fine", resolving the parent
task's remaining acceptance gate. This is overall appearance acceptance; no individual scene
results or GPU timing measurements were supplied. The agent did not launch the game.

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
