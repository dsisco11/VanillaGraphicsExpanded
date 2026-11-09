using VanillaGraphicsExpanded.Tests.Support.Cache;
using System;
using System.IO;

using VanillaGraphicsExpanded.Cache.Disk;

namespace VanillaGraphicsExpanded.Tests.Component.Cache;

/// <summary>Checks blob cache persistence and recovery from a missing payload.</summary>
public sealed class DiskBlobCacheStoreTests
{
    #region Public API
    /// <summary>Writes a blob and reads back the same bytes.</summary>
    [Fact]
    public void RoundTrip_Writes_And_Reads_Bytes()
    {
        // Arrange: create an isolated cache store with controlled filesystem access.
        string root = CreateTempDir();
        try
        {
            using var store = new DiskBlobCacheStore(root, storage);

            // Act: persist and retrieve one payload.
            Assert.True(store.TryWriteAtomic("k1", new byte[] { 1, 2, 3 }));

            // Assert: the retrieved payload preserves the original bytes.
            Assert.True(store.TryRead("k1", out byte[] read));
            Assert.Equal(new byte[] { 1, 2, 3 }, read);
        }
        finally
        {
            TryDeleteDir(root);
        }
    }

    /// <summary>A missing indexed payload is reported as a miss and removed from the index.</summary>
    [Fact]
    public void Corrupt_Payload_Is_Treated_As_Miss_And_Purged()
    {
        // Arrange: create an indexed payload in an isolated store.
        string root = CreateTempDir();
        try
        {
            using var store = new DiskBlobCacheStore(root, storage);
            Assert.True(store.TryWriteAtomic("k1", new byte[] { 9, 9 }));

            // Act: remove the indexed file to simulate a missing payload, then read its entry.
            string payloadPath = Path.Combine(root, "payloads", "k1.bin");
            storage.DeleteFile(payloadPath);

            // Assert: the missing payload is a miss and its stale index entry is purged.
            Assert.False(store.TryRead("k1", out _));
            Assert.DoesNotContain("k1", store.EnumerateEntryIds());
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
