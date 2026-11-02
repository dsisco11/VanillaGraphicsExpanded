using OpenTK.Graphics.OpenGL4;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Tests.GPU.Assertions;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Separates synchronous shared-storage interpretation from retained temporal history.</summary>
public sealed partial class GpuImageAssertionTests
{
    #region Public API
    /// <summary>Immediate observations preserve raw stored channels despite hostile source sampling state.</summary>
    [Theory]
    [InlineData(TextureDimension.Two)] [InlineData(TextureDimension.TwoArray)]
    public void ImmediateObservationSharesStorageWithoutSourceSelectorMutation(TextureDimension dimension)
    {
        EnsureContextValid();
        using var image = Create(dimension, TextureFormat.Rgba32f, 2, 1);
        Upload(image, [2, 3, 4, 5, 2, 3, 4, 5], PixelComponentType.Float);
        GL.TextureParameter(image.TextureId, TextureParameterName.TextureSwizzleR, 0);
        using var history = new GpuImageSnapshot(image);
        using (var input = new GpuObservationImage(image))
        {
            Assert.False(input.CopiesStorage);
            Assert.NotEqual(image.TextureId, input.Texture.TextureId);
            ImageAssertions.Comparisons.Compare(image, [2, 3, 4, 5, 2, 3, 4, 5]).AssertEqual();
            // A shared interpretation sees the original allocation's later content, while explicitly
            // retained history remains independent. No checker execution overlaps this mutation.
            Upload(image, [7, 3, 4, 5, 7, 3, 4, 5], PixelComponentType.Float);
            Assert.Equal(7f, GpuLocalRead.NativeColorTuple(input.Texture, 0, 0)[0]);
            Assert.True(ImageAssertions.Comparisons.CompareHistory(image, history).Mismatches > 0);
        }
        Assert.True(image.IsValid);
        GL.GetTextureParameter(image.TextureId, GetTextureParameter.TextureSwizzleR, out int swizzle);
        Assert.Equal(0, swizzle);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Spatial crops retain their exact conversion, while full selected mips/layers need no image copy.</summary>
    [Fact]
    public void ObservationInterpretationsRetainSelectedDomainsAndRejectInvalidRanges()
    {
        EnsureContextValid();
        using var image = Create(TextureDimension.TwoArray, TextureFormat.R32f, 4, 4, 3, 2);
        Upload(image, [5, 5, 5, 5], PixelComponentType.Float, layer: 1, mip: 1, width: 2, height: 2);
        Upload(image, [6, 6, 6, 6], PixelComponentType.Float, layer: 2, mip: 1, width: 2, height: 2);
        using (var mip = new GpuObservationImage(image, new(new(1, 1), 0, 0, 0, 2, 2, 2)))
        {
            Assert.False(mip.CopiesStorage);
            Assert.Equal((2, 2, 2), (mip.Texture.Width, mip.Texture.Height, mip.Texture.Depth));
            Assert.Equal(5f, GpuLocalRead.NativeColorTuple(mip.Texture, 0, 0, 0)[0]);
            Assert.Equal(6f, GpuLocalRead.NativeColorTuple(mip.Texture, 1, 1, 1)[0]);
        }
        using (var crop = new GpuObservationImage(image, new(new(0, 1), 1, 1, 0, 2, 2)))
        {
            Assert.True(crop.CopiesStorage);
            Assert.Equal((2, 2, 1), (crop.Texture.Width, crop.Texture.Height, crop.Texture.Depth));
        }
        Assert.Throws<ArgumentOutOfRangeException>(() => new GpuObservationImage(image, new(new(0, -1), 0, 0, 0, 4, 4)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GpuObservationImage(image, new(new(2), 0, 0, 0, 1, 1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GpuObservationImage(image, new(new(0), 0, 0, 0, 4, 4, 4)));
    }
    #endregion
}
