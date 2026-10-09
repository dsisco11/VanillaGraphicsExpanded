using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.LumOn;

/// <summary>Declares the GPU binding contract for LumOnProbeAtlasPisMaskShaderProgram.</summary>
[ShaderBindingSet(typeof(IShaderInterfaceLocations), Defaults = true)]
[ShaderBindingSet(typeof(IShaderIncludeBindings), Defaults = true)]
internal interface ILumOnProbeAtlasPisMaskShaderProgramBindings
{
    #region Public API
    /// <summary>Supplies common camera and frame values from the universal view snapshot.</summary>
    [ShaderBinding("VgeFrameUBO", ShaderBindingKind.UniformBlock, GpuBindingRegistry.Ubo.Frame, ShaderStageKind.Vertex, ShaderStageKind.Fragment, Required = false)]
    CpuUniformBuffer FrameInputs { get; }
    /// <summary>Declares the fragment stage's frame parameter block.</summary>
    [ShaderBinding("LumOnFrameUBO", ShaderBindingKind.UniformBlock, GpuBindingRegistry.Ubo.LumOnFrame, ShaderStageKind.Fragment)]
    GpuUniformBuffer? LumOnFrame { get; }
    /// <summary>Declares the fragment stage's anchor position sampler.</summary>
    [ShaderBinding("probeAnchors", ShaderBindingKind.Sampler, 0, ShaderStageKind.Fragment, TextureTarget = ShaderTextureTarget.Texture2DArray, Sampler = ShaderSamplerPolicy.NearestClamp)]
    GpuTexture? ProbeAnchors { set; }
    /// <summary>Declares the fragment stage's history radiance sampler.</summary>
    [ShaderBinding("octahedralHistory", ShaderBindingKind.Sampler, 1, ShaderStageKind.Fragment)]
    GpuTexture? ScreenProbeAtlasHistory { set; }
    /// <summary>Declares the fragment stage's history metadata sampler.</summary>
    [ShaderBinding("probeAtlasMetaHistory", ShaderBindingKind.Sampler, 2, ShaderStageKind.Fragment)]
    GpuTexture? ScreenProbeAtlasMetaHistory { set; }
    /// <summary>Supplies optional shared world-probe storage.</summary>
    [ShaderBinding("LumOnWorldProbeUBO", ShaderBindingKind.UniformBlock, GpuBindingRegistry.Ubo.WorldProbe, ShaderStageKind.Fragment, Required = false)]
    GpuUniformBuffer? LumOnWorldProbe { get; }
    #endregion
}
