using VanillaGraphicsExpanded.Tests.TestSupport;
using System;
using System.IO;

using VanillaGraphicsExpanded.Cache.Disk;

namespace VanillaGraphicsExpanded.Tests.Unit.Cache;

public sealed class DiskBlobCacheStoreTests
{
    private readonly CountingDiskCacheFileSystem storage = new();

    [Fact]
    public void RoundTrip_Writes_And_Reads_Bytes()
    {
        string root = CreateTempDir();
        try
        {
            using var store = new DiskBlobCacheStore(root, storage);

            Assert.True(store.TryWriteAtomic("k1", new byte[] { 1, 2, 3 }));

            Assert.True(store.TryRead("k1", out byte[] read));
            Assert.Equal(new byte[] { 1, 2, 3 }, read);
        }
        finally
        {
            TryDeleteDir(root);
        }
    }

    [Fact]
    public void Corrupt_Payload_Is_Treated_As_Miss_And_Purged()
    {
        string root = CreateTempDir();
        try
        {
            using var store = new DiskBlobCacheStore(root, storage);
            Assert.True(store.TryWriteAtomic("k1", new byte[] { 9, 9 }));

            // Delete payload file to simulate corruption/missing.
            string payloadPath = Path.Combine(root, "payloads", "k1.bin");
            storage.DeleteFile(payloadPath);

            Assert.False(store.TryRead("k1", out _));
            Assert.DoesNotContain("k1", store.EnumerateEntryIds());
        }
        finally
        {
            TryDeleteDir(root);
        }
    }

    private string CreateTempDir()
    {
        string path = Path.Combine(Path.GetTempPath(), "vge-cachetests", Guid.NewGuid().ToString("N"));
        storage.CreateDirectory(path);
        return path;
    }

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
}
