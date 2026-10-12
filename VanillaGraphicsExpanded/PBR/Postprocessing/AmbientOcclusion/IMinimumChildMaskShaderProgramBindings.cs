using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
namespace VanillaGraphicsExpanded.PBR.Postprocessing;
/// <summary>Declares read-only shared depth and per-level parameters for AO source-recovery masks.</summary>
[ShaderBindingSet(typeof(IShaderInterfaceLocations), Defaults = true)]
internal interface IMinimumChildMaskShaderProgramBindings
{
    #region Public API
    /// <summary>Supplies the parent hierarchy level for the current reduction draw.</summary>
    [ShaderBinding("AoMinimumChildMaskInputs", ShaderBindingKind.UniformBlock, GpuBindingRegistry.Ubo.ShaderInputs, ShaderStageKind.Fragment)]
    CpuUniformBuffer Parameters { get; }
    /// <summary>Borrows every completed R32F depth level without modifying shared storage.</summary>
    [ShaderBinding("depthHierarchy", ShaderBindingKind.Sampler, 0, ShaderStageKind.Fragment, TextureTarget = ShaderTextureTarget.Texture2D, Sampler = ShaderSamplerPolicy.NearestClamp)]
    GpuTexture? Hierarchy { set; }
    #endregion
}
