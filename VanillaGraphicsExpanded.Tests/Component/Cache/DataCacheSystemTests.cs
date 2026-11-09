using VanillaGraphicsExpanded.Tests.Support.Cache;
using System;
using System.IO;

using VanillaGraphicsExpanded.Cache;
using VanillaGraphicsExpanded.Cache.Disk;
using VanillaGraphicsExpanded.Tests.Unit.Cache;

namespace VanillaGraphicsExpanded.Tests.Component.Cache;

/// <summary>Checks the cache system composed with a codec and dictionary store.</summary>
public sealed class DataCacheSystemTests
{
    #region Public API
    /// <summary>Stores and retrieves a payload through the configured codec and entry mapping.</summary>
    [Fact]
    public void Store_Then_TryGet_RoundTrips_Through_Codec_And_Store()
    {
        // Arrange: create an isolated store and a cache with a reversible integer entry mapping.
        string root = CreateTempDir();
        try
        {
            using var store = new DiskJsonDictionaryCacheStore(root, "meta.json", storage);
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

            // Act: store the encoded payload, then retrieve it through the same cache key.
            cache.Store(123, "\"ok\"");

            // Assert: the original payload survives both cache and codec boundaries.
            Assert.True(cache.TryGet(123, out string payload));
            Assert.Equal("\"ok\"", payload);
        }
        finally
        {
            TryDeleteDir(root);
        }
    }

    /// <summary>Treats a decode failure as a cache miss and records it in the statistics snapshot.</summary>
    [Fact]
    public void Codec_DecodeFailure_Is_Treated_As_Miss()
    {
        // Arrange: seed valid JSON bytes directly so only the test codec rejects decoding.
        string root = CreateTempDir();
        try
        {
            var store = new DiskJsonDictionaryCacheStore(root, "meta.json", storage);
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

            Assert.True(store.TryWriteAtomic("k1", "\"hello\""u8));

            // Act: retrieve the entry through a codec that always fails to decode.
            Assert.False(cache.TryGet(1, out _));

            // Assert: the failure is reported as both a miss and a decode failure.
            DataCacheStats stats = cache.GetStatsSnapshot();
            Assert.True(stats.Misses >= 1);
            Assert.True(stats.DecodeFailures >= 1);
        }
        finally
        {
            TryDeleteDir(root);
        }
    }
    #endregion

    #region Private
    private readonly CountingDiskCacheFileSystem storage = new();

    /// <summary>Encodes normally but rejects every decode request.</summary>
    private sealed class FailingDecodeCodec : ICacheCodec<string>
    {
        #region Public API
        /// <summary>Gets the test codec schema version.</summary>
        public int SchemaVersion => 1;

        /// <summary>Encodes the string payload as UTF-8.</summary>
        public bool TryEncode(in string payload, out byte[] bytes)
        {
            bytes = System.Text.Encoding.UTF8.GetBytes(payload);
            return true;
        }

        /// <summary>Always fails while providing an empty output value.</summary>
        public bool TryDecode(ReadOnlySpan<byte> bytes, out string payload)
        {
            payload = string.Empty;
            return false;
        }
        #endregion
    }

    /// <summary>Creates a unique cache directory through the controlled filesystem.</summary>
    private string CreateTempDir()
    {
        string path = Path.Combine(Path.GetTempPath(), "vge-cachetests", Guid.NewGuid().ToString("N"));
        storage.CreateDirectory(path);
        return path;
    }

    /// <summary>Removes the isolated cache directory on a best-effort basis.</summary>
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
