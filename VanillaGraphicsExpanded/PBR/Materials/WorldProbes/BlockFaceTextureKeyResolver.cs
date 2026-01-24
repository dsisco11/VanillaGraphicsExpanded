using System;
using System.Collections.Generic;

using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.PBR.Materials.WorldProbes;

internal static class BlockFaceTextureKeyResolver
{
    public static bool TryResolveBaseTextureLocation(Block block, byte faceIndex, out AssetLocation texture, out string? resolvedFromKey)
    {
        texture = default!;
        resolvedFromKey = null;

        if (block is null)
        {
            return false;
        }

        IDictionary<string, CompositeTexture>? textures = block.Textures;
        if (textures is null || textures.Count == 0)
        {
            return false;
        }

        string faceKey = faceIndex switch
        {
            0 => "north",
            1 => "east",
            2 => "south",
            3 => "west",
            4 => "up",
            5 => "down",
            _ => "all",
        };

        // Deterministic fallback order per face:
        // face-specific -> all-faces -> first texture.
        // For side faces, insert "side" between face-specific and "all".
        ReadOnlySpan<string> keys = faceIndex is 0 or 1 or 2 or 3
            ? [faceKey, "side", "all"]
            : [faceKey, "all"];

        CompositeTexture? chosen = null;

        foreach (string key in keys)
        {
            if (textures.TryGetValue(key, out CompositeTexture? ct) && ct is not null && ct.Base is not null)
            {
                chosen = ct;
                resolvedFromKey = key;
                break;
            }
        }

        if (chosen is null)
        {
            // Last resort: first declared texture.
            foreach ((string key, CompositeTexture? ct) in textures)
            {
                if (ct is null || ct.Base is null)
                {
                    continue;
                }

                chosen = ct;
                resolvedFromKey = key;
                break;
            }
        }

        if (chosen?.Base is null)
        {
            return false;
        }

        string domain = !string.IsNullOrWhiteSpace(chosen.Base.Domain)
            ? chosen.Base.Domain
            : block.Code?.Domain ?? "game";

        string path = (chosen.Base.Path ?? string.Empty).Replace('\\', '/');

        // Base textures are typically authored as "block/..." (without the textures/ prefix).
        if (!path.StartsWith("textures/", StringComparison.OrdinalIgnoreCase))
        {
            path = "textures/" + path.TrimStart('/');
        }

        // Mapping keys in the registry are file-like and include the extension.
        // CompositeTexture.Base often omits it, so default to .png when absent.
        int lastSlash = path.LastIndexOf('/');
        int lastDot = path.LastIndexOf('.');
        bool hasExt = lastDot > lastSlash;
        if (!hasExt)
        {
            path += ".png";
        }

        texture = new AssetLocation(domain.ToLowerInvariant(), path.ToLowerInvariant());
        return true;
    }
}
