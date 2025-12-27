using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

using HarmonyLib;

using VanillaGraphicsExpanded.PBR;

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
            _logger?.Warning("[VGE][Shaders] AssetManager not available");
            return;
        }

        // Get all shader include assets - same assets the original method will iterate
        List<IAsset> shaderIncludes = _assetManager.GetManyInCategory(
            AssetCategory.shaderincludes.Code,
            pathBegins: "",
            domain: null,
            loadAsset: true);
        
        _logger?.Audit($"[VGE][Shaders] Processing {shaderIncludes.Count} shader includes");

        int patchedCount = 0;
        foreach (IAsset asset in shaderIncludes)
        {
            if (VanillaShaderPatches.TryPatchAsset(_logger, asset))
            {
                patchedCount++;
            }
        }

        _logger?.Audit($"[VGE][Shaders] Patched {patchedCount} shader include(s)");
        // Now process the actual shader source files as well
        patchedCount = 0;
        _logger?.Audit($"[VGE][Shaders] Processing shader source files");

        List<IAsset> shaderSources = _assetManager.GetManyInCategory(
            AssetCategory.shaders.Code,
            pathBegins: "",
            domain: null,
            loadAsset: true);
        foreach (IAsset asset in shaderSources)
        {
            if (VanillaShaderPatches.TryPatchAsset(_logger, asset))
            {
                patchedCount++;
            }
        }    

        if (patchedCount > 0)
        {
            _logger?.Notification($"[VGE][Shaders] Patched {patchedCount} shader files(s)");
        }
    }
}
