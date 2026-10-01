using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.LumOn.Scene.Shaders;

/// <summary>Declares the GPU binding contract for LumonSceneFeedbackMarkPagesComputeShader.</summary>
[ShaderBindingSet(typeof(IShaderInterfaceLocations), Defaults = true)]
[ShaderBindingSet(typeof(IShaderIncludeBindings), Defaults = true)]
internal interface ILumonSceneFeedbackMarkPagesComputeShaderBindings
{
    #region Public API
    /// <summary>Declares the VgeLumOnSceneFeedbackMarkParamsUBO UniformBlock slot.</summary>
    [ShaderBinding("VgeLumOnSceneFeedbackMarkParamsUBO", ShaderBindingKind.UniformBlock, GpuBindingRegistry.Ubo.Object, ShaderStageKind.Compute)]
    GpuUniformBuffer Parameters { set; }
    /// <summary>Declares the vge_patchIdGBuffer Sampler slot.</summary>
    [ShaderBinding("vge_patchIdGBuffer", ShaderBindingKind.Sampler, 0, ShaderStageKind.Compute)]
    GpuTexture PatchIdG { set; }
    /// <summary>Declares the vge_chunkSlotGenerationTex Sampler slot.</summary>
    [ShaderBinding("vge_chunkSlotGenerationTex", ShaderBindingKind.Sampler, 1, ShaderStageKind.Compute, Required = false)]
    GpuTexture ChunkSlotGenerationTex { set; }
    /// <summary>Declares the vge_pageUsageStamp Image slot.</summary>
    [ShaderBinding("vge_pageUsageStamp", ShaderBindingKind.Image, 0, ShaderStageKind.Compute)]
    GpuTextureBinding PageUsageStamp { set; }
    #endregion
}
