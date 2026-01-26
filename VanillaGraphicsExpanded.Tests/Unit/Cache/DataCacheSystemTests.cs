using System;
using System.IO;

using VanillaGraphicsExpanded.Cache;
using VanillaGraphicsExpanded.Cache.Disk;

namespace VanillaGraphicsExpanded.Tests.Unit.Cache;

public sealed class DataCacheSystemTests
{
    [Fact]
    public void Store_Then_TryGet_RoundTrips_Through_Codec_And_Store()
    {
        string root = CreateTempDir();
        try
        {
            var store = new DiskJsonDictionaryCacheStore(root);
            var codec = new TestStringUtf8JsonCodec();

            var cache = new DataCacheSystem<int, string>(
                store,
                codec,
                keyToEntryId: k => "k" + k,
                tryParseKey: (string id, out int key) =>
                {
                    if (id.StartsWith("k", StringComparison.Ordinal) && int.TryParse(id[1..], out int v))
                    {
                        key = v;
                        return true;
                    }

                    key = default;
                    return false;
                });

            cache.Store(123, "\"ok\"");

            Assert.True(cache.TryGet(123, out string payload));
            Assert.Equal("\"ok\"", payload);
        }
        finally
        {
            TryDeleteDir(root);
        }
    }

    [Fact]
    public void Codec_DecodeFailure_Is_Treated_As_Miss()
    {
        string root = CreateTempDir();
        try
        {
            var store = new DiskJsonDictionaryCacheStore(root);
            var codec = new FailingDecodeCodec();

            var cache = new DataCacheSystem<int, string>(
                store,
                codec,
                keyToEntryId: k => "k" + k,
                tryParseKey: (string _, out int key) =>
                {
                    key = 1;
                    return true;
                });

            // Store valid JSON bytes directly in the store.
            Assert.True(store.TryWriteAtomic("k1", "\"hello\""u8));

            Assert.False(cache.TryGet(1, out _));
            DataCacheStats stats = cache.GetStatsSnapshot();
            Assert.True(stats.Misses >= 1);
            Assert.True(stats.DecodeFailures >= 1);
        }
        finally
        {
            TryDeleteDir(root);
        }
    }

    private sealed class FailingDecodeCodec : ICacheCodec<string>
    {
        public int SchemaVersion => 1;

        public bool TryEncode(in string payload, out byte[] bytes)
        {
            bytes = System.Text.Encoding.UTF8.GetBytes(payload);
            return true;
        }

        public bool TryDecode(ReadOnlySpan<byte> bytes, out string payload)
        {
            payload = string.Empty;
            return false;
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
