using VanillaGraphicsExpanded.PBR.Materials;
using VanillaGraphicsExpanded.PBR.Materials.Cache;
using VanillaGraphicsExpanded.Tests.Support.Cache;

namespace VanillaGraphicsExpanded.Tests.Component.PBR.Materials;

/// <summary>Verifies material-channel preservation through the atlas disk cache.</summary>
public sealed class MaterialParamsZeroPreservationTests
{
    #region Public API
    /// <summary>Every roughness quantization level preserves zero metalness and emission through cache storage.</summary>
    [Fact]
    public void MaterialCachePreservesZeroMetalnessAndEmissionAcrossRoughness()
    {
        // Arrange: create unique in-memory storage and collect any channel violations across all roughness values.
        var fs = new InMemoryMaterialAtlasFileSystem();
        var cache = new MaterialAtlasDiskCache($"memcache/{Guid.NewGuid():N}", 64L * 1024 * 1024, fs);
        var errors = new List<string>();

        // Act: store and reload each constant-color roughness tile.
        for (int roughness = 0; roughness <= 255; roughness++)
        {
            float[] source = new float[4 * 4 * 3];
            for (int pixel = 0; pixel < 16; pixel++) source[pixel * 3] = roughness / 255f;
            var key = AtlasCacheKey.FromUtf8(1, $"zero-channels/{roughness}");
            cache.StoreMaterialParamsTile(key, 4, 4, source);
            Assert.True(cache.TryLoadMaterialParamsTile(key, out var loaded));
            for (int pixel = 0; pixel < 16; pixel++)
            {
                if (loaded[pixel * 3 + 1] != 0 || loaded[pixel * 3 + 2] != 0)
                    errors.Add($"roughness={roughness}/255 pixel={pixel} metalness={loaded[pixel * 3 + 1]:R} emission={loaded[pixel * 3 + 2]:R}");
            }
        }

        // Assert: cache encoding does not create nonzero material channels.
        Assert.True(errors.Count == 0, $"Cache created nonzero material channels in {errors.Count} texels. First: {errors.FirstOrDefault()}");
    }

    /// <summary>A mixed tile preserves exact quantized channels across a newly opened cache instance.</summary>
    [Fact]
    public void MixedMaterialTilePreservesExactQuantizedChannels()
    {
        // Arrange: prepare channel values including zero and one-step emission levels.
        var fs = new InMemoryMaterialAtlasFileSystem();
        string root = $"memcache/{Guid.NewGuid():N}";
        var cache = new MaterialAtlasDiskCache(root, 64L * 1024 * 1024, fs);
        var key = AtlasCacheKey.FromUtf8(1, "mixed-material");
        float[] source = Enumerable.Range(0, 16).SelectMany(pixel => new[]
        {
            (pixel * 17) / 255f,
            pixel % 4 == 3 ? 1f : 0f,
            (pixel % 4) switch { 0 => 0f, 1 => 1f / 255f, 2 => .25f, _ => 1f }
        }).ToArray();
        cache.StoreMaterialParamsTile(key, 4, 4, source);
        var reopened = new MaterialAtlasDiskCache(root, 64L * 1024 * 1024, fs);

        // Act: reload the tile through a distinct cache instance.
        Assert.True(reopened.TryLoadMaterialParamsTile(key, out var loaded));

        // Assert: every channel matches the codec's expected nearest-byte quantization.
        for (int channel = 0; channel < source.Length; channel++)
            Assert.InRange(Math.Abs(loaded[channel] - MathF.Floor(source[channel] * 255f + .5f) / 255f), 0, 1e-7f);
    }

    /// <summary>Lossy legacy BC7 payloads miss the cache and can be replaced with current channel-preserving data.</summary>
    [Fact]
    public void LegacyBc7PayloadIsAMissAndCanBeRebuilt()
    {
        // Arrange: write a legacy payload and matching index entry for the requested material tile.
        var fs = new InMemoryMaterialAtlasFileSystem();
        string root = $"memcache/{Guid.NewGuid():N}";
        var key = AtlasCacheKey.FromUtf8(1, "legacy-material");
        string entryId = $"v{key.SchemaVersion}-{key.Hash64:x16}.material";
        using var stream = new MemoryStream();
        DdsBc7Rgba8Codec.WriteBc7Dds(stream, 4, 4,
            Enumerable.Range(0, 16).SelectMany(_ => new byte[] { 128, 0, 0, 255 }).ToArray());
        byte[] bytes = stream.ToArray();
        fs.WriteAllBytes($"{root}/{entryId}.dds", bytes);
        var index = MaterialAtlasDiskCacheIndex.CreateEmpty(DateTime.UtcNow.Ticks);
        index.Entries[entryId] = new("materialParams", key.SchemaVersion, key.Hash64, 4, 4, 3,
            DateTime.UtcNow.Ticks, DateTime.UtcNow.Ticks, bytes.Length, null, null, null);
        index.RecomputeTotals();
        index.SetSavedNow(DateTime.UtcNow.Ticks);
        index.SaveAtomic(fs, $"{root}/meta.json");
        var cache = new MaterialAtlasDiskCache(root, 64L * 1024 * 1024, fs);

        // Act: verify the legacy entry misses, then store and reload a replacement tile.
        Assert.False(cache.TryLoadMaterialParamsTile(key, out _));
        float[] source = Enumerable.Range(0, 16).SelectMany(_ => new[] { .5f, 0f, 0f }).ToArray();
        cache.StoreMaterialParamsTile(key, 4, 4, source);
        Assert.True(cache.TryLoadMaterialParamsTile(key, out var loaded));

        // Assert: rebuilt data retains exactly zero metalness and emission.
        for (int pixel = 0; pixel < 16; pixel++)
        {
            Assert.Equal(0, loaded[pixel * 3 + 1]);
            Assert.Equal(0, loaded[pixel * 3 + 2]);
        }
    }
    #endregion
}

