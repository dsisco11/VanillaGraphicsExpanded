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
        "chunktransparent.fsh",
        "chunkopaque.fsh",
        "chunktopsoil.fsh",
        "chunkliquid.fsh"
    ];

    private static readonly ImmutableArray<string> PatchedChunkVertexShaders =
    [
        "chunktransparent.vsh",
        "chunkopaque.vsh",
        "chunktopsoil.vsh"
    ];

    private static readonly ImmutableArray<string> PatchedGenericShaders =
    [
        "instanced.fsh",
        "standard.fsh"
    ];
    #endregion

    #region G-Buffer Injection Code

    // G-Buffer output declarations (locations 4-5, after VS's 0-3)
    // Location 4: World-space normals (RGBA16F)
    // Location 5: Material properties (RGBA16F) - Roughness, Metallic, Emissive, Reflectivity
    // Note: VS's ColorAttachment0 (outColor) serves as albedo
    private const string GBufferInputDeclarations = @"
// VGE G-Buffer outputs
layout(location = 4) out vec4 vge_outNormal;    // World-space normal (XYZ), unused (W)
layout(location = 5) out vec4 vge_outMaterial;  // Roughness, Metallic, Emissive, Reflectivity
";

    private const string ChunkMaterialParamsSamplerDeclaration = @"
// VGE: Per-texel material params for block atlas (RGB16F: roughness, metallic, emissive)
uniform sampler2D vge_materialParamsTex;
// VGE: Per-texel normal+depth for block atlas (RGBA16F: normalXYZ_packed01, depth01)
uniform sampler2D vge_normalDepthTex;
";

    private const string UvRectVaryings_Vsh = @"

// VGE: Per-face atlas tile rect (base + extent in atlas UV space)
flat out vec2 vge_uvBase;
flat out vec2 vge_uvExtent;
";

    private const string UvRectVaryings_Fsh = @"

// VGE: Per-face atlas tile rect (base + extent in atlas UV space)
flat in vec2 vge_uvBase;
flat in vec2 vge_uvExtent;
";

    private const string UvRectAssign_Vsh = @"

    // VGE: Compute per-face atlas UV rect.
    // Prefer SSBO path (FaceData has the packed UV origin/extent); non-SSBO lacks the required information.
    // NOTE: Do not rely on a local `vdata` variable; re-fetch from `faces[...]` to keep this injection location-stable.
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

#undef uv
";

    private const string ParallaxUvProlog_Topsoil = @"

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

#undef uv
#undef uv2
";

    // chunkliquid: keep an alias for our own sampling, but do not macro-override `uv`
    // (liquid uses `uv` and `uvBase` in special flow logic).
    private const string ParallaxUvProlog_ChunkLiquid = @"

    // VGE: Local alias for UV used by VGE sampling
    vec2 vge_uv = uv;
";

    // Code to inject before the final closing brace of main() to write G-buffer data
    // Uses available shader variables: normal, renderFlags, texColor/rgba
    // Note: VS's outColor (ColorAttachment0) serves as albedo
    private const string GBufferOutputWrites_Default = @"

    // VGE: Write G-buffer outputs
    // Normal: world-space normal packed to [0,1] range
    vge_outNormal = vec4(normal * 0.5 + 0.5, 1.0);
    
    // Material: extract properties from renderFlags
    float vge_roughness = 0.5;  // Default roughness
    float vge_metallic = getMatMetallicFromRenderFlags(renderFlags);
    float vge_emissive = glowLevel;
    float vge_reflectivity = getMatMetallicFromRenderFlags(renderFlags);
    vge_outMaterial = vec4(vge_roughness, vge_metallic, vge_emissive, vge_reflectivity);
";

    private const string GBufferOutputWrites_Chunk = @"

    // VGE: Write G-buffer outputs
    // Normal: world-space normal packed to [0,1] range
    // Also sample the per-texel normal+depth atlas so the sampler uniform stays live.
    // We store encoded height01 (0..1) in the otherwise-unused W channel for optional debugging.
    vge_outNormal = VgeComputePackedWorldNormal01Height01_WithTbn(vge_uv, normal, worldPos.xyz, vge_tbn, vge_tbnHandedness);
#if VGE_PBR_ENABLE_POM && VGE_PBR_POM_DEBUG_MODE > 0
    // VGE: POM debug metric (scalar) in the otherwise-unused W channel.
    vge_outNormal.w = clamp(vge_pomDebugValue, 0.0, 1.0);
