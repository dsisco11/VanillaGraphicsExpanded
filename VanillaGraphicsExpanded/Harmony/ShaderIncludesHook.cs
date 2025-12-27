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
/// Harmony patch that intercepts shader loading to apply import inlining and shader modifications.
/// This class only defines WHERE the patch is applied, delegating WHAT to do to other systems.
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
    /// Processes shader assets in-place before the original method compiles them.
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

        // Process shader includes first
        List<IAsset> shaderIncludes = _assetManager.GetManyInCategory(
            AssetCategory.shaderincludes.Code,
            pathBegins: "",
            domain: null,
            loadAsset: true);
        
        _logger?.Audit($"[VGE][Shaders] Processing {shaderIncludes.Count} shader includes");

        int patchedCount = 0;
        foreach (IAsset asset in shaderIncludes)
        {
            // First: inline any #import directives
            ShaderImportsSystem.Instance.InlineImports(asset, _logger);
            
            // Then: apply any shader modifications
            if (VanillaShaderPatches.TryPatchAsset(_logger, asset))
            {
                patchedCount++;
            }
        }

        if (patchedCount > 0)
        {
            _logger?.Audit($"[VGE][Shaders] Patched {patchedCount} shader include(s)");
        }

        // Process main shader source files
        List<IAsset> shaderSources = _assetManager.GetManyInCategory(
            AssetCategory.shaders.Code,
            pathBegins: "",
            domain: null,
            loadAsset: true);
        
        _logger?.Audit($"[VGE][Shaders] Processing {shaderSources.Count} shader source files");

        patchedCount = 0;
        foreach (IAsset asset in shaderSources)
        {
            // First: inline any #import directives
            ShaderImportsSystem.Instance.InlineImports(asset, _logger);
            
            // Then: apply any shader modifications
            if (VanillaShaderPatches.TryPatchAsset(_logger, asset))
            {
                patchedCount++;
            }
        }

        if (patchedCount > 0)
        {
            _logger?.Notification($"[VGE][Shaders] Patched {patchedCount} shader source file(s)");
        }
    }
}
