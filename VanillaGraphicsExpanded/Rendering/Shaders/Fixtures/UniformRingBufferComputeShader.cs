using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.Rendering.Shaders.Fixtures;

/// <summary>Owns shader declarations for this packaged source or fixture.</summary>
[ShaderProgram("Contract", "tests/GpuUniformRingBufferIntegrationTests_1", 1)]
[ShaderStage("Contract", ShaderStageKind.Compute, "tests/GpuUniformRingBufferIntegrationTests_1.csh")]
[ShaderBindingSet(typeof(ShaderInterfaceLocations), Defaults = true)]
[ShaderBindingSet(typeof(ShaderIncludeBindings), Defaults = true)]
internal static partial class UniformRingBufferComputeShader
{

    #region Private: GPU binding declarations
    /// <summary>Declares the TestParams UniformBlock slot.</summary>
    [ShaderBinding("TestParams", ShaderBindingKind.UniformBlock, 0, ShaderStageKind.Compute)]
    private static partial ShaderUniformBlockBinding Parameters { get; }
    #endregion

}
