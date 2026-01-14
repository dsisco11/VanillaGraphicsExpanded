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
    {
        // VGE mapping rules are authored against the actual asset file location, e.g.
        //   game:textures/block/stone/granite.png
        // but the block atlas is keyed by composite texture bases, e.g.
        //   game:block/stone/granite
        // The conversion is deterministic:
        //   1) strip leading "textures/"
        //   2) trim the file extension (".png", ".jpg", etc.)

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
