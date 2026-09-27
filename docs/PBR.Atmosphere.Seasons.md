# Seasonal atmospheric boundary approximation

The atmospheric ground boundary now estimates regional snow reflectance from average temperature
and seasonal temperature trend. It does **not** inspect blocks, surface materials, snow depth,
rain-height maps, vegetation or terrain cover. The bare-ground boundary stays at the previous 0.1.
This is an assumed regional boundary for the atmospheric multiple-scattering model, not a claim
about the appearance or actual snow coverage of nearby terrain.

## Engine inputs and coordinates

The checked-out API `../vsapi/Common/API/IGameCalendar.cs` defines `OnGetLatitude(worldZ)` as
-1 at the south pole, 0 at the equator and +1 at the north pole. World Z itself is not a latitude;
the callback owns that mapping. `../vssurvivalmod/Systems/Core.cs::GetSolarSphericalCoords` already
combines this latitude with the 23.44-degree axial tilt and year progress. Rendering continues to
use `Calendar.SunPositionNormalized` unchanged. No additional tilt or seasonal tint is applied.

`../vssurvivalmod/Systems/Temperature.cs::updateTemperature` derives seasonal temperature from
worldgen climate, latitude, year progress, hemisphere, season override, diurnal variation and noise.
Northern and southern seasonal cycles are reversed. The atmospheric capture uses the public
`IBlockAccessor.GetClimateAt` supplied-date overload to reuse this behavior rather than duplicate
the game's seasonal formula. The API documents that this overload mutates only the supplied climate
values while retaining its worldgen temperature/rainfall. No engine objects enter background work.

`AtmosphereSeasonInputs` obtains one regional worldgen climate value at the centre of the player's
64-block X/Z cell. Sea level is the representative regional climate elevation, deliberately independent
of underground viewpoints or flight height. It then requests temperatures at dates surrounding the
current date; this is a single climate-map query plus bounded date evaluations, not a terrain scan.
Alternate dimensions, where the normal-world climate map is not meaningful, use neutral reflectance.

## Temperature and snow estimate

Let `w = DaysPerYear / 32`. The current, past and future means are centred at `t`, `t-w` and `t+w`.
Each mean averages three dates across a width of `w`, with four equally spaced times of day per
date. This suppresses diurnal variation and short climate noise without hardcoding a calendar length.
Normal capture evaluates 36 temperatures. A pinned `SeasonOverride` evaluates only the current
12-sample mean and fixes seasonal trend to zero.

The pure approximation uses:

```
trend = clamp((futureMean - pastMean) / 2, -8, 8)
effectiveTemperature = currentMean - trend
cold = clamp((2 - effectiveTemperature) / 10, 0, 1)
snowCoverage = cold * cold * (3 - 2 * cold)
snowAlbedo = 0.6 + 0.2 * clamp(-currentMean / 10, 0, 1)
groundAlbedo = lerp(0.1, snowAlbedo, snowCoverage)
```

Positive trend retains snow further into spring; negative trend delays accumulation during autumn.
At equal temperature, warming therefore generally has more snow than cooling. The lag is an
assumption reconstructed from the current calendar, not accumulated frame history. Login, teleport,
year wrap and forward/backward time changes cannot retain the previous region's snow state.
Cold snow is assumed brighter than warm, wet snow. These thresholds and reflectances are explicit
heuristics, not a snow hydrology simulation or empirically fitted Vintage Story material values.
Cold arid areas may be overestimated because precipitation history is not modeled. Sea-level
sampling also does not represent elevated mountain snow separately.

## Capture, caching and lifetime

Capture returns an immutable `AtmosphereSeasonSnapshot`. Its climate data is refreshed when the
64-block cell, dimension, one-eighth-game-day bucket, year length or season override changes.
A missing region returns neutral 0.1 and retries at most once per real second while its date key
remains unchanged. Invalid temperature inputs also select the neutral estimate.

Only the resulting ground reflectance affects transport. Both backends round it to increments of
0.02 and use that same admitted value in the multiple-scattering calculation. CPU lookup/worker
keys and GPU request/source-table keys include the reflectance bucket. Date and latitude metadata
do not invalidate a table when its actual physical inputs have not changed. The sun, altitude,
weather and quality keys retain their existing quantization.

Admitted generations finish coherently before the newest ground boundary is accepted. The previous
sky remains displayed during rebuilding. CPU workers receive only copied scalar/vector values;
all climate/calendar access stays on the game thread. World reset creates a fresh seasonal capture
owner and discards outstanding atmospheric work. Shader reload retains the valid climate estimate
but replaces the transport backend as before.

No seasonal modification is made to molecular coefficients, ozone, scale heights or solar irradiance.
The game's local ground temperature is not enough to infer a vertical pressure/temperature profile.
Likewise it does not provide a calibrated aerosol concentration; the existing bounded cloud/haze
mapping remains the only weather proxy. Avoiding additional guessed coefficients keeps the two
transport backends and fixed physical model consistent.

## Validation

Release validation passed 114 atmosphere cases, with five optional measurements skipped, in
`artifacts/AtmosphereSeason/seasonal.trx`. The final focused receipt covers the new approximation,
climate capture, CPU/GPU reflectance admission and actual render-owner world reset. Tests include
opposing hemisphere temperature curves, the two seasonal extrema and transitions, warming versus
cooling, year wrap and backward/forward time changes, pinned season overrides, negative coordinates,
missing-climate retry, alternative-dimension fallback and quantized cache reuse. Calendar tests use
controlled temperature callbacks; they do not claim an exact simulation of the engine's climate noise.
Strict mocks reject all block reads, height-map access and unapproved terrain APIs.

The normal capture budget is one regional climate lookup plus 36 reused-climate date evaluations;
a pinned season needs one plus 12. Cached captures issue zero climate evaluations. Mock-based CPU
timings were collected as diagnostics only: they include mocking/JIT overhead and are not reliable
estimates of production engine cost. The real engine's climate-callback CPU time remains unmeasured.
Transport itself retains its existing budgets; a changed reflectance bucket incurs the same bounded
source-table rebuild as a weather change. CPU and GPU full-sky comparisons exercise 0.1 and 0.8
ground reflectance, including completion of an admitted old generation before the new boundary.

The planning item is marked complete at user direction. Implementation and focused automated
validation are complete; seasonal appearance and live climate-query cost remain unverified and
are not completion blockers. No game was launched.
