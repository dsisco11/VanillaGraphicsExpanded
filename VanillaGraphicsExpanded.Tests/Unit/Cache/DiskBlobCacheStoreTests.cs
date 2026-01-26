using System;
using System.IO;

using VanillaGraphicsExpanded.Cache.Disk;

namespace VanillaGraphicsExpanded.Tests.Unit.Cache;

public sealed class DiskBlobCacheStoreTests
{
    [Fact]
    public void RoundTrip_Writes_And_Reads_Bytes()
    {
        string root = CreateTempDir();
        try
        {
            var store = new DiskBlobCacheStore(root);

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
            var store = new DiskBlobCacheStore(root);
            Assert.True(store.TryWriteAtomic("k1", new byte[] { 9, 9 }));

            // Delete payload file to simulate corruption/missing.
            string payloadPath = Path.Combine(root, "payloads", "k1.bin");
            File.Delete(payloadPath);

            Assert.False(store.TryRead("k1", out _));
            Assert.DoesNotContain("k1", store.EnumerateEntryIds());
        }
        finally
        {
            TryDeleteDir(root);
        }
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
