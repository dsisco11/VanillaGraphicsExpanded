using VanillaGraphicsExpanded.Rendering.RHI;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Validates the scalar interpretation shared by linked shader types and resource formats.</summary>
internal static class ShaderResourceFormatCompatibility
{
    /// <summary>Distinguishes integer storage from normalized, depth and floating-point values.</summary>
    private enum ScalarClass { Floating, Signed, Unsigned }

    #region Public API
    /// <summary>Checks the resource's format interpretation without reflecting its executable or allocation.</summary>
    internal static bool Matches(ActiveUniformType type, TextureFormat format)
    {
        // Comparison samplers require depth storage in addition to floating-point interpretation.
        bool shadow = (ShaderResourceType)type is ShaderResourceType.Sampler1DShadow or ShaderResourceType.Sampler2DShadow or
            ShaderResourceType.Sampler2DRectShadow or ShaderResourceType.Sampler1DArrayShadow or ShaderResourceType.Sampler2DArrayShadow or
            ShaderResourceType.SamplerCubeShadow or ShaderResourceType.SamplerCubeMapArrayShadow;
        return (!shadow || TextureFormatHelper.IsDepthFormat(format)) && Classify((ShaderResourceType)type) == Classify(format);
    }
    #endregion

    #region Private
    /// <summary>Classifies the standardized numeric linked resource type.</summary>
    private static ScalarClass Classify(ShaderResourceType type) => type switch
    {
        ShaderResourceType.IntSampler1D or
            ShaderResourceType.IntSampler2D or
            ShaderResourceType.IntSampler3D or
            ShaderResourceType.IntSamplerCube or
            ShaderResourceType.IntSampler2DRect or
            ShaderResourceType.IntSampler1DArray or
            ShaderResourceType.IntSampler2DArray or
            ShaderResourceType.IntSamplerBuffer or
            ShaderResourceType.IntSamplerCubeMapArray or
            ShaderResourceType.IntImage1D or
            ShaderResourceType.IntImage2D or
            ShaderResourceType.IntImage3D or
            ShaderResourceType.IntImage2DRect or
            ShaderResourceType.IntImageCube or
            ShaderResourceType.IntImageBuffer or
            ShaderResourceType.IntImage1DArray or
            ShaderResourceType.IntImage2DArray or
            ShaderResourceType.IntImageCubeMapArray or
            ShaderResourceType.IntImage2DMultisample or
            ShaderResourceType.IntImage2DMultisampleArray or
            ShaderResourceType.IntSampler2DMultisample or
            ShaderResourceType.IntSampler2DMultisampleArray => ScalarClass.Signed,
        ShaderResourceType.UnsignedIntSampler1D or
            ShaderResourceType.UnsignedIntSampler2D or
            ShaderResourceType.UnsignedIntSampler3D or
            ShaderResourceType.UnsignedIntSamplerCube or
            ShaderResourceType.UnsignedIntSampler2DRect or
            ShaderResourceType.UnsignedIntSampler1DArray or
            ShaderResourceType.UnsignedIntSampler2DArray or
            ShaderResourceType.UnsignedIntSamplerBuffer or
            ShaderResourceType.UnsignedIntSamplerCubeMapArray or
            ShaderResourceType.UnsignedIntImage1D or
            ShaderResourceType.UnsignedIntImage2D or
            ShaderResourceType.UnsignedIntImage3D or
            ShaderResourceType.UnsignedIntImage2DRect or
            ShaderResourceType.UnsignedIntImageCube or
            ShaderResourceType.UnsignedIntImageBuffer or
            ShaderResourceType.UnsignedIntImage1DArray or
            ShaderResourceType.UnsignedIntImage2DArray or
            ShaderResourceType.UnsignedIntImageCubeMapArray or
            ShaderResourceType.UnsignedIntImage2DMultisample or
            ShaderResourceType.UnsignedIntImage2DMultisampleArray or
            ShaderResourceType.UnsignedIntSampler2DMultisample or
            ShaderResourceType.UnsignedIntSampler2DMultisampleArray => ScalarClass.Unsigned,
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
