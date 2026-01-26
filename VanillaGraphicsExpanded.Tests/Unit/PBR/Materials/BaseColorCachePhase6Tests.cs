using System;
using System.IO;
using System.Numerics;

using VanillaGraphicsExpanded.Cache;
using VanillaGraphicsExpanded.Cache.Disk;
using VanillaGraphicsExpanded.PBR.Materials;
using VanillaGraphicsExpanded.PBR.Materials.Cache;

using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.Tests.Unit.PBR.Materials;

[Trait("Category", "Unit")]
[Collection("PbrMaterialRegistry")]
public sealed class BaseColorCachePhase6Tests
{
    [Fact]
    public void Corrupt_Entry_DoesNotPoison_Other_Entries()
    {
        string root = CreateTempDir();
        try
        {
            var store = new DiskJsonDictionaryCacheStore(root);
            using (store)
            {

            var inputs = BaseColorCacheKeyInputs.CreateDefaults();
            var builder = new BaseColorCacheKeyBuilder();

            AtlasCacheKey goodKey = builder.BuildKey(inputs, new AssetLocation("game", "textures/block/good.png"), originPath: "a", assetBytes: 1);
            AtlasCacheKey badKey = builder.BuildKey(inputs, new AssetLocation("game", "textures/block/bad.png"), originPath: "b", assetBytes: 2);

            Assert.True(store.TryWriteAtomic(BaseColorCacheKeyBuilder.ToEntryId(goodKey), "{\"schemaVersion\":1,\"r\":15360,\"g\":0,\"b\":0}"u8));
            Assert.True(store.TryWriteAtomic(BaseColorCacheKeyBuilder.ToEntryId(badKey), "\"not-an-object\""u8));

            var cache = new DataCacheSystem<AtlasCacheKey, BaseColorRgb16f>(
                store,
                new BaseColorRgb16fJsonCodec(),
                keyToEntryId: BaseColorCacheKeyBuilder.ToEntryId,
                tryParseKey: BaseColorCacheKeyBuilder.TryParseEntryId);

            Assert.True(cache.TryGet(goodKey, out BaseColorRgb16f good));
            Assert.Equal(new BaseColorRgb16f(0x3C00, 0x0000, 0x0000), good);

            Assert.False(cache.TryGet(badKey, out _));
            }
        }
        finally
        {
            TryDeleteDir(root);
        }
    }

    [Fact]
    public void Session_Cancellation_Drops_Stale_Regen_Result()
    {
        var reg = PbrMaterialRegistry.Instance;
        reg.Clear();

        var inputs = BaseColorCacheKeyInputs.CreateDefaults();
        var builder = new BaseColorCacheKeyBuilder();

        AtlasCacheKey key = builder.BuildKey(inputs, new AssetLocation("game", "textures/block/test.png"), originPath: "a", assetBytes: 1);

        long session1 = reg.BumpBaseColorRegenSessionForTests();
        long session2 = reg.BumpBaseColorRegenSessionForTests();

        // Old session must not apply.
        Assert.False(reg.TryApplyBaseColorRegenResultForTests(session1, key, new Vector3(1, 0, 0)));
        Assert.False(reg.TryGetBaseColorInMemoryForTests(key, out _));

        // Current session can apply.
        Assert.True(reg.TryApplyBaseColorRegenResultForTests(session2, key, new Vector3(0, 1, 0)));
        Assert.True(reg.TryGetBaseColorInMemoryForTests(key, out Vector3 v));
        Assert.Equal(new Vector3(0, 1, 0), v);

        reg.Clear();
    }

    [Fact]
    public void Cache_Hit_Avoids_Expensive_Compute_Path()
    {
        var reg = PbrMaterialRegistry.Instance;
        reg.Clear();

        var inputs = BaseColorCacheKeyInputs.CreateDefaults();
        var builder = new BaseColorCacheKeyBuilder();

        AssetLocation tex = new("game", "textures/block/hit.png");
        string origin = "a";
        long bytes = 1;

        AtlasCacheKey key = builder.BuildKey(inputs, tex, originPath: origin, assetBytes: bytes);
        reg.PutBaseColorInMemoryForTests(key, new Vector3(0.25f, 0.5f, 0.75f));

        int computeCalls = 0;

        Assert.True(reg.TryGetOrComputeBaseColorLinearForTests(
            tex,
            originPath: origin,
            bytes: bytes,
            expensiveCompute: () =>
            {
                computeCalls++;
                throw new InvalidOperationException("Should not be called on cache hit");
            },
            out Vector3 value,
            out bool cacheHit));

        Assert.True(cacheHit);
        Assert.Equal(0, computeCalls);
        Assert.Equal(new Vector3(0.25f, 0.5f, 0.75f), value);

        reg.Clear();
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
