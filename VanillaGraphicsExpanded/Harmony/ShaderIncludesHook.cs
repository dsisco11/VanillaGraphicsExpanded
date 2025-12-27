using System.Collections.Generic;
using System.Text;

using HarmonyLib;

using VanillaGraphicsExpanded.PBR;

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
        int patchedCount = ProcessShaderAssets(shaderIncludes);
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
        patchedCount = ProcessShaderAssets(shaderSources);
        if (patchedCount > 0)
        {
            _logger?.Notification($"[VGE][Shaders] Patched {patchedCount} shader source file(s)");
        }
    }

    /// <summary>
    /// Processes a list of shader assets through the full pipeline:
    /// 1. Pre-processing (before imports)
    /// 2. Import inlining
    /// 3. Post-processing (after imports)
    /// Single tokenization pass per asset.
    /// </summary>
    private static int ProcessShaderAssets(List<IAsset> assets)
    {
        int patchedCount = 0;
        
        foreach (IAsset asset in assets)
        {
            // Create patcher without processing imports yet
            var patcher = ShaderImportsSystem.Instance.CreatePatcher(asset);
            if (patcher is null)
            {
                continue;
            }

            // Stage 1: Pre-processing (before imports are inlined)
            bool preProcessed = VanillaShaderPatches.TryApplyPreProcessing(_logger, patcher);

            // Stage 2: Inline imports
            ShaderImportsSystem.Instance.InlineImports(patcher, _logger);

            // Stage 3: Post-processing (after imports are inlined)
            bool postProcessed = VanillaShaderPatches.TryApplyPatches(_logger, patcher);

            // Build and write back to asset
            asset.Data = Encoding.UTF8.GetBytes(patcher.Build());
            
            if (preProcessed || postProcessed)
            {
                asset.IsPatched = true;
                patchedCount++;
            }
        }

        return patchedCount;
    }
}
