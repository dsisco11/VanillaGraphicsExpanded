using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.PBR;

/// <summary>
/// Singleton system responsible for loading shader import files from mod assets
/// and inlining @import directives into shader IAsset instances.
/// </summary>
public sealed class ShaderImportsSystem
{
    private const string ModDomain = "vanillagraphicsexpanded";

    /// <summary>
    /// Singleton instance.
    /// </summary>
    public static ShaderImportsSystem Instance { get; } = new();

    /// <summary>
    /// Cache mapping import file names to their contents.
    /// </summary>
    public IReadOnlyDictionary<string, string> ImportsCache => _importsCache;
    private readonly Dictionary<string, string> _importsCache = [];

    private ILogger? _logger;

    // Private constructor for singleton pattern
    private ShaderImportsSystem() { }

    /// <summary>
    /// Initializes the imports system by loading all shader import files from the mod's assets.
    /// Should be called during AssetsLoaded.
    /// </summary>
    /// <param name="api">The core API instance.</param>
    public void Initialize(ICoreAPI api)
    {
        if (api.Side != EnumAppSide.Client)
        {
            return;
        }

        _logger = api.Logger;

        var assetManager = api.Assets;
        var shaderImportAssets = assetManager.GetManyInCategory(
            AssetCategory.shaderincludes.Code,
            pathBegins: "",
            domain: ModDomain,
            loadAsset: true);

        if (shaderImportAssets.Count == 0)
        {
            _logger?.Debug("[VGE] No shader imports found in mod assets");
            return;
        }

        _importsCache.Clear();

        foreach (var asset in shaderImportAssets)
        {
            var fileName = Path.GetFileName(asset.Location.Path);
            var code = asset.ToText();

            if (string.IsNullOrEmpty(code))
            {
                _logger?.Warning($"[VGE] Shader import '{fileName}' is empty or failed to load");
                continue;
            }

            _importsCache[fileName] = code;
            _logger?.Debug($"[VGE] Loaded shader import: {fileName} ({code.Length} chars)");
        }

        _logger?.Notification($"[VGE] Loaded {_importsCache.Count} shader import(s) from mod assets");
    }

    /// <summary>
    /// Clears the imports cache. Should be called on mod dispose.
    /// </summary>
    public void Clear()
    {
        _importsCache.Clear();
        _logger = null;
    }

    /// <summary>
    /// Creates a patcher for the given asset without processing imports.
    /// Use this when you need to apply pre-processing before import inlining.
    /// </summary>
    /// <param name="asset">The shader asset to process.</param>
    /// <returns>A patcher instance, or null if the asset is empty.</returns>
    public SourceCodeImportsProcessor? CreatePatcher(IAsset asset)
    {
        if (asset.Data is null || asset.Data.Length == 0)
        {
            return null;
        }

        var sourceCode = asset.ToText();
        return new SourceCodeImportsProcessor(sourceCode, _importsCache, asset.Name);
    }

    public SourceCodeImportsProcessor? CreatePatcher(string sourceCode, string sourceName)
    {
        if (string.IsNullOrEmpty(sourceCode))
        {
            return null;
        }
        return new SourceCodeImportsProcessor(sourceCode, _importsCache, sourceName);
    }

    /// <summary>
    /// Processes all @import directives on an existing patcher.
    /// </summary>
    /// <param name="patcher">The patcher to process imports on.</param>
    /// <param name="log">Optional logger for warnings/errors.</param>
    /// <returns>True if processing succeeded, false on failure.</returns>
    public bool InlineImports(SourceCodeImportsProcessor patcher, ILogger? log = null)
    {
        try
        {
            patcher.ProcessImports(log ?? _logger);
            log?.Debug($"[VGE] Inlined imports for: {patcher.SourceName}");
            return true;
        }
        catch (SourceCodePatchException ex)
        {
            (log ?? _logger)?.Warning($"[VGE] Failed to inline imports for '{patcher.SourceName}': {ex.Message}");
            return false;
        }
        catch (Exception ex)
        {
            (log ?? _logger)?.Warning($"[VGE] Unexpected error inlining imports for '{patcher.SourceName}': {ex.Message}");
            return false;
        }
    }
}
