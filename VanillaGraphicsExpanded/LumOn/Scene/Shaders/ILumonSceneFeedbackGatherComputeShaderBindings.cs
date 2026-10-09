using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.LumOn.Scene.Shaders;

/// <summary>Declares the GPU binding contract for LumonSceneFeedbackGatherComputeShader.</summary>
[ShaderBindingSet(typeof(IShaderInterfaceLocations), Defaults = true)]
[ShaderBindingSet(typeof(IShaderIncludeBindings), Defaults = true)]
internal interface ILumonSceneFeedbackGatherComputeShaderBindings
{
    #region Public API
    /// <summary>Reuses the shared integer rendering frame for randomized sampling.</summary>
    [ShaderBinding("VgeFrameUBO", ShaderBindingKind.UniformBlock, GpuBindingRegistry.Ubo.Frame, ShaderStageKind.Compute)]
    CpuUniformBuffer FrameInputs { get; }
    /// <summary>Declares the VgeLumOnSceneFeedbackGatherParamsUBO UniformBlock slot.</summary>
    [ShaderBinding("VgeLumOnSceneFeedbackGatherParamsUBO", ShaderBindingKind.UniformBlock, GpuBindingRegistry.Ubo.Object, ShaderStageKind.Compute)]
    CpuUniformBuffer Parameters { get; }
    /// <summary>Declares the vge_patchIdGBuffer Sampler slot.</summary>
    [ShaderBinding("vge_patchIdGBuffer", ShaderBindingKind.Sampler, 0, ShaderStageKind.Compute, TextureTarget = ShaderTextureTarget.Texture2D, Sampler = ShaderSamplerPolicy.NearestClamp)]
    int PatchIdG { set; }
    /// <summary>Declares the VgePageRequests StorageBlock slot.</summary>
    [ShaderBinding("VgePageRequests", ShaderBindingKind.StorageBlock, 0, ShaderStageKind.Compute)]
    GpuShaderStorageBuffer PageRequests { set; }
    /// <summary>Retains the counter buffer without clearing its execution-owned contents.</summary>
    [ShaderBinding("vge_pageRequestCount", ShaderBindingKind.AtomicCounter, 0, ShaderStageKind.Compute)]
    GpuAtomicCounterBuffer? RequestCounter { set; }
    #endregion
}