#endif
    
    // Material: per-texel params stored in vge_materialParamsTex (RGB16F)
    vec3 vge_params = ReadMaterialParams(vge_uv);
    vge_params = ApplyMaterialNoise(vge_params, vge_uv, renderFlags);
    float vge_roughness = clamp(vge_params.r, 0.0, 1.0);
    float vge_metallic  = clamp(vge_params.g, 0.0, 1.0);
    float vge_emissive  = clamp(vge_params.b, 0.0, 1.0);

    float vge_reflectivity = ComputeReflectivity(vge_roughness, vge_metallic);

    vge_outMaterial = vec4(vge_roughness, vge_metallic, vge_emissive, vge_reflectivity);
";

    // chunkliquid.fsh does not define `normal` or `renderFlags`.
    // Use inputs that are actually present in that shader (fragNormal, uv), and keep the samplers live.
    private const string GBufferOutputWrites_ChunkLiquid = @"

    // VGE: Write G-buffer outputs (liquid shader variant)
    // Normal: use the per-fragment normal provided by the liquid shader.
    // We store encoded height01 (0..1) in the otherwise-unused W channel for optional debugging.
    vec3 vge_liquidNormal = normalize(fragNormal);
    vge_outNormal = VgeComputePackedWorldNormal01Height01(vge_uv, vge_liquidNormal, fWorldPos);

    // Material: read per-texel params but do not require renderFlags.
    vec3 vge_params = ReadMaterialParams(vge_uv);
    vge_params = ApplyMaterialNoise(vge_params, vge_uv);
    float vge_roughness = clamp(vge_params.r, 0.0, 1.0);
    float vge_metallic  = clamp(vge_params.g, 0.0, 1.0);
    float vge_emissive  = clamp(vge_params.b, 0.0, 1.0);

    float vge_reflectivity = ComputeReflectivity(vge_roughness, vge_metallic);
    vge_outMaterial = vec4(vge_roughness, vge_metallic, vge_emissive, vge_reflectivity);
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
            // Chunk vertex shaders - inject only vertex-safe helpers
            if (PatchedChunkVertexShaders.Contains(sourceName))
            {
                var mainQuery = Query.Syntax<GlFunctionNode>().Named("main");
                tree.CreateEditor()
                    .InsertBefore(mainQuery, "@import \"./includes/vge_uvrect.glsl\"\n")
                    .Commit();

                log?.Audit($"[VGE] Applied pre-processing to shader: {sourceName}");
                return true;
            }

            // Chunk shaders - inject vsFunctions AND vge_material imports
            if (PatchedChunkShaders.Contains(sourceName))
            {
                InjectPomDefines(tree);

                // Find main function and insert @import before it
                var mainQuery = Query.Syntax<GlFunctionNode>().Named("main");
                tree.CreateEditor()
                    .InsertBefore(mainQuery, "@import \"./includes/vsfunctions.glsl\"\n")
                    .InsertBefore(mainQuery, "@import \"./includes/vge_material.glsl\"\n")
                    .InsertBefore(mainQuery, "@import \"./includes/vge_normaldepth.glsl\"\n")
                    .InsertBefore(mainQuery, "@import \"./includes/vge_parallax.glsl\"\n")
                    .Commit();

                log?.Audit($"[VGE] Applied pre-processing to shader: {sourceName}");
                return true;
            }

            // Entity/item shaders - inject vsFunctions only (no per-texel material params)
            if (PatchedGenericShaders.Contains(sourceName))
            {
                var mainQuery = Query.Syntax<GlFunctionNode>().Named("main");
                tree.CreateEditor()
                    .InsertBefore(mainQuery, "@import \"./includes/vsfunctions.glsl\"\n")
                    .Commit();

                log?.Audit($"[VGE] Applied pre-processing to shader: {sourceName}");
                return true;
            }
            return false;
        }
        catch (Exception ex)
        {
            log?.Warning($"[VGE] Failed to pre-process shader '{sourceName}': {ex.Message}");
            return false;
        }
    }

    private static void InjectPomDefines(SyntaxTree tree)
    {
        if (!ConfigModSystem.Config.MaterialAtlas.EnableParallaxOcclusionMapping) return;
        if (!ConfigModSystem.Config.MaterialAtlas.EnableNormalMaps) return;

        var versionQuery = Query.Syntax<GlDirectiveNode>().Named("version");
        if (!tree.Select(versionQuery).Any()) return;

        var cfg = ConfigModSystem.Config.MaterialAtlas;

        string scale = cfg.ParallaxScale.ToString("0.0####", CultureInfo.InvariantCulture);
        string fadeStart = cfg.ParallaxFadeStart.ToString("0.0####", CultureInfo.InvariantCulture);
        string fadeEnd = cfg.ParallaxFadeEnd.ToString("0.0####", CultureInfo.InvariantCulture);
        string maxTexels = cfg.ParallaxMaxTexels.ToString("0.0####", CultureInfo.InvariantCulture);

        string defineBlock = $@"

// VGE: POM settings
#define {VgeShaderDefines.PbrEnablePom} 1
#define {VgeShaderDefines.PbrPomScale} {scale}
#define {VgeShaderDefines.PbrPomMinSteps} {cfg.ParallaxMinSteps}
#define {VgeShaderDefines.PbrPomMaxSteps} {cfg.ParallaxMaxSteps}
#define {VgeShaderDefines.PbrPomRefinementSteps} {cfg.ParallaxRefinementSteps}
#define {VgeShaderDefines.PbrPomFadeStart} {fadeStart}
#define {VgeShaderDefines.PbrPomFadeEnd} {fadeEnd}
#define {VgeShaderDefines.PbrPomMaxTexels} {maxTexels}
#define {VgeShaderDefines.PbrPomDebugMode} {cfg.ParallaxDebugMode}
";

        tree.CreateEditor()
            .InsertAfter(versionQuery, defineBlock)
            .Commit();
    }

    /// <summary>
    /// Attempts to apply patches to the given SyntaxTree based on the shader name.
    /// </summary>
    /// <param name="log">Logger for warnings/errors.</param>
    /// <param name="tree">The SyntaxTree instance (already has imports processed).</param>
    /// <param name="sourceName">The name of the shader source.</param>
    /// <returns>True if patches were applied, false if no patches needed for this shader.</returns>
    internal static bool TryApplyPatches(ILogger? log, SyntaxTree tree, string sourceName)
    {
        try
        {
            if (PatchedChunkVertexShaders.Contains(sourceName))
            {
                InjectUvRectVaryings_Vsh(tree);
                InjectUvRectAssign_Vsh(tree);

                log?.Audit($"[VGE] Applied patches to shader: {sourceName}");
                return true;
            }

            if (PatchedChunkShaders.Contains(sourceName))
            {// Chunk shaders - inject G-buffer inputs, material sampler, and outputs
                InjectGBufferInputs(tree);
                InjectChunkMaterialSampler(tree);

                // Provide uv rect varyings for POM/atlas-safe UV indirection (even if currently unused).
                // chunkliquid already has its own `uvBase`/`uvSize` plumbing.
                if (sourceName != "chunkliquid.fsh")
                {
                    InjectUvRectVaryings_Fsh(tree);
                }

                string outputWrites = sourceName == "chunkliquid.fsh"
                    ? GBufferOutputWrites_ChunkLiquid
                    : GBufferOutputWrites_Chunk;

                // Must be at the start of main() to survive early returns.
                InjectGBufferOutputs(tree, outputWrites);

                // Inject UV/TBN helpers after output injection (still placed at start of main()).
                InjectParallaxUvMapping(tree, sourceName);

                PatchFogAndLight(tree);
                log?.Audit($"[VGE] Applied patches to shader: {sourceName}");
                return true;
            }
            else if (PatchedGenericShaders.Contains(sourceName))
            {// Main shader files - inject G-buffer outputs
                InjectGBufferInputs(tree);
                InjectGBufferOutputs(tree, GBufferOutputWrites_Default);
                PatchFogAndLight(tree);
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
                        InjectGBufferInputs(tree);
                        InjectSkyGBufferOutputs(tree);
                        log?.Audit($"[VGE] Applied patches to shader: {sourceName}");
                        return true;
                    }
                default:
                    return false;
            }
        }
        catch (Exception ex)
        {
            log?.Warning($"[VGE] Failed to patch shader '{sourceName}': {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Injects G-buffer output declarations after #version directive.
    /// </summary>
    private static void InjectGBufferInputs(SyntaxTree tree)
    {
        // Find the #version directive and insert after it
        var versionQuery = Query.Syntax<GlDirectiveNode>().Named("version");

        tree.CreateEditor()
            .InsertAfter(versionQuery, GBufferInputDeclarations)
            .Commit();
    }

    private static void InjectChunkMaterialSampler(SyntaxTree tree)
    {
        var versionQuery = Query.Syntax<GlDirectiveNode>().Named("version");

        tree.CreateEditor()
            .InsertAfter(versionQuery, ChunkMaterialParamsSamplerDeclaration)
            .Commit();
    }

    private static void InjectUvRectVaryings_Vsh(SyntaxTree tree)
    {
        var versionQuery = Query.Syntax<GlDirectiveNode>().Named("version");
        tree.CreateEditor()
            .InsertAfter(versionQuery, UvRectVaryings_Vsh)
            .Commit();
    }

    private static void InjectUvRectVaryings_Fsh(SyntaxTree tree)
    {
        var versionQuery = Query.Syntax<GlDirectiveNode>().Named("version");
        tree.CreateEditor()
            .InsertAfter(versionQuery, UvRectVaryings_Fsh)
            .Commit();
    }

    private static void InjectUvRectAssign_Vsh(SyntaxTree tree)
    {
        // Insert at the start of main() body to avoid AST editor wrapping issues.
        // We re-fetch FaceData from the SSBO so this does not depend on local variable ordering.
        var mainStart = Query.Syntax<GlFunctionNode>().Named("main").InnerStart("body");
        tree.CreateEditor()
            .InsertAfter(mainStart, UvRectAssign_Vsh)
            .Commit();
    }

    private static void InjectParallaxUvMapping(SyntaxTree tree, string sourceName)
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
        else if (sourceName == "chunkliquid.fsh")
        {
            prolog = ParallaxUvProlog_ChunkLiquid;
            epilog = null;
        }
        else
        {
            prolog = ParallaxUvProlog_Chunk;
            epilog = ParallaxUvEpilog_Chunk;
        }

        var editor = tree.CreateEditor();
        editor.InsertAfter(mainStart, prolog);

        if (!string.IsNullOrEmpty(epilog))
        {
            editor.InsertBefore(mainEnd, epilog);
        }

        editor.Commit();
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
        // Sky shader only needs to write default values to G-buffer outputs
        const string skyGBufferWrites = @"
    // VGE: Write default G-buffer outputs for sky
    vge_outNormal = vec4(0.0); // Upward normal
    vge_outMaterial = vec4(0.0, 0.0, outGlow.g, 0.0); // Default material properties
";

        // Find main function and insert at inner end of body (before closing brace)
        var mainQuery = Query.Syntax<GlFunctionNode>().Named("main").InnerEnd("body");
        tree.CreateEditor()
            .InsertBefore(mainQuery, skyGBufferWrites)
            .Commit();
    }

    /// <summary>
    /// Patches the fogandlight.fsh shader to disable fog, shadow, and normal shading effects.
    /// </summary>
    private static void PatchFogAndLight(SyntaxTree tree)
    {
        var editor = tree.CreateEditor();

        // intercept 'applyFog' function and just return unadjusted color
        ReplaceFunctionBody(tree, editor, "applyFog", "\nreturn rgbaPixel;");

        // intercept 'getBrightnessFromShadowMap' function and return full brightness
        ReplaceFunctionBody(tree, editor, "getBrightnessFromShadowMap", "\nreturn 1.0;");

        // intercept 'getBrightnessFromNormal' function and return full brightness
        ReplaceFunctionBody(tree, editor, "getBrightnessFromNormal", "\nreturn 1.0;");

        // intercept 'applyFogAndShadow' function and just return unadjusted color
        ReplaceFunctionBody(tree, editor, "applyFogAndShadow", "\nreturn rgbaPixel;");

        // intercept 'applyFogAndShadowWithNormal' function and just return unadjusted color
        ReplaceFunctionBody(tree, editor, "applyFogAndShadowWithNormal", "\nreturn rgbaPixel;");

        // intercept 'applyFogAndShadowFromBrightness' function and just return unadjusted color
        ReplaceFunctionBody(tree, editor, "applyFogAndShadowFromBrightness", "\nreturn rgbaPixel;");

        editor.Commit();
    }

    /// <summary>
    /// Helper method to insert code at the top of a function body using the editor.
    /// </summary>
    private static void ReplaceFunctionBody(SyntaxTree tree, SyntaxEditor editor, string functionName, string code)
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
        ReplaceFunctionBody(tree, editor, "getBrightnessFromNormal", "\nreturn 1.0;");

        editor.Commit();
    }
}