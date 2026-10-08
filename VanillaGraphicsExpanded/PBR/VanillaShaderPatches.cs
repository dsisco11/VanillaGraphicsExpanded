using System;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;

using TinyTokenizer.Ast;

using VanillaGraphicsExpanded.ModSystems;
using VanillaGraphicsExpanded.Rendering.Shaders;

using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.PBR;

/// <summary>
/// Handles WHAT modifications are applied to vanilla shader assets.
/// Responsible for defining and applying shader code patches using TinyAst.
/// </summary>
internal static class VanillaShaderPatches
{
    #region Constants
    private static readonly ImmutableArray<string> PatchedChunkShaders =
    [
        "chunkopaque.fsh",
        "chunktopsoil.fsh"
    ];

    private static readonly ImmutableArray<string> PatchedChunkVertexShaders =
    [
        "chunkopaque.vsh",
        "chunktopsoil.vsh"
    ];

    /// <summary>Reports whether a shader stage receives any VGE pre- or post-processing edits.</summary>
    internal static bool Supports(string sourceName) => sourceName is
        "chunkshadowmap.vsh" or "sky.fsh"
        || PatchedChunkVertexShaders.Contains(sourceName)
        || PatchedChunkShaders.Contains(sourceName)
        || SceneColor.SceneColorLegacyPatches.Supports(sourceName)
        || PbrSurfaceShaderPatches.Supports(sourceName);

    #endregion

    #region G-Buffer Injection Code

    // G-Buffer output declarations (locations 4-5, after VS's 0-3)
    // Location 4: World-space normals (RGBA16F)
    // Location 5: Material properties (RGBA16F) - Roughness, Metallic, Emissive, Transmission
    // Note: VS's ColorAttachment0 (outColor) serves as albedo
    private const string GBufferInputDeclarations = @"
// VGE G-Buffer outputs
layout(location = 4) out vec4 vge_outNormal;    // World-space normal (XYZ), unused (W)
layout(location = 5) out vec4 vge_outMaterial;  // Roughness, Metallic, Emissive, Transmission
layout(location = 7) out vec4 vge_outEnvironment;
layout(location = 6) out uvec4 vge_outPatchId;  // (chunkSlot, patchId, packedPatchUv, misc/flags)
";

    private const string ChunkMaterialParamsSamplerDeclaration = @"
in vec4 vge_environment;
in vec4 vge_surfaceBasePosition;
in vec3 vge_surfaceBaseNormal;
in float vge_surfaceDisplaced;
// VGE: Per-texel material params for block atlas (RGB16F: roughness, metallic, emissive)
uniform sampler2D vge_materialParamsTex;
// VGE: Per-texel normal+depth for block atlas (RGBA16F: normalXYZ_packed01, depth01)
uniform sampler2D vge_normalDepthTex;
";

    private const string UvRectVaryings_Vsh = @"

// VGE: Per-face atlas tile rect (base + extent in atlas UV space)
flat out vec2 vge_uvBase;
flat out vec2 vge_uvExtent;
flat out uint vge_faceId;
";

    private const string UvRectVaryings_Fsh = @"

// VGE: Per-face atlas tile rect (base + extent in atlas UV space)
flat in vec2 vge_uvBase;
flat in vec2 vge_uvExtent;
flat in uint vge_faceId;
";

    private const string UvRectAssign_Vsh = @"

    // VGE: Compute per-face atlas UV rect.
    // Prefer SSBO path (FaceData has the packed UV origin/extent); non-SSBO lacks the required information.
    // NOTE: Do not rely on a local `vdata` variable; re-fetch from `faces[...]` to keep this injection location-stable.
    vge_faceId = uint(gl_VertexID / 4);
    #if USESSBO > 0
        FaceData vge_face = faces[gl_VertexID / 4];
        VgeComputeFaceUvRect(vge_face.uv, vge_face.uvSize, subpixelPaddingX, subpixelPaddingY, vge_uvBase, vge_uvExtent);
    #else
        vge_uvBase = vec2(-1.0);
        vge_uvExtent = vec2(0.0);
    #endif
";

    private const string ParallaxUvProlog_Chunk = @"

