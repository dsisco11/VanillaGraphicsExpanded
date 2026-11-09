using VanillaGraphicsExpanded.Tests.Support.Cache;
using System;
using System.IO;

using VanillaGraphicsExpanded.Cache.Disk;

namespace VanillaGraphicsExpanded.Tests.Component.Cache;

/// <summary>Checks JSON dictionary cache persistence and recovery from an invalid index.</summary>
public sealed class DiskJsonDictionaryCacheStoreTests
{
    #region Public API
    /// <summary>Writes a JSON payload and reads back the same encoded bytes.</summary>
    [Fact]
    public void RoundTrip_Writes_And_Reads_JsonBytes()
    {
        // Arrange: create an isolated JSON dictionary cache store.
        string root = CreateTempDir();
        try
        {
            using var store = new DiskJsonDictionaryCacheStore(root, "meta.json", storage);

            // Act: persist and retrieve one JSON payload.
            Assert.True(store.TryWriteAtomic("k1", "\"hello\""u8));

            // Assert: the store preserves the encoded JSON bytes.
            Assert.True(store.TryRead("k1", out byte[] read));
            Assert.Equal("\"hello\"", System.Text.Encoding.UTF8.GetString(read));
        }
        finally
        {
            TryDeleteDir(root);
        }
    }

    /// <summary>An invalid persisted index reopens as an empty dictionary.</summary>
    [Fact]
    public void Corrupt_Index_Is_Treated_As_Empty()
    {
        // Arrange: persist an entry with a valid index, then create a fresh index on disk.
        string root = CreateTempDir();
        try
        {
            using var store = new DiskJsonDictionaryCacheStore(root, "meta.json", storage);
            Assert.True(store.TryWriteAtomic("k1", "{\"a\":1}"u8));

            // Act: replace the metadata file with invalid JSON and reopen the store.
            storage.WriteAllText(Path.Combine(root, "meta.json"), "not-json");

            using var reloaded = new DiskJsonDictionaryCacheStore(root, "meta.json", storage);

            // Assert: the reopened cache is empty because its index cannot be parsed.
            Assert.False(reloaded.TryRead("k1", out _));
            Assert.Empty(reloaded.EnumerateEntryIds());
        }
        finally
        {
            TryDeleteDir(root);
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
