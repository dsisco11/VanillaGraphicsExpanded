# Baseline PBR ownership and comparison contract

## Status and evidence boundary

Source/IL audit completed against the checked-out API/survival source and installed client assets.
No runtime performance or appearance results are claimed. The comparison cases below are design
constraints for the implementation.

The tables and findings below record the pre-fix audit baseline. The subsequent opaque material
and display-boundary implementation is described in [PBR.MaterialColorAndDisplay.md](PBR.MaterialColorAndDisplay.md).
That document supersedes the terrain vertex-lighting, color-decode and raw HDR-blit findings;
Entity and late/transparent integration is tracked in
[PBR.EntityAndLateCoverage.md](PBR.EntityAndLateCoverage.md); atmosphere, liquid optics and
mode-selection gaps retain their separate task ownership.

## Current ownership

| Surface or operation       | Verified owner/path                                                                                                                                                                        | Boundary or gap                                                                                                                                                                                           |
| -------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Opaque terrain and topsoil | [VanillaShaderPatches](../VanillaGraphicsExpanded/PBR/VanillaShaderPatches.cs) patches chunkopaque/chunktopsoil fragment and vertex programs; material atlases supply RME and normal/depth | Texture mappings own material identity; no block-local face overrides. Topsoil has two UV inputs; material output uses the primary UV.                                                                    |
| Generic meshes             | The same patch owner patches standard.fsh and instanced.fsh with default G-buffer material output                                                                                          | Animated entities use Entityanimated instead; see IL findings below. Held-item standard-derived variants also run outside the opaque deferred pass.                                                       |
| Transparent terrain        | chunktransparent fragment/vertex programs are patched                                                                                                                                      | Installed fragment shader still multiplies texture color by incoming rgba. IL confirms OIT follows opaque composition and uses separate attachments; it is outside this deferred lighting pass.           |
| Liquids                    | chunkliquid fragment shader receives material/G-buffer writes, but its vertex shader is outside the patched chunk-vertex list                                                              | Installed game shader retains rgba lighting, reflection mixing and a sun-specular calculation. Adding G-buffer writes does not make this a fully replaced PBR liquid renderer.                            |
| Sky                        | sky.fsh receives default G-buffer outputs                                                                                                                                                  | Installed shader still calls getSkyColorAt and applies underwater/night-vision effects. Composite preserves background color without indirect light or fog.                                               |
| Fog                        | Patched fragment fog helpers become no-ops; PBR composite applies fog                                                                                                                      | Composite currently uses sampled depth directly in its exponential expression. This is nonlinear window depth, not reconstructed distance. OIT and underwater paths need explicit separate fog ownership. |
| Direct light and shadows   | [DirectLightingRenderer](../VanillaGraphicsExpanded/PBR/DirectLightingRenderer.cs), Opaque order 9, and pbr_direct_lighting.fsh                                                            | Sun shadow visibility multiplies direct sunlight; point lights and emission are separate.                                                                                                                 |
| Indirect light             | LumOnRenderer and LumOnBufferManager                                                                                                                                                       | Optional published indirect texture is consumed by PBR composition. Preserve distinction between lighting availability and shader readiness.                                                              |
| Composition                | [PBRCompositeRenderer](../VanillaGraphicsExpanded/PBR/PBRCompositeRenderer.cs), Opaque order 11                                                                                            | Draws into scratch texture, then blits to primary color for base-game postprocessing.                                                                                                                     |
| Display transform          | Installed game final.fsh                                                                                                                                                                   | Applies gammaLevel/extraGamma and other display adjustments. No new PBR exposure owner is established by these changes.                                                                                   |

Installed assets inspected under G:/Vintagestory/assets/game/shaders: chunkopaque.vsh,
chunktransparent.fsh, chunkliquid.fsh, sky.fsh and final.fsh; face packing is in
shaderincludes/vertexflagbits.ash. These paths describe this installation, not a portable dependency.

## Confirmed coupling and missing behavior

1. In [pbr_composite.fsh](../VanillaGraphicsExpanded/assets/vanillagraphicsexpanded/shaders/pbr_composite.fsh),
   the non-LumOn branch is direct diffuse + direct specular + emission, followed by fog. It has no
   standalone environmental term. This is a source-backed explanation for dark unlit/shadowed
   surfaces with LumOn off; it does not establish the appearance of every vanilla draw path.
