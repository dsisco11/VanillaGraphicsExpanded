using System;
using System.Globalization;
using System.IO;

using VanillaGraphicsExpanded.Cache;
using VanillaGraphicsExpanded.Cache.Disk;
using VanillaGraphicsExpanded.PBR.Materials;
using VanillaGraphicsExpanded.PBR.Materials.Cache;

using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.Tests.Unit.PBR.Materials;

[Trait("Category", "Unit")]
public sealed class BaseColorCacheKeyAndCodecTests
{
    [Fact]
    public void BaseColorKeys_AreStable_AcrossCalls_AndCultureIndependent()
    {
        CultureInfo original = CultureInfo.CurrentCulture;

        try
        {
            var inputs = BaseColorCacheKeyInputs.CreateDefaults();
            var builder = new BaseColorCacheKeyBuilder();

            var texture = new AssetLocation("game", "textures/block/test.png");
            string origin = "assets/game/textures/block/test.png";
            long bytes = 12345;

            AtlasCacheKey k1 = builder.BuildKey(inputs, texture, originPath: origin, assetBytes: bytes);

            CultureInfo.CurrentCulture = new CultureInfo("fr-FR");
            AtlasCacheKey k2 = builder.BuildKey(inputs, texture, originPath: origin, assetBytes: bytes);

            Assert.Equal(k1, k2);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void BaseColorKeys_Change_WhenOriginBytesOrAvgAlgoChanges()
    {
        var inputs = BaseColorCacheKeyInputs.CreateDefaults();
        var builder = new BaseColorCacheKeyBuilder();

        var texture = new AssetLocation("game", "textures/block/test.png");

        AtlasCacheKey kA = builder.BuildKey(inputs, texture, originPath: "a", assetBytes: 10);
        AtlasCacheKey kB = builder.BuildKey(inputs, texture, originPath: "b", assetBytes: 10);
        AtlasCacheKey kC = builder.BuildKey(inputs, texture, originPath: "a", assetBytes: 11);

        Assert.NotEqual(kA, kB);
        Assert.NotEqual(kA, kC);

        // AvgAlgo is part of StablePrefix, so changing it must change the key.
        var inputs2 = new BaseColorCacheKeyInputs(
            SchemaVersion: inputs.SchemaVersion,
            StablePrefix: inputs.StablePrefix.Replace("avgAlgo=1", "avgAlgo=2", StringComparison.Ordinal));

        AtlasCacheKey kD = builder.BuildKey(inputs2, texture, originPath: "a", assetBytes: 10);
        Assert.NotEqual(kA, kD);
    }

    [Fact]
    public void JsonStore_RoundTrips_Rgb16fPayload_WithoutLoss()
    {
        string root = CreateTempDir();
        try
        {
            var store = new DiskJsonDictionaryCacheStore(root);
            var codec = new BaseColorRgb16fJsonCodec();

            var cache = new DataCacheSystem<AtlasCacheKey, BaseColorRgb16f>(
                store,
                codec,
                keyToEntryId: BaseColorCacheKeyBuilder.ToEntryId,
                tryParseKey: BaseColorCacheKeyBuilder.TryParseEntryId);

            var inputs = BaseColorCacheKeyInputs.CreateDefaults();
            var builder = new BaseColorCacheKeyBuilder();
            AtlasCacheKey key = builder.BuildKey(inputs, new AssetLocation("game", "textures/block/test.png"), originPath: "a", assetBytes: 1);

            var payload = new BaseColorRgb16f(R: 0x3C00, G: 0x0000, B: 0x7BFF);

            cache.Store(key, payload);

            Assert.True(cache.TryGet(key, out BaseColorRgb16f loaded));
            Assert.Equal(payload, loaded);
        }
        finally
        {
            TryDeleteDir(root);
        }
    }

    private static string CreateTempDir()
    {
        string path = Path.Combine(Path.GetTempPath(), "vge-cachetests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void TryDeleteDir(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // Best-effort.
        }
    }
}
