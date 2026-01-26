using System;
using System.IO;

using VanillaGraphicsExpanded.Cache.Disk;

namespace VanillaGraphicsExpanded.Tests.Unit.Cache;

public sealed class DiskJsonDictionaryCacheStoreTests
{
    [Fact]
    public void RoundTrip_Writes_And_Reads_JsonBytes()
    {
        string root = CreateTempDir();
        try
        {
            var store = new DiskJsonDictionaryCacheStore(root);

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
            var store = new DiskJsonDictionaryCacheStore(root);
            Assert.True(store.TryWriteAtomic("k1", "{\"a\":1}"u8));

            // Corrupt the backing JSON file.
            File.WriteAllText(Path.Combine(root, "meta.json"), "not-json");

            var reloaded = new DiskJsonDictionaryCacheStore(root);
            Assert.False(reloaded.TryRead("k1", out _));
            Assert.Empty(reloaded.EnumerateEntryIds());
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