2. PbrModSystem obtains LumOnModSystem, supplies its dependencies, and passes its current buffer
   manager to the composite constructor. The composite retains that reference. When startup is disabled, LumOn initialization returns before creating the manager. Later enable
   creates it, but the already-constructed composite retains null: a confirmed reference-lifetime
   gap to fix in the mode-selection task.
3. Composite options depend on both LumOn.Enabled and an available IndirectFullTex. They are set
   before shader readiness/use. Missing GI currently selects direct-only behavior, not an explicit
   standalone environment policy.
4. Composite includes LumOn common/PBR helpers and reads AO/PBR settings from LumOn configuration.
   AO is currently a shader no-op (ao=1); material alpha is reflectivity, not an occlusion signal.
5. Disabling fragment fog/shadow helpers does not remove all incoming vanilla vertex lighting or
   inline liquid reflection calculations. The vertex applyLight call survives the patch and chunkopaque multiplies its texture by rgba;
   primary scene color is not guaranteed to be unlit albedo.

## Shared lighting contract for subsequent work

- Material identity remains texture-based. Roughness and metallic are scalar BRDF inputs; direct
  evaluation clamps roughness to at least 0.04. Emission is a separate radiance contribution.
  The representative cached baseColor statistic is not a replacement for visible texture sampling.
- Direct and indirect buffers are intended to contain linear, fog-free signals. The engine atlas upload uses RGBA (not an sRGB internal format), while primary color is RGBA8.
  Current direct/LumOn albedo reads do not decode sRGB, so this intended contract is not presently
  enforced. Correct decoding and HDR/display boundaries together, rather than adding gamma twice.
- Sun visibility only attenuates direct sunlight and must allow full occlusion. Standalone ambient
  illumination must not weaken shadow visibility or introduce a global floor inside sealed rooms.
- Point-light positions are view-space. Keep distances in that space; rotate light directions into
  the world space used by material normals. Preserve the full inverse-view terrain shadow lookup.
- LumOn's published gather has already applied its intensity/tint; composite supplies neutral
  multipliers and applies receiver response once. Standalone environmental lighting must replace,
  rather than accumulate with, the GI contribution when mode changes.
- Keep pre-display lighting separate from exposure/gamma/postprocessing. Share one explicit display
  boundary across modes; equal screenshots require equal display settings.
- Atmosphere owns sky/environment inputs, material code owns BRDF inputs, direct lighting owns
  direct visibility, liquid code owns liquid optics, and composition owns combination/output.
  Future features should not place atmosphere or liquid algorithms inside the composition root.

## Source-backed engine findings

IL evidence was obtained with dotnet-ildasm 0.12.2, installed only under artifacts/ildasm.
The full-assembly export did not complete; findings below use successful individual-class exports.
Reproduce with DOTNET_ROLL_FORWARD=Major and:

```powershell
artifacts/ildasm/dotnet-ildasm.exe G:/Vintagestory/VintagestoryLib.dll -i ClientMain -o artifacts/basegame-clientmain.il -f
```

Other exports use the same command with the simple class name. GameContent classes below come
from G:/Vintagestory/Mods/VSEssentials.dll. Artifacts are local inspection evidence; method names
and IL offsets provide durable pointers if the exports are regenerated.

- ClientMain.MainRenderLoop: IL_02cd triggers Opaque; IL_0300 loads framebuffer 1, IL_0332
  triggers OIT, IL_0358 merges transparency, IL_0397 triggers AfterOIT. EnumRenderStage in
  ../vsapi/Client/Render/EnumRenderStage.cs defines these stages and explicitly describes held
  items after OIT. Opaque render order 11 does not mean after transparency.
- SystemRenderTerrain constructor registers OnRenderOIT at stage 2/order 0.37. Survival's
  Entity/Behavior/BehaviorHideWaterSurface.cs independently documents liquid order 0.37 and
  restores OIT draw-buffer mappings. GBufferManager.LoadGBuffer only enables our MRT mapping
  for Primary; it does not turn the separate OIT framebuffer into our material G-buffer.
