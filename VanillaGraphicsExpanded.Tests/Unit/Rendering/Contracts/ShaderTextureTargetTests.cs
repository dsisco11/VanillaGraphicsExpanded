using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.Spirv;

namespace VanillaGraphicsExpanded.Tests.Unit.Rendering.Contracts;

/// <summary>Checks semantic texture dimensions independently of native token numbering.</summary>
public sealed class ShaderTextureTargetTests
{
    #region Public API
    /// <summary>Every declared target matches the dimension of its reflected sampler type.</summary>
    [Fact]
    public void AllContractTargetsMatchReflectedDimensions()
    {
        var reflected = new Dictionary<ShaderTextureTarget, ShaderValueType>
        {
            [ShaderTextureTarget.Texture1D] = ShaderValueType.Sampler1D,
            [ShaderTextureTarget.Texture2D] = ShaderValueType.Sampler2D,
            [ShaderTextureTarget.Texture3D] = ShaderValueType.Sampler3D,
            [ShaderTextureTarget.TextureCubeMap] = ShaderValueType.SamplerCube,
            [ShaderTextureTarget.Texture2DArray] = ShaderValueType.Sampler2DArray,
            [ShaderTextureTarget.TextureBuffer] = ShaderValueType.SamplerBuffer
        };
        foreach (ShaderTextureTarget target in Enum.GetValues<ShaderTextureTarget>())
        {
            Assert.True(reflected.TryGetValue(target, out var type));
            Assert.Equal((int)target, GpuPreparedBindings.CompatibleTextureTarget(type));
        }
    }
    #endregion
}
