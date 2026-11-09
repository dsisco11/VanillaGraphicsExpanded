using System;
using System.IO;

using VanillaGraphicsExpanded.Cache;
using VanillaGraphicsExpanded.Cache.Disk;
using VanillaGraphicsExpanded.PBR.Materials;
using VanillaGraphicsExpanded.PBR.Materials.Cache;
using VanillaGraphicsExpanded.Tests.Support.Cache;

using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.Tests.Component.PBR.Materials;

/// <summary>Checks the composed base-color cache path preserves encoded channel bits across storage.</summary>
[Trait("Category", "Unit")]
public sealed class BaseColorCacheKeyAndCodecTests
{
    #region Public API
    /// <summary>The JSON cache store round-trips an RGB16F payload without changing channel bits.</summary>
    [Fact]
    public void JsonStore_RoundTrips_Rgb16fPayload_WithoutLoss()
    {
        // Arrange: create an isolated store root, codec, and exact half-float channel values.
        string root = CreateTempDir();
        try
        {
            using var store = new DiskJsonDictionaryCacheStore(root, "meta.json", storage);
            var cache = new DataCacheSystem<AtlasCacheKey, BaseColorRgb16f>(
                store,
                new BaseColorRgb16fJsonCodec(),
                keyToEntryId: BaseColorCacheKeyBuilder.ToEntryId,
                tryParseKey: BaseColorCacheKeyBuilder.TryParseEntryId);
            var inputs = BaseColorCacheKeyInputs.CreateDefaults();
            var builder = new BaseColorCacheKeyBuilder();
            AtlasCacheKey key = builder.BuildKey(inputs, new AssetLocation("game", "textures/block/test.png"), originPath: "a", assetBytes: 1);
            var payload = new BaseColorRgb16f(R: 0x3C00, G: 0x0000, B: 0x7BFF);

            // Act: persist and retrieve the encoded cache entry.
            cache.Store(key, payload);

            // Assert: the decoded channels preserve the original 16-bit values.
            Assert.True(cache.TryGet(key, out BaseColorRgb16f loaded));
            Assert.Equal(payload, loaded);
        }
        finally
        {
            TryDeleteDir(root);
        }
    }
    #endregion

    #region Private
    private readonly CountingDiskCacheFileSystem storage = new();

    /// <summary>Creates a unique cache directory through the controlled test filesystem.</summary>
    private string CreateTempDir()
    {
        string path = Path.Combine(Path.GetTempPath(), "vge-cachetests", Guid.NewGuid().ToString("N"));
        storage.CreateDirectory(path);
        return path;
    }

    /// <summary>Removes the isolated cache directory after the test has finished.</summary>
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
            // Best-effort cleanup allows a failed filesystem test to report its original assertion.
        }
    }
    #endregion
}
