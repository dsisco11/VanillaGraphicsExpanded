using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;

namespace VanillaGraphicsExpanded.Rendering.Pipeline;

/// <summary>Exact scalar class and component coverage for supported sized color targets.</summary>
internal static class ShaderTargetCompatibility
{
    #region Public API
    /// <summary>Requires enough stored components and matching signed, unsigned or floating shader output class.</summary>
    internal static bool Accepts(PixelInternalFormat format, ShaderInterfaceShape output)
    {
        bool integer = TargetFormatPolicy.IsInteger(format);
        bool unsigned = format is PixelInternalFormat.R8ui or PixelInternalFormat.R16ui or PixelInternalFormat.R32ui
            or PixelInternalFormat.Rg8ui or PixelInternalFormat.Rg16ui or PixelInternalFormat.Rg32ui
            or PixelInternalFormat.Rgb8ui or PixelInternalFormat.Rgb16ui or PixelInternalFormat.Rgb32ui
            or PixelInternalFormat.Rgba8ui or PixelInternalFormat.Rgba16ui or PixelInternalFormat.Rgba32ui or PixelInternalFormat.Rgb10A2ui;
        int components = format switch
        {
            PixelInternalFormat.R8 or PixelInternalFormat.R16 or PixelInternalFormat.R16f or PixelInternalFormat.R32f
                or PixelInternalFormat.R8i or PixelInternalFormat.R16i or PixelInternalFormat.R32i
                or PixelInternalFormat.R8ui or PixelInternalFormat.R16ui or PixelInternalFormat.R32ui => 1,
            PixelInternalFormat.Rg8 or PixelInternalFormat.Rg16 or PixelInternalFormat.Rg16f or PixelInternalFormat.Rg32f
                or PixelInternalFormat.Rg8i or PixelInternalFormat.Rg16i or PixelInternalFormat.Rg32i
                or PixelInternalFormat.Rg8ui or PixelInternalFormat.Rg16ui or PixelInternalFormat.Rg32ui => 2,
            PixelInternalFormat.R11fG11fB10f or PixelInternalFormat.Rgb8 or PixelInternalFormat.Rgb16 or PixelInternalFormat.Rgb16f or PixelInternalFormat.Rgb32f
                or PixelInternalFormat.Rgb8i or PixelInternalFormat.Rgb16i or PixelInternalFormat.Rgb32i
                or PixelInternalFormat.Rgb8ui or PixelInternalFormat.Rgb16ui or PixelInternalFormat.Rgb32ui or PixelInternalFormat.Srgb8 => 3,
            _ => 4
        };
        return TargetFormatPolicy.IsColor(format) && components >= output.Components
            && (integer ? output.Interpretation == VertexInterpretation.Integer && output.Unsigned == unsigned
                : output.Interpretation == VertexInterpretation.Floating);
    }
    #endregion
}
