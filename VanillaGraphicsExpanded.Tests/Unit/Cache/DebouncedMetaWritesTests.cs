using System;

using VanillaGraphicsExpanded.Cache.Disk;
using VanillaGraphicsExpanded.Tests.TestSupport;

namespace VanillaGraphicsExpanded.Tests.Unit.Cache;

[Trait("Category", "Unit")]
public sealed class DebouncedMetaWritesTests
{
    [Fact]
    public void DiskJsonDictionaryCacheStore_Debounces_Touch_MetaWrites()
    {
        var fs = new CountingDiskCacheFileSystem();
        string root = "/root";

        // Use an extremely long debounce delay so the touch flush won't fire during the test.
        using var store = new DiskJsonDictionaryCacheStore(rootDirectory: root, fileName: "meta.json", fileSystem: fs, debounceDelay: TimeSpan.FromDays(1));

        Assert.True(store.TryWriteAtomic("k1", "\"hello\""u8));
        int writesAfterStore = fs.MetaJsonCommitWrites;
        Assert.True(writesAfterStore >= 1);

        for (int i = 0; i < 25; i++)
        {
            Assert.True(store.TryRead("k1", out _));
        }

        // Touch writes should be debounced, so the meta.json commit count should not increase per read.
        Assert.Equal(writesAfterStore, fs.MetaJsonCommitWrites);
    }

    [Fact]
    public void DiskJsonDictionaryCacheStore_Debounces_Structural_Writes()
    {
        var fs = new CountingDiskCacheFileSystem();
        string root = "/root";

        using var store = new DiskJsonDictionaryCacheStore(rootDirectory: root, fileName: "meta.json", fileSystem: fs, debounceDelay: TimeSpan.FromDays(1));

        int baseline = fs.MetaJsonCommitWrites;

        for (int i = 0; i < 50; i++)
        {
            Assert.True(store.TryWriteAtomic("k" + i, "{\"a\":1}"u8));
        }

        // With a long debounce, we should not commit meta.json for every write.
        Assert.Equal(baseline, fs.MetaJsonCommitWrites);
    }

    [Fact]
    public void DiskBlobCacheStore_Debounces_Index_Writes()
    {
        var fs = new CountingDiskCacheFileSystem();
        string root = "/root";

        using var store = new DiskBlobCacheStore(rootDirectory: root, fileSystem: fs, debounceDelay: TimeSpan.FromDays(1));

        int baseline = fs.MetaJsonCommitWrites;

        for (int i = 0; i < 25; i++)
        {
            Assert.True(store.TryWriteAtomic("k" + i, new byte[] { 1, 2, 3, 4 }));
        }

        // Payloads are written immediately, but meta.json commits should be debounced.
        Assert.Equal(baseline, fs.MetaJsonCommitWrites);
    }
}
