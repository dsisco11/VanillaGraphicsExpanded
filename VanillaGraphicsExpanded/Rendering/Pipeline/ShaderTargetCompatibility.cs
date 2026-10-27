using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;

namespace VanillaGraphicsExpanded.Rendering.Pipeline;

/// <summary>Exact scalar class and component coverage for supported sized color targets.</summary>
internal static class ShaderTargetCompatibility
{
    #region Public API
    /// <summary>Requires enough stored components and matching signed, unsigned or floating shader output class.</summary>
    internal static bool Accepts(TextureFormat format, ShaderInterfaceShape output)
    {
        bool integer = TargetFormatPolicy.IsInteger(format);
        bool unsigned = format is TextureFormat.R8ui or TextureFormat.R16ui or TextureFormat.R32ui
            or TextureFormat.Rg8ui or TextureFormat.Rg16ui or TextureFormat.Rg32ui
            or TextureFormat.Rgb8ui or TextureFormat.Rgb16ui or TextureFormat.Rgb32ui
            or TextureFormat.Rgba8ui or TextureFormat.Rgba16ui or TextureFormat.Rgba32ui or TextureFormat.Rgb10A2ui;
        int components = format switch
        {
            TextureFormat.R8 or TextureFormat.R16 or TextureFormat.R16f or TextureFormat.R32f
                or TextureFormat.R8i or TextureFormat.R16i or TextureFormat.R32i
                or TextureFormat.R8ui or TextureFormat.R16ui or TextureFormat.R32ui => 1,
            TextureFormat.Rg8 or TextureFormat.Rg16 or TextureFormat.Rg16f or TextureFormat.Rg32f
                or TextureFormat.Rg8i or TextureFormat.Rg16i or TextureFormat.Rg32i
                or TextureFormat.Rg8ui or TextureFormat.Rg16ui or TextureFormat.Rg32ui => 2,
            TextureFormat.R11fG11fB10f or TextureFormat.Rgb8 or TextureFormat.Rgb16 or TextureFormat.Rgb16f or TextureFormat.Rgb32f
                or TextureFormat.Rgb8i or TextureFormat.Rgb16i or TextureFormat.Rgb32i
                or TextureFormat.Rgb8ui or TextureFormat.Rgb16ui or TextureFormat.Rgb32ui or TextureFormat.Srgb8 => 3,
            _ => 4
        };
        return TargetFormatPolicy.IsColor(format) && components >= output.Components
            && (integer ? output.Interpretation == VertexInterpretation.Integer && output.Unsigned == unsigned
                : output.Interpretation == VertexInterpretation.Floating);
    }
    #endregion
}
