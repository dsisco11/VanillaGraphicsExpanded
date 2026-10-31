using VanillaGraphicsExpanded.Tests.TestSupport;
using System;
using System.IO;

using VanillaGraphicsExpanded.Cache.Disk;

namespace VanillaGraphicsExpanded.Tests.Unit.Cache;

public sealed class DiskJsonDictionaryCacheStoreTests
{
    private readonly CountingDiskCacheFileSystem storage = new();

    [Fact]
    public void RoundTrip_Writes_And_Reads_JsonBytes()
    {
        string root = CreateTempDir();
        try
        {
            using var store = new DiskJsonDictionaryCacheStore(root, "meta.json", storage);

            Assert.True(store.TryWriteAtomic("k1", "\"hello\""u8));

            Assert.True(store.TryRead("k1", out byte[] read));
            Assert.Equal("\"hello\"", System.Text.Encoding.UTF8.GetString(read));
        }
        finally
        {
            TryDeleteDir(root);
        }
    }

    [Fact]
    public void Corrupt_Index_Is_Treated_As_Empty()
    {
        string root = CreateTempDir();
        try
        {
            using var store = new DiskJsonDictionaryCacheStore(root, "meta.json", storage);
            Assert.True(store.TryWriteAtomic("k1", "{\"a\":1}"u8));

            // Corrupt the backing JSON file.
            storage.WriteAllText(Path.Combine(root, "meta.json"), "not-json");

            using var reloaded = new DiskJsonDictionaryCacheStore(root, "meta.json", storage);
            Assert.False(reloaded.TryRead("k1", out _));
            Assert.Empty(reloaded.EnumerateEntryIds());
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
