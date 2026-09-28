# First-person lighting positions

The engine draws first-person hands and held items with a separate projection and
an offset `gl_FragDepth`. That depth controls visibility, but cannot reconstruct
their physical position using the world projection used by deferred lighting.

Patched `ALLOWDEPTHOFFSET` variants of the standard and animated-entity shaders
write their actual view-space position into attachment 3 and mark `gNormal.a` as
negative. The normal's RGB encoding remains unchanged. Ordinary receivers retain
their existing nonnegative alpha and depth reconstruction.

Direct lighting reads the explicit position for marked pixels, so sun-shadow
coordinates, point-light distances and BRDF view directions no longer depend on
the first-person visibility offset or hand FOV. PBR composition uses the same
position for its view direction and fog/aerial distance. The engine's depth write
and the item's coverage remain unchanged.

When SSAO provides attachment 3, VGE borrows that texture and preserves its
first-person SSAO exclusion value in alpha. With SSAO disabled, VGE owns an
RGBA16F fallback at the same attachment slot. It is not inserted into the engine's
texture deletion list. No ninth MRT attachment is required. Position writes are
unblended; only marked pixels consume the texture, so ordinary draws need not
populate the fallback.

This corrects the direct-lighting and PBR-composite position contract. It does not
redesign LumOn's depth-based tracing or temporal reconstruction for first-person
models.

## Primary outputs with the engine OIT define

The captured first-person `standard` shader defines `USEOIT=1` even though this
shader family writes primary-framebuffer outputs rather than OIT accumulation.
Guarding VGE declarations and writes with `USEOIT == 0` removed attachments 4–7
and the explicit-position marker, while the engine continued writing its own
normal and position outputs. The same guard also removed the deferred albedo
branch, allowing forward-lit color to enter deferred lighting with missing
surface data.

`VGE_SURFACE_PRIMARY_OUTPUTS` now describes the actual shader family: enabled for
standard and instanced, conditional on `USEOIT` for animated entities, and disabled
for transparent terrain. The sun's metadata clearing uses the same contract.
Installed-shader coverage includes standard with `USEOIT=1`, `SSAOLEVEL=2` and
`ALLOWDEPTHOFFSET=1`; raster coverage checks the first-person position marker,
material publication, unlit albedo and foreground depth occlusion. Live rendering
confirmation remains a user-run check.

Focused validation passed 69 tests across installed shader variants, mesh depth
and G-buffer capture, material capture boundaries and sun rasterization. Receipt:
`artifacts/StandardOutputGuards/standard-output-guards.trx`.
