using OpenTK.Graphics.OpenGL;

using VanillaGraphicsExpanded.Rendering;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.Unit.Rendering;

public sealed class TextureFormatHelperTests
{
    [Theory]
    [InlineData(PixelInternalFormat.R32ui, PixelFormat.RedInteger, PixelType.UnsignedInt)]
    [InlineData(PixelInternalFormat.Rg32ui, PixelFormat.RgInteger, PixelType.UnsignedInt)]
    [InlineData(PixelInternalFormat.Rgba32ui, PixelFormat.RgbaInteger, PixelType.UnsignedInt)]
    public void IntegerFormats_MapToIntegerPixelFormatAndUnsignedType(
        PixelInternalFormat internalFormat,
        PixelFormat expectedFormat,
        PixelType expectedType)
    {
        Assert.Equal(expectedFormat, TextureFormatHelper.GetPixelFormat(internalFormat));
        Assert.Equal(expectedType, TextureFormatHelper.GetPixelType(internalFormat));
    }
}

