using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.LumOn;

/// <summary>Declares the GPU binding contract for LumOnProbeAtlasPisMaskShaderProgram.</summary>
[ShaderBindingSet(typeof(IShaderInterfaceLocations), Defaults = true)]
[ShaderBindingSet(typeof(IShaderIncludeBindings), Defaults = true)]
internal interface ILumOnProbeAtlasPisMaskShaderProgramBindings
{
    #region Public API
    /// <summary>Declares the fragment stage's frame parameter block.</summary>
    [ShaderBinding("LumOnFrameUBO", ShaderBindingKind.UniformBlock, GpuBindingRegistry.Ubo.Frame, ShaderStageKind.Fragment)]
    GpuUniformBuffer? LumOnFrame { get; }
    /// <summary>Declares the fragment stage's anchor position sampler.</summary>
    [ShaderBinding("probeAnchorPosition", ShaderBindingKind.Sampler, 0, ShaderStageKind.Fragment)]
    GpuTexture? ProbeAnchorPosition { set; }
    /// <summary>Declares the fragment stage's anchor normal sampler.</summary>
    [ShaderBinding("probeAnchorNormal", ShaderBindingKind.Sampler, 1, ShaderStageKind.Fragment)]
    GpuTexture? ProbeAnchorNormal { set; }
    /// <summary>Declares the fragment stage's history radiance sampler.</summary>
    [ShaderBinding("octahedralHistory", ShaderBindingKind.Sampler, 2, ShaderStageKind.Fragment)]
    GpuTexture? ScreenProbeAtlasHistory { set; }
    /// <summary>Declares the fragment stage's history metadata sampler.</summary>
    [ShaderBinding("probeAtlasMetaHistory", ShaderBindingKind.Sampler, 3, ShaderStageKind.Fragment)]
    GpuTexture? ScreenProbeAtlasMetaHistory { set; }
    /// <summary>Supplies optional shared world-probe storage.</summary>
    [ShaderBinding("LumOnWorldProbeUBO", ShaderBindingKind.UniformBlock, GpuBindingRegistry.Ubo.WorldProbe, ShaderStageKind.Fragment, Required = false)]
    GpuUniformBuffer? LumOnWorldProbe { get; }
    #endregion
}
