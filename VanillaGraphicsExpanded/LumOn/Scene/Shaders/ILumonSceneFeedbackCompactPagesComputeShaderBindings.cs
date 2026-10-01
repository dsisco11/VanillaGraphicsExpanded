using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.LumOn.Scene.Shaders;

/// <summary>Declares the GPU binding contract for LumonSceneFeedbackCompactPagesComputeShader.</summary>
[ShaderBindingSet(typeof(IShaderInterfaceLocations), Defaults = true)]
[ShaderBindingSet(typeof(IShaderIncludeBindings), Defaults = true)]
internal interface ILumonSceneFeedbackCompactPagesComputeShaderBindings
{
    #region Public API
    /// <summary>Declares the VgeLumOnSceneFeedbackCompactParamsUBO UniformBlock slot.</summary>
    [ShaderBinding("VgeLumOnSceneFeedbackCompactParamsUBO", ShaderBindingKind.UniformBlock, GpuBindingRegistry.Ubo.Object, ShaderStageKind.Compute)]
    CpuUniformBuffer Parameters { get; }
    /// <summary>Declares the vge_pageUsageStamp Sampler slot.</summary>
    [ShaderBinding("vge_pageUsageStamp", ShaderBindingKind.Sampler, 0, ShaderStageKind.Compute, TextureTarget = ShaderTextureTarget.Texture2DArray, Sampler = ShaderSamplerPolicy.NearestClamp)]
    int PageUsageStamp { set; }
    /// <summary>Declares the vge_pageTableMip0 Sampler slot.</summary>
    [ShaderBinding("vge_pageTableMip0", ShaderBindingKind.Sampler, 1, ShaderStageKind.Compute, TextureTarget = ShaderTextureTarget.Texture2DArray, Sampler = ShaderSamplerPolicy.NearestClamp)]
    int PageTableMip0 { set; }
    /// <summary>Declares the VgePageRequests StorageBlock slot.</summary>
    [ShaderBinding("VgePageRequests", ShaderBindingKind.StorageBlock, 0, ShaderStageKind.Compute)]
    GpuShaderStorageBuffer PageRequests { set; }
    /// <summary>Retains the counter buffer without clearing its execution-owned contents.</summary>
    [ShaderBinding("vge_pageRequestCount", ShaderBindingKind.AtomicCounter, 0, ShaderStageKind.Compute)]
    GpuAtomicCounterBuffer? RequestCounter { set; }
    #endregion
}
