using System;
using System.IO;
using System.Linq;
using System.Threading;

using VanillaGraphicsExpanded.PBR.Materials;
using VanillaGraphicsExpanded.PBR.Materials.Cache;

namespace VanillaGraphicsExpanded.Tests.Unit.PBR.Materials;

[Trait("Category", "Unit")]
public sealed class MaterialAtlasDiskCacheIndexBehaviorTests
{
    private static string NewTempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "vge-tests", "diskcache", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void Store_WritesMetaJson_AndHitUpdatesLastAccess()
    {
        string dir = NewTempDir();
        try
        {
            string metaPath = Path.Combine(dir, "meta.json");

            var cache = new MaterialAtlasDiskCache(dir, maxBytes: 64L * 1024 * 1024);
            var key = AtlasCacheKey.FromUtf8(1, "test|matparams|meta");

            cache.StoreMaterialParamsTile(key, width: 1, height: 1, rgbTriplets: [0.1f, 0.2f, 0.3f]);

            Assert.True(File.Exists(metaPath));
            Assert.True(MaterialAtlasDiskCacheIndex.TryLoad(metaPath, out MaterialAtlasDiskCacheIndex index1));
            Assert.True(index1.Entries.Count >= 1);

            string entryId = index1.Entries.Keys.Single(k => index1.Entries[k].Kind == "materialParams");
            long lastAccess1 = index1.Entries[entryId].LastAccessUtcTicks;

            Thread.Sleep(10);

            Assert.True(cache.TryLoadMaterialParamsTile(key, out _));

            Assert.True(MaterialAtlasDiskCacheIndex.TryLoad(metaPath, out MaterialAtlasDiskCacheIndex index2));
            long lastAccess2 = index2.Entries[entryId].LastAccessUtcTicks;

            Assert.True(lastAccess2 >= lastAccess1);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void Init_DropsIndexEntries_WhenPayloadMissing()
    {
        string dir = NewTempDir();
        try
        {
            string metaPath = Path.Combine(dir, "meta.json");

            var key = AtlasCacheKey.FromUtf8(1, "test|matparams|missingpayload");
            {
                var cache = new MaterialAtlasDiskCache(dir, maxBytes: 64L * 1024 * 1024);
                cache.StoreMaterialParamsTile(key, width: 1, height: 1, rgbTriplets: [0.1f, 0.2f, 0.3f]);
            }

            Assert.True(MaterialAtlasDiskCacheIndex.TryLoad(metaPath, out MaterialAtlasDiskCacheIndex index));
            var entry = index.Entries.Values.Single(e => e.Kind == "materialParams");
            string entryId = index.Entries.Keys.Single(k => index.Entries[k].Kind == "materialParams");
            string payloadPath = Path.Combine(dir, entry.DdsFileName ?? (entryId + ".dds"));

            Assert.True(File.Exists(payloadPath));
            File.Delete(payloadPath);

            var reloaded = new MaterialAtlasDiskCache(dir, maxBytes: 64L * 1024 * 1024);
            Assert.False(reloaded.HasMaterialParamsTile(key));
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void InvalidMetaJson_IsIgnoredAndDoesNotThrow()
    {
        string dir = NewTempDir();
        try
        {
            string metaPath = Path.Combine(dir, "meta.json");

            File.WriteAllText(metaPath, "{\"schemaVersion\":999,\"entries\":{}}", System.Text.Encoding.UTF8);

            var cache = new MaterialAtlasDiskCache(dir, maxBytes: 64L * 1024 * 1024);
            MaterialAtlasDiskCacheStats stats = cache.GetStatsSnapshot();

            Assert.Equal(0, stats.TotalEntries);
            Assert.Equal(0, stats.TotalBytes);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void CorruptPayload_IsDeferredUntilRead_ThenEvicted()
    {
        string dir = NewTempDir();
        try
        {
            string metaPath = Path.Combine(dir, "meta.json");

            var cache = new MaterialAtlasDiskCache(dir, maxBytes: 64L * 1024 * 1024);
            var key = AtlasCacheKey.FromUtf8(1, "test|matparams|corrupt");

            cache.StoreMaterialParamsTile(key, width: 1, height: 1, rgbTriplets: [0.1f, 0.2f, 0.3f]);

            Assert.True(MaterialAtlasDiskCacheIndex.TryLoad(metaPath, out MaterialAtlasDiskCacheIndex index));
            string entryId = index.Entries.Keys.Single(k => index.Entries[k].Kind == "materialParams");
            var entry = index.Entries[entryId];
            string payloadPath = Path.Combine(dir, entry.DdsFileName ?? (entryId + ".dds"));

            Assert.True(File.Exists(payloadPath));
            File.WriteAllBytes(payloadPath, [1, 2, 3, 4, 5, 6, 7, 8]);

            Assert.False(cache.TryLoadMaterialParamsTile(key, out _));
            Assert.False(cache.HasMaterialParamsTile(key));
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void CorruptNormalDepthPayload_IsDeferredUntilRead_ThenEvicted()
    {
        string dir = NewTempDir();
        try
        {
            string metaPath = Path.Combine(dir, "meta.json");

            var cache = new MaterialAtlasDiskCache(dir, maxBytes: 64L * 1024 * 1024);
            var key = AtlasCacheKey.FromUtf8(1, "test|normaldepth|corrupt");

            cache.StoreNormalDepthTile(key, width: 1, height: 1, rgbaQuads: [0.1f, 0.2f, 0.3f, 0.4f]);

            Assert.True(MaterialAtlasDiskCacheIndex.TryLoad(metaPath, out MaterialAtlasDiskCacheIndex index));
            string entryId = index.Entries.Keys.Single(k => index.Entries[k].Kind == "normalDepth");
            var entry = index.Entries[entryId];
            string payloadPath = Path.Combine(dir, entry.DdsFileName ?? (entryId + ".dds"));

            Assert.True(File.Exists(payloadPath));
            File.WriteAllBytes(payloadPath, [1, 2, 3, 4, 5, 6, 7, 8]);

            Assert.False(cache.TryLoadNormalDepthTile(key, out _));
            Assert.False(cache.HasNormalDepthTile(key));
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }
}
