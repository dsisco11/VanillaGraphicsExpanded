using System;
using System.Collections.Generic;
using System.Text;

using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.PBR;

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
    float vge_reflectivity = ((renderFlags & ReflectiveBitMask) != 0) ? 1.0 : 0.0;
    float vge_roughness = 0.5;  // Default roughness (could be extracted from texture)
    float vge_metallic = 0.0;   // Default non-metallic
    float vge_emissive = glowLevel;
    vge_outMaterial = vec4(vge_reflectivity, vge_roughness, vge_metallic, vge_emissive);
";
    #endregion

    #region Fields
    /// <summary>
    /// Tracks which shaders have already been patched to avoid double injection.
    /// </summary>
    public static HashSet<string> AlreadyPatchedShaders = [];
    /// <summary>
    /// Cache for imported shader code snippets to avoid redundant loading.
    /// </summary>
    public static Dictionary<string, string> ImportsCache = [];
    #endregion

    /// <summary>
    /// Attempts to apply patches to the given base-game (vanilla) shader asset.
    /// </summary>
    /// <param name="asset">The shader asset to patch.</param>
    /// <returns>True if the asset was modified, false otherwise.</returns>
    internal static bool TryPatchAsset(ILogger? log, IAsset asset)
    {
        if (asset.Data is null || asset.Data.Length == 0)
        {
            return false;
        }

        var patcher = new SourceCodeImportsProcessor(asset.ToText(), ImportsCache, asset.Name)
            .ProcessImports(log);

        try
        {
            switch (asset.Name)
            {
                case "fogandlight.fsh":
                    TryPatchFogAndLight(asset, patcher);
                    break;
                // case "normalshading.fsh": // Note: Disabled since we don't really care to change the lighting for gui items or first-person view items.
                //     PatchNormalshading(asset);
                //     break;
                default:
                    break;
            }

            log?.Audit($"[VGE] Patched shader include: {asset.Name}");
            return true;
        }
        catch (SourceCodePatchException ex)
        {
            log?.Warning($"[VGE] Failed to patch shader include '{asset.Name}': {ex.Message}");
            return false;
        }
        catch (Exception ex)
        {
            log?.Error($"[VGE] Unexpected error patching shader include '{asset.Name}': {ex.Message}");
            return false;
        }
        finally
        {
            // Write modified content back to the asset
            var patchedCode = patcher.Build();
            asset.Data = Encoding.UTF8.GetBytes(patchedCode);
            asset.IsPatched = true;
        }
    }

    /// <summary>
    /// Patches the fogandlight.fsh shader to disable fog, shadow, and normal shading effects.
    /// </summary>
    /// <param name="asset"></param>
    private static void TryPatchFogAndLight(IAsset asset, SourceCodePatcher patcher)
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
    /// <param name="asset"></param>
    private static void PatchNormalshading(IAsset asset, SourceCodePatcher patcher)
    {
        // intercept 'getBrightnessFromNormal' function and return full brightness
        patcher.FindFunction("getBrightnessFromNormal").AtTop()
            .Insert(@"return 1.0;");
    }
}