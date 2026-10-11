using System;
using VanillaGraphicsExpanded.Rendering.ProgramBinaries;
using VanillaGraphicsExpanded.Rendering.ShaderCompilation;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.Rendering.Integration;

/// <summary>Adapts engine assets and logging to the binary compilation dependencies.</summary>
internal static class EngineShaderAssets
{
    #region Public API
    /// <summary>Preserves asset-manager identity and shared digest caching without reading assets.</summary>
    internal static ShaderAssetSource Create(IAssetManager assets, string domain, ILogger? logger = null)
    {
        ReadOnlySpan<byte> Read(string path) => assets.TryGet(AssetLocation.Create("shaders/" + path, domain), true)?.Data
            ?? throw new InvalidOperationException("Missing built shader asset: " + domain + ":shaders/" + path);
        return new(assets, domain, Read, () => ShaderDigestIndexCache.ForAssets(assets, domain),
            logger is null ? null : message => logger.Warning(message),
            logger is null ? null : message => logger.Error(message));
    }
    #endregion
}
