using VanillaGraphicsExpanded.Tests.Support.Cache;
using System;
using System.IO;
using System.Numerics;

using VanillaGraphicsExpanded.Cache;
using VanillaGraphicsExpanded.Cache.Disk;
using VanillaGraphicsExpanded.PBR.Materials;
using VanillaGraphicsExpanded.PBR.Materials.Cache;

using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.Tests.Component.PBR.Materials;

/// <summary>Checks base-color cache recovery, regeneration admission, and cache-hit behavior.</summary>
[Trait("Category", "Unit")]
[Collection("PbrMaterialRegistry")]
public sealed class BaseColorCacheBehaviorTests
{
    #region Public API
    /// <summary>A malformed entry does not prevent a neighboring valid entry from loading.</summary>
    [Fact]
    public void Corrupt_Entry_DoesNotPoison_Other_Entries()
    {
        // Arrange: seed one valid and one malformed entry, then compose the cache with its codec and store.
        string root = CreateTempDir();
        try
        {
            var store = new DiskJsonDictionaryCacheStore(root, "meta.json", storage);
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

            // Act: read the valid and malformed entries independently.
            Assert.True(cache.TryGet(goodKey, out BaseColorRgb16f good));
            Assert.Equal(new BaseColorRgb16f(0x3C00, 0x0000, 0x0000), good);

            // Assert: the good value survives and the malformed entry is a miss.
            Assert.False(cache.TryGet(badKey, out _));
            }
        }
        finally
        {
            TryDeleteDir(root);
        }
    }

    /// <summary>Rejects a regeneration result from an earlier registry session.</summary>
    [Fact]
    public void Session_Cancellation_Drops_Stale_Regen_Result()
    {
        // Arrange: clear the shared registry and begin two consecutive regeneration sessions.
        var reg = PbrMaterialRegistry.Instance;
        reg.Clear();
        try
        {
            var inputs = BaseColorCacheKeyInputs.CreateDefaults();
            var builder = new BaseColorCacheKeyBuilder();
            AtlasCacheKey key = builder.BuildKey(inputs, new AssetLocation("game", "textures/block/test.png"), originPath: "a", assetBytes: 1);

            long session1 = reg.BumpBaseColorRegenSessionForTests();
            long session2 = reg.BumpBaseColorRegenSessionForTests();

            // Act: submit a result for the earlier regeneration session.
            // Assert: stale work is rejected and leaves no published value.
            Assert.False(reg.TryApplyBaseColorRegenResultForTests(session1, key, new Vector3(1, 0, 0)));
            Assert.False(reg.TryGetBaseColorInMemoryForTests(key, out _));

            // Act: submit a result for the current session.
            // Assert: the current result is accepted and available.
            Assert.True(reg.TryApplyBaseColorRegenResultForTests(session2, key, new Vector3(0, 1, 0)));
            Assert.True(reg.TryGetBaseColorInMemoryForTests(key, out Vector3 v));
            Assert.Equal(new Vector3(0, 1, 0), v);
        }
        finally
        {
            reg.Clear();
        }
    }

    /// <summary>A memory-cache hit returns the value without invoking the compute callback.</summary>
    [Fact]
    public void Cache_Hit_Avoids_Expensive_Compute_Path()
    {
        // Arrange: seed the in-memory cache and provide a callback that fails if invoked.
        var reg = PbrMaterialRegistry.Instance;
        reg.Clear();
        try
        {
            var inputs = BaseColorCacheKeyInputs.CreateDefaults();
            var builder = new BaseColorCacheKeyBuilder();

            AssetLocation tex = new("game", "textures/block/hit.png");
            string origin = "a";
            long bytes = 1;

            AtlasCacheKey key = builder.BuildKey(inputs, tex, originPath: origin, assetBytes: bytes);
            reg.PutBaseColorInMemoryForTests(key, new Vector3(0.25f, 0.5f, 0.75f));

            int computeCalls = 0;

            // Act: request the cached value with the guarded compute callback.
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

            // Assert: the cache hit returns the seeded value without computing.
            Assert.True(cacheHit);
            Assert.Equal(0, computeCalls);
            Assert.Equal(new Vector3(0.25f, 0.5f, 0.75f), value);
        }
        finally
        {
            reg.Clear();
        }
    }
    #endregion

    #region Private
    private readonly CountingDiskCacheFileSystem storage = new();

    /// <summary>Creates a unique cache directory through the controlled filesystem.</summary>
    private string CreateTempDir()
    {
        string path = Path.Combine(Path.GetTempPath(), "vge-cachetests", Guid.NewGuid().ToString("N"));
        storage.CreateDirectory(path);
        return path;
    }

    /// <summary>Removes the temporary cache directory on a best-effort basis.</summary>
    private void TryDeleteDir(string path)
    {
        try
        {
            if (storage.DirectoryExists(path))
            {
                storage.DeleteDirectory(path, recursive: true);
            }
        }
        catch
        {
            // Best-effort.
        }
    }
    #endregion
}
