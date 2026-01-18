using System.Threading;

using VanillaGraphicsExpanded.PBR.Materials;

using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.Tests.Unit.PBR.Materials;

public sealed class MaterialAtlasParamsPipelineTests
{
    [Fact]
    public void MaterialAtlasParamsBuilder_BuildRgb16fTile_NoNoise_FillsExpectedTriplets()
    {
        var tex = new AssetLocation("game", "textures/block/dirt.png");

        var def = new PbrMaterialDefinition(
            Roughness: 0.25f,
            Metallic: 0.50f,
            Emissive: 0.00f,
            Noise: new PbrMaterialNoise(0f, 0f, 0f, 0f, 0f),
            Scale: PbrOverrideScale.Identity,
            Priority: 0,
            Notes: null);

        float[] rgb = MaterialAtlasParamsBuilder.BuildRgb16fTile(
            tex,
            def,
            rectWidth: 2,
            rectHeight: 2,
            CancellationToken.None);

        Assert.Equal(2 * 2 * 3, rgb.Length);
        for (int i = 0; i < rgb.Length; i += 3)
        {
            Assert.Equal(0.25f, rgb[i + 0]);
            Assert.Equal(0.50f, rgb[i + 1]);
            Assert.Equal(0.00f, rgb[i + 2]);
        }
    }

    [Fact]
    public void MaterialAtlasParamsBuilder_ApplyOverrideToTileRgb16f_CopiesRgbAndAppliesScaleWithClamp()
    {
        float[] tile = new float[2 * 1 * 3];

        float[] rgba01 =
        [
            0.2f, 0.3f, 0.4f, 1.0f,
            0.5f, 0.6f, 0.7f, 1.0f
        ];

        var scale = new PbrOverrideScale(
            Roughness: 2.0f,
            Metallic: 0.5f,
            Emissive: 1.0f,
            Normal: 1.0f,
            Depth: 1.0f);

        MaterialAtlasParamsBuilder.ApplyOverrideToTileRgb16f(
            tileRgbTriplets: tile,
            rectWidth: 2,
            rectHeight: 1,
            overrideRgba01: rgba01,
            scale: scale);

        Assert.Equal(new[] { 0.4f, 0.15f, 0.4f, 1.0f, 0.3f, 0.7f }, tile);
    }
}
