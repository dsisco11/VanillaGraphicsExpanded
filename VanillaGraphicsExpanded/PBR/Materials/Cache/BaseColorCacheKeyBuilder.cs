using System;
using System.Globalization;

using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.PBR.Materials.Cache;

/// <summary>
/// Deterministic cache key builder for average base color (linear RGB) derived from textures.
/// </summary>
internal sealed class BaseColorCacheKeyBuilder
{
    public AtlasCacheKey BuildKey(in BaseColorCacheKeyInputs inputs, AssetLocation texture, string? originPath, long assetBytes)
    {
        ArgumentNullException.ThrowIfNull(texture);

        string stableKey = string.Format(
            CultureInfo.InvariantCulture,
            "{0}|kind=baseColor|tex={1}|origin={2}|bytes={3}",
            inputs.StablePrefix,
            texture,
            string.IsNullOrWhiteSpace(originPath) ? "(unknown)" : originPath,
            assetBytes);

        return AtlasCacheKey.FromUtf8(inputs.SchemaVersion, stableKey);
    }

    public static string ToEntryId(AtlasCacheKey key)
    {
        if (key.SchemaVersion <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(key), "SchemaVersion must be > 0");
        }

        return string.Format(CultureInfo.InvariantCulture, "v{0}-{1:x16}", key.SchemaVersion, key.Hash64);
    }

    public static bool TryParseEntryId(string entryId, out AtlasCacheKey key)
    {
        key = default;

        if (string.IsNullOrWhiteSpace(entryId))
        {
            return false;
        }

        if (!entryId.StartsWith("v", StringComparison.Ordinal))
        {
            return false;
        }

        int dash = entryId.IndexOf('-', StringComparison.Ordinal);
        if (dash <= 1)
        {
            return false;
        }

        if (!int.TryParse(entryId[1..dash], NumberStyles.Integer, CultureInfo.InvariantCulture, out int schemaVersion))
        {
            return false;
        }

        if (!ulong.TryParse(entryId[(dash + 1)..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong hash64))
        {
            return false;
        }

        if (schemaVersion <= 0)
        {
            return false;
        }

        key = new AtlasCacheKey(schemaVersion, hash64);
        return true;
    }
}
