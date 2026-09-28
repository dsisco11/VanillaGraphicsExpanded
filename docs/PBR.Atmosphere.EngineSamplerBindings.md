# Atmospheric samplers on engine shaders

The installed engine's `collectUniformNames` source scanner recognizes
`sampler2DShadow`, `sampler2D` and `samplerCube`, but omits `sampler3D`.
The compiler uses that list to populate its uniform-location table. As a result,
`HasUniform` returned false for the two active aerial-volume samplers on patched
standard shaders. Our previous all-or-nothing aerial check then skipped their
initialization, leaving them on unit zero alongside the item's 2D texture.
Installed DLL evidence is retained in `artifacts/aerial-engine-il.txt`.

After successful compilation, VGE resolves only its allowlisted atmospheric
inputs through the linked-program lookup in `GpuProgramLayout` and registers
active locations in the engine's existing table. Legitimately optimized-out
inputs are omitted. No source scanning or driver queries are added to drawing.

Radiance, attenuation and aerial parameters are tracked independently. Active
radiance and attenuation samplers are assigned units 11 and 12 immediately after
linking, restoring the previous program through `GlStateCache`. The existing use
hook binds the published 3D textures independently, including assigning sampler
units before an atmospheric snapshot exists. Recompilation discards old metadata
and rebuilds the linked interface.

The regression compiles installed, production-patched first-person standard
shaders through the real engine compiler and invokes the actual Harmony compile
and use hooks. It does not manually initialize atmospheric sampler uniforms.
Visual acceptance of the reported held-item artifact remains user-run.

Validation passed: 28 focused cases, including real engine compilation/use with
SSAO off/on, independent aerial-input discovery and installed first-person depth
coverage. The GPU cases check both the repaired engine uniform table and sampler
units immediately after compilation and after use.
Receipt: `artifacts/AerialBindings/aerial-binding.trx`.
