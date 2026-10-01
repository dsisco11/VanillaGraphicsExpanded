using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.LumOn.Scene.Shaders;

namespace VanillaGraphicsExpanded.LumOn;

/// <summary>Declares the GPU binding contract for LumOnScreenProbeAtlasTraceShaderProgram.</summary>
[ShaderBindingSet(typeof(IShaderInterfaceLocations), Defaults = true)]
[ShaderBindingSet(typeof(IShaderIncludeBindings), Defaults = true)]
[ShaderBindingSet(typeof(ISurfaceLightingBindingSet), Program = "Contract")]
internal interface ILumOnScreenProbeAtlasTraceShaderProgramBindings
{
    #region Public API
    /// <summary>Declares the traceSceneFaces Sampler slot.</summary>
    [ShaderBinding("traceSceneFaces", ShaderBindingKind.Sampler, 19, ShaderStageKind.Vertex, ShaderStageKind.Fragment, Required = false)]
    GpuTexture TraceSceneFaces { set; }
    /// <summary>Declares the probeAnchorPosition Sampler slot.</summary>
    [ShaderBinding("probeAnchorPosition", ShaderBindingKind.Sampler, 0, ShaderStageKind.Vertex, ShaderStageKind.Fragment)]
    GpuTexture? ProbeAnchorPosition { set; }
    /// <summary>Declares the probeAnchorNormal Sampler slot.</summary>
    [ShaderBinding("probeAnchorNormal", ShaderBindingKind.Sampler, 1, ShaderStageKind.Vertex, ShaderStageKind.Fragment)]
    GpuTexture? ProbeAnchorNormal { set; }
    /// <summary>Declares the primaryDepth Sampler slot.</summary>
    [ShaderBinding("primaryDepth", ShaderBindingKind.Sampler, 2, ShaderStageKind.Vertex, ShaderStageKind.Fragment)]
    int PrimaryDepth { set; }
    /// <summary>Declares the surfaceAlbedo Sampler slot.</summary>
    [ShaderBinding("surfaceAlbedo", ShaderBindingKind.Sampler, 3, ShaderStageKind.Vertex, ShaderStageKind.Fragment)]
    GpuTexture? SurfaceAlbedo { set; }
    /// <summary>Declares the gBufferMaterial Sampler slot.</summary>
    [ShaderBinding("gBufferMaterial", ShaderBindingKind.Sampler, 4, ShaderStageKind.Vertex, ShaderStageKind.Fragment)]
    int GBufferMaterial { set; }
    /// <summary>Declares the octahedralHistory Sampler slot.</summary>
    [ShaderBinding("octahedralHistory", ShaderBindingKind.Sampler, 5, ShaderStageKind.Vertex, ShaderStageKind.Fragment)]
    GpuTexture? ScreenProbeAtlasHistory { set; }
    /// <summary>Declares the hzbDepth Sampler slot.</summary>
    [ShaderBinding("hzbDepth", ShaderBindingKind.Sampler, 6, ShaderStageKind.Vertex, ShaderStageKind.Fragment)]
    GpuTexture? HzbDepth { set; }
    /// <summary>Declares the probeAtlasMetaHistory Sampler slot.</summary>
    [ShaderBinding("probeAtlasMetaHistory", ShaderBindingKind.Sampler, 7, ShaderStageKind.Vertex, ShaderStageKind.Fragment)]
    GpuTexture? ScreenProbeAtlasMetaHistory { set; }
    /// <summary>Declares the worldProbeRadianceAtlas Sampler slot.</summary>
    [ShaderBinding("worldProbeRadianceAtlas", ShaderBindingKind.Sampler, 8, ShaderStageKind.Vertex, ShaderStageKind.Fragment, Required = false)]
    GpuTexture? WorldProbeRadianceAtlas { set; }
    /// <summary>Declares the probeTraceMask Sampler slot.</summary>
    [ShaderBinding("probeTraceMask", ShaderBindingKind.Sampler, 9, ShaderStageKind.Vertex, ShaderStageKind.Fragment)]
    GpuTexture? ProbeTraceMask { set; }
    /// <summary>Declares the worldProbeVis0 Sampler slot.</summary>
    [ShaderBinding("worldProbeVis0", ShaderBindingKind.Sampler, 11, ShaderStageKind.Vertex, ShaderStageKind.Fragment, Required = false)]
    GpuTexture? WorldProbeVis0 { set; }
    /// <summary>Declares the worldProbeMeta0 Sampler slot.</summary>
    [ShaderBinding("worldProbeMeta0", ShaderBindingKind.Sampler, 12, ShaderStageKind.Vertex, ShaderStageKind.Fragment, Required = false)]
    GpuTexture? WorldProbeMeta0 { set; }
    /// <summary>Declares the nearFieldGeometry Sampler slot.</summary>
    [ShaderBinding("nearFieldGeometry", ShaderBindingKind.Sampler, 10, ShaderStageKind.Vertex, ShaderStageKind.Fragment, Required = false)]
    GpuTexture NearFieldGeometry { set; }
    /// <summary>Declares the nearFieldLight Sampler slot.</summary>
    [ShaderBinding("nearFieldLight", ShaderBindingKind.Sampler, 13, ShaderStageKind.Vertex, ShaderStageKind.Fragment, Required = false)]
    GpuTexture NearFieldLight { set; }
    /// <summary>Declares the nearFieldRegions Sampler slot.</summary>
    [ShaderBinding("nearFieldRegions", ShaderBindingKind.Sampler, 14, ShaderStageKind.Vertex, ShaderStageKind.Fragment, Required = false)]
    GpuTexture NearFieldRegions { set; }
    /// <summary>Declares the nearFieldMaterials Sampler slot.</summary>
    [ShaderBinding("nearFieldMaterials", ShaderBindingKind.Sampler, 15, ShaderStageKind.Vertex, ShaderStageKind.Fragment, Required = false)]
    GpuTexture NearFieldMaterials { set; }
    /// <summary>Declares the LumOnFrameUBO UniformBlock slot.</summary>
    [ShaderBinding("LumOnFrameUBO", ShaderBindingKind.UniformBlock, GpuBindingRegistry.Ubo.Frame, ShaderStageKind.Vertex, ShaderStageKind.Fragment)]
    GpuUniformBuffer LumOnFrame { set; }
    /// <summary>Declares the LumOnWorldProbeUBO UniformBlock slot.</summary>
    [ShaderBinding("LumOnWorldProbeUBO", ShaderBindingKind.UniformBlock, GpuBindingRegistry.Ubo.WorldProbe, ShaderStageKind.Vertex, ShaderStageKind.Fragment, Required = false)]
    GpuUniformBuffer LumOnWorldProbe { set; }
    /// <summary>Declares the LumOnNearFieldUBO UniformBlock slot.</summary>
    [ShaderBinding("LumOnNearFieldUBO", ShaderBindingKind.UniformBlock, GpuBindingRegistry.Ubo.Material, ShaderStageKind.Vertex, ShaderStageKind.Fragment, Required = false)]
    GpuUniformBuffer LumOnNearField { set; }
    /// <summary>Declares the VgeLumOnProbeParamsUBO UniformBlock slot.</summary>
    [ShaderBinding("VgeLumOnProbeParamsUBO", ShaderBindingKind.UniformBlock, GpuBindingRegistry.Ubo.Object, ShaderStageKind.Vertex, ShaderStageKind.Fragment)]
    GpuUniformBuffer Parameters { set; }
    #endregion
}
