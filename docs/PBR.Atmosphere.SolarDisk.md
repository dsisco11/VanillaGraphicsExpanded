# Atmospheric solar disk

## Engine draw ownership

The installed Vintage Story 1.22.7 `SystemRenderSunMoon` was inspected with `dotnet-ildasm`
(`artifacts/RenderSunMoon.il`). `OnRenderFrame3D` runs at Opaque order 0.3. It draws the sun using
`ShaderPrograms.Standard`, setting `skyShaded`, calendar `SunColor`, the authored sun texture and
`sunmat`. It subsequently draws the moon using `ShaderPrograms.Celestialobject`. Its
`OnRenderFrame3DPost` callback at order 999 draws the solar quad with Standard again, color writes
disabled, for the depth-tested occlusion query. Both callbacks call Standard.Use explicitly.

`AtmosphereSunDrawHook` scopes these two callbacks with exception-safe nested restoration.
The explicit Standard atmospheric binding sets `vge_atmosphereSunDraw` on every Use, including
resetting it for ordinary draws. No shader-family name guess identifies an individual sun draw,
and the moon shader is not patched. The existing mesh, draw order, blending, query and renderer
lifetimes remain engine-owned; no second solar renderer or disk is added.

The Standard vertex/fragment sources import stage-specific solar helpers during the existing AST
patch process. Only scoped solar draws return early from main. They bypass the authored solar
texture, calendar tint, geometry warp and vanilla atmospheric fog. The moon and stars retain
their existing shaders and order. The atmospheric LUT already contains the scattering halo;
the new disk adds no textured halo. Underwater treatment and the engine godray output channel
are retained.

The disk also participates in the engine's existing bloom extraction. Its `outGlow.r`
is the bounded maximum RGB component after atmospheric attenuation, display conversion and
underwater treatment, before dithering. Attachment blending applies disk coverage; the
godray channel is unchanged. The engine bloom setting controls the resulting blurred glare.
This adds no atmospheric scattering energy or extra rendering pass, and does not change the
disk's displayed color. It restores bloom participation within the current display pipeline;
it is not an HDR bloom conversion.

## Radiometric contract

The angular radius is 0.021377339 radians (approximately 2.45 degrees diameter), selected as
half the vanilla bright disk diameter, excluding its translucent halo. The installed vanilla
sun texture has an approximately 48-pixel bright-core radius; `prepareSunMat` uses scale 0.04
and a -128-pixel depth offset, while the calendar places the sun at distance 50. The reference is
therefore `0.5 * atan(48 * 0.04 / (50 - 128 * 0.04))`. This is a nominal visual reference;
vanilla eye-height/altitude offsets and texture replacements can alter its apparent size. Solar
irradiance outside the atmosphere remains `(1.474, 1.8504, 1.91198)` in the existing relative
scene-linear units. A circular emitter with uniform radiance L supplies normal irradiance
`E = L * pi * sin(radius)^2` when fully visible.

Let centre elevation be e, observer geometric horizon h and radius r. Set
`q = clamp((e-h)/r, -1, 1)`. The visible fraction is
`F = (acos(-q) + q*sqrt(1-q*q))/pi`. The visible segment centroid lies at elevation
`e + r * 2*(1-q*q)^(3/2)/(3*pi*F)`. With F zero the irradiance is zero; otherwise the existing
12-step atmospheric transmission is evaluated at that centroid and multiplied by F. CPU and
GPU use the same formula. This replaces the previous point-source horizon cutoff.
Near either limb, a thin-segment series avoids cancellation between the acos and chord terms.

Disk radiance is recovered from the published irradiance as `E/(F*pi*sin(r)^2)`. Shader
coverage clips the finite disk against the same published geometric horizon. Thus the visible
portion is not attenuated twice by F. It receives one existing Reinhard/sRGB display conversion.
The vertex helper builds a billboard from the published solar direction and UVs; a rotation-only
view removes camera translation. Clip depth stays just inside clear depth so the engine's solar
occlusion query still counts an unobstructed disk. Pixel derivatives antialias disk and horizon
edges without adding a glow halo.

