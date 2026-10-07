# Horizon-focused sky sampling

Sky LUT dimensions and the quality policy are unchanged. CPU and GPU generation now place more
elevation rows near the observer's geometric planetary horizon, using a quadratic mapping on either
side. Longitude remains uniformly sampled with repeat wrapping. The same radiance integration and
multiple-scattering table are used, so angular sampling changes without increasing ray counts.

## Coordinates and publication

For observer altitude `a` in kilometres, the horizon elevation is
`h = -acos(6360 / (6360 + clamp(a, 0.001, 99)))`. For row coordinate `v` from zero to one,
let `t = 2*v - 1`. Elevation is `h - (pi/2 + h)*t*t` below the middle coordinate and
`h + (pi/2 - h)*t*t` above it. The inverse uses the corresponding square root. At altitude this
concentrates rows around the depressed visible limb, rather than incorrectly keeping them at zero.

Row centres use `v = y/(height-1)`, making the first and last rows exact poles. Azimuth retains
`(x+0.5)/width * 2*pi`. The owned sky shader applies the inverse elevation mapping and
converts it to texture coordinates `(v*(height-1)+0.5)/height`; this accounts for the endpoint rows
and GL's half-texel convention. Longitude repeats across the azimuth seam and latitude clamps at
the poles. The existing azimuth fallback avoids `atan(0,0)` at a pole. The 1x1 startup placeholder
still samples its sole row.

`AtmosphereSkyMapping` supplies CPU coordinates and `atmosphere_sky_mapping.glsl` supplies the
matching GPU generation and sky lookup functions. GPU parameters receive the CPU-computed `h`
for the admitted generation. `AtmosphereLighting.HorizonElevation` travels with the completed LUT;
the owned `SkyInputs.skySunHorizon.w` binding uses that value, never a newer player altitude.
An in-flight altitude change therefore cannot reinterpret the previously published texture.

## Shared illumination

Uneven elevation spacing requires new integration weights. Each row's cell boundaries are mapped
from `(y-0.5)/(height-1)` and `(y+0.5)/(height-1)`, clamped at the poles. Its upward-hemisphere
irradiance/pi weight per azimuth texel is
`(sin(max(0, upperElevation))^2 - sin(max(0, lowerElevation))^2) / width`.
This integrates cosine-weighted solid angle exactly for constant radiance, so concentrating rows
does not brighten the shared environment just by adding more horizon samples. Variable radiance
still uses a bounded quadrature approximation.

Shared horizon lighting means the local horizontal direction (elevation zero), not the depressed
planetary limb. Both backends interpolate the two surrounding LUT rows at elevation zero and
average their azimuth samples. Previously the implementation used the first row above the middle;
that shortcut is no longer valid under the new mapping. No additional atmospheric rays are traced.

The denser horizon trades away some mid-elevation resolution at fixed storage. Exact pole rows
preserve endpoint behavior, but this is not a universal full-sphere error reduction or an increase
in the physical model's ray-integration accuracy.

## Validation

The receipts below describe the earlier engine-patched lookup. Current owned-program lookup and
draw validation is recorded in [owned dome submission](PBR.Atmosphere.md#owned-dome-submission).

Release validation passed 164 distinct tests; five optional measurement tests were skipped.
Receipts: `artifacts/AtmosphereHorizon/horizon.trx` (162 passed) and
`artifacts/AtmosphereHorizon/lookup.trx` (two passed).

Coverage includes CPU/GPU transport parity, installed sky shader linking, invertible monotonic
mapping, all quality dimensions plus an odd extent, pole endpoints and normalized environment
weights. GPU lookup tests execute the production sky patch against a published synthetic LUT,
checking horizon continuity, poles and azimuth wrapping at low and high observer altitudes.

At an unchanged 24-row budget, interpolation was compared against 303 direct scalar radiance
samples per case: 101 elevations within 0.06 radians of the geometric horizon at three azimuths.
This isolates single-scattering vertical reconstruction; it does not measure all-angle accuracy.

| Altitude (km) / sun input Y | Uniform-row RMSE | Horizon-focused RMSE |
| --- | ---: | ---: |
| 0.001 / +0.05 | 0.0709723 | 0.0128280 |
| 0.001 / -0.05 | 0.00225672 | 0.000378327 |
| 25 / +0.05 | 0.0332414 | 0.0105891 |
| 99 / -0.05 | 0.0357843 | 0.0169461 |

Storage and radiance integration counts remain unchanged. Production frame cost and live sunset
appearance were not measured; live appearance remains a user-run observation.
