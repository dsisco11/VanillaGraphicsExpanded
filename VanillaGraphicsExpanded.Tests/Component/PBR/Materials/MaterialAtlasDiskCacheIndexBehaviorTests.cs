using System;
using System.Linq;

using VanillaGraphicsExpanded.PBR.Materials;
using VanillaGraphicsExpanded.PBR.Materials.Cache;
using VanillaGraphicsExpanded.Tests.Support.Cache;

namespace VanillaGraphicsExpanded.Tests.Component.PBR.Materials;

/// <summary>Checks index persistence, invalidation, and deferred corruption handling.</summary>
[Trait("Category", "Unit")]
public sealed class MaterialAtlasDiskCacheIndexBehaviorTests
{
    #region Public API
    /// <summary>Stores an entry in metadata and advances its access time on a hit.</summary>
    [Fact]
    public void Store_WritesMetaJson_AndHitUpdatesLastAccess()
    {
        // Arrange: create a cache and one material-parameter entry.
        var fs = new InMemoryMaterialAtlasFileSystem();
        string root = $"memcache/{Guid.NewGuid():N}";
        string metaPath = $"{root}/meta.json";

        var cache = new MaterialAtlasDiskCache(root, maxBytes: 64L * 1024 * 1024, fs);
            var key = AtlasCacheKey.FromUtf8(1, "test|matparams|meta");

            cache.StoreMaterialParamsTile(key, width: 1, height: 1, rgbTriplets: [0.1f, 0.2f, 0.3f]);

        Assert.True(fs.FileExists(metaPath));
        Assert.True(MaterialAtlasDiskCacheIndex.TryLoad(fs, metaPath, out MaterialAtlasDiskCacheIndex index1));
            Assert.True(index1.Entries.Count >= 1);

            string entryId = index1.Entries.Keys.Single(k => index1.Entries[k].Kind == "materialParams");
        long lastAccess1 = DateTime.UtcNow.AddDays(-1).Ticks;
        index1.Entries[entryId] = index1.Entries[entryId] with { LastAccessUtcTicks = lastAccess1 };
        index1.SaveAtomic(fs, metaPath);

        // Reopen to make the known older timestamp the cache's current persisted state.
        cache = new MaterialAtlasDiskCache(root, maxBytes: 64L * 1024 * 1024, fs);

        // Act: read the entry so the cache persists a fresh access timestamp.
        Assert.True(cache.TryLoadMaterialParamsTile(key, out _));

        // Assert: the updated index contains a strictly later access timestamp.
        Assert.True(MaterialAtlasDiskCacheIndex.TryLoad(fs, metaPath, out MaterialAtlasDiskCacheIndex index2));
            long lastAccess2 = index2.Entries[entryId].LastAccessUtcTicks;

        Assert.True(lastAccess2 > lastAccess1);
    }

    /// <summary>Initialization removes index entries whose payload is absent.</summary>
    [Fact]
    public void Init_DropsIndexEntries_WhenPayloadMissing()
    {
        // Arrange: persist one entry and remove its payload while retaining the index.
        var fs = new InMemoryMaterialAtlasFileSystem();
        string root = $"memcache/{Guid.NewGuid():N}";
        string metaPath = $"{root}/meta.json";

            var key = AtlasCacheKey.FromUtf8(1, "test|matparams|missingpayload");
            {
                var cache = new MaterialAtlasDiskCache(root, maxBytes: 64L * 1024 * 1024, fs);
                cache.StoreMaterialParamsTile(key, width: 1, height: 1, rgbTriplets: [0.1f, 0.2f, 0.3f]);
            }

        Assert.True(MaterialAtlasDiskCacheIndex.TryLoad(fs, metaPath, out MaterialAtlasDiskCacheIndex index));
            var entry = index.Entries.Values.Single(e => e.Kind == "materialParams");
            string entryId = index.Entries.Keys.Single(k => index.Entries[k].Kind == "materialParams");
        string payloadPath = $"{root}/" + (entry.DdsFileName ?? (entryId + ".dds"));

        Assert.True(fs.FileExists(payloadPath));
        // Act: delete the payload and reopen the cache.
        fs.DeleteFile(payloadPath);

        var reloaded = new MaterialAtlasDiskCache(root, maxBytes: 64L * 1024 * 1024, fs);
        // Assert: initialization no longer exposes the missing payload as a hit.
        Assert.False(reloaded.HasMaterialParamsTile(key));
    }

