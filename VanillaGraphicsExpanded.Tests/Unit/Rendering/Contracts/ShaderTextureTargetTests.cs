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
        // Arrange: Pair every semantic target with the shader type whose storage dimension it represents.
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
            // Act: Resolve the expected reflected type and prepared-binding target for this enum member.
            bool found = reflected.TryGetValue(target, out var type);
            // Assert: Every target has an authored reflection mapping.
            Assert.True(found);
            // Act: Query the semantic target selected by prepared binding.
            int actual = GpuPreparedBindings.CompatibleTextureTarget(type);
            // Assert: The production enum value and semantic mapping agree.
            Assert.Equal((int)target, actual);
        }
    }
    #endregion
}
