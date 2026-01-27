using System;
using System.Collections.Generic;

using VanillaGraphicsExpanded.PBR.Materials;
using VanillaGraphicsExpanded.PBR.Materials.Cache;

namespace VanillaGraphicsExpanded.Tests.Unit.PBR.Materials;

[Trait("Category", "Unit")]
public sealed class MaterialAtlasCacheWarmupPlannerTests
{
    private sealed class FakeDiskCache : IMaterialAtlasDiskCache
    {
        private readonly HashSet<AtlasCacheKey> materialKeys = new();
        private readonly HashSet<AtlasCacheKey> normalDepthKeys = new();

        public void Clear()
        {
            materialKeys.Clear();
            normalDepthKeys.Clear();
        }

        public MaterialAtlasDiskCacheStats GetStatsSnapshot()
            => new(
                MaterialParams: default,
                NormalDepth: default,
                TotalEntries: materialKeys.Count + normalDepthKeys.Count,
                TotalBytes: 0,
                EvictedEntries: 0,
                EvictedBytes: 0);

        public int CountExisting(MaterialAtlasDiskCachePayloadKind kind, IReadOnlyList<AtlasCacheKey> keys)
        {
            if (keys is null || keys.Count == 0)
            {
                return 0;
            }

            HashSet<AtlasCacheKey> set = kind switch
            {
                MaterialAtlasDiskCachePayloadKind.MaterialParams => materialKeys,
                MaterialAtlasDiskCachePayloadKind.NormalDepth => normalDepthKeys,
                _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, message: "Unknown payload kind."),
            };

            int hits = 0;
            for (int i = 0; i < keys.Count; i++)
            {
                if (set.Contains(keys[i]))
                {
                    hits++;
                }
            }

            return hits;
        }

        public bool HasMaterialParamsTile(AtlasCacheKey key) => materialKeys.Contains(key);

        public bool TryLoadMaterialParamsTile(AtlasCacheKey key, out float[] rgbTriplets)
        {
            rgbTriplets = Array.Empty<float>();
            return false;
        }

        public void StoreMaterialParamsTile(AtlasCacheKey key, int width, int height, float[] rgbTriplets)
            => materialKeys.Add(key);

        public bool HasNormalDepthTile(AtlasCacheKey key) => normalDepthKeys.Contains(key);

        public bool TryLoadNormalDepthTile(AtlasCacheKey key, out float[] rgbaQuads)
        {
            rgbaQuads = Array.Empty<float>();
            return false;
        }

        public void StoreNormalDepthTile(AtlasCacheKey key, int width, int height, float[] rgbaQuads)
            => normalDepthKeys.Add(key);
    }

    [Fact]
    public void CountExisting_CountsHits_ByPayloadKind()
    {
        var cache = new FakeDiskCache();

        AtlasCacheKey a = AtlasCacheKey.FromUtf8(1, "schema=1|a");
        AtlasCacheKey b = AtlasCacheKey.FromUtf8(1, "schema=1|b");
        AtlasCacheKey c = AtlasCacheKey.FromUtf8(1, "schema=1|c");

        cache.StoreMaterialParamsTile(a, width: 1, height: 1, rgbTriplets: [0f, 0f, 0f]);
        cache.StoreNormalDepthTile(b, width: 1, height: 1, rgbaQuads: [0f, 0f, 0f, 0f]);

        Assert.Equal(1, cache.CountExisting(MaterialAtlasDiskCachePayloadKind.MaterialParams, new[] { a, b, c }));
        Assert.Equal(1, cache.CountExisting(MaterialAtlasDiskCachePayloadKind.NormalDepth, new[] { a, b, c }));
    }
}
