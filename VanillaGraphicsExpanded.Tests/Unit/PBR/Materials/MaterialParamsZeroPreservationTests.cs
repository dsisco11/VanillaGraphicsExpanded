using VanillaGraphicsExpanded.PBR.Materials;
using VanillaGraphicsExpanded.PBR.Materials.Cache;
using VanillaGraphicsExpanded.Tests.TestSupport;

namespace VanillaGraphicsExpanded.Tests.Unit.PBR.Materials;

/// <summary>Checks that disk caching cannot create metalness or emission on ordinary materials.</summary>
public sealed class MaterialParamsZeroPreservationTests
{
    #region Public API
    /// <summary>All quantized roughness levels preserve exactly zero nonmetallic and nonemissive channels.</summary>
    [Fact]
    public void MaterialCachePreservesZeroMetalnessAndEmissionAcrossRoughness()
    {
        var fs = new InMemoryMaterialAtlasFileSystem();
        var cache = new MaterialAtlasDiskCache($"memcache/{Guid.NewGuid():N}", 64L * 1024 * 1024, fs);
        var errors = new List<string>();
        // Constant tiles isolate shared channel endpoint quantization from texture filtering.
        for (int roughness = 0; roughness <= 255; roughness++)
        {
            float[] source = new float[4 * 4 * 3];
            for (int pixel = 0; pixel < 16; pixel++) source[pixel * 3] = roughness / 255f;
            var key = AtlasCacheKey.FromUtf8(1, $"zero-channels/{roughness}");
            cache.StoreMaterialParamsTile(key, 4, 4, source);
            Assert.True(cache.TryLoadMaterialParamsTile(key, out var loaded));
            for (int pixel = 0; pixel < 16; pixel++)
                if (loaded[pixel * 3 + 1] != 0 || loaded[pixel * 3 + 2] != 0)
                    errors.Add($"roughness={roughness}/255 pixel={pixel} metalness={loaded[pixel * 3 + 1]:R} emission={loaded[pixel * 3 + 2]:R}");
        }
        var artifact = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../artifacts/MaterialEmission"));
        Directory.CreateDirectory(artifact);
        File.WriteAllLines(Path.Combine(artifact, "zero-channel-roundtrip.txt"), errors);
        Assert.True(errors.Count == 0, $"Cache created nonzero material channels in {errors.Count} texels. First: {errors.FirstOrDefault()}");
    }

    /// <summary>A mixed tile preserves authored zeros and weak positive emission through a cold cache instance.</summary>
    [Fact]
    public void MixedMaterialTilePreservesExactQuantizedChannels()
    {
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
        Assert.True(reopened.TryLoadMaterialParamsTile(key, out var loaded));
        for (int channel = 0; channel < source.Length; channel++)
            Assert.InRange(Math.Abs(loaded[channel] - MathF.Floor(source[channel] * 255f + .5f) / 255f), 0, 1e-7f);
    }

    /// <summary>Old lossy material payloads are rejected and replaced instead of returning invented emission.</summary>
    [Fact]
    public void LegacyBc7PayloadIsAMissAndCanBeRebuilt()
    {
        var fs = new InMemoryMaterialAtlasFileSystem();
        string root = $"memcache/{Guid.NewGuid():N}";
        var key = AtlasCacheKey.FromUtf8(1, "legacy-material");
        string entryId = $"v{key.SchemaVersion}-{key.Hash64:x16}.material";
        using var stream = new MemoryStream();
        DdsBc7Rgba8Codec.WriteBc7Dds(stream, 4, 4, Enumerable.Range(0, 16).SelectMany(_ => new byte[] {128, 0, 0, 255}).ToArray());
        byte[] bytes = stream.ToArray();
        fs.WriteAllBytes($"{root}/{entryId}.dds", bytes);
        var index = MaterialAtlasDiskCacheIndex.CreateEmpty(DateTime.UtcNow.Ticks);
        index.Entries[entryId] = new("materialParams", key.SchemaVersion, key.Hash64, 4, 4, 3,
            DateTime.UtcNow.Ticks, DateTime.UtcNow.Ticks, bytes.Length, null, null, null);
        index.RecomputeTotals();
        index.SetSavedNow(DateTime.UtcNow.Ticks);
        index.SaveAtomic(fs, $"{root}/meta.json");
        var cache = new MaterialAtlasDiskCache(root, 64L * 1024 * 1024, fs);
        Assert.False(cache.TryLoadMaterialParamsTile(key, out _));
        float[] source = Enumerable.Range(0, 16).SelectMany(_ => new[] {.5f, 0f, 0f}).ToArray();
        cache.StoreMaterialParamsTile(key, 4, 4, source);
        Assert.True(cache.TryLoadMaterialParamsTile(key, out var loaded));
        for (int pixel = 0; pixel < 16; pixel++)
        {
            Assert.Equal(0, loaded[pixel * 3 + 1]);
            Assert.Equal(0, loaded[pixel * 3 + 2]);
        }
    }
    #endregion
}
