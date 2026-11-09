using System;
using System.Collections.Generic;

using VanillaGraphicsExpanded.PBR.Materials;
using VanillaGraphicsExpanded.PBR.Materials.Cache;

namespace VanillaGraphicsExpanded.Tests.Unit.PBR.Materials;

/// <summary>Retains the existing payload-kind hit-count baseline for the test-owned cache double.</summary>
[Trait("Category", "Unit")]
public sealed class MaterialAtlasCacheWarmupPlannerTests
{
    #region Public API
    /// <summary>The in-memory double counts seeded material and normal-depth keys independently.</summary>
    [Fact]
    public void CountExisting_CountsHits_ByPayloadKind()
    {
        // Arrange: seed one key in each payload-kind set and include a key absent from both.
        var cache = new FakeDiskCache();

        AtlasCacheKey a = AtlasCacheKey.FromUtf8(1, "schema=1|a");
        AtlasCacheKey b = AtlasCacheKey.FromUtf8(1, "schema=1|b");
        AtlasCacheKey c = AtlasCacheKey.FromUtf8(1, "schema=1|c");

        cache.StoreMaterialParamsTile(a, width: 1, height: 1, rgbTriplets: [0f, 0f, 0f]);
        cache.StoreNormalDepthTile(b, width: 1, height: 1, rgbaQuads: [0f, 0f, 0f, 0f]);

        // Act: count hits separately for each payload kind.
        int materialHits = cache.CountExisting(MaterialAtlasDiskCachePayloadKind.MaterialParams, new[] { a, b, c });
        int normalDepthHits = cache.CountExisting(MaterialAtlasDiskCachePayloadKind.NormalDepth, new[] { a, b, c });

        // Assert: each query counts only keys in its selected in-memory set.
        Assert.Equal(1, materialHits);
        Assert.Equal(1, normalDepthHits);
    }
    #endregion

    #region Private
    /// <summary>Stores only test-seeded cache keys and returns fixed misses for unused payload operations.</summary>
    private sealed class FakeDiskCache : IMaterialAtlasDiskCache
    {
        private readonly HashSet<AtlasCacheKey> materialKeys = new();
        private readonly HashSet<AtlasCacheKey> normalDepthKeys = new();

        #region Public API
        /// <summary>Removes all keys recorded by this in-memory test double.</summary>
        public void Clear()
        {
            materialKeys.Clear();
            normalDepthKeys.Clear();
        }

        /// <summary>Reports counts derived from the two key sets without modeling byte storage.</summary>
        public MaterialAtlasDiskCacheStats GetStatsSnapshot()
            => new(
                MaterialParams: default,
                NormalDepth: default,
                TotalEntries: materialKeys.Count + normalDepthKeys.Count,
                TotalBytes: 0,
                EvictedEntries: 0,
                EvictedBytes: 0);

        /// <summary>Counts seeded keys in the set selected by payload kind.</summary>
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

        /// <summary>Reports whether the fake has recorded a material-parameter key.</summary>
        public bool HasMaterialParamsTile(AtlasCacheKey key) => materialKeys.Contains(key);

        /// <summary>Returns the fixed cache miss used by this test double.</summary>
        public bool TryLoadMaterialParamsTile(AtlasCacheKey key, out float[] rgbTriplets)
        {
            rgbTriplets = Array.Empty<float>();
            return false;
        }

        /// <summary>Records a material-parameter key without storing its payload.</summary>
        public void StoreMaterialParamsTile(AtlasCacheKey key, int width, int height, float[] rgbTriplets)
            => materialKeys.Add(key);

        /// <summary>Reports whether the fake has recorded a normal-depth key.</summary>
        public bool HasNormalDepthTile(AtlasCacheKey key) => normalDepthKeys.Contains(key);

        /// <summary>Returns the fixed cache miss used by this test double.</summary>
        public bool TryLoadNormalDepthTile(AtlasCacheKey key, out float[] rgbaQuads)
        {
            rgbaQuads = Array.Empty<float>();
            return false;
        }

        /// <summary>Records a normal-depth key without storing its payload.</summary>
        public void StoreNormalDepthTile(AtlasCacheKey key, int width, int height, float[] rgbaQuads)
            => normalDepthKeys.Add(key);
        #endregion
    }
    #endregion
}
