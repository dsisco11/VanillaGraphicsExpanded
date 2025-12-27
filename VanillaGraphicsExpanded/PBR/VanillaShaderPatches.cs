using System;
using System.Collections.Generic;
using System.Text;

using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.PBR;

/// <summary>
/// Handles WHAT modifications are applied to vanilla shader assets.
/// Responsible for defining and applying shader code patches.
/// </summary>
internal static class VanillaShaderPatches
{
    #region G-Buffer Injection Code

    // G-Buffer output declarations (locations 4-5, after VS's 0-3)
    // Location 4: World-space normals (RGBA16F)
    // Location 5: Material properties (RGBA16F) - Reflectivity, Roughness, Metallic, Emissive
    // Note: VS's ColorAttachment0 (outColor) serves as albedo
    private const string GBufferOutputDeclarations = @"
// VGE G-Buffer outputs
layout(location = 4) out vec4 vge_outNormal;    // World-space normal (XYZ), unused (W)
layout(location = 5) out vec4 vge_outMaterial;  // Reflectivity, Roughness, Metallic, Emissive
";

    // Code to inject before the final closing brace of main() to write G-buffer data
    // Uses available shader variables: normal, renderFlags, texColor/rgba
    // Note: VS's outColor (ColorAttachment0) serves as albedo
    private const string GBufferOutputWrites = @"
    // VGE: Write G-buffer outputs
    // Normal: world-space normal packed to [0,1] range
    vge_outNormal = vec4(normal * 0.5 + 0.5, 1.0);
    
    // Material: extract properties from renderFlags
    float vge_reflectivity = getMatMetallicFromRenderFlags(renderFlags); // Using same function for reflectivity for simplicity
    float vge_roughness = 0.5;  // Default roughness (could be extracted from texture)
    float vge_metallic = getMatMetallicFromRenderFlags(renderFlags);
    float vge_emissive = glowLevel;
    vge_outMaterial = vec4(vge_reflectivity, vge_roughness, vge_metallic, vge_emissive);
";
    #endregion

    /// <summary>
    /// Attempts to apply pre-processing patches BEFORE imports are inlined.
    /// Use this for modifications that need to happen on the raw shader source.
    /// </summary>
    /// <param name="log">Logger for warnings/errors.</param>
    /// <param name="patcher">The patcher instance (imports NOT yet processed).</param>
    /// <returns>True if pre-processing patches were applied, false otherwise.</returns>
    internal static bool TryApplyPreProcessing(ILogger? log, SourceCodePatcher patcher)
    {
        try
        {
            switch (patcher.SourceName)
            {
                // Add pre-processing cases here as needed
                // Example:
                // case "someshader.fsh":
                //     PreProcessSomeShader(patcher);
                //     log?.Audit($"[VGE] Pre-processed shader: {patcher.SourceName}");
                //     return true;

                default:
                    return false;
            }
        }
        catch (SourceCodePatchException ex)
        {
            log?.Warning($"[VGE] Failed to pre-process shader '{patcher.SourceName}': {ex.Message}");
            return false;
        }
        catch (Exception ex)
        {
            log?.Warning($"[VGE] Unexpected error pre-processing shader '{patcher.SourceName}': {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Attempts to apply patches to the given patcher based on the shader name.
    /// Does not build or write to asset - caller is responsible for that.
    /// </summary>
    /// <param name="log">Logger for warnings/errors.</param>
    /// <param name="patcher">The patcher instance (already has imports processed).</param>
    /// <returns>True if patches were applied, false if no patches needed for this shader.</returns>
    internal static bool TryApplyPatches(ILogger? log, SourceCodePatcher patcher)
    {
        try
        {
            switch (patcher.SourceName)
            {
                // Shader includes
                case "fogandlight.fsh":
                    PatchFogAndLight(patcher);
                    log?.Audit($"[VGE] Applied patches to shader: {patcher.SourceName}");
                    return true;
                // case "normalshading.fsh": // Note: Disabled since we don't really care to change the lighting for gui items or first-person view items.
                //     PatchNormalshading(patcher);
                //     return true;

                // Main shader files - inject G-buffer outputs
                case "chunkopaque.fsh":
                case "chunktopsoil.fsh":
                case "standard.fsh":
                case "instanced.fsh":
                    InjectGBufferOutputs(patcher);
                    log?.Audit($"[VGE] Applied patches to shader: {patcher.SourceName}");
                    return true;

                default:
                    return false;
            }
        }
        catch (SourceCodePatchException ex)
        {
            log?.Warning($"[VGE] Failed to patch shader '{patcher.SourceName}': {ex.Message}");
            return false;
        }
        catch (Exception ex)
        {
            log?.Warning($"[VGE] Unexpected error patching shader '{patcher.SourceName}': {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Injects G-buffer output declarations and writes into a main shader.
    /// </summary>
    private static void InjectGBufferOutputs(SourceCodePatcher patcher)
    {
        patcher
            .FindVersionDirective().After().Insert(GBufferOutputDeclarations)
            .FindFunction("main").BeforeClose().Insert(GBufferOutputWrites);
    }

    /// <summary>
    /// Patches the fogandlight.fsh shader to disable fog, shadow, and normal shading effects.
    /// </summary>
    private static void PatchFogAndLight(SourceCodePatcher patcher)
    {
        // intercept 'applyFog' function and just return unadjusted color
        patcher.FindFunction("applyFog").AtTop()
            .Insert(@"return rgbaPixel;");

        // intercept 'getBrightnessFromShadowMap' function and return full brightness
        patcher.FindFunction("getBrightnessFromShadowMap").AtTop()
            .Insert(@"return 1.0;");

        // intercept 'getBrightnessFromNormal' function and return full brightness
        patcher.FindFunction("getBrightnessFromNormal").AtTop()
            .Insert(@"return 1.0;");

        // intercept 'applyFogAndShadow' function and just return unadjusted color
        patcher.FindFunction("applyFogAndShadow").AtTop()
            .Insert(@"return rgbaPixel;");

        // intercept 'applyFogAndShadowWithNormal' function and just return unadjusted color
        patcher.FindFunction("applyFogAndShadowWithNormal").AtTop()
            .Insert(@"return rgbaPixel;");

        // intercept 'applyFogAndShadowFromBrightness' function and just return unadjusted color
        patcher.FindFunction("applyFogAndShadowFromBrightness").AtTop()
            .Insert(@"return rgbaPixel;");
    }

    /// <summary>
    /// Patches the normalshading.fsh shader to return full brightness.
    /// </summary>
    private static void PatchNormalshading(SourceCodePatcher patcher)
    {
        // intercept 'getBrightnessFromNormal' function and return full brightness
        patcher.FindFunction("getBrightnessFromNormal").AtTop()
            .Insert(@"return 1.0;");
    }
}