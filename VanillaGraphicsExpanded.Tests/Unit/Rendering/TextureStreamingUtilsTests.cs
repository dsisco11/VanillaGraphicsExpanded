using OpenTK.Graphics.OpenGL;

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
        Assert.Equal(12, TextureStreamingUtils.GetBytesPerPixel(PixelFormat.Rgb, PixelType.Float));
        Assert.Equal(8, TextureStreamingUtils.GetBytesPerPixel(PixelFormat.Rgba, PixelType.HalfFloat));
        Assert.Equal(4, TextureStreamingUtils.GetBytesPerPixel(PixelFormat.Rg, PixelType.UnsignedShort));
        Assert.Equal(1, TextureStreamingUtils.GetBytesPerPixel(PixelFormat.Red, PixelType.UnsignedByte));
    }

    [Fact]
    public void GetUploadDimension_ClassifiesTargets()
    {
        Assert.Equal(UploadDimension.Tex1D, TextureStreamingUtils.GetUploadDimension(TextureTarget.Texture1D));
        Assert.Equal(UploadDimension.Tex2D, TextureStreamingUtils.GetUploadDimension(TextureTarget.Texture2D));
        Assert.Equal(UploadDimension.Tex2D, TextureStreamingUtils.GetUploadDimension(TextureTarget.Texture1DArray));
        Assert.Equal(UploadDimension.Tex3D, TextureStreamingUtils.GetUploadDimension(TextureTarget.Texture2DArray));
        Assert.Equal(UploadDimension.Tex3D, TextureStreamingUtils.GetUploadDimension(TextureTarget.Texture3D));
    }

    [Fact]
    public void GetCubeFaceTarget_MatchesCubeTargets()
    {
        Assert.Equal(TextureTarget.TextureCubeMapPositiveX, TextureStreamingUtils.GetCubeFaceTarget(TextureCubeFace.PositiveX));
        Assert.Equal(TextureTarget.TextureCubeMapNegativeX, TextureStreamingUtils.GetCubeFaceTarget(TextureCubeFace.NegativeX));
        Assert.Equal(TextureTarget.TextureCubeMapPositiveY, TextureStreamingUtils.GetCubeFaceTarget(TextureCubeFace.PositiveY));
        Assert.Equal(TextureTarget.TextureCubeMapNegativeY, TextureStreamingUtils.GetCubeFaceTarget(TextureCubeFace.NegativeY));
        Assert.Equal(TextureTarget.TextureCubeMapPositiveZ, TextureStreamingUtils.GetCubeFaceTarget(TextureCubeFace.PositiveZ));
        Assert.Equal(TextureTarget.TextureCubeMapNegativeZ, TextureStreamingUtils.GetCubeFaceTarget(TextureCubeFace.NegativeZ));
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

