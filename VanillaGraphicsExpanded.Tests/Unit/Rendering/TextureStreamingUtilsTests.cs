using VanillaGraphicsExpanded.Rendering.RHI;

using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.Tests.Unit.Rendering;

/// <summary>Verifies texture upload sizing, dimension mapping, and bounded upload-data copying.</summary>
[Trait("Category", "Unit")]
public sealed class TextureStreamingUtilsTests
{
    #region Public API

    /// <summary>Already aligned values remain unchanged for power-of-two and arbitrary alignments.</summary>
    [Fact]
    public void AlignUp_ReturnsSame_WhenAlreadyAligned()
    {
        // Arrange, act, and assert
        Assert.Equal(256, TextureStreamingUtils.AlignUp(256, 256));
        Assert.Equal(15, TextureStreamingUtils.AlignUp(15, 5));
    }

    /// <summary>Unaligned values round upward to the next requested alignment.</summary>
    [Fact]
    public void AlignUp_AlignsUp_ForNonPowerOfTwo()
    {
        // Arrange, act, and assert
        Assert.Equal(15, TextureStreamingUtils.AlignUp(14, 5));
        Assert.Equal(16, TextureStreamingUtils.AlignUp(13, 4));
    }

    /// <summary>Channel layouts and component types produce their expected byte sizes.</summary>
    [Fact]
    public void GetBytesPerPixel_ComputesExpectedSizes()
    {
        // Arrange, act, and assert
        Assert.Equal(12, TextureStreamingUtils.GetBytesPerPixel(PixelChannelLayout.Rgb, PixelComponentType.Float));
        Assert.Equal(8, TextureStreamingUtils.GetBytesPerPixel(PixelChannelLayout.Rgba, PixelComponentType.Half));
        Assert.Equal(4, TextureStreamingUtils.GetBytesPerPixel(PixelChannelLayout.Rg, PixelComponentType.UnsignedShort));
        Assert.Equal(1, TextureStreamingUtils.GetBytesPerPixel(PixelChannelLayout.Red, PixelComponentType.UnsignedByte));
    }

    /// <summary>Texture dimensions map to the upload dimensionality used by the uploader.</summary>
    [Fact]
    public void GetUploadDimension_ClassifiesTargets()
    {
        // Arrange, act, and assert
        Assert.Equal(UploadDimension.Tex1D, TextureStreamingUtils.GetUploadDimension(TextureDimension.One));
        Assert.Equal(UploadDimension.Tex2D, TextureStreamingUtils.GetUploadDimension(TextureDimension.Two));
        Assert.Equal(UploadDimension.Tex2D, TextureStreamingUtils.GetUploadDimension(TextureDimension.OneArray));
        Assert.Equal(UploadDimension.Tex3D, TextureStreamingUtils.GetUploadDimension(TextureDimension.TwoArray));
        Assert.Equal(UploadDimension.Tex3D, TextureStreamingUtils.GetUploadDimension(TextureDimension.Three));
    }

    /// <summary>Copying upload data into a smaller destination copies only its available prefix.</summary>
    [Fact]
    public void TextureUploadData_CopyTo_AllowsPartialCopy()
    {
        // Arrange
        TextureUploadData data = TextureUploadData.From(new byte[] { 1, 2, 3, 4 });
        var dst = new byte[2];
        // Act
        data.CopyTo(dst);
        // Assert
        Assert.Equal(new byte[] { 1, 2 }, dst);
    }

    #endregion
}

