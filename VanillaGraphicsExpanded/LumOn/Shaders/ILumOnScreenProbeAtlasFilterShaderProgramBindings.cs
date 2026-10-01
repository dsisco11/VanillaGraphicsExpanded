using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.LumOn;

/// <summary>Declares the GPU binding contract for LumOnScreenProbeAtlasFilterShaderProgram.</summary>
[ShaderBindingSet(typeof(IShaderInterfaceLocations), Defaults = true)]
[ShaderBindingSet(typeof(IShaderIncludeBindings), Defaults = true)]
internal interface ILumOnScreenProbeAtlasFilterShaderProgramBindings
{
    #region Public API
    /// <summary>Declares the LumOnFrameUBO UniformBlock slot.</summary>
    [ShaderBinding("LumOnFrameUBO", ShaderBindingKind.UniformBlock, GpuBindingRegistry.Ubo.Frame, ShaderStageKind.Vertex, ShaderStageKind.Fragment)]
    GpuUniformBuffer? LumOnFrame { get; }
    /// <summary>Declares the VgeLumOnProbeParamsUBO UniformBlock slot.</summary>
    [ShaderBinding("VgeLumOnProbeParamsUBO", ShaderBindingKind.UniformBlock, GpuBindingRegistry.Ubo.Object, ShaderStageKind.Vertex, ShaderStageKind.Fragment)]
    CpuUniformBuffer Parameters { get; }
    /// <summary>Declares the octahedralAtlas Sampler slot.</summary>
    [ShaderBinding("octahedralAtlas", ShaderBindingKind.Sampler, 0, ShaderStageKind.Vertex, ShaderStageKind.Fragment)]
    GpuTexture? ScreenProbeAtlas { set; }
    /// <summary>Declares the probeAtlasMeta Sampler slot.</summary>
    [ShaderBinding("probeAtlasMeta", ShaderBindingKind.Sampler, 1, ShaderStageKind.Vertex, ShaderStageKind.Fragment)]
    GpuTexture? ScreenProbeAtlasMeta { set; }
    /// <summary>Declares the probeAnchorPosition Sampler slot.</summary>
    [ShaderBinding("probeAnchorPosition", ShaderBindingKind.Sampler, 2, ShaderStageKind.Vertex, ShaderStageKind.Fragment)]
    GpuTexture? ProbeAnchorPosition { set; }
    /// <summary>Supplies optional shared world-probe storage.</summary>
    [ShaderBinding("LumOnWorldProbeUBO", ShaderBindingKind.UniformBlock, GpuBindingRegistry.Ubo.WorldProbe, ShaderStageKind.Vertex, ShaderStageKind.Fragment, Required = false)]
    GpuUniformBuffer? LumOnWorldProbe { get; }
    #endregion
}
