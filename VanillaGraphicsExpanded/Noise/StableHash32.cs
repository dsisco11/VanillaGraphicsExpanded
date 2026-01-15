using System;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.Noise;

/// <summary>
/// Deterministic 32-bit hashing utilities intended for seed generation.
/// </summary>
internal static class StableHash32
{
    private const uint FnvOffsetBasis = 2_166_136_261u;
    private const uint FnvPrime = 16_777_619u;

    /// <summary>
    /// Computes a stable FNV-1a 32-bit hash for an <see cref="AssetLocation"/>.
    /// Uses a normalized <c>domain:path</c> string with invariant-lowercasing.
    /// </summary>
    public static uint HashAssetLocation(AssetLocation assetLocation)
    {
        if (assetLocation is null) throw new ArgumentNullException(nameof(assetLocation));

        uint hash = FnvOffsetBasis;

        hash = HashLowerInvariant(hash, assetLocation.Domain);
        hash = HashLowerInvariant(hash, ':');
        hash = HashLowerInvariant(hash, assetLocation.Path);

        return hash;
    }

    private static uint HashLowerInvariant(uint hash, string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return hash;
        }

        foreach (char c in text)
        {
            hash = HashLowerInvariant(hash, c);
        }

        return hash;
    }

    private static uint HashLowerInvariant(uint hash, char c)
    {
        char lower = char.ToLowerInvariant(c);
        hash ^= lower;
        return unchecked(hash * FnvPrime);
    }
}
