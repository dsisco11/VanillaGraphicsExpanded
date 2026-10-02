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
    internal static bool Matches(ActiveUniformType type, PixelInternalFormat format)
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
    private static ScalarClass Classify(PixelInternalFormat format) => format switch
    {
        PixelInternalFormat.R8i or PixelInternalFormat.R16i or PixelInternalFormat.R32i or
            PixelInternalFormat.Rg8i or PixelInternalFormat.Rg16i or PixelInternalFormat.Rg32i or
            PixelInternalFormat.Rgb8i or PixelInternalFormat.Rgb16i or PixelInternalFormat.Rgb32i or
            PixelInternalFormat.Rgba8i or PixelInternalFormat.Rgba16i or PixelInternalFormat.Rgba32i => ScalarClass.Signed,
        PixelInternalFormat.R8ui or PixelInternalFormat.R16ui or PixelInternalFormat.R32ui or
            PixelInternalFormat.Rg8ui or PixelInternalFormat.Rg16ui or PixelInternalFormat.Rg32ui or
            PixelInternalFormat.Rgb8ui or PixelInternalFormat.Rgb16ui or PixelInternalFormat.Rgb32ui or
            PixelInternalFormat.Rgba8ui or PixelInternalFormat.Rgba16ui or PixelInternalFormat.Rgba32ui or
            PixelInternalFormat.Rgb10A2ui => ScalarClass.Unsigned,
        _ => ScalarClass.Floating
    };
    #endregion
}
