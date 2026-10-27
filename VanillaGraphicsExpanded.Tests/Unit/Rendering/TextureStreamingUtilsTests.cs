using VanillaGraphicsExpanded.Rendering.RHI;

using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.Tests.Unit.Rendering;

[Trait("Category", "Unit")]
public sealed class TextureStreamingUtilsTests
{
    [Fact]
    public void AlignUp_ReturnsSame_WhenAlreadyAligned()
    {
        Assert.Equal(256, TextureStreamingUtils.AlignUp(256, 256));
        Assert.Equal(15, TextureStreamingUtils.AlignUp(15, 5));
    }

    [Fact]
    public void AlignUp_AlignsUp_ForNonPowerOfTwo()
    {
        Assert.Equal(15, TextureStreamingUtils.AlignUp(14, 5));
        Assert.Equal(16, TextureStreamingUtils.AlignUp(13, 4));
    }

    [Fact]
    public void GetBytesPerPixel_ComputesExpectedSizes()
    {
        Assert.Equal(12, TextureStreamingUtils.GetBytesPerPixel(PixelChannelLayout.Rgb, PixelComponentType.Float));
        Assert.Equal(8, TextureStreamingUtils.GetBytesPerPixel(PixelChannelLayout.Rgba, PixelComponentType.Half));
        Assert.Equal(4, TextureStreamingUtils.GetBytesPerPixel(PixelChannelLayout.Rg, PixelComponentType.UnsignedShort));
        Assert.Equal(1, TextureStreamingUtils.GetBytesPerPixel(PixelChannelLayout.Red, PixelComponentType.UnsignedByte));
    }

    [Fact]
    public void GetUploadDimension_ClassifiesTargets()
    {
        Assert.Equal(UploadDimension.Tex1D, TextureStreamingUtils.GetUploadDimension(TextureDimension.One));
        Assert.Equal(UploadDimension.Tex2D, TextureStreamingUtils.GetUploadDimension(TextureDimension.Two));
        Assert.Equal(UploadDimension.Tex2D, TextureStreamingUtils.GetUploadDimension(TextureDimension.OneArray));
        Assert.Equal(UploadDimension.Tex3D, TextureStreamingUtils.GetUploadDimension(TextureDimension.TwoArray));
        Assert.Equal(UploadDimension.Tex3D, TextureStreamingUtils.GetUploadDimension(TextureDimension.Three));
    }

    [Fact]
    public void TextureUploadData_CopyTo_AllowsPartialCopy()
    {
        TextureUploadData data = TextureUploadData.From(new byte[] { 1, 2, 3, 4 });
        var dst = new byte[2];
        data.CopyTo(dst);
        Assert.Equal(new byte[] { 1, 2 }, dst);
    }
}

