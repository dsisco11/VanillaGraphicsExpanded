using System;
using System.IO;

using VanillaGraphicsExpanded.PBR.Materials;
using VanillaGraphicsExpanded.PBR.Materials.Cache;

namespace VanillaGraphicsExpanded.Tests.Unit.PBR.Materials;

[Trait("Category", "Unit")]
public sealed class MaterialAtlasDiskCacheRoundTripTests
{
    private static string NewTempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "vge-tests", "diskcache", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void StoreAndLoad_MaterialParams_RoundTripsWithinQuantization()
    {
        string dir = NewTempDir();
        try
        {
            var cache = new MaterialAtlasDiskCache(dir, maxBytes: 64L * 1024 * 1024);

            var key = AtlasCacheKey.FromUtf8(1, "test|matparams|a");

            // 2x2 tile, RGB triplets
            float[] rgb =
            [
                0.0f, 0.5f, 1.0f,
                0.25f, 0.75f, 0.1f,
                0.9f, 0.2f, 0.3f,
                0.6f, 0.4f, 0.8f
            ];

            cache.StoreMaterialParamsTile(key, width: 2, height: 2, rgbTriplets: rgb);

            Assert.True(cache.TryLoadMaterialParamsTile(key, out float[] loaded));
            Assert.Equal(rgb.Length, loaded.Length);

            const float Eps = (1f / 65535f) + 1e-6f;
            for (int i = 0; i < rgb.Length; i++)
            {
                Assert.InRange(MathF.Abs(loaded[i] - rgb[i]), 0f, Eps);
            }
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void StoreAndLoad_NormalDepth_RoundTripsWithinQuantization()
    {
        string dir = NewTempDir();
        try
        {
            var cache = new MaterialAtlasDiskCache(dir, maxBytes: 64L * 1024 * 1024);

            var key = AtlasCacheKey.FromUtf8(1, "test|normaldepth|a");

            // 2x2 tile, RGBA quads
            float[] rgba =
            [
                0.0f, 0.5f, 1.0f, 0.25f,
                0.25f, 0.75f, 0.1f, 0.5f,
                0.9f, 0.2f, 0.3f, 0.75f,
                0.6f, 0.4f, 0.8f, 1.0f
            ];

            cache.StoreNormalDepthTile(key, width: 2, height: 2, rgbaQuads: rgba);

            Assert.True(cache.TryLoadNormalDepthTile(key, out float[] loaded));
            Assert.Equal(rgba.Length, loaded.Length);

            const float Eps = (1f / 65535f) + 1e-6f;
            for (int i = 0; i < rgba.Length; i++)
            {
                Assert.InRange(MathF.Abs(loaded[i] - rgba[i]), 0f, Eps);
            }
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void StatsSnapshot_TracksHitsMissesStoresAndTotals()
    {
        string dir = NewTempDir();
        try
        {
            var cache = new MaterialAtlasDiskCache(dir, maxBytes: 64L * 1024 * 1024);

            var matKey = AtlasCacheKey.FromUtf8(1, "test|matparams|stats");
            var ndKey = AtlasCacheKey.FromUtf8(1, "test|normaldepth|stats");

            // Misses
            _ = cache.TryLoadMaterialParamsTile(AtlasCacheKey.FromUtf8(1, "missing|mat"), out _);
            _ = cache.TryLoadNormalDepthTile(AtlasCacheKey.FromUtf8(1, "missing|nd"), out _);

            // Stores
            cache.StoreMaterialParamsTile(matKey, 1, 1, rgbTriplets: [0.1f, 0.2f, 0.3f]);
            cache.StoreNormalDepthTile(ndKey, 1, 1, rgbaQuads: [0.1f, 0.2f, 0.3f, 0.4f]);

            // Hits
            Assert.True(cache.TryLoadMaterialParamsTile(matKey, out _));
            Assert.True(cache.TryLoadNormalDepthTile(ndKey, out _));

            MaterialAtlasDiskCacheStats stats = cache.GetStatsSnapshot();

            Assert.True(stats.MaterialParams.Misses >= 1);
            Assert.True(stats.NormalDepth.Misses >= 1);
            Assert.True(stats.MaterialParams.Stores >= 1);
            Assert.True(stats.NormalDepth.Stores >= 1);
            Assert.True(stats.MaterialParams.Hits >= 1);
            Assert.True(stats.NormalDepth.Hits >= 1);

            Assert.True(stats.TotalEntries >= 2);
            Assert.True(stats.TotalBytes > 0);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void CachedMaterialParams_MatchesFreshBake_WithinQuantization()
    {
        string dir = NewTempDir();
        try
        {
            var cache = new MaterialAtlasDiskCache(dir, maxBytes: 64L * 1024 * 1024);
            var key = AtlasCacheKey.FromUtf8(1, "test|matparams|parity");

            var texture = new Vintagestory.API.Common.AssetLocation("game", "textures/block/test.png");
            var def = new PbrMaterialDefinition(
                Roughness: 0.25f,
                Metallic: 0.75f,
                Emissive: 0.1f,
                Noise: new PbrMaterialNoise(Roughness: 0.2f, Metallic: 0.1f, Emissive: 0.0f, Reflectivity: 0f, Normals: 0f),
                Scale: new PbrOverrideScale(Roughness: 0.9f, Metallic: 1.0f, Emissive: 1.0f, Normal: 1.0f, Depth: 1.0f),
                Priority: 0,
                Notes: null);

            float[] fresh1 = MaterialAtlasParamsBuilder.BuildRgb16fTile(texture, def, rectWidth: 8, rectHeight: 8, cancellationToken: TestContext.Current.CancellationToken);
            cache.StoreMaterialParamsTile(key, 8, 8, fresh1);

            Assert.True(cache.TryLoadMaterialParamsTile(key, out float[] cached));
            float[] fresh2 = MaterialAtlasParamsBuilder.BuildRgb16fTile(texture, def, rectWidth: 8, rectHeight: 8, cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(fresh2.Length, cached.Length);

            const float Eps = (1f / 65535f) + 1e-6f;
            for (int i = 0; i < cached.Length; i++)
            {
                Assert.InRange(MathF.Abs(cached[i] - fresh2[i]), 0f, Eps);
            }
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }
}
