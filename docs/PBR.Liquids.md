# Integrated PBR liquid rendering

## Engine boundary

Inspection of the installed engine on 2026-09-29 establishes that `ChunkRenderer.RenderOIT` selects `ShaderPrograms.Chunkliquid`, binds the primary framebuffer depth as `depthTex`, and draws liquid mesh pools into the engine's transparency targets. The existing terrain atlas call-site hook already covers its `TerrainTex2D` setter.

The vertex stage retains engine liquid displacement, flow vectors, texture coordinates, colormaps, shadow coordinates and the depth offset used near slabs/stairs. Additional varyings carry view position, unlit block illumination and propagated sky visibility. Legacy Fresnel alpha is bypassed for PBR scene draws so it cannot discard the interface before the replacement optics execute.

The fragment interception occurs after animated terrain sampling and colormap/psychedelic tint, before `rgbaFinal` introduces legacy lighting. The existing murkiness discard and final sphere fog remain. PBR scene draws emit through the original `OIT` function and return before legacy foam/specular/distance fog. Offscreen routes retain vanilla shading. Patch failure uses the established source-patch recovery boundary.

## Material contract

Liquid shaders sample the existing RGBA material atlas. Roughness controls GGX reflection; emission is combined with engine glow without multiplying already-lit texture RGB. Positive `transmission` selects water-like optics only for non-emissive, non-lava, non-full-alpha liquids. Zero transmission selects an opaque liquid body response.

The built-in water material has transmission 1. Lava retains its authored emission; tar and honey keep their own material mappings. Unclassified milk, dyes, beer and other liquid textures now map to `liquid_opaque` rather than inheriting water optics. Mod authors can deliberately opt another liquid into water-like optics, but this is not a general optical-medium schema with per-fluid IOR or measured extinction coefficients.

## Optical approximation

Water uses IOR 1.333, dielectric Fresnel including underwater total internal reflection, and GGX direct reflection. The base normal comes from derivatives of displaced world position. Sunlight uses atmosphere radiance, local sky visibility and engine shadow cascades; dynamic lights retain the established view-space point-light convention.

One opaque-depth sample estimates the distance behind the water interface, corrected from view-axis depth to ray distance. Extinction is exponential and tinted by the sampled material color; in-scattering uses local block/sky illumination. This spectrum is an artistic approximation, not measured water chemistry. Thickness is bounded to 32 blocks; sky/missing opaque depth uses 16 blocks; foreground intersections clamp to zero. Underwater camera-to-interface absorption is left to the existing underwater fog so it is not applied twice.

Transmission is straight-through weighted-OIT blending. No screen-space refracted color or reflection ray is sampled. Reflection uses an atmosphere environment approximation when scene reflection is unavailable; it cannot show nearby objects. OIT has scalar revealage, so colored background attenuation is approximated with scalar transmittance plus colored scattering. Multiple liquid interfaces retain the engine's approximate OIT ordering.

Both lighting modes use the same direct interface and local environment fallback. No opaque surface's screen-space GI is projected onto the liquid. Air transport uses the atmosphere's aerial perspective; submerged views use the engine underwater medium. The current display-space OIT resolve remains an approximation; scene-linear transparency is separately tracked in the baseline plan.

## Outputs and cost

Liquid geometry continues to write only engine OIT accumulation, revealage and glow. It does not publish VGE opaque G-buffer normals/depth or overwrite opaque receiver metadata. No extra framebuffer, color copy, renderer, resize resource or GPU attachment is introduced. Sampling adds one material-atlas read and one opaque-depth read for water, plus at most two cascade reads; point-light work is bounded by DYNLIGHTS. Existing legacy foam's neighborhood samples are bypassed in the PBR path.

Those are static operation counts, not measured GPU timing. Pass time and bandwidth measurements, and user-run acceptance, remain outstanding. Caustics are outside this change.

## Validation and acceptance

Installed shader tests cover liquid vertex/fragment linkage with both lighting modes, shadow qualities and OIT output locations. Numerical GPU tests cover normal-incidence Fresnel, total internal reflection, finite sky-depth fallback and foreground depth rejection.

Before completion, the user should inspect shallow/deep and flowing water, silhouettes and slab intersections, submerged transitions, lava and other liquids, day/night and LumOn switching. Record matched GPU pass timing and bandwidth separately from optional caustics. No game process is launched by these tests.

Validation: the installed surface/terrain GPU run passed 86/86. The final installed-shader/liquid-optics run passed 71 cases, including foam/waving variants; the atmosphere interface suite passed 21/21 after updating its liquid-family expectation. Shader and C# compilation succeeded using isolated output. User-run visual acceptance and GPU timing remain open.