    // VGE: Parallax mapping (UV indirection)
    vec3 vge_terrainNormal = VgeTerrainNormal(normal, worldPos.xyz, texture(vge_materialParamsTex, uv).a);
#define normal vge_terrainNormal
    vec2 vge_uv = uv;
    mat3 vge_tbn;
    float vge_tbnHandedness;
    VgeTryBuildTbnFromDerivatives(worldPos.xyz, vge_uv, normalize(normal), vge_tbn, vge_tbnHandedness);

    // Tier 2: POM (requires per-face rect).
#if VGE_PBR_ENABLE_POM
    vge_uv = VgeApplyPomUv_WithTbn(vge_uv, vge_tbn, vge_tbnHandedness, worldPos.xyz, vge_uvBase, vge_uvExtent);
#endif

#define uv vge_uv
";

    private const string ParallaxUvEpilog_Chunk = @"

#undef normal
#undef uv
";

    private const string ParallaxUvProlog_Topsoil = @"

    // VGE: Transmitting topsoil materials also use the visible geometric normal.
    vec3 vge_terrainNormal = VgeTerrainNormal(normal, worldPos.xyz, texture(vge_materialParamsTex, uv).a);
#define normal vge_terrainNormal
    // VGE: Parallax mapping (UV indirection)
    vec2 vge_uv = uv;
    vec2 vge_uv2 = uv2;
    mat3 vge_tbn;
    float vge_tbnHandedness;
    VgeTryBuildTbnFromDerivatives(worldPos.xyz, vge_uv, normalize(normal), vge_tbn, vge_tbnHandedness);

    mat3 vge_tbn2;
    float vge_tbnHandedness2;
    VgeTryBuildTbnFromDerivatives(worldPos.xyz, vge_uv2, normalize(normal), vge_tbn2, vge_tbnHandedness2);

    // Tier 2: POM for the primary topsoil uv (requires per-face rect).
#if VGE_PBR_ENABLE_POM
    vge_uv = VgeApplyPomUv_WithTbn(vge_uv, vge_tbn, vge_tbnHandedness, worldPos.xyz, vge_uvBase, vge_uvExtent);
#endif

#define uv vge_uv
#define uv2 vge_uv2
";

    private const string ParallaxUvEpilog_Topsoil = @"

#undef normal
#undef uv
#undef uv2
";

    private const string GBufferOutputWrites_Chunk = @"

    vge_outEnvironment = vge_environment;
    // VGE: Write G-buffer outputs
    // Normal: world-space normal packed to [0,1] range
    // Also sample the per-texel normal+depth atlas so the sampler uniform stays live.
    // We store encoded height01 (0..1) in the otherwise-unused W channel for optional debugging.
    vge_outNormal = VgeComputePackedWorldNormal01Height01_WithTbn(vge_uv, normal, worldPos.xyz, vge_tbn, vge_tbnHandedness);
#if VGE_PBR_ENABLE_POM && VGE_PBR_POM_DEBUG_MODE > 0
    // VGE: POM debug metric (scalar) in the otherwise-unused W channel.
    vge_outNormal.w = clamp(vge_pomDebugValue, 0.0, 1.0);
#endif
    
    // Material: per-texel params stored in vge_materialParamsTex (RGBA16F)
    vec3 vge_params = ReadMaterialParams(vge_uv);
    vge_params = ApplyMaterialNoise(vge_params, vge_uv, renderFlags);
    float vge_roughness = clamp(vge_params.r, 0.0, 1.0);
    float vge_metallic  = clamp(vge_params.g, 0.0, 1.0);
    float vge_emissive  = clamp(vge_params.b, 0.0, 1.0);

    float vge_transmission = clamp(texture(vge_materialParamsTex, vge_uv).a, 0.0, 1.0);

    vge_outMaterial = vec4(vge_roughness, vge_metallic, vge_emissive, vge_transmission);

    // VGE: PatchId (Phase 22 - LumonScene voxel patches)
    // Keep injected code small: the mapping logic lives in an include.
    uint patchId = 0u;
    vec2 vge_patchUv = vec2(0.0);
    ivec3 vge_owningBlock;
    VgeLumonSceneComputeVoxelPatchIdAndUv(vge_surfaceBasePosition.xyz, vge_surfaceBaseNormal, patchId, vge_patchUv, vge_owningBlock);

