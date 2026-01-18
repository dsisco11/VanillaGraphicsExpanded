using System;
using System.Collections.Generic;

using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.PBR.Materials;

internal static class PbrMaterialAtlasPositionResolver
{
    public static bool TryResolve(
        System.Func<AssetLocation, TextureAtlasPosition?> tryGetAtlasPosition,
        AssetLocation textureAsset,
        out TextureAtlasPosition? textureAtlasPosition)
    {
        if (tryGetAtlasPosition is null) throw new ArgumentNullException(nameof(tryGetAtlasPosition));
        if (textureAsset is null) throw new ArgumentNullException(nameof(textureAsset));

        AssetLocation normalized = NormalizeToAtlasKey(textureAsset);
        textureAtlasPosition = tryGetAtlasPosition(normalized);
        return textureAtlasPosition is not null;
    }

    internal static AssetLocation NormalizeToAtlasKey(AssetLocation textureAsset)
        => AtlasAssetKeyNormalizer.NormalizeToBlockAtlasKey(textureAsset);
}
