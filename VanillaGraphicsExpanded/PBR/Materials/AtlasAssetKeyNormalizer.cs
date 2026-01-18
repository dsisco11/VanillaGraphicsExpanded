using System;

using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.PBR.Materials;

/// <summary>
/// Normalizes asset locations to the key format used by the block texture atlas.
/// This centralizes the mapping from file-like texture paths ("textures/.../foo.png") to atlas keys (".../foo").
/// </summary>
internal static class AtlasAssetKeyNormalizer
{
    /// <summary>
    /// Normalizes a texture asset location (e.g. <c>game:textures/block/foo.png</c>) to the block atlas key
    /// used by <see cref="Vintagestory.API.Client.IBlockTextureAtlasAPI"/> (e.g. <c>game:block/foo</c>).
    /// </summary>
    public static AssetLocation NormalizeToBlockAtlasKey(AssetLocation textureAsset)
    {
        ArgumentNullException.ThrowIfNull(textureAsset);

        string path = textureAsset.Path;

        if (path.StartsWith("textures/", StringComparison.OrdinalIgnoreCase))
        {
            path = path["textures/".Length..];
        }

        int lastSlash = path.LastIndexOf('/');
        int lastDot = path.LastIndexOf('.');
        if (lastDot > lastSlash)
        {
            path = path[..lastDot];
        }

        return new AssetLocation(textureAsset.Domain, path);
    }
}
