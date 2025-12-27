using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

using HarmonyLib;

using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.Harmony;

/// <summary>
/// Harmony patch to intercept shader includes loading and process custom #import directives
/// before the base game populates its includes dictionary.
/// </summary>
[HarmonyPatch]
public static class ShaderIncludesHook
{
    private static ILogger? _logger;
    private static IAssetManager? _assetManager;

    /// <summary>
    /// Initializes the hook with dependencies.
    /// Called from ShaderPatches.Apply().
    /// </summary>
    public static void Initialize(ILogger? logger, IAssetManager? assetManager)
    {
        _logger = logger;
        _assetManager = assetManager;
    }

    /// <summary>
    /// Prefix patch for ShaderRegistry.loadRegisteredShaderPrograms.
    /// Modifies shader asset content in-place before the original method processes them.
    /// </summary>
    [HarmonyPatch(typeof(ShaderRegistry), "loadRegisteredShaderPrograms")]
    [HarmonyPrefix]
    static void Prefix()
    {
        if (_assetManager is null)
        {
            _logger?.Warning("[VGE] ShaderIncludesHook: AssetManager not available");
            return;
        }

        // Get all shader include assets - same assets the original method will iterate
        List<IAsset> shaderIncludes = _assetManager.GetManyInCategory(
            AssetCategory.shaderincludes.Code,
            pathBegins: "",
            domain: null,
            loadAsset: true);
        
        _logger?.Debug($"[VGE] ShaderIncludesHook: Processing {shaderIncludes.Count} shader includes");

        int patchedCount = 0;
        foreach (IAsset asset in shaderIncludes)
        {
            if (TryPatchAsset(asset))
            {
                patchedCount++;
            }
        }

        if (patchedCount > 0)
        {
            _logger?.Notification($"[VGE] ShaderIncludesHook: Patched {patchedCount} shader include(s)");
        }
    }

    /// <summary>
    /// Attempts to patch a shader asset by processing #import directives.
    /// </summary>
    /// <param name="asset">The shader asset to patch.</param>
    /// <returns>True if the asset was modified, false otherwise.</returns>
    private static bool TryPatchAsset(IAsset asset)
    {
        if (asset.Data is null || asset.Data.Length == 0)
        {
            return false;
        }

        try
        {
            switch (asset.Name)
            {
                case "fogandlight.fsh":
                    TryPatchFogAndLight(asset);
                    break;
                case "normalshading.fsh":
                    PatchNormalshading(asset);
                    break;
                default:
                    break;
            }

            _logger?.Debug($"[VGE] Patched shader include: {asset.Name}");
            return true;
        }
        catch (SourceCodePatchException ex)
        {
            _logger?.Warning($"[VGE] Failed to patch shader include '{asset.Name}': {ex.Message}");
            return false;
        }
        catch (Exception ex)
        {
            _logger?.Error($"[VGE] Unexpected error patching shader include '{asset.Name}': {ex.Message}");
            return false;
        }
    }

    private static void TryPatchFogAndLight(IAsset asset)
    {
        SourceCodePatcher patcher = new(asset.ToText(), asset.Name);
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

        // Write modified content back to the asset
        asset.Data = Encoding.UTF8.GetBytes(patcher.Build());
        asset.IsPatched = true;
    }

    private static void PatchNormalshading(IAsset asset)
    {
        SourceCodePatcher patcher = new(asset.ToText(), asset.Name);

        // intercept 'getBrightnessFromNormal' function and return full brightness
        patcher.FindFunction("getBrightnessFromNormal").AtTop()
            .Insert(@"return 1.0;");

        // Write modified content back to the asset
        asset.Data = Encoding.UTF8.GetBytes(patcher.Build());
        asset.IsPatched = true;
    }
}
