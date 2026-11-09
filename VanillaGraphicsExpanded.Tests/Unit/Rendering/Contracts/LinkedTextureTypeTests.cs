using VanillaGraphicsExpanded.Rendering.RHI;
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
    [InlineData(ShaderValueType.Sampler2D, (int)ShaderTextureTarget.Texture2D)]
    [InlineData(ShaderValueType.IntSampler2D, (int)ShaderTextureTarget.Texture2D)]
    [InlineData(ShaderValueType.Sampler2DShadow, (int)ShaderTextureTarget.Texture2D)]
    [InlineData(ShaderValueType.Image3D, (int)ShaderTextureTarget.Texture3D)]
    [InlineData(ShaderValueType.Sampler2DArrayShadow, (int)ShaderTextureTarget.Texture2DArray)]
    [InlineData(ShaderValueType.Sampler2DRect, 0)]
    [InlineData(ShaderValueType.Sampler2DMultisample, 0)]
    [InlineData(ShaderValueType.SamplerCubeMapArray, 0)]
    [InlineData(ShaderValueType.Sampler1DArray, 0)]
    public void LinkedDimensionsRequireCompatibleStorage(ShaderValueType type, int expected)
    {
        // Arrange: xUnit supplies one shader sampler/image type and its declared storage target.
        // Act: Resolve the semantic target accepted by prepared resource binding.
        int actual = GpuPreparedBindings.CompatibleTextureTarget(type);
        // Assert: Unsupported specialized storage remains distinguishable from ordinary 2D storage.
        Assert.Equal(expected, actual);
    }
    #endregion
}
