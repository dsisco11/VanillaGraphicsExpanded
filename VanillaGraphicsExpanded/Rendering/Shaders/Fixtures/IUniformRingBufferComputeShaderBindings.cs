using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.Rendering.Shaders.Fixtures;

/// <summary>Declares the GPU binding contract for UniformRingBufferComputeShader.</summary>
[ShaderBindingSet(typeof(IShaderInterfaceLocations), Defaults = true)]
[ShaderBindingSet(typeof(IShaderIncludeBindings), Defaults = true)]
internal interface IUniformRingBufferComputeShaderBindings : IOutputImageComputeBindings
{
    #region Public API
    /// <summary>Declares the TestParams UniformBlock slot.</summary>
    [ShaderBinding("TestParams", ShaderBindingKind.UniformBlock, 0, ShaderStageKind.Compute)]
    ShaderUniformBlockBinding Parameters { get; }
    #endregion
}