- Installed shaderincludes/oit.fsh assigns reveal/glow/accumulation outputs at locations 0–5;
  locations 4/5 already mean OIT accumulation, whereas our patch uses them for normal/material.
  Those contracts must be reconciled in transparent/liquid shader variants. This audit does not
  claim a specific compilation failure: variant preprocessing/linkage determines that outcome.
- SystemRenderEntities registers opaque entities at order 0.4, OIT entities at 0.4 and AfterOIT
  at 0.7. OnRenderOpaque3D selects ShaderPrograms.Entityanimated. entityanimated.fsh is absent
  from VanillaShaderPatches' lists and only declares vanilla outputs, so animated-entity PBR
  material/normal coverage is missing; standard/instanced coverage does not imply entity coverage.
- SystemRenderEntities.OnRenderAfterOIT calls EntityRenderer.DoRender3DAfterOIT. VSEssentials
  EntityShapeRenderer.RenderHeldItem calls RenderItem; getReadyShader selects StandardShader.
  EntityPlayerShapeRenderer overrides selection for first-person hands. ModSystemFpHands creates
  standard-derived item and entityanimated-derived hand programs with ALLOWDEPTHOFFSET. Preserve
  these variants and their stage; do not assume every held item goes through one normalshading shader.
- ClientPlatformWindows.SetupDefaultFrameBuffers allocates the first primary color texture with
  internal format 32856 (RGBA8), IL_0209. Our composite scratch is RGBA16F and then blits back to
  primary: normalized output storage is an HDR range/precision boundary before final grading.
- TextureAtlas.Upload (the method calling LoadOrUpdateTextureFromBgra_DeferMipMap) passes atlas
  pixels to ClientPlatformWindows.LoadOrUpdateTextureFromPixels. Its allocation uses 6408 (RGBA),
  IL_00c5, with unsigned-byte source data. It is not automatic sRGB decoding. chunkopaque.fsh
  samples the atlas and multiplies by rgba; lumonGetAlbedo and pbr_direct_lighting read RGB as-is.
  The representative CPU baseColor LUT, in contrast, explicitly converts sRGB to linear.
- Installed final.fsh applies gammaLevel and extraGamma after scene composition. That grading
  cannot substitute for decoding material texture colors before BRDF evaluation.
- SystemRenderOITLayers also binds a cloudvolumetric program/liquid-depth input. The cloud task
  must evaluate the existing vanilla implementation rather than assume no volumetric path exists.

Inspected DLL SHA256 values:

- VintagestoryLib.dll: E08F22B493B92FEAF0AAEB79D22437EA0F7EFC38AA7F72A04A47F98BC0E40DF0
- VSEssentials.dll: A28565C5C9181F8CC84B98A2B7457AB824B8ECB7763714448DA1F7C245AECD6E

## Comparison cases and follow-up ownership

| Case                              | Required invariant                                                        | Implementation owner                               |
| --------------------------------- | ------------------------------------------------------------------------- | -------------------------------------------------- |
| Outdoor sun/roof shadow           | Direct occlusion stays intact; standalone environment replaces missing GI | Mode selection and environment lighting            |
| Sealed room, no lamps             | No universal brightness floor or sun-direction leakage                    | Environment visibility and direct shadows          |
| Emissive/held light               | Emission once, view-space light positions, correct late held-item stage   | Direct lighting plus forward/held-item integration |
| Water edge/underwater             | Separate OIT material outputs, transmission/reflection and fog ordering   | Liquid renderer                                    |
| Night exterior/interior           | Explicit environmental model, consistent display transform                | Atmosphere/environment owners                      |
| Animated entity/transparent block | Material/normal contract at its actual stage                              | Entity coverage and forward/OIT lighting           |
| Startup LumOn off, then enable    | Composite sees newly created current resources                            | Mode/resource lifecycle                            |

Existing PBR.DirectLighting and PBR.Composite profiler scopes surround draw calls and exclude
some setup/blit work. Shader linking/cache work is separate. This establishes measurement ownership;
no milliseconds or live timing collection are required to complete the source audit.

The audit is complete as an ownership/contract analysis. It does not certify the current pipeline
as physically correct. The concrete gaps above are implementation requirements for subsequent
material/color-boundary, mode-selection, entity/OIT, atmosphere and liquid work.
