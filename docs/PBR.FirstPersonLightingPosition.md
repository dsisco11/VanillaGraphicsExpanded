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
