using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using OpenTK.Graphics.OpenGL;

using VanillaGraphicsExpanded.Rendering;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.Unit.Rendering;

public sealed class TextureFormatHelperTests
{
    [Theory]
    [InlineData(TextureFormat.R32ui, PixelChannelLayout.RedInteger, PixelComponentType.UnsignedInt)]
    [InlineData(TextureFormat.Rg32ui, PixelChannelLayout.RgInteger, PixelComponentType.UnsignedInt)]
    [InlineData(TextureFormat.Rgba32ui, PixelChannelLayout.RgbaInteger, PixelComponentType.UnsignedInt)]
    public void IntegerFormats_MapToIntegerPixelFormatAndUnsignedType(
        TextureFormat internalFormat,
        PixelChannelLayout expectedFormat,
        PixelComponentType expectedType)
    {
        Assert.Equal(expectedFormat, TextureFormatHelper.GetPixelFormat(internalFormat));
        Assert.Equal(expectedType, TextureFormatHelper.GetPixelType(internalFormat));
    }
}

