using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.LumOn;

/// <summary>Declares the GPU binding contract for LumOnScreenProbeAtlasTemporalShaderProgram.</summary>
[ShaderBindingSet(typeof(IShaderInterfaceLocations), Defaults = true)]
[ShaderBindingSet(typeof(IShaderIncludeBindings), Defaults = true)]
internal interface ILumOnScreenProbeAtlasTemporalShaderProgramBindings
{
    #region Public API
    /// <summary>Declares the LumOnFrameUBO UniformBlock slot.</summary>
    [ShaderBinding("LumOnFrameUBO", ShaderBindingKind.UniformBlock, GpuBindingRegistry.Ubo.Frame, ShaderStageKind.Vertex, ShaderStageKind.Fragment)]
    GpuUniformBuffer? LumOnFrame { get; }
    /// <summary>Declares the VgeLumOnProbeParamsUBO UniformBlock slot.</summary>
    [ShaderBinding("VgeLumOnProbeParamsUBO", ShaderBindingKind.UniformBlock, GpuBindingRegistry.Ubo.Object, ShaderStageKind.Vertex, ShaderStageKind.Fragment)]
    CpuUniformBuffer Parameters { get; }
    /// <summary>Declares the octahedralCurrent Sampler slot.</summary>
    [ShaderBinding("octahedralCurrent", ShaderBindingKind.Sampler, 0, ShaderStageKind.Vertex, ShaderStageKind.Fragment)]
    GpuTexture? ScreenProbeAtlasCurrent { set; }
    /// <summary>Declares the octahedralHistory Sampler slot.</summary>
    [ShaderBinding("octahedralHistory", ShaderBindingKind.Sampler, 1, ShaderStageKind.Vertex, ShaderStageKind.Fragment)]
    GpuTexture? ScreenProbeAtlasHistory { set; }
    /// <summary>Declares the position and normal anchor array sampler.</summary>
    [ShaderBinding("probeAnchors", ShaderBindingKind.Sampler, 2, ShaderStageKind.Vertex, ShaderStageKind.Fragment, TextureTarget = ShaderTextureTarget.Texture2DArray, Sampler = ShaderSamplerPolicy.NearestClamp)]
    GpuTexture? ProbeAnchors { set; }
    /// <summary>Declares the probeAtlasMetaCurrent Sampler slot.</summary>
    [ShaderBinding("probeAtlasMetaCurrent", ShaderBindingKind.Sampler, 3, ShaderStageKind.Vertex, ShaderStageKind.Fragment)]
    GpuTexture? ScreenProbeAtlasMetaCurrent { set; }
    /// <summary>Declares the probeAtlasMetaHistory Sampler slot.</summary>
    [ShaderBinding("probeAtlasMetaHistory", ShaderBindingKind.Sampler, 4, ShaderStageKind.Vertex, ShaderStageKind.Fragment)]
    GpuTexture? ScreenProbeAtlasMetaHistory { set; }
    /// <summary>Declares the velocityTex Sampler slot.</summary>
    [ShaderBinding("velocityTex", ShaderBindingKind.Sampler, 5, ShaderStageKind.Vertex, ShaderStageKind.Fragment)]
    GpuTexture? VelocityTex { set; }
    /// <summary>Declares the pmjJitter Sampler slot.</summary>
    [ShaderBinding("pmjJitter", ShaderBindingKind.Sampler, 6, ShaderStageKind.Vertex, ShaderStageKind.Fragment)]
    GpuTexture? PmjJitter { set; }
    /// <summary>Declares the probeTraceMask Sampler slot.</summary>
    [ShaderBinding("probeTraceMask", ShaderBindingKind.Sampler, 7, ShaderStageKind.Vertex, ShaderStageKind.Fragment, Required = false)]
    GpuTexture? ProbeTraceMask { set; }
    /// <summary>Supplies optional shared world-probe storage.</summary>
    [ShaderBinding("LumOnWorldProbeUBO", ShaderBindingKind.UniformBlock, GpuBindingRegistry.Ubo.WorldProbe, ShaderStageKind.Vertex, ShaderStageKind.Fragment, Required = false)]
    GpuUniformBuffer? LumOnWorldProbe { get; }
    #endregion
}
