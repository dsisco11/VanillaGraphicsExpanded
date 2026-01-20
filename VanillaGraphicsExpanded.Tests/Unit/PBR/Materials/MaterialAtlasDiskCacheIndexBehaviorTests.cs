using System;
using System.Linq;
using System.Threading;

using VanillaGraphicsExpanded.PBR.Materials;
using VanillaGraphicsExpanded.PBR.Materials.Cache;
using VanillaGraphicsExpanded.Tests.TestSupport;

namespace VanillaGraphicsExpanded.Tests.Unit.PBR.Materials;

[Trait("Category", "Unit")]
public sealed class MaterialAtlasDiskCacheIndexBehaviorTests
{
    [Fact]
    public void Store_WritesMetaJson_AndHitUpdatesLastAccess()
    {
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
            long lastAccess1 = index1.Entries[entryId].LastAccessUtcTicks;

            Thread.Sleep(10);

            Assert.True(cache.TryLoadMaterialParamsTile(key, out _));

        Assert.True(MaterialAtlasDiskCacheIndex.TryLoad(fs, metaPath, out MaterialAtlasDiskCacheIndex index2));
            long lastAccess2 = index2.Entries[entryId].LastAccessUtcTicks;

            Assert.True(lastAccess2 >= lastAccess1);
    }

    [Fact]
    public void Init_DropsIndexEntries_WhenPayloadMissing()
    {
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
        fs.DeleteFile(payloadPath);

        var reloaded = new MaterialAtlasDiskCache(root, maxBytes: 64L * 1024 * 1024, fs);
        Assert.False(reloaded.HasMaterialParamsTile(key));
    }

    [Fact]
    public void InvalidMetaJson_IsIgnoredAndDoesNotThrow()
    {
        var fs = new InMemoryMaterialAtlasFileSystem();
        string root = $"memcache/{Guid.NewGuid():N}";
        string metaPath = $"{root}/meta.json";

        fs.WriteAllBytes(metaPath, System.Text.Encoding.UTF8.GetBytes("{\"schemaVersion\":999,\"entries\":{}}"));

        var cache = new MaterialAtlasDiskCache(root, maxBytes: 64L * 1024 * 1024, fs);
        MaterialAtlasDiskCacheStats stats = cache.GetStatsSnapshot();

            Assert.Equal(0, stats.TotalEntries);
            Assert.Equal(0, stats.TotalBytes);
    }

    [Fact]
    public void CorruptPayload_IsDeferredUntilRead_ThenEvicted()
    {
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

        Assert.False(cache.TryLoadMaterialParamsTile(key, out _));
        Assert.False(cache.HasMaterialParamsTile(key));
    }

    [Fact]
    public void CorruptNormalDepthPayload_IsDeferredUntilRead_ThenEvicted()
    {
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

        Assert.False(cache.TryLoadNormalDepthTile(key, out _));
        Assert.False(cache.HasNormalDepthTile(key));
    }
}
