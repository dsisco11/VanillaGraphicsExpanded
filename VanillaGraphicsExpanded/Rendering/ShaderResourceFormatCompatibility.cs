using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Validates the scalar interpretation shared by linked shader types and resource formats.</summary>
internal static class ShaderResourceFormatCompatibility
{
    /// <summary>Distinguishes integer storage from normalized, depth and floating-point values.</summary>
    private enum ScalarClass { Floating, Signed, Unsigned }

    #region Public API
    /// <summary>Checks the resource's format interpretation without reflecting its executable or allocation.</summary>
    internal static bool Matches(ShaderValueType type, TextureFormat format)
    {
        // Comparison samplers require depth storage in addition to floating-point interpretation.
        bool shadow = type is ShaderValueType.Sampler1DShadow or ShaderValueType.Sampler2DShadow or
            ShaderValueType.Sampler2DRectShadow or ShaderValueType.Sampler1DArrayShadow or ShaderValueType.Sampler2DArrayShadow or
            ShaderValueType.SamplerCubeShadow or ShaderValueType.SamplerCubeMapArrayShadow;
        return (!shadow || TextureFormatHelper.IsDepthFormat(format)) && Classify(type) == Classify(format);
    }
    #endregion

    #region Private
    /// <summary>Classifies the standardized numeric linked resource type.</summary>
    private static ScalarClass Classify(ShaderValueType type) => type switch
    {
        ShaderValueType.IntSampler1D or
            ShaderValueType.IntSampler2D or
            ShaderValueType.IntSampler3D or
            ShaderValueType.IntSamplerCube or
            ShaderValueType.IntSampler2DRect or
            ShaderValueType.IntSampler1DArray or
            ShaderValueType.IntSampler2DArray or
            ShaderValueType.IntSamplerBuffer or
            ShaderValueType.IntSamplerCubeMapArray or
            ShaderValueType.IntImage1D or
            ShaderValueType.IntImage2D or
            ShaderValueType.IntImage3D or
            ShaderValueType.IntImage2DRect or
            ShaderValueType.IntImageCube or
            ShaderValueType.IntImageBuffer or
            ShaderValueType.IntImage1DArray or
            ShaderValueType.IntImage2DArray or
            ShaderValueType.IntImageCubeMapArray or
            ShaderValueType.IntImage2DMultisample or
            ShaderValueType.IntImage2DMultisampleArray or
            ShaderValueType.IntSampler2DMultisample or
            ShaderValueType.IntSampler2DMultisampleArray => ScalarClass.Signed,
        ShaderValueType.UnsignedIntSampler1D or
            ShaderValueType.UnsignedIntSampler2D or
            ShaderValueType.UnsignedIntSampler3D or
            ShaderValueType.UnsignedIntSamplerCube or
            ShaderValueType.UnsignedIntSampler2DRect or
            ShaderValueType.UnsignedIntSampler1DArray or
            ShaderValueType.UnsignedIntSampler2DArray or
            ShaderValueType.UnsignedIntSamplerBuffer or
            ShaderValueType.UnsignedIntSamplerCubeMapArray or
            ShaderValueType.UnsignedIntImage1D or
            ShaderValueType.UnsignedIntImage2D or
            ShaderValueType.UnsignedIntImage3D or
            ShaderValueType.UnsignedIntImage2DRect or
            ShaderValueType.UnsignedIntImageCube or
            ShaderValueType.UnsignedIntImageBuffer or
            ShaderValueType.UnsignedIntImage1DArray or
            ShaderValueType.UnsignedIntImage2DArray or
            ShaderValueType.UnsignedIntImageCubeMapArray or
            ShaderValueType.UnsignedIntImage2DMultisample or
            ShaderValueType.UnsignedIntImage2DMultisampleArray or
            ShaderValueType.UnsignedIntSampler2DMultisample or
            ShaderValueType.UnsignedIntSampler2DMultisampleArray => ScalarClass.Unsigned,
        _ => ScalarClass.Floating
    };

    /// <summary>Classifies integer formats explicitly; normalized, depth and floating storage yields floating values.</summary>
    private static ScalarClass Classify(TextureFormat format) => format switch
    {
        TextureFormat.R8i or TextureFormat.R16i or TextureFormat.R32i or
            TextureFormat.Rg8i or TextureFormat.Rg16i or TextureFormat.Rg32i or
            TextureFormat.Rgb8i or TextureFormat.Rgb16i or TextureFormat.Rgb32i or
            TextureFormat.Rgba8i or TextureFormat.Rgba16i or TextureFormat.Rgba32i => ScalarClass.Signed,
        TextureFormat.R8ui or TextureFormat.R16ui or TextureFormat.R32ui or
            TextureFormat.Rg8ui or TextureFormat.Rg16ui or TextureFormat.Rg32ui or
            TextureFormat.Rgb8ui or TextureFormat.Rgb16ui or TextureFormat.Rgb32ui or
            TextureFormat.Rgba8ui or TextureFormat.Rgba16ui or TextureFormat.Rgba32ui or
            TextureFormat.Rgb10A2ui => ScalarClass.Unsigned,
        _ => ScalarClass.Floating
    };
    #endregion
}
