using System;

using VanillaGraphicsExpanded.Cache.Disk;
using VanillaGraphicsExpanded.Tests.Support.Cache;

namespace VanillaGraphicsExpanded.Tests.Component.Cache;

/// <summary>Checks that metadata persistence coalesces touch and structural updates.</summary>
[Trait("Category", "Unit")]
public sealed class DebouncedMetaWritesTests
{
    #region Public API
    /// <summary>Repeated reads do not commit metadata once per accessed entry.</summary>
    [Fact]
    public void DiskJsonDictionaryCacheStore_Debounces_Touch_MetaWrites()
    {
        // Arrange: use a long delay to keep the metadata flush pending during the test.
        var fs = new CountingDiskCacheFileSystem();
        string root = "/root";

        // Use an extremely long debounce delay so the touch flush won't fire during the test.
        using var store = new DiskJsonDictionaryCacheStore(rootDirectory: root, fileName: "meta.json", fileSystem: fs, debounceDelay: TimeSpan.FromDays(1));

        // Act: store one entry, then read it repeatedly to trigger touch updates.
        Assert.True(store.TryWriteAtomic("k1", "\"hello\""u8));
        int writesAfterStore = fs.MetaJsonCommitWrites;
        Assert.True(writesAfterStore >= 1);

        for (int i = 0; i < 25; i++)
        {
            Assert.True(store.TryRead("k1", out _));
        }

        // Assert: touch writes are debounced instead of committing once per read.
        Assert.Equal(writesAfterStore, fs.MetaJsonCommitWrites);
    }

    /// <summary>Entry additions are coalesced while the long debounce window is pending.</summary>
    [Fact]
    public void DiskJsonDictionaryCacheStore_Debounces_Structural_Writes()
    {
        // Arrange: track metadata commits while a long debounce remains pending.
        var fs = new CountingDiskCacheFileSystem();
        string root = "/root";


        using var store = new DiskJsonDictionaryCacheStore(rootDirectory: root, fileName: "meta.json", fileSystem: fs, debounceDelay: TimeSpan.FromDays(1));
        int baseline = fs.MetaJsonCommitWrites;

        // Act: add many entries during the same debounce window.
        for (int i = 0; i < 50; i++)
        {
            Assert.True(store.TryWriteAtomic("k" + i, "{\"a\":1}"u8));
        }

        // Assert: no metadata commit occurs for each individual entry.
        Assert.Equal(baseline, fs.MetaJsonCommitWrites);
    }

    /// <summary>Blob payload writes proceed while index commits are coalesced.</summary>
    [Fact]
    public void DiskBlobCacheStore_Debounces_Index_Writes()
    {
        // Arrange: track index metadata commits while retaining immediate payload writes.
        var fs = new CountingDiskCacheFileSystem();
        string root = "/root";

        using var store = new DiskBlobCacheStore(rootDirectory: root, fileSystem: fs, debounceDelay: TimeSpan.FromDays(1));

        int baseline = fs.MetaJsonCommitWrites;

        // Act: write multiple payloads while the index debounce is pending.
        for (int i = 0; i < 25; i++)
        {
            Assert.True(store.TryWriteAtomic("k" + i, new byte[] { 1, 2, 3, 4 }));
        }

        // Assert: payload writes do not cause a metadata commit per entry.
        Assert.Equal(baseline, fs.MetaJsonCommitWrites);
    }
    #endregion
}
