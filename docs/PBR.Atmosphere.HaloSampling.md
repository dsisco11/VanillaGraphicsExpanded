# Atmospheric halo sampling investigation

The sky halo is baked into the 2D sky radiance lookup. The aerial-perspective
3D volume applies related scattering to geometry; it is not sampled by the sky
shader. Both include the sun-facing Mie term with asymmetry 0.76.

`AtmosphereTextureSet` uses RGBA16F and linear filtering for all three textures,
with repeating azimuth and clamped elevation/distance. The engine binding hook
unbinds sampler overrides. Sky generation and lookup agree on azimuth texel
centers and horizon-focused elevation mapping. The sky lookup uses normalized
sky geometry direction, without an explicit camera-facing halo-size multiplier.
This does not constitute live verification of engine camera transforms.

## Numerical evidence

A temporary subagent-run diagnostic compared scalar single-scattering transport
with bilinear interpolation on the production angular grid. Altitude was 0.1 km,
aerosol 1, and solar/view azimuth 0.3 radians. Each case sampled 401 elevations
within +/-10 degrees of the sun at 0.05-degree intervals, clamped to 0..90 degrees.
Multiple scattering, display conversion and texture precision were excluded to
isolate angular interpolation. The diagnostic passed and its source was removed;
output is retained in `artifacts/halo-sampling-diagnostic.log`.

| Sun elevation | Default 32x24 row spacing | Default maximum relative RGB error | 128x96 maximum error |
| --- | --- | --- | --- |
| 45 degrees | 10.927 degrees | 5.414% | 0.564% |
| 60 degrees | 12.293 degrees | 4.349% | 0.604% |
| 75 degrees | 15.025 degrees | 2.909% | 0.634% |
| 85 degrees | 15.025 degrees | 8.340% | 0.705% |

Error is the norm of interpolated-minus-direct RGB divided by direct RGB norm.
These results demonstrate interpolation distortion, not a reproduction of the
reported visible bands. Linear filtering keeps values continuous but does not
recover the shape between sparsely sampled rows. Output dithering cannot correct
that shape error.

## Implemented reconstruction

The CPU tensor integrator and GPU compute integrator now accumulate unweighted
single-scattering Mie transport alongside total radiance, reusing the existing
density, sunlight transmission and view integration work. Shared lighting still
uses total radiance. The display textures pack smooth background (Rayleigh plus
multiple scattering) into one elevation band and unweighted Mie transport into
the other. Sampling evaluates the concentrated angular factor at the actual
pixel direction and adds its contribution after interpolation. The Rayleigh
angular factor remains baked because it varies smoothly.

Sky and aerial perspective use the same representation. Aerial terminal matching
normalizes background and Mie separately to avoid baking the angular lobe back
into the correction. Sky and surface consumers use the sun direction from the
same published generation as the textures. Elevation addressing stays within
each band, including poles, and azimuth remains periodic.

The change adds one texture sample and an analytic angular evaluation to each
sky/aerial lookup, without new texture units or integration passes. Sky and
aerial-radiance storage doubles; attenuation storage stays unchanged. GPU output
adds one Mie record per sky/volume cell. Upload scratch arrays are reused by each
texture-set owner. Performance is not inferred from correctness tests.

The user's actual banding source and apparent camera-dependent expansion remain
unconfirmed; live comparison is still required.

## Reconstruction validation

The retained `AtmosphereMieTransportTests` comparison uses the default 32x24 grid,
altitude 0.001 km, aerosol 1, and solar direction `normalize(0.25, 1, 0.1)`.
It compares cell-center interpolation across the upper sky against direct scalar
single scattering. Split/original summed squared RGB error is 0.0851072, a 91.5%
reduction in this workload. This is an interpolation-quality result, not a
production timing or live visual claim, and differs from the earlier solar
elevation scan above.

Release build and 191/191 subagent-run focused tests passed. Coverage includes
CPU/GPU sky and aerial Mie parity, nonzero GPU angular reconstruction at poles,
horizon and seam, packed publication, and forward/deferred consumers. Receipts:
`artifacts/TestResults/mie-transport-final.trx` and `artifacts/mie-transport-final.log`.
An aggregate-only engine fixture error was fixed by synchronizing its null engine
program with GL program zero and the state cache before compilation. No game was
launched, and production GPU cost has not been measured for this change.

## Installed sky declaration-order correction

The initial Mie patch omitted the forward declaration of `atmMieFactor`. Engine
include expansion places `getSkyColorAt` before the imported helper definitions,
so the installed shader failed with `C1503: undefined variable atmMieFactor`.
The synthetic sky lookup tests placed helpers first and missed this ordering.
The existing `InstalledSkyLinks` tests reproduced the exact error with SSAO both
off and on (`artifacts/TestResults/sky-patch-before.trx`). The patch now declares
the helper beside the existing display and coordinate prototypes.
After that declaration-only production correction, the Release build and all
six installed-sky/lookup cases pass (`artifacts/TestResults/sky-patch-after.trx`).
Installed linking also asserts that the per-pixel solar-direction uniform is active.

## Missing-halo follow-up

The latest development log (2026-09-28 20:13:42) shows successful patched sky
compilation. A test exercising engine Compile/Use with the production binding
hooks and a real published CPU lookup verifies the solar direction and sky
sampler unit 13. The installed engine sky renderer binds its original sky and
glow textures to other units after Use. Its sky model-view removes translation
through `MatFollowPlayer`; no camera-facing halo-size multiplier was found.

The solar-disk fragment explicitly writes zero to `outGlow.r`, while the engine's
`findbright.fsh` uses that channel in `ambientBloomLevel + 3*glowLevel + extraBloom`.
The disk is also tone-mapped before bloom extraction. Thus it has no dedicated
solar bloom contribution, although ambient bloom and the separate god-ray channel
can still contribute. This behavior predates the Mie split. Atmospheric
forward scattering and postprocessing glare are distinct effects; increasing
scattering to replace glare would change atmospheric lighting as well.

The earlier failed sky compilations restored vanilla source, so a visual comparison
made during that interval may include the engine's authored halo. These source
findings do not prove which effect was missing in the user's current view.

A rendered dry-scene diagnostic uses the unmodified installed sky fragment,
engine Compile/Use hooks, and a real 16x8 CPU lookup at altitude 0.1 km with clear
weather and sun direction `normalize(0.3, 0.8, 0.4)`. The vertex stage supplies
controlled directions; the engine liquid-depth input is populated for dry sky.
Display RGB toward the sun is (0.17618, 0.23838, 0.32130) with the split,
(0.13320, 0.20533, 0.30239) with old full-radiance interpolation, and
(0.09876, 0.18238, 0.29032) with Mie removed. Thirty degrees away, split RGB is
(0.11069, 0.19046, 0.29555). This demonstrates surviving Mie contribution through
the engine binding and rendering path; it does not reproduce the user's exact
quality-3 weather, camera or postprocessing conditions. No production behavior
was changed during this follow-up.
All 10 focused Release cases passed, including installed sky linking, lookup
reconstruction and engine binding/raster coverage. Log:
`artifacts/missing-halo-focused-tests.log`.
