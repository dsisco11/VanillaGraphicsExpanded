using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.LumOn;

/// <summary>Declares the GPU binding contract for LumOnDebugShaderProgram.</summary>
[ShaderBindingSet(typeof(IShaderInterfaceLocations), Defaults = true)]
[ShaderBindingSet(typeof(IShaderIncludeBindings), Defaults = true)]
internal interface ILumOnDebugShaderProgramBindings
{
    #region Public API
    /// <summary>Declares the LumOnFrameUBO UniformBlock slot.</summary>
    [ShaderBinding("LumOnFrameUBO", ShaderBindingKind.UniformBlock, GpuBindingRegistry.Ubo.Frame, ShaderStageKind.Vertex, ShaderStageKind.Fragment)]
    GpuUniformBuffer LumOnFrame { set; }
    /// <summary>Declares the LumOnWorldProbeUBO UniformBlock slot.</summary>
    [ShaderBinding("LumOnWorldProbeUBO", ShaderBindingKind.UniformBlock, GpuBindingRegistry.Ubo.WorldProbe, ShaderStageKind.Vertex, ShaderStageKind.Fragment, Required = false)]
    GpuUniformBuffer LumOnWorldProbe { set; }
    /// <summary>Declares the LumOnTerrainBridgeUBO UniformBlock slot.</summary>
    [ShaderBinding("LumOnTerrainBridgeUBO", ShaderBindingKind.UniformBlock, GpuBindingRegistry.Ubo.TerrainBridge, ShaderStageKind.Vertex, ShaderStageKind.Fragment, Required = false)]
    GpuUniformBuffer LumOnTerrainBridge { set; }
    /// <summary>Declares the VgeLumOnDebugParamsUBO UniformBlock slot.</summary>
    [ShaderBinding("VgeLumOnDebugParamsUBO", ShaderBindingKind.UniformBlock, GpuBindingRegistry.Ubo.Object, ShaderStageKind.Vertex, ShaderStageKind.Fragment)]
    GpuUniformBuffer Parameters { set; }
    /// <summary>Declares the primaryDepth Sampler slot.</summary>
    [ShaderBinding("primaryDepth", ShaderBindingKind.Sampler, 0, ShaderStageKind.Vertex, ShaderStageKind.Fragment)]
    int PrimaryDepth { set; }
    /// <summary>Declares the gBufferNormal Sampler slot.</summary>
    [ShaderBinding("gBufferNormal", ShaderBindingKind.Sampler, 1, ShaderStageKind.Vertex, ShaderStageKind.Fragment)]
    int GBufferNormal { set; }
    /// <summary>Declares the probeAnchorPosition Sampler slot.</summary>
    [ShaderBinding("probeAnchorPosition", ShaderBindingKind.Sampler, 2, ShaderStageKind.Vertex, ShaderStageKind.Fragment, Required = false)]
    GpuTexture? ProbeAnchorPosition { set; }
    /// <summary>Declares the probeAnchorNormal Sampler slot.</summary>
    [ShaderBinding("probeAnchorNormal", ShaderBindingKind.Sampler, 3, ShaderStageKind.Vertex, ShaderStageKind.Fragment, Required = false)]
    GpuTexture? ProbeAnchorNormal { set; }
    /// <summary>Declares the radianceTexture0 Sampler slot.</summary>
    [ShaderBinding("radianceTexture0", ShaderBindingKind.Sampler, 4, ShaderStageKind.Vertex, ShaderStageKind.Fragment, Required = false)]
    GpuTexture? RadianceTexture0 { set; }
    /// <summary>Declares the radianceTexture1 Sampler slot.</summary>
    [ShaderBinding("radianceTexture1", ShaderBindingKind.Sampler, 5, ShaderStageKind.Vertex, ShaderStageKind.Fragment, Required = false)]
    GpuTexture? RadianceTexture1 { set; }
    /// <summary>Declares the indirectHalf Sampler slot.</summary>
    [ShaderBinding("indirectHalf", ShaderBindingKind.Sampler, 6, ShaderStageKind.Vertex, ShaderStageKind.Fragment, Required = false)]
    GpuTexture? IndirectHalf { set; }
    /// <summary>Declares the historyMeta Sampler slot.</summary>
    [ShaderBinding("historyMeta", ShaderBindingKind.Sampler, 7, ShaderStageKind.Vertex, ShaderStageKind.Fragment, Required = false)]
    GpuTexture? HistoryMeta { set; }
    /// <summary>Declares the probeAtlasMeta Sampler slot.</summary>
    [ShaderBinding("probeAtlasMeta", ShaderBindingKind.Sampler, 8, ShaderStageKind.Vertex, ShaderStageKind.Fragment, Required = false)]
    GpuTexture? ProbeAtlasMeta { set; }
    /// <summary>Declares the probeAtlasCurrent Sampler slot.</summary>
    [ShaderBinding("probeAtlasCurrent", ShaderBindingKind.Sampler, 9, ShaderStageKind.Vertex, ShaderStageKind.Fragment, Required = false)]
    GpuTexture? ProbeAtlasCurrent { set; }
    /// <summary>Declares the probeAtlasFiltered Sampler slot.</summary>
    [ShaderBinding("probeAtlasFiltered", ShaderBindingKind.Sampler, 10, ShaderStageKind.Vertex, ShaderStageKind.Fragment, Required = false)]
    GpuTexture? ProbeAtlasFiltered { set; }
    /// <summary>Declares the probeAtlasGatherInput Sampler slot.</summary>
    [ShaderBinding("probeAtlasGatherInput", ShaderBindingKind.Sampler, 11, ShaderStageKind.Vertex, ShaderStageKind.Fragment, Required = false)]
    GpuTexture? ProbeAtlasGatherInput { set; }
    /// <summary>Declares the indirectDiffuseFull Sampler slot.</summary>
    [ShaderBinding("indirectDiffuseFull", ShaderBindingKind.Sampler, 12, ShaderStageKind.Vertex, ShaderStageKind.Fragment, Required = false)]
    GpuTexture? IndirectDiffuseFull { set; }
    /// <summary>Declares the gBufferAlbedo Sampler slot.</summary>
    [ShaderBinding("gBufferAlbedo", ShaderBindingKind.Sampler, 13, ShaderStageKind.Vertex, ShaderStageKind.Fragment, Required = false)]
    GpuTexture? GBufferAlbedo { set; }
    /// <summary>Declares the gBufferMaterial Sampler slot.</summary>
    [ShaderBinding("gBufferMaterial", ShaderBindingKind.Sampler, 14, ShaderStageKind.Vertex, ShaderStageKind.Fragment, Required = false)]
    int GBufferMaterial { set; }
    /// <summary>Declares the directDiffuse Sampler slot.</summary>
    [ShaderBinding("directDiffuse", ShaderBindingKind.Sampler, 15, ShaderStageKind.Vertex, ShaderStageKind.Fragment, Required = false)]
    GpuTexture? DirectDiffuse { set; }
    /// <summary>Declares the directSpecular Sampler slot.</summary>
    [ShaderBinding("directSpecular", ShaderBindingKind.Sampler, 16, ShaderStageKind.Vertex, ShaderStageKind.Fragment, Required = false)]
    GpuTexture? DirectSpecular { set; }
    /// <summary>Declares the emissive Sampler slot.</summary>
    [ShaderBinding("emissive", ShaderBindingKind.Sampler, 17, ShaderStageKind.Vertex, ShaderStageKind.Fragment, Required = false)]
    GpuTexture? Emissive { set; }
    /// <summary>Declares the velocityTex Sampler slot.</summary>
    [ShaderBinding("velocityTex", ShaderBindingKind.Sampler, 18, ShaderStageKind.Vertex, ShaderStageKind.Fragment, Required = false)]
    GpuTexture? VelocityTex { set; }
    /// <summary>Declares the worldProbeRadianceAtlas Sampler slot.</summary>
    [ShaderBinding("worldProbeRadianceAtlas", ShaderBindingKind.Sampler, 19, ShaderStageKind.Vertex, ShaderStageKind.Fragment, Required = false)]
    GpuTexture? WorldProbeRadianceAtlas { set; }
    /// <summary>Declares the traceSceneLegacy Sampler slot.</summary>
    [ShaderBinding("traceSceneLegacy", ShaderBindingKind.Sampler, 20, ShaderStageKind.Vertex, ShaderStageKind.Fragment, Required = false)]
    GpuTexture? TraceSceneLegacy { set; }
    /// <summary>Declares the worldProbeSuppressedLighting Sampler slot.</summary>
    [ShaderBinding("worldProbeSuppressedLighting", ShaderBindingKind.Sampler, 21, ShaderStageKind.Vertex, ShaderStageKind.Fragment, Required = false)]
    GpuTexture WorldProbeSuppressedLightingTexture { set; }
    /// <summary>Declares the worldProbeVis0 Sampler slot.</summary>
    [ShaderBinding("worldProbeVis0", ShaderBindingKind.Sampler, 22, ShaderStageKind.Vertex, ShaderStageKind.Fragment, Required = false)]
    GpuTexture? WorldProbeVis0 { set; }
    /// <summary>Declares the worldProbeDist0 Sampler slot.</summary>
    [ShaderBinding("worldProbeDist0", ShaderBindingKind.Sampler, 23, ShaderStageKind.Vertex, ShaderStageKind.Fragment, Required = false)]
    GpuTexture? WorldProbeDist0 { set; }
    /// <summary>Declares the worldProbeMeta0 Sampler slot.</summary>
    [ShaderBinding("worldProbeMeta0", ShaderBindingKind.Sampler, 24, ShaderStageKind.Vertex, ShaderStageKind.Fragment, Required = false)]
    GpuTexture? WorldProbeMeta0 { set; }
    /// <summary>Declares the worldProbeDebugState0 Sampler slot.</summary>
    [ShaderBinding("worldProbeDebugState0", ShaderBindingKind.Sampler, 25, ShaderStageKind.Vertex, ShaderStageKind.Fragment, Required = false)]
    GpuTexture? WorldProbeDebugState0 { set; }
    /// <summary>Declares the probeTraceMask Sampler slot.</summary>
    [ShaderBinding("probeTraceMask", ShaderBindingKind.Sampler, 26, ShaderStageKind.Vertex, ShaderStageKind.Fragment, Required = false)]
    GpuTexture? ProbeTraceMask { set; }
    /// <summary>Declares the probeAtlasTrace Sampler slot.</summary>
    [ShaderBinding("probeAtlasTrace", ShaderBindingKind.Sampler, 27, ShaderStageKind.Vertex, ShaderStageKind.Fragment, Required = false)]
    GpuTexture? ProbeAtlasTrace { set; }
    /// <summary>Declares the probePisEnergy Sampler slot.</summary>
    [ShaderBinding("probePisEnergy", ShaderBindingKind.Sampler, 28, ShaderStageKind.Vertex, ShaderStageKind.Fragment, Required = false)]
    GpuTexture? ProbePisEnergy { set; }
    /// <summary>Declares the gBufferPatchId Sampler slot.</summary>
    [ShaderBinding("gBufferPatchId", ShaderBindingKind.Sampler, 29, ShaderStageKind.Vertex, ShaderStageKind.Fragment, Required = false)]
    int GBufferPatchId { set; }
    /// <summary>Declares the vge_lumonScenePageTableMip0 Sampler slot.</summary>
    [ShaderBinding("vge_lumonScenePageTableMip0", ShaderBindingKind.Sampler, 30, ShaderStageKind.Vertex, ShaderStageKind.Fragment, Required = false)]
    GpuTexture? LumonScenePageTableMip0 { set; }
    /// <summary>Declares the vge_lumonSceneIrradianceAtlas Sampler slot.</summary>
    [ShaderBinding("vge_lumonSceneIrradianceAtlas", ShaderBindingKind.Sampler, 31, ShaderStageKind.Vertex, ShaderStageKind.Fragment, Required = false)]
    GpuTexture? LumonSceneIrradianceAtlas { set; }
    /// <summary>Declares the vge_lumonSceneMaterialAtlas Sampler slot.</summary>
    [ShaderBinding("vge_lumonSceneMaterialAtlas", ShaderBindingKind.Sampler, 32, ShaderStageKind.Vertex, ShaderStageKind.Fragment, Required = false)]
    GpuTexture? LumonSceneMaterialAtlas { set; }
    /// <summary>Declares the vge_lumonSceneSurfaceLut Sampler slot.</summary>
    [ShaderBinding("vge_lumonSceneSurfaceLut", ShaderBindingKind.Sampler, 33, ShaderStageKind.Vertex, ShaderStageKind.Fragment, Required = false)]
    GpuTexture? LumonSceneSurfaceLut { set; }
    /// <summary>Declares the LumOnNearFieldUBO UniformBlock slot.</summary>
    [ShaderBinding("LumOnNearFieldUBO", ShaderBindingKind.UniformBlock, GpuBindingRegistry.Ubo.Material, ShaderStageKind.Vertex, ShaderStageKind.Fragment, Required = false)]
    GpuUniformBuffer LumOnNearField { set; }
    /// <summary>Declares the nearFieldGeometry Sampler slot.</summary>
    [ShaderBinding("nearFieldGeometry", ShaderBindingKind.Sampler, 34, ShaderStageKind.Vertex, ShaderStageKind.Fragment, Required = false)]
    GpuTexture NearFieldGeometry { set; }
    /// <summary>Declares the nearFieldRegions Sampler slot.</summary>
    [ShaderBinding("nearFieldRegions", ShaderBindingKind.Sampler, 35, ShaderStageKind.Vertex, ShaderStageKind.Fragment, Required = false)]
    GpuTexture NearFieldRegions { set; }
    #endregion
}