This is a bounded uniform-disk approximation: transmission is represented by the visible segment
centroid, not integrated independently at every point across the disk. Circular-segment coverage
uses the small-angle tangent-plane approximation. Refraction, solar limb darkening and eclipses
are not modeled. Atmospheric scattering sources still use the directional sun approximation.

## Shared generations and cost

Disk centre, horizon, radiance and direct-lighting direction use the completed atmosphere snapshot.
Deferred PBR, forward PBR, LumOn frame inputs and Surface Cache lighting therefore use its solar
direction and irradiance together. Engine shadow maps retain their engine-owned scheduling.
Pending atmospheric builds keep displaying the previous complete generation.

The sun-direction admission threshold changes from 1/256 to 1/65536 per normalized component
in CPU and GPU scheduling. The old threshold spans much of a solar radius and would make sunrise
advance in a few visible steps. This finer threshold can cause more frequent sky rebuilds while
the sun moves, but never increases the number of concurrent builds or rebuilds the cached
multiple-scattering table solely because of sun motion. Source-table and texture sizes are unchanged.
Direct extinction still uses one 12-step ray per completed snapshot. Disk fragment shading does
no atmospheric integration and allocates no extra render target.

The engine's legacy liquid-specular query still normalizes passing samples against its authored
1500-pixel constant. Its response therefore remains resolution/FOV dependent and is affected by
the new physical disk size. This does not scale VGE's direct solar irradiance; replacing the
base-game liquid lighting remains a separate baseline PBR task.

## Validation

The solar bloom correction passed 58 focused Release GPU cases
(`artifacts/TestResults/solar-bloom.trx` and `solar-bloom-installed.trx`). They exercise the production disk's color/glow
outputs and installed `findbright.fsh` with ambient and extra bloom disabled, proving a
positive dedicated contribution that decreases with attenuated radiance and underwater
treatment and vanishes for extinguished input. Raster coverage checks include horizon
clipping and foreground depth occlusion; installed sky and Standard shader linking also pass.
No live appearance check was performed for this correction.

Release validation passed 199 distinct tests; five optional measurement tests were skipped.
The broad receipt is `artifacts/AtmosphereSolar/solar-stable.trx`. Its grazing-centroid failure
identified CPU/GPU multiply-subtract cancellation and was fixed with a factored chord expression.
`artifacts/AtmosphereSolar/solar-final-math.trx` passed all 26 affected focused cases afterward;
these receipts together cover the final implementation without rerunning unchanged shader variants.

Coverage includes installed Standard shader variants, 16 forward numerical cases (with engine
and atmospheric sun directions deliberately opposed), CPU/GPU complete atmospheric publication
at partial disk visibility, and segment geometry down to grazing offsets of 1e-7 radii.
Three 101-step sunrise irradiance sweeps at 0.001, 25 and 99 km check finite nonnegative lighting,
full occultation, and continuity. Separate checks cover sunset reddening and radiance/irradiance
normalization. GPU raster checks execute the production solar includes, proving half-disk horizon
coverage, translation independence, HDR radiance before display conversion and successful Less-depth
testing against clear depth while foreground depth occludes the disk. Binding tests cover explicit
Standard ownership, complete linked interfaces and celestialobject exclusion; scope tests cover
nested restoration and exception unwinding.

No game process was launched. Live sunrise/sunset appearance, engine callback execution in a running
world and the increased sky-refresh frequency's production cost remain unmeasured.

### Half-vanilla disk sizing

The updated diameter passed the Release/SPIR-V build and 27 focused solar geometry, CPU/GPU
transport and raster tests in artifacts/AtmosphereSolar/solar-half-vanilla.trx. The raster fixture
uses the production radius; camera translation independence, horizon clipping and energy
normalization remain covered. No live game verification was performed.
