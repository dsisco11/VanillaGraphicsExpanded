using VanillaGraphicsExpanded.PBR.Materials;

namespace VanillaGraphicsExpanded.Tests.Unit.PBR.Materials;

/// <summary>Verifies packed material transmission without changing cached color data.</summary>
[Collection("PbrMaterialRegistry")]
public sealed class MaterialTransmissionTests
{
    #region Public API
    /// <summary>RGBA packing clamps the current transmission and leaves the RGB cache untouched.</summary>
    [Theory]
    [InlineData(0.35f, 0.35f)]
    [InlineData(0f, 0f)]
    [InlineData(-1f, 0f)]
    [InlineData(2f, 1f)]
    [InlineData(float.NaN, 0f)]
    public void PacksCurrentTransmissionWithoutMutatingCachedRgb(float input, float expected)
    {
        // Arrange: keep the cached RGB triplets visible and provide one current transmission value.
        float[] rgb = [0.2f, 0.3f, 0.4f, 0.5f, 0.6f, 0.7f];

        // Act: pack the current alpha alongside the cached RGB values.
        float[] packed = MaterialTransmission.Pack(rgb, input);

        // Assert: both pixels receive bounded alpha and the source array remains unchanged.
        Assert.Equal(new float[] { .2f, .3f, .4f, expected, .5f, .6f, .7f, expected }, packed);
        Assert.Equal(new float[] { .2f, .3f, .4f, .5f, .6f, .7f }, rgb);
    }
    #endregion
}
