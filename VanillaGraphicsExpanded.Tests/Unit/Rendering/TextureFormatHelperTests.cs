using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using OpenTK.Graphics.OpenGL;

using VanillaGraphicsExpanded.Rendering;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.Unit.Rendering;

/// <summary>Verifies integer texture formats select integer pixel layouts and unsigned types.</summary>
public sealed class TextureFormatHelperTests
{
    #region Public API

    /// <summary>Integer internal formats map to their matching integer transfer format and type.</summary>
    [Theory]
    [InlineData(TextureFormat.R32ui, PixelChannelLayout.RedInteger, PixelComponentType.UnsignedInt)]
    [InlineData(TextureFormat.Rg32ui, PixelChannelLayout.RgInteger, PixelComponentType.UnsignedInt)]
    [InlineData(TextureFormat.Rgba32ui, PixelChannelLayout.RgbaInteger, PixelComponentType.UnsignedInt)]
    public void IntegerFormats_MapToIntegerPixelFormatAndUnsignedType(
        TextureFormat internalFormat,
        PixelChannelLayout expectedFormat,
        PixelComponentType expectedType)
    {
        // Arrange inputs are provided by the theory cases.
        // Act and assert both transfer properties.
        Assert.Equal(expectedFormat, TextureFormatHelper.GetPixelFormat(internalFormat));
        Assert.Equal(expectedType, TextureFormatHelper.GetPixelType(internalFormat));
    }

    #endregion
}