    uint vge_u = uint(clamp(vge_patchUv.x, 0.0, 1.0) * 65535.0 + 0.5);
    uint vge_v = uint(clamp(vge_patchUv.y, 0.0, 1.0) * 65535.0 + 0.5);
    uint vge_packedUv = (vge_v << 16) | vge_u;

    // VGE: chunkSlot (Phase 22.X - ChunkSlots)
    // Safe fallback: if slot mapping uniforms are not configured (dims <= 0), mapping is treated as disabled
    // and chunkSlot defaults to 0.
    uint chunkSlot = 0u;
    // Signed shift floors negative block coordinates into the same owning chunk as PatchId.
    bool vge_slotOk = VgeLumonSceneTryMapChunkCoordToSlot(vge_owningBlock >> 5, chunkSlot);

    // Slot generation for stale rejection (Phase 22.X).
    uint vge_slotGeneration16 = VgeLumonSceneGetChunkSlotGeneration16(chunkSlot);

    // If mapping is enabled and the chunk is outside the active window, suppress PatchId output entirely.
    // This prevents out-of-window chunks from spamming feedback requests once multi-slot is enabled.
    if (!vge_slotOk)
    {
        vge_outPatchId = uvec4(0u);
    }
    else
    {
        vge_outPatchId = uvec4(chunkSlot, patchId, vge_packedUv, vge_slotGeneration16);
    }
    // Low 16 bits retain slot generation; bit 16 marks geometry without previous-height history.
    if (vge_surfaceDisplaced > 0.0) vge_outPatchId.w |= 1u << 16;
";

    #endregion

