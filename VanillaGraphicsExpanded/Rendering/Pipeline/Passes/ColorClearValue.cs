using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;

namespace VanillaGraphicsExpanded.Rendering.Pipeline.Passes;

/// <summary>Owns a typed, immutable four-component clear value without floating-point conversion of integers.</summary>
internal sealed class ColorClearValue
{
    private readonly float[]? floats;
    private readonly int[]? integers;
    private readonly uint[]? unsigned;

    #region Public API
    /// <summary>Creates a normalized or floating-point clear value.</summary>
    internal static ColorClearValue Float(float r, float g, float b, float a) => new([r, g, b, a], null, null);
    /// <summary>Creates a signed integer clear value.</summary>
    internal static ColorClearValue Int(int r, int g, int b, int a) => new(null, [r, g, b, a], null);
    /// <summary>Creates an unsigned integer clear value.</summary>
    internal static ColorClearValue UInt(uint r, uint g, uint b, uint a) => new(null, null, [r, g, b, a]);
    /// <summary>Rejects clear values whose scalar class differs from the target storage.</summary>
    internal void Validate(PixelInternalFormat format)
    {
        bool signed = format is PixelInternalFormat.R8i or PixelInternalFormat.R16i or PixelInternalFormat.R32i
            or PixelInternalFormat.Rg8i or PixelInternalFormat.Rg16i or PixelInternalFormat.Rg32i
            or PixelInternalFormat.Rgb8i or PixelInternalFormat.Rgb16i or PixelInternalFormat.Rgb32i
            or PixelInternalFormat.Rgba8i or PixelInternalFormat.Rgba16i or PixelInternalFormat.Rgba32i;
        if ((floats is not null) != !TargetFormatPolicy.IsInteger(format)
            || (integers is not null) != signed)
            throw new ArgumentException("Clear scalar class does not match the attachment.");
    }
    /// <summary>Executes a typed clear for the bound draw-output slot after pass state is established.</summary>
    internal void Execute(int output)
    {
        if (floats is not null) GL.ClearBuffer(ClearBuffer.Color, output, floats);
        else if (integers is not null) GL.ClearBuffer(ClearBuffer.Color, output, integers);
        else GL.ClearBuffer(ClearBuffer.Color, output, unsigned!);
    }
    #endregion

    #region Private
    /// <summary>Retains only privately allocated typed payloads.</summary>
    private ColorClearValue(float[]? floats, int[]? integers, uint[]? unsigned)
    { this.floats = floats; this.integers = integers; this.unsigned = unsigned; }
    #endregion
}
