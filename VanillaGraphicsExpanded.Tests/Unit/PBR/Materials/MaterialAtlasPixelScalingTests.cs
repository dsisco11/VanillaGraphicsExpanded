using VanillaGraphicsExpanded.PBR.Materials;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.Unit.PBR.Materials;
/// <summary>Verifies material atlas pixel scaling.</summary>
public sealed class MaterialAtlasPixelScalingTests
{
    #region Public API
    /// <summary>Material channel scales affect the values written into mapped atlas pixels.</summary>
    [Fact]
    public void PixelBuilder_AppliesScaleToMappedRects()
    {
        // Arrange: author a scaled material over a precisely mapped atlas rectangle.
        const int atlasTexId = 1;
        const int width = 8;
        const int height = 8;

        var texPos = new TextureAtlasPosition
        {
            atlasTextureId = atlasTexId,
            x1 = 2f / width,
            x2 = 4f / width,
            y1 = 1f / height,
            y2 = 3f / height
        };

        var texture = new AssetLocation("game", "textures/block/test.png");

        var materialsByTexture = new Dictionary<AssetLocation, PbrMaterialDefinition>
        {
            [texture] = new PbrMaterialDefinition(
                Roughness: 0.20f,
                Metallic: 0.30f,
                Emissive: 0.40f,
                Noise: default,
                Scale: new PbrOverrideScale(
                    Roughness: 0.5f,
                    Metallic: 2.0f,
                    Emissive: 1.0f,
                    Normal: 1.0f,
                    Depth: 1.0f),
                Priority: 0,
                Notes: null)
        };

        // Act: build the pixel buffers after applying the material scale.
        var result = MaterialAtlasParamsBuilder.BuildRgb16fPixelBuffers(
            atlasPages: new[] { (atlasTexId, width, height) },
            texturePositions: new Dictionary<AssetLocation, TextureAtlasPosition> { [texture] = texPos },
            materialsByTexture: materialsByTexture);

        // Assert: mapped pixels contain the scaled channel values.
        Assert.True(result.PixelBuffersByAtlasTexId.TryGetValue(atlasTexId, out var pixels));

        // Mapped rect should be scaled and clamped.
        // roughness: 0.2 * 0.5 = 0.1
        // metallic: 0.3 * 2.0 = 0.6
        // emissive: 0.4 * 1.0 = 0.4
        AssertPixelRgb(pixels, width, x: 2, y: 1, r: 0.10f, g: 0.60f, b: 0.40f);
    }
    /// <summary>Checks the expected RGB triple at an atlas pixel.</summary>
    #endregion

    #region Private
    private static void AssertPixelRgb(float[] pixels, int width, int x, int y, float r, float g, float b)
    {
        int idx = (y * width + x) * 3;
        Assert.Equal(r, pixels[idx + 0], 2);
        Assert.Equal(g, pixels[idx + 1], 2);
        Assert.Equal(b, pixels[idx + 2], 2);
    }
    #endregion
}
