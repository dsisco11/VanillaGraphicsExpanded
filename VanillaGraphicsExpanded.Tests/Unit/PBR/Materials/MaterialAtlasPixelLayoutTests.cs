using VanillaGraphicsExpanded.PBR.Materials;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.Unit.PBR.Materials;
/// <summary>Verifies material atlas pixel layout and default coverage.</summary>
public sealed class MaterialAtlasPixelLayoutTests
{
    #region Public API
    /// <summary>Defaults and mapped materials occupy their expected atlas rectangles.</summary>
    [Fact]
    public void PixelBuilder_FillsDefaultAndMappedRectsCorrectly()
    {
        // Arrange: author a material over a precisely mapped atlas rectangle.
        const int atlasTexId = 1;
        const int width = 8;
        const int height = 8;

        // Chosen so floor/ceil math yields an exact 2x2 rect at pixels x=[2..3], y=[1..2]
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
                Scale: PbrOverrideScale.Identity,
                Priority: 0,
                Notes: null)
        };

        // Act: build the pixel buffers for the atlas page.
        var result = MaterialAtlasParamsBuilder.BuildRgb16fPixelBuffers(
            atlasPages: new[] { (atlasTexId, width, height) },
            texturePositions: new Dictionary<AssetLocation, TextureAtlasPosition> { [texture] = texPos },
            materialsByTexture: materialsByTexture);

        // Assert: defaults remain outside the mapped rectangle and authored values fill it.
        Assert.True(result.PixelBuffersByAtlasTexId.TryGetValue(atlasTexId, out var pixels));
        Assert.Equal((width, height), result.SizesByAtlasTexId[atlasTexId]);
        Assert.Equal(width * height * 3, pixels.Length);

        // Defaults (unmapped)
        AssertPixelRgb(pixels, width, x: 0, y: 0,
            r: MaterialAtlasParamsBuilder.DefaultRoughness,
            g: MaterialAtlasParamsBuilder.DefaultMetallic,
            b: MaterialAtlasParamsBuilder.DefaultEmissive);

        // Mapped rect
        AssertPixelRgb(pixels, width, x: 2, y: 1, r: 0.20f, g: 0.30f, b: 0.40f);
        AssertPixelRgb(pixels, width, x: 3, y: 2, r: 0.20f, g: 0.30f, b: 0.40f);

        // Neighbor outside rect remains default
        AssertPixelRgb(pixels, width, x: 4, y: 1,
            r: MaterialAtlasParamsBuilder.DefaultRoughness,
            g: MaterialAtlasParamsBuilder.DefaultMetallic,
            b: MaterialAtlasParamsBuilder.DefaultEmissive);
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