    /// <summary>Invalid metadata is ignored and yields empty cache statistics.</summary>
    [Fact]
    public void InvalidMetaJson_IsIgnoredAndDoesNotThrow()
    {
        // Arrange: supply metadata with an unsupported schema version.
        var fs = new InMemoryMaterialAtlasFileSystem();
        string root = $"memcache/{Guid.NewGuid():N}";
        string metaPath = $"{root}/meta.json";

        fs.WriteAllBytes(metaPath, System.Text.Encoding.UTF8.GetBytes("{\"schemaVersion\":999,\"entries\":{}}"));

        // Act: construct the cache and take its statistics snapshot.
        var cache = new MaterialAtlasDiskCache(root, maxBytes: 64L * 1024 * 1024, fs);
        MaterialAtlasDiskCacheStats stats = cache.GetStatsSnapshot();

        // Assert: malformed metadata does not create cache entries or account bytes.
            Assert.Equal(0, stats.TotalEntries);
            Assert.Equal(0, stats.TotalBytes);
    }

    /// <summary>Defers material-parameter payload validation until read, then evicts corruption.</summary>
    [Fact]
    public void CorruptPayload_IsDeferredUntilRead_ThenEvicted()
    {
        // Arrange: persist a material-parameter payload and replace it with malformed bytes.
        var fs = new InMemoryMaterialAtlasFileSystem();
        string root = $"memcache/{Guid.NewGuid():N}";
        string metaPath = $"{root}/meta.json";

        var cache = new MaterialAtlasDiskCache(root, maxBytes: 64L * 1024 * 1024, fs);
        var key = AtlasCacheKey.FromUtf8(1, "test|matparams|corrupt");

            cache.StoreMaterialParamsTile(key, width: 1, height: 1, rgbTriplets: [0.1f, 0.2f, 0.3f]);

        Assert.True(MaterialAtlasDiskCacheIndex.TryLoad(fs, metaPath, out MaterialAtlasDiskCacheIndex index));
        string entryId = index.Entries.Keys.Single(k => index.Entries[k].Kind == "materialParams");
        var entry = index.Entries[entryId];
        string payloadPath = $"{root}/" + (entry.DdsFileName ?? (entryId + ".dds"));

        Assert.True(fs.FileExists(payloadPath));
        fs.WriteAllBytes(payloadPath, [1, 2, 3, 4, 5, 6, 7, 8]);

        // Act: attempt to load the malformed payload.
        Assert.False(cache.TryLoadMaterialParamsTile(key, out _));

        // Assert: the failed read evicts the corrupt entry.
        Assert.False(cache.HasMaterialParamsTile(key));
    }

    /// <summary>Defers normal-depth payload validation until read, then evicts corruption.</summary>
    [Fact]
    public void CorruptNormalDepthPayload_IsDeferredUntilRead_ThenEvicted()
    {
        // Arrange: persist a normal-depth payload and replace it with malformed bytes.
        var fs = new InMemoryMaterialAtlasFileSystem();
        string root = $"memcache/{Guid.NewGuid():N}";
        string metaPath = $"{root}/meta.json";

        var cache = new MaterialAtlasDiskCache(root, maxBytes: 64L * 1024 * 1024, fs);
        var key = AtlasCacheKey.FromUtf8(1, "test|normaldepth|corrupt");

            cache.StoreNormalDepthTile(key, width: 1, height: 1, rgbaQuads: [0.1f, 0.2f, 0.3f, 0.4f]);

        Assert.True(MaterialAtlasDiskCacheIndex.TryLoad(fs, metaPath, out MaterialAtlasDiskCacheIndex index));
        string entryId = index.Entries.Keys.Single(k => index.Entries[k].Kind == "normalDepth");
        var entry = index.Entries[entryId];
        string payloadPath = $"{root}/" + (entry.DdsFileName ?? (entryId + ".dds"));

        Assert.True(fs.FileExists(payloadPath));
        fs.WriteAllBytes(payloadPath, [1, 2, 3, 4, 5, 6, 7, 8]);

        // Act: attempt to load the malformed payload.
        Assert.False(cache.TryLoadNormalDepthTile(key, out _));

        // Assert: the failed read evicts the corrupt entry.
        Assert.False(cache.HasNormalDepthTile(key));
    }
    #endregion
}