    /// <summary>
    /// Attempts to apply pre-processing patches BEFORE imports are inlined.
    /// Use this for modifications that need to happen on the raw shader source.
    /// </summary>
    /// <param name="log">Logger for warnings/errors.</param>
    /// <param name="tree">The SyntaxTree instance (imports NOT yet processed).</param>
    /// <param name="sourceName">The name of the shader source.</param>
    /// <returns>True if pre-processing patches were applied, false otherwise.</returns>
    internal static bool TryApplyPreProcessing(ILogger? log, SyntaxTree tree, string sourceName)
    {
        try
        {
            if (SceneColor.SceneColorLegacyPatches.Supports(sourceName))
            {
                SceneColor.SceneColorLegacyPatches.Preprocess(tree);
                return true;
            }
            if (sourceName == "chunkshadowmap.vsh")
                return Tessellation.TerrainDisplacementPatches.Apply(tree, sourceName);
            if (PbrSurfaceShaderPatches.Supports(sourceName))
            {
                var editor = tree.CreateEditor();
                bool patched = PbrSurfaceShaderPatches.Preprocess(tree, editor, sourceName);
                if (sourceName is "standard.vsh" or "standard.fsh")
                {
                    Atmosphere.AtmosphereSunPatches.Preprocess(editor, sourceName);
                    patched = true;
                }
                if (patched) editor.Commit();
                return patched;
            }
            // Chunk vertex shaders - inject only vertex-safe helpers
            if (PatchedChunkVertexShaders.Contains(sourceName))
            {
                var editor = tree.CreateEditor();
                Tessellation.TerrainDisplacementPatches.Apply(editor, sourceName);
                editor.InsertBefore(Query.Syntax<GlFunctionNode>().Named("main"),
                    "@import \"./includes/vge_uvrect.glsl\"\n");
                editor.Commit();

                log?.Audit($"[VGE] Applied pre-processing to shader: {sourceName}");
                return true;
            }

            // Chunk shaders - inject vsFunctions AND vge_material imports
            if (PatchedChunkShaders.Contains(sourceName))
            {
                var editor = tree.CreateEditor();
                InjectNormalMapDefines(editor);
                InjectPomDefines(tree, editor);

                // Find main function and insert @import before it
                var mainQuery = Query.Syntax<GlFunctionNode>().Named("main");
                editor.InsertBefore(mainQuery, "@import \"./includes/vsfunctions.glsl\"\n")
                    .InsertBefore(mainQuery, "@import \"./includes/vge_material.glsl\"\n")
                    .InsertBefore(mainQuery, "@import \"./includes/pbr_color.glsl\"\n")
                    .InsertBefore(mainQuery, "@import \"./includes/vge_normaldepth.glsl\"\n")
                    .InsertBefore(mainQuery, "@import \"./includes/vge_parallax.glsl\"\n")
                    .InsertBefore(mainQuery, "@import \"./includes/lumonscene_patchid.glsl\"\n")
                    .InsertBefore(mainQuery, "@import \"./includes/lumonscene_chunkslot.glsl\"\n")
                    .Commit();

                log?.Audit($"[VGE] Applied pre-processing to shader: {sourceName}");
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to pre-process shader '{sourceName}'.", ex);
        }
    }

    /// <summary>Publishes configured parallax options for material-aware engine shaders.</summary>
    internal static void InjectPomDefines(SyntaxTree tree)
    {
        var editor = tree.CreateEditor();
        if (!InjectPomDefines(tree, editor)) return;
        editor.Commit();
    }

    /// <summary>Queues configured parallax options into a stage-scoped transaction.</summary>
    internal static bool InjectPomDefines(SyntaxTree tree, SyntaxEditor editor)
    {
        if (ConfigModSystem.Config.MaterialAtlas.TerrainSurfaceDetailMode != (int)Materials.TerrainSurfaceDetailMode.Relief) return false;

        var versionQuery = Query.Syntax<GlDirectiveNode>().Named("version");
        if (!tree.Select(versionQuery).Any()) return false;

        var cfg = ConfigModSystem.Config.MaterialAtlas;

        string fadeStart = cfg.ParallaxFadeStart.ToString("0.0####", CultureInfo.InvariantCulture);
        string fadeEnd = cfg.ParallaxFadeEnd.ToString("0.0####", CultureInfo.InvariantCulture);
        string maxTexels = cfg.ParallaxMaxTexels.ToString("0.0####", CultureInfo.InvariantCulture);

        string defineBlock = $"""

            // VGE: material-controlled relief settings
            #define {VgeShaderDefines.PbrEnablePom} 1
            #define {VgeShaderDefines.PbrPomMinSteps} {cfg.ParallaxMinSteps}
            #define {VgeShaderDefines.PbrPomMaxSteps} {cfg.ParallaxMaxSteps}
            #define {VgeShaderDefines.PbrPomRefinementSteps} {cfg.ParallaxRefinementSteps}
            #define {VgeShaderDefines.PbrPomFadeStart} {fadeStart}
            #define {VgeShaderDefines.PbrPomFadeEnd} {fadeEnd}
            #define {VgeShaderDefines.PbrPomMaxTexels} {maxTexels}
            #define {VgeShaderDefines.PbrPomDebugMode} {cfg.ParallaxDebugMode}

            """;

        editor.InsertAfter(versionQuery, defineBlock);
        return true;
    }

    /// <summary>Publishes configured normal-map options for material-aware engine shaders.</summary>
    internal static void InjectNormalMapDefines(SyntaxTree tree)
    {
        var editor = tree.CreateEditor();
        InjectNormalMapDefines(editor);
        editor.Commit();
    }

    /// <summary>Queues configured normal-map options into a stage-scoped transaction.</summary>
    internal static void InjectNormalMapDefines(SyntaxEditor editor)
    {
        var versionQuery = Query.Syntax<GlDirectiveNode>().Named("version");
        var cfg = ConfigModSystem.Config.MaterialAtlas;
        string scale = cfg.NormalMapScale.ToString("0.0####", CultureInfo.InvariantCulture);
        string defineBlock = $@"

        // VGE: normal-map settings
        #define {VgeShaderDefines.PbrEnableNormalMaps} {(cfg.EnableNormalMaps ? 1 : 0)}
        #define {VgeShaderDefines.PbrNormalMapScale} {scale}
        ";

        editor.InsertAfter(versionQuery, defineBlock);
    }

    /// <summary>
    /// Attempts to apply patches to the given SyntaxTree based on the shader name.
    /// </summary>
    /// <param name="log">Logger for warnings/errors.</param>
    /// <param name="tree">The SyntaxTree instance (already has imports processed).</param>
    /// <param name="sourceName">The name of the shader source.</param>
    /// <returns>True if patches were applied, false if no patches needed for this shader.</returns>
    internal static bool TryApplyPatches(ILogger? log, SyntaxTree tree, string sourceName, Action<ShaderCapability>? declare = null)
    {
        try
        {
            if (SceneColor.SceneColorLegacyPatches.Supports(sourceName))
            {
                SceneColor.SceneColorLegacyPatches.Apply(tree, sourceName);
                declare?.Invoke(ShaderCapability.SceneColorConvention);
                return true;
            }
            if (PbrSurfaceShaderPatches.Supports(sourceName))
            {
                var editor = tree.CreateEditor();
                if (sourceName is "standard.vsh" or "standard.fsh")
                    Atmosphere.AtmosphereSunPatches.Apply(editor);
                PbrSurfaceShaderPatches.Apply(tree, editor, sourceName);
                editor.Commit();
                if (sourceName == "chunktransparent.fsh") declare?.Invoke(ShaderCapability.TwoSidedSurfaceNormals);
                if (sourceName.EndsWith(".fsh", StringComparison.Ordinal))
                    declare?.Invoke(ShaderCapability.SceneColorConvention);
                return true;
            }
            if (PatchedChunkVertexShaders.Contains(sourceName))
            {
                ApplyChunkVertexPatches(tree, sourceName);

                log?.Audit($"[VGE] Applied patches to shader: {sourceName}");
                return true;
            }

            if (PatchedChunkShaders.Contains(sourceName))
            {
                ApplyChunkFragmentPatches(tree, sourceName);
                declare?.Invoke(ShaderCapability.SceneMaterialCapture);
                if (sourceName == "chunkopaque.fsh") declare?.Invoke(ShaderCapability.TwoSidedSurfaceNormals);
                log?.Audit($"[VGE] Applied patches to shader: {sourceName}");
                return true;
            }
            switch (sourceName)
            {
                // case "normalshading.fsh": // Note: Disabled since we don't really care to change the lighting for gui items or first-person view items.
                //     PatchNormalshading(tree);
                //     return true;
                case "sky.fsh":
                    {
                        var editor = tree.CreateEditor();
                        InjectGBufferInputs(editor);
                        InjectSkyGBufferOutputs(editor);
                        editor.Commit();
                        log?.Audit($"[VGE] Applied patches to shader: {sourceName}");
                        return true;
                    }
                default:
                    return false;
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to patch shader '{sourceName}'.", ex);
        }
    }

    /// <summary>Applies all opaque terrain vertex edits in one tree rebind.</summary>
    private static void ApplyChunkVertexPatches(SyntaxTree tree, string sourceName)
    {
        var main = Query.Syntax<GlFunctionNode>().Named("main");
        var editor = tree.CreateEditor();
        editor.InsertAfter(Query.Syntax<GlDirectiveNode>().Named("version"), UvRectVaryings_Vsh)
            .InsertAfter(main.InnerStart("body"), UvRectAssign_Vsh);
        PbrTerrainColorPatches.ApplyVertex(editor, sourceName);
        editor.Commit();
    }

    /// <summary>Applies all opaque terrain fragment edits in one tree rebind.</summary>
    private static void ApplyChunkFragmentPatches(SyntaxTree tree, string sourceName)
    {
        var main = Query.Syntax<GlFunctionNode>().Named("main");
        string prolog = sourceName == "chunktopsoil.fsh" ? ParallaxUvProlog_Topsoil : ParallaxUvProlog_Chunk;
        string epilog = sourceName == "chunktopsoil.fsh" ? ParallaxUvEpilog_Topsoil : ParallaxUvEpilog_Chunk;
        var editor = tree.CreateEditor();

        editor.InsertAfter(Query.Syntax<GlDirectiveNode>().Named("version"),
                UvRectVaryings_Fsh + ChunkMaterialParamsSamplerDeclaration + GBufferInputDeclarations)
            .InsertAfter(main.InnerStart("body"), prolog + GBufferOutputWrites_Chunk)
            .InsertBefore(main.InnerEnd("body"), epilog);

        ReplaceFogAndLightFunctions(editor);
        PbrTerrainColorPatches.ApplyFragment(tree, editor, sourceName);
        editor.Commit();
    }

    /// <summary>
    /// Injects G-buffer output declarations after #version directive.
    /// </summary>
    private static void InjectGBufferInputs(SyntaxTree tree)
    {
        var editor = tree.CreateEditor();
        InjectGBufferInputs(editor);
        editor.Commit();
    }

    /// <summary>Queues shared G-buffer declarations into a stage-scoped transaction.</summary>
    private static void InjectGBufferInputs(SyntaxEditor editor) =>
        editor.InsertAfter(Query.Syntax<GlDirectiveNode>().Named("version"), GBufferInputDeclarations);

    private static void InjectChunkMaterialSampler(SyntaxTree tree)
    {
        var versionQuery = Query.Syntax<GlDirectiveNode>().Named("version");

        tree.CreateEditor()
            .InsertAfter(versionQuery, ChunkMaterialParamsSamplerDeclaration)
            .Commit();
    }

    /// <summary>Declares the terrain vertex tile bounds used by atlas-safe parallax.</summary>
    internal static void InjectUvRectVaryings_Vsh(SyntaxTree tree)
    {
        var editor = tree.CreateEditor();
        InjectUvRectVaryings_Vsh(editor);
        editor.Commit();
    }

    /// <summary>Queues vertex tile-bound declarations into a stage-scoped transaction.</summary>
    internal static void InjectUvRectVaryings_Vsh(SyntaxEditor editor) =>
        editor.InsertAfter(Query.Syntax<GlDirectiveNode>().Named("version"), UvRectVaryings_Vsh);

    /// <summary>Declares matching fragment tile bounds.</summary>
    internal static void InjectUvRectVaryings_Fsh(SyntaxTree tree)
    {
        var editor = tree.CreateEditor();
        InjectUvRectVaryings_Fsh(editor);
        editor.Commit();
    }

    /// <summary>Queues fragment tile-bound declarations into a stage-scoped transaction.</summary>
    internal static void InjectUvRectVaryings_Fsh(SyntaxEditor editor) =>
        editor.InsertAfter(Query.Syntax<GlDirectiveNode>().Named("version"), UvRectVaryings_Fsh);

    /// <summary>Reads terrain tile bounds from the engine's packed face data.</summary>
    internal static void InjectUvRectAssign_Vsh(SyntaxTree tree)
    {
        var editor = tree.CreateEditor();
        InjectUvRectAssign_Vsh(editor);
        editor.Commit();
    }

    /// <summary>Queues terrain tile-bound initialization into a stage-scoped transaction.</summary>
    internal static void InjectUvRectAssign_Vsh(SyntaxEditor editor)
    {
        // Insert at the start of main() body to avoid AST editor wrapping issues.
        // We re-fetch FaceData from the SSBO so this does not depend on local variable ordering.
        var mainStart = Query.Syntax<GlFunctionNode>().Named("main").InnerStart("body");
        editor.InsertAfter(mainStart, UvRectAssign_Vsh);
    }

    /// <summary>Applies shared atlas-safe UV indirection and derivative tangent construction.</summary>
    internal static void InjectParallaxUvMapping(SyntaxTree tree, string sourceName)
    {
        var editor = tree.CreateEditor();
        InjectParallaxUvMapping(editor, sourceName);
        editor.Commit();
    }

    /// <summary>Queues atlas-safe UV indirection into a stage-scoped transaction.</summary>
    internal static void InjectParallaxUvMapping(SyntaxEditor editor, string sourceName)
    {
        // Insert at the top of main() so subsequent vanilla code can see the uv macros.
        var mainStart = Query.Syntax<GlFunctionNode>().Named("main").InnerStart("body");
        var mainEnd = Query.Syntax<GlFunctionNode>().Named("main").InnerEnd("body");

        string prolog;
        string? epilog;

        if (sourceName == "chunktopsoil.fsh")
        {
            prolog = ParallaxUvProlog_Topsoil;
            epilog = ParallaxUvEpilog_Topsoil;
        }
        else
        {
            prolog = ParallaxUvProlog_Chunk;
            epilog = ParallaxUvEpilog_Chunk;
        }

        editor.InsertAfter(mainStart, prolog);

        if (!string.IsNullOrEmpty(epilog))
        {
            editor.InsertBefore(mainEnd, epilog);
        }
    }

    /// <summary>
    /// Injects G-buffer output writes at the start of main() function body.
    /// This ensures normal, glowLevel, and renderFlags have been computed.
    /// </summary>
    private static void InjectGBufferOutputs(SyntaxTree tree, string outputWrites)
    {
        // Find main function and insert at inner start of body (after opening brace)
        var mainQuery = Query.Syntax<GlFunctionNode>().Named("main").InnerStart("body");
        tree.CreateEditor()
            .InsertAfter(mainQuery, outputWrites)
            .Commit();
    }

    private static void InjectSkyGBufferOutputs(SyntaxTree tree)
    {
        var editor = tree.CreateEditor();
        InjectSkyGBufferOutputs(editor);
        editor.Commit();
    }

    /// <summary>Queues default sky G-buffer values into a stage-scoped transaction.</summary>
    private static void InjectSkyGBufferOutputs(SyntaxEditor editor)
    {
        const string skyGBufferWrites = @"
    // VGE: Write default G-buffer outputs for sky
    vge_outEnvironment = vec4(0.0);
    vge_outPatchId = uvec4(0u);
    vge_outNormal = vec4(0.0); // Upward normal
    vge_outMaterial = vec4(0.0, 0.0, outGlow.g, 0.0); // Default material properties
";
        editor.InsertBefore(Query.Syntax<GlFunctionNode>().Named("main").InnerEnd("body"), skyGBufferWrites);
    }

    /// <summary>Patches the fog and lighting helpers in one transaction.</summary>
    private static void PatchFogAndLight(SyntaxTree tree)
    {
        var editor = tree.CreateEditor();

        ReplaceFogAndLightFunctions(editor);

        editor.Commit();
    }

    /// <summary>Queues replacement bodies for the engine's forward fog and light helpers.</summary>
    private static void ReplaceFogAndLightFunctions(SyntaxEditor editor)
    {

        // intercept 'applyFog' function and just return unadjusted color
        ReplaceFunctionBody(editor, "applyFog", "\nreturn rgbaPixel;");

        // intercept 'getBrightnessFromShadowMap' function and return full brightness
        ReplaceFunctionBody(editor, "getBrightnessFromShadowMap", "\nreturn 1.0;");

        // intercept 'getBrightnessFromNormal' function and return full brightness
        ReplaceFunctionBody(editor, "getBrightnessFromNormal", "\nreturn 1.0;");

        // intercept 'applyFogAndShadow' function and just return unadjusted color
        ReplaceFunctionBody(editor, "applyFogAndShadow", "\nreturn rgbaPixel;");

        // intercept 'applyFogAndShadowWithNormal' function and just return unadjusted color
        ReplaceFunctionBody(editor, "applyFogAndShadowWithNormal", "\nreturn rgbaPixel;");

        // intercept 'applyFogAndShadowFromBrightness' function and just return unadjusted color
        ReplaceFunctionBody(editor, "applyFogAndShadowFromBrightness", "\nreturn rgbaPixel;");
    }

    /// <summary>
    /// Helper method to insert code at the top of a function body using the editor.
    /// </summary>
    private static void ReplaceFunctionBody(SyntaxEditor editor, string functionName, string code)
    {
        var methodBody = Query.Syntax<GlFunctionNode>().Named(functionName).Block("body");
        var bodyContents = methodBody.Inner();
        editor.Replace(bodyContents, code);
    }

    /// <summary>
    /// Patches the normalshading.fsh shader to return full brightness.
    /// </summary>
    private static void PatchNormalshading(SyntaxTree tree)
    {
        var editor = tree.CreateEditor();

        // intercept 'getBrightnessFromNormal' function and return full brightness
        ReplaceFunctionBody(editor, "getBrightnessFromNormal", "\nreturn 1.0;");

        editor.Commit();
    }
}
