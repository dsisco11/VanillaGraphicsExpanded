using System.Threading;

using VanillaGraphicsExpanded.PBR.Materials;

using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.Tests.Unit.PBR.Materials;

/// <summary>Checks the material atlas parameter builder's simple tile and override operations.</summary>
public sealed class MaterialAtlasParamsPipelineTests
{
    #region Public API
    /// <summary>A noise-free material fills every pixel with its authored RGB parameters.</summary>
    [Fact]
    public void MaterialAtlasParamsBuilder_BuildRgb16fTile_NoNoise_FillsExpectedTriplets()
    {
        // Arrange: use constant material parameters and zero noise for a small tile.
        var tex = new AssetLocation("game", "textures/block/dirt.png");

        var def = new PbrMaterialDefinition(
            Roughness: 0.25f,
            Metallic: 0.50f,
            Emissive: 0.00f,
            Noise: new PbrMaterialNoise(0f, 0f, 0f, 0f, 0f),
            Scale: PbrOverrideScale.Identity,
            Priority: 0,
            Notes: null);

        // Act: build the tile with cancellation disabled for this synchronous input.
        float[] rgb = MaterialAtlasParamsBuilder.BuildRgb16fTile(
            tex,
            def,
            rectWidth: 2,
            rectHeight: 2,
            CancellationToken.None);

        // Assert: the buffer contains one RGB triplet per pixel with the authored channels.
        Assert.Equal(2 * 2 * 3, rgb.Length);
        for (int i = 0; i < rgb.Length; i += 3)
        {
            Assert.Equal(0.25f, rgb[i + 0]);
            Assert.Equal(0.50f, rgb[i + 1]);
            Assert.Equal(0.00f, rgb[i + 2]);
        }
    }

    /// <summary>An override copies RGB data, applies per-channel scales, and clamps the result.</summary>
    [Fact]
    public void MaterialAtlasParamsBuilder_ApplyOverrideToTileRgb16f_CopiesRgbAndAppliesScaleWithClamp()
    {
        // Arrange: prepare two RGBA pixels and channel scales with roughness upper-clamping.
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

        // Act: apply the override to the two-pixel tile.
        MaterialAtlasParamsBuilder.ApplyOverrideToTileRgb16f(
            tileRgbTriplets: tile,
            rectWidth: 2,
            rectHeight: 1,
            overrideRgba01: rgba01,
            scale: scale);

        // Assert: output keeps RGB order, omits alpha, and clamps scaled channels.
        Assert.Equal(new[] { 0.4f, 0.15f, 0.4f, 1.0f, 0.3f, 0.7f }, tile);
    }
    #endregion
}
