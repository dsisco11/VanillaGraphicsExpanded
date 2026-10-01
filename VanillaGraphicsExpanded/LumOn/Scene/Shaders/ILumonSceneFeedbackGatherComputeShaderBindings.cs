using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.LumOn.Scene.Shaders;

/// <summary>Declares the GPU binding contract for LumonSceneFeedbackGatherComputeShader.</summary>
[ShaderBindingSet(typeof(IShaderInterfaceLocations), Defaults = true)]
[ShaderBindingSet(typeof(IShaderIncludeBindings), Defaults = true)]
internal interface ILumonSceneFeedbackGatherComputeShaderBindings
{
    #region Public API
    /// <summary>Declares the VgeLumOnSceneFeedbackGatherParamsUBO UniformBlock slot.</summary>
    [ShaderBinding("VgeLumOnSceneFeedbackGatherParamsUBO", ShaderBindingKind.UniformBlock, GpuBindingRegistry.Ubo.Object, ShaderStageKind.Compute)]
    GpuUniformBuffer Parameters { set; }
    /// <summary>Declares the vge_patchIdGBuffer Sampler slot.</summary>
    [ShaderBinding("vge_patchIdGBuffer", ShaderBindingKind.Sampler, 0, ShaderStageKind.Compute)]
    GpuTexture PatchIdG { set; }
    /// <summary>Declares the VgePageRequests StorageBlock slot.</summary>
    [ShaderBinding("VgePageRequests", ShaderBindingKind.StorageBlock, 0, ShaderStageKind.Compute)]
    GpuShaderStorageBuffer PageRequests { set; }
    #endregion
}
