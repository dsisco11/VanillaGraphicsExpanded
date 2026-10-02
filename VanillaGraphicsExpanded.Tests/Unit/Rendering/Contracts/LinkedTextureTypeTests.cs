using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Spirv;

namespace VanillaGraphicsExpanded.Tests.Unit.Rendering.Contracts;

/// <summary>Protects dimension compatibility where superficially similar texture types require different GL targets.</summary>
public sealed class LinkedTextureTypeTests
{
    #region Public API
    /// <summary>Ordinary, integer and shadow types share dimensions; specialized targets cannot use ordinary storage.</summary>
    [Theory]
    [InlineData(ActiveUniformType.Sampler2D, (int)ShaderTextureTarget.Texture2D)]
    [InlineData(ActiveUniformType.IntSampler2D, (int)ShaderTextureTarget.Texture2D)]
    [InlineData(ActiveUniformType.Sampler2DShadow, (int)ShaderTextureTarget.Texture2D)]
    [InlineData(ActiveUniformType.Image3D, (int)ShaderTextureTarget.Texture3D)]
    [InlineData(ActiveUniformType.Sampler2DArrayShadow, (int)ShaderTextureTarget.Texture2DArray)]
    [InlineData(ActiveUniformType.Sampler2DRect, 0)]
    [InlineData(ActiveUniformType.Sampler2DMultisample, 0)]
    [InlineData(ActiveUniformType.SamplerCubeMapArray, 0)]
    [InlineData(ActiveUniformType.Sampler1DArray, 0)]
    public void LinkedDimensionsRequireCompatibleStorage(ActiveUniformType type, int expected)
    {
        Assert.Equal(expected, GpuPreparedBindings.CompatibleTextureTarget(type));
    }
    #endregion
}
