using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.PBR.Materials;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks source-bound admission against the actual owned binary16 material texture.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class PhotometricMaterialAtlasTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>Warm disk-cached terrain uploads retain zero emission and authored weak sources in the actual GPU atlas.</summary>
    [Fact]
    public void CachedMaterialPublicationRetainsZeroAndWeakEmission()
    {
        EnsureContextValid();
        var fs = new VanillaGraphicsExpanded.Tests.TestSupport.InMemoryMaterialAtlasFileSystem();
        string root = $"memcache/{Guid.NewGuid():N}";
        var cache = new VanillaGraphicsExpanded.PBR.Materials.Cache.MaterialAtlasDiskCache(root, 64L * 1024 * 1024, fs);
        var key = AtlasCacheKey.FromUtf8(1, "cached-gpu-zero-positive");
        float[] source = Enumerable.Range(0, 64).SelectMany(pixel => new[]
            { pixel / 63f, 0f, (pixel % 4) switch { 0 => 0f, 1 => 1f / 255f, 2 => .25f, _ => 1f } }).ToArray();
        cache.StoreMaterialParamsTile(key, 8, 8, source);
        var reopened = new VanillaGraphicsExpanded.PBR.Materials.Cache.MaterialAtlasDiskCache(root, 64L * 1024 * 1024, fs);
        Assert.True(reopened.TryLoadMaterialParamsTile(key, out var loaded));
        using var store = new MaterialAtlasTextureStore();
        store.SyncToAtlasPages([(7, 8, 8)], false);
        Assert.True(store.TryUploadMaterialParamsTile(7, new(0, 0, 8, 8), loaded, 0));
        for (int tick = 0; tick < 8 && TextureStreamingSystem.GetDiagnosticsSnapshot().Pending > 0; tick++)
            TextureStreamingSystem.TickOnRenderThread();
        Assert.Equal(0, TextureStreamingSystem.GetDiagnosticsSnapshot().Pending);
        Assert.True(store.TryGetPageTextures(7, out var page));
        float[] actual = page.MaterialParamsTexture.ReadPixels();
        for (int pixel = 0; pixel < 64; pixel++)
        {
            Assert.Equal(0, actual[pixel * 4 + 1]);
            float expected = (float)(Half)loaded[pixel * 3 + 2];
            Assert.Equal(expected, actual[pixel * 4 + 2]);
        }
        Assert.Equal(LightingCalibration.EmissionReferenceNits, store.MaximumEmissionNits);
    }

    /// <summary>Uploaded rounding is included in the bound, lower replacements retain history, and retirement releases it.</summary>
    [Theory]
    [InlineData(1.0006f, 1.0009765625f)]
    [InlineData(1.0002f, 1.0009765625f)]
    [InlineData(1e-9f, 5.960464477539063e-8f)]
    public void MaterialEmissionBoundTracksActualHalfAcrossAtlasLifetime(float coefficient, float upperHalf)
    {
        EnsureContextValid();
        using var store = new MaterialAtlasTextureStore();
        store.SyncToAtlasPages([(7, 1, 1)], false);
        Assert.True(store.TryUploadMaterialParamsTile(7, new(0, 0, 1, 1), [.5f, 0, coefficient], .25f));
        // A cold streaming backend retains the initial staged fallback until a later tick.
        for (int tick = 0; tick < 8 && TextureStreamingSystem.GetDiagnosticsSnapshot().Pending > 0; tick++)
            TextureStreamingSystem.TickOnRenderThread();
        Assert.Equal(0, TextureStreamingSystem.GetDiagnosticsSnapshot().Pending);
        Assert.True(store.TryGetPageTextures(7, out var page));
        float stored = page.MaterialParamsTexture.ReadPixels()[2];
        Assert.InRange(stored, 0, upperHalf);
        float maximum = upperHalf * LightingCalibration.EmissionReferenceNits;
        Assert.Equal(maximum, store.MaximumEmissionNits);
        Assert.True(maximum >= coefficient * LightingCalibration.EmissionReferenceNits);
        store.ReplaceMaterialParamsTextureWithData(7, 1, 1, [.5f, 0, 0]);
        Assert.True(store.TryGetPageTextures(7, out page));
        Assert.Equal(0, page.MaterialParamsTexture.ReadPixels()[2]);
        Assert.Equal(maximum, store.MaximumEmissionNits);
        store.SyncToAtlasPages([], false);
        Assert.Equal(0, store.MaximumEmissionNits);
        Assert.Equal(0, store.PageCount);
        store.SyncToAtlasPages([(7, 1, 1)], false);
        Assert.Equal(0, store.MaximumEmissionNits);
    }

    /// <summary>Unsupported parameters reject both tile and whole-page writes before texture identity, pixels or source bounds change.</summary>
    [Theory]
    [InlineData(false, 0, float.NaN)] [InlineData(true, 0, float.NaN)]
    [InlineData(false, 1, 65520f)] [InlineData(true, 1, 65520f)]
    [InlineData(false, 2, float.NaN)] [InlineData(true, 2, float.NaN)]
    [InlineData(false, 2, 65520f)] [InlineData(true, 2, 65520f)]
    public void RejectedMaterialUpdateRetainsCompletePublication(bool replacement, int component, float invalid)
    {
        EnsureContextValid();
        using var store = new MaterialAtlasTextureStore();
        store.SyncToAtlasPages([(7, 1, 1)], false);
        store.ReplaceMaterialParamsTextureWithData(7, 1, 1, [.5f, .25f, 2]);
        Assert.True(store.TryGetPageTextures(7, out var page));
        int textureId = page.MaterialParamsTexture.TextureId;
        float[] pixels = page.MaterialParamsTexture.ReadPixels();
        float maximum = store.MaximumEmissionNits;
        float[] candidate = [.75f, .5f, 3];
        candidate[component] = invalid;
        if (replacement)
            Assert.Throws<ArgumentOutOfRangeException>(() => store.ReplaceMaterialParamsTextureWithData(7, 1, 1, candidate));
        else
            Assert.Throws<ArgumentOutOfRangeException>(() => store.TryUploadMaterialParamsTile(7, new(0, 0, 1, 1), candidate, .5f));
        TextureStreamingSystem.TickOnRenderThread();
        Assert.True(store.TryGetPageTextures(7, out page));
        Assert.Equal(textureId, page.MaterialParamsTexture.TextureId);
        Assert.Equal(pixels, page.MaterialParamsTexture.ReadPixels());
        Assert.Equal(maximum, store.MaximumEmissionNits);
    }
    #endregion
}
