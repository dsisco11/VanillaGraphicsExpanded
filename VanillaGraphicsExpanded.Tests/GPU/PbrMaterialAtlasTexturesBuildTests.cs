using VanillaGraphicsExpanded.PBR.Materials;

using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.Tests;

public sealed class PbrMaterialAtlasTexturesBuildTests
{
    [Fact]
    public void PixelBuilder_FillsDefaultAndMappedRectsCorrectly()
    {
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
                Priority: 0,
                Notes: null)
        };

        var result = PbrMaterialParamsPixelBuilder.BuildRgb16fPixelBuffers(
            atlasPages: new[] { (atlasTexId, width, height) },
            texturePositions: new Dictionary<AssetLocation, TextureAtlasPosition> { [texture] = texPos },
            materialsByTexture: materialsByTexture);

        Assert.True(result.PixelBuffersByAtlasTexId.TryGetValue(atlasTexId, out var pixels));
        Assert.Equal((width, height), result.SizesByAtlasTexId[atlasTexId]);
        Assert.Equal(width * height * 3, pixels.Length);

        // Defaults (unmapped)
        AssertPixelRgb(pixels, width, x: 0, y: 0,
            r: PbrMaterialParamsPixelBuilder.DefaultRoughness,
            g: PbrMaterialParamsPixelBuilder.DefaultMetallic,
            b: PbrMaterialParamsPixelBuilder.DefaultEmissive);

        // Mapped rect
        AssertPixelRgb(pixels, width, x: 2, y: 1, r: 0.20f, g: 0.30f, b: 0.40f);
        AssertPixelRgb(pixels, width, x: 3, y: 2, r: 0.20f, g: 0.30f, b: 0.40f);

        // Neighbor outside rect remains default
        AssertPixelRgb(pixels, width, x: 4, y: 1,
            r: PbrMaterialParamsPixelBuilder.DefaultRoughness,
            g: PbrMaterialParamsPixelBuilder.DefaultMetallic,
            b: PbrMaterialParamsPixelBuilder.DefaultEmissive);
    }

    private static void AssertPixelRgb(float[] pixels, int width, int x, int y, float r, float g, float b)
    {
        int idx = (y * width + x) * 3;
        Assert.Equal(r, pixels[idx + 0], 2);
        Assert.Equal(g, pixels[idx + 1], 2);
        Assert.Equal(b, pixels[idx + 2], 2);
    }
}
