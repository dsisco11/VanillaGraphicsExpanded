using VanillaGraphicsExpanded.Rendering.RHI;
using System;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;

namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Owns a typed, immutable four-component clear value without floating-point conversion of integers.</summary>
public sealed class ColorClearValue
{
    private readonly float[]? floats;
    private readonly int[]? integers;
    private readonly uint[]? unsigned;

    #region Public API
    /// <summary>Creates a normalized or floating-point clear value.</summary>
    public static ColorClearValue Float(float r, float g, float b, float a) => new([r, g, b, a], null, null);
    /// <summary>Creates a signed integer clear value.</summary>
    public static ColorClearValue Int(int r, int g, int b, int a) => new(null, [r, g, b, a], null);
    /// <summary>Creates an unsigned integer clear value.</summary>
    public static ColorClearValue UInt(uint r, uint g, uint b, uint a) => new(null, null, [r, g, b, a]);
    /// <summary>Rejects clear values whose scalar class differs from the target storage.</summary>
    internal void Validate(TextureFormat format)
    {
        bool signed = format is TextureFormat.R8i or TextureFormat.R16i or TextureFormat.R32i
            or TextureFormat.Rg8i or TextureFormat.Rg16i or TextureFormat.Rg32i
            or TextureFormat.Rgb8i or TextureFormat.Rgb16i or TextureFormat.Rgb32i
            or TextureFormat.Rgba8i or TextureFormat.Rgba16i or TextureFormat.Rgba32i;
        if ((floats is not null) != !TargetFormatPolicy.IsInteger(format)
            || (integers is not null) != signed)
            throw new ArgumentException("Clear scalar class does not match the attachment.");
    }
    /// <summary>Gets the immutable floating payload for native publication.</summary>
    internal System.ReadOnlySpan<float> FloatComponents => floats;
    /// <summary>Gets the immutable signed payload for native publication.</summary>
    internal System.ReadOnlySpan<int> SignedComponents => integers;
    /// <summary>Gets the immutable unsigned payload for native publication.</summary>
    internal System.ReadOnlySpan<uint> UnsignedComponents => unsigned;
    #endregion

    #region Private
    /// <summary>Retains only privately allocated typed payloads.</summary>
    private ColorClearValue(float[]? floats, int[]? integers, uint[]? unsigned)
    { this.floats = floats; this.integers = integers; this.unsigned = unsigned; }
    #endregion
}
