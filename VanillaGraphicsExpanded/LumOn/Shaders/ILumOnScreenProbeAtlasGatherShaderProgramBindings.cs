using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.LumOn;

/// <summary>Declares the GPU binding contract for LumOnScreenProbeAtlasGatherShaderProgram.</summary>
[ShaderBindingSet(typeof(IShaderInterfaceLocations), Defaults = true)]
[ShaderBindingSet(typeof(IShaderIncludeBindings), Defaults = true)]
internal interface ILumOnScreenProbeAtlasGatherShaderProgramBindings
{
    #region Public API
    /// <summary>Declares the octahedralAtlas Sampler slot.</summary>
    [ShaderBinding("octahedralAtlas", ShaderBindingKind.Sampler, 0, ShaderStageKind.Vertex, ShaderStageKind.Fragment)]
    GpuTexture? ScreenProbeAtlas { set; }
    /// <summary>Declares the probeAnchorPosition Sampler slot.</summary>
    [ShaderBinding("probeAnchorPosition", ShaderBindingKind.Sampler, 1, ShaderStageKind.Vertex, ShaderStageKind.Fragment)]
    GpuTexture? ProbeAnchorPosition { set; }
    /// <summary>Declares the probeAnchorNormal Sampler slot.</summary>
    [ShaderBinding("probeAnchorNormal", ShaderBindingKind.Sampler, 2, ShaderStageKind.Vertex, ShaderStageKind.Fragment)]
    GpuTexture? ProbeAnchorNormal { set; }
    /// <summary>Declares the primaryDepth Sampler slot.</summary>
    [ShaderBinding("primaryDepth", ShaderBindingKind.Sampler, 3, ShaderStageKind.Vertex, ShaderStageKind.Fragment, TextureTarget = ShaderTextureTarget.Texture2D, Sampler = ShaderSamplerPolicy.NearestClamp)]
    int PrimaryDepth { set; }
    /// <summary>Declares the gBufferNormal Sampler slot.</summary>
    [ShaderBinding("gBufferNormal", ShaderBindingKind.Sampler, 4, ShaderStageKind.Vertex, ShaderStageKind.Fragment, TextureTarget = ShaderTextureTarget.Texture2D, Sampler = ShaderSamplerPolicy.NearestClamp)]
    int GBufferNormal { set; }
    /// <summary>Declares the worldProbeRadianceAtlas Sampler slot.</summary>
    [ShaderBinding("worldProbeRadianceAtlas", ShaderBindingKind.Sampler, 5, ShaderStageKind.Vertex, ShaderStageKind.Fragment, Required = false)]
    GpuTexture? WorldProbeRadianceAtlas { set; }
    /// <summary>Declares the worldProbeVis0 Sampler slot.</summary>
    [ShaderBinding("worldProbeVis0", ShaderBindingKind.Sampler, 8, ShaderStageKind.Vertex, ShaderStageKind.Fragment, Required = false)]
    GpuTexture? WorldProbeVis0 { set; }
    /// <summary>Declares the worldProbeMeta0 Sampler slot.</summary>
    [ShaderBinding("worldProbeMeta0", ShaderBindingKind.Sampler, 9, ShaderStageKind.Vertex, ShaderStageKind.Fragment, Required = false)]
    GpuTexture? WorldProbeMeta0 { set; }
    /// <summary>Declares the LumOnFrameUBO UniformBlock slot.</summary>
    [ShaderBinding("LumOnFrameUBO", ShaderBindingKind.UniformBlock, GpuBindingRegistry.Ubo.Frame, ShaderStageKind.Vertex, ShaderStageKind.Fragment)]
    GpuUniformBuffer? LumOnFrame { get; }
    /// <summary>Declares the VgeLumOnProbeParamsUBO UniformBlock slot.</summary>
    [ShaderBinding("VgeLumOnProbeParamsUBO", ShaderBindingKind.UniformBlock, GpuBindingRegistry.Ubo.Object, ShaderStageKind.Vertex, ShaderStageKind.Fragment)]
    CpuUniformBuffer Parameters { get; }
    /// <summary>Declares the LumOnNearFieldUBO UniformBlock slot.</summary>
    [ShaderBinding("LumOnNearFieldUBO", ShaderBindingKind.UniformBlock, GpuBindingRegistry.Ubo.Material, ShaderStageKind.Vertex, ShaderStageKind.Fragment, Required = false)]
    CpuUniformBuffer LumOnNearField { get; }
    /// <summary>Declares the nearFieldGeometry Sampler slot.</summary>
    [ShaderBinding("nearFieldGeometry", ShaderBindingKind.Sampler, 6, ShaderStageKind.Vertex, ShaderStageKind.Fragment, Required = false, TextureTarget = ShaderTextureTarget.Texture3D, Sampler = ShaderSamplerPolicy.NearestClamp)]
    GpuTexture? NearFieldGeometry { get; }
    /// <summary>Declares the nearFieldRegions Sampler slot.</summary>
    [ShaderBinding("nearFieldRegions", ShaderBindingKind.Sampler, 7, ShaderStageKind.Vertex, ShaderStageKind.Fragment, Required = false, TextureTarget = ShaderTextureTarget.Texture3D, Sampler = ShaderSamplerPolicy.NearestClamp)]
    GpuTexture? NearFieldRegions { get; }
    /// <summary>Supplies optional shared world-probe storage.</summary>
    [ShaderBinding("LumOnWorldProbeUBO", ShaderBindingKind.UniformBlock, GpuBindingRegistry.Ubo.WorldProbe, ShaderStageKind.Vertex, ShaderStageKind.Fragment, Required = false)]
    GpuUniformBuffer? LumOnWorldProbe { get; }
    #endregion
}
