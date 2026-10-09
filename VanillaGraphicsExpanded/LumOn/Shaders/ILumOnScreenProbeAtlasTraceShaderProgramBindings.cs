using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.LumOn.Scene.Shaders;

namespace VanillaGraphicsExpanded.LumOn;

/// <summary>Declares the GPU binding contract for LumOnScreenProbeAtlasTraceShaderProgram.</summary>
[ShaderBindingSet(typeof(IShaderInterfaceLocations), Defaults = true)]
[ShaderBindingSet(typeof(IShaderIncludeBindings), Defaults = true)]

internal interface ILumOnScreenProbeAtlasTraceShaderProgramBindings
{
    #region Public API
    /// <summary>Supplies common camera and frame values from the universal view snapshot.</summary>
    [ShaderBinding("VgeFrameUBO", ShaderBindingKind.UniformBlock, GpuBindingRegistry.Ubo.Frame, ShaderStageKind.Vertex, ShaderStageKind.Fragment, Required = false)]
    CpuUniformBuffer FrameInputs { get; }
    /// <summary>Declares the traceSceneFaces Sampler slot.</summary>
    [ShaderBinding("traceSceneFaces", ShaderBindingKind.Sampler, 0, ShaderStageKind.Vertex, ShaderStageKind.Fragment, Required = false)]
    GpuTexture? TraceSceneFaces { set; }
    /// <summary>Declares the position and normal anchor array sampler.</summary>
    [ShaderBinding("probeAnchors", ShaderBindingKind.Sampler, 1, ShaderStageKind.Vertex, ShaderStageKind.Fragment, TextureTarget = ShaderTextureTarget.Texture2DArray, Sampler = ShaderSamplerPolicy.NearestClamp)]
    GpuTexture? ProbeAnchors { set; }
    /// <summary>Declares the primaryDepth Sampler slot.</summary>
    [ShaderBinding("primaryDepth", ShaderBindingKind.Sampler, 2, ShaderStageKind.Vertex, ShaderStageKind.Fragment, TextureTarget = ShaderTextureTarget.Texture2D, Sampler = ShaderSamplerPolicy.NearestClamp)]
    int PrimaryDepth { set; }
    /// <summary>Declares the surfaceAlbedo Sampler slot.</summary>
    [ShaderBinding("surfaceAlbedo", ShaderBindingKind.Sampler, 3, ShaderStageKind.Vertex, ShaderStageKind.Fragment)]
    GpuTexture? SurfaceAlbedo { set; }
    /// <summary>Declares the normal, material and environment surface array sampler.</summary>
    [ShaderBinding("gBufferSurface", ShaderBindingKind.Sampler, 4, ShaderStageKind.Vertex, ShaderStageKind.Fragment, TextureTarget = ShaderTextureTarget.Texture2DArray, Sampler = ShaderSamplerPolicy.NearestClamp)]
    GpuTexture? GBufferSurface { set; }
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
    [ShaderBinding("worldProbeVis0", ShaderBindingKind.Sampler, 10, ShaderStageKind.Vertex, ShaderStageKind.Fragment, Required = false)]
    GpuTexture? WorldProbeVis0 { set; }
    /// <summary>Declares the worldProbeMeta0 Sampler slot.</summary>
    [ShaderBinding("worldProbeMeta0", ShaderBindingKind.Sampler, 11, ShaderStageKind.Vertex, ShaderStageKind.Fragment, Required = false)]
    GpuTexture? WorldProbeMeta0 { set; }
    /// <summary>Declares the nearFieldGeometry Sampler slot.</summary>
    [ShaderBinding("nearFieldGeometry", ShaderBindingKind.Sampler, 12, ShaderStageKind.Vertex, ShaderStageKind.Fragment, Required = false, TextureTarget = ShaderTextureTarget.Texture3D, Sampler = ShaderSamplerPolicy.NearestClamp)]
    GpuTexture? NearFieldGeometry { set; }
    /// <summary>Declares the nearFieldLight Sampler slot.</summary>
    [ShaderBinding("nearFieldLight", ShaderBindingKind.Sampler, 13, ShaderStageKind.Vertex, ShaderStageKind.Fragment, Required = false, TextureTarget = ShaderTextureTarget.Texture3D, Sampler = ShaderSamplerPolicy.NearestClamp)]
    GpuTexture? NearFieldLight { set; }
    /// <summary>Declares the nearFieldRegions Sampler slot.</summary>
    [ShaderBinding("nearFieldRegions", ShaderBindingKind.Sampler, 14, ShaderStageKind.Vertex, ShaderStageKind.Fragment, Required = false, TextureTarget = ShaderTextureTarget.Texture3D, Sampler = ShaderSamplerPolicy.NearestClamp)]
    GpuTexture? NearFieldRegions { set; }
    /// <summary>Declares the nearFieldMaterials Sampler slot.</summary>
    [ShaderBinding("nearFieldMaterials", ShaderBindingKind.Sampler, 15, ShaderStageKind.Vertex, ShaderStageKind.Fragment, Required = false)]
    GpuTexture? NearFieldMaterials { set; }
    /// <summary>Declares the LumOnFrameUBO UniformBlock slot.</summary>
    [ShaderBinding("LumOnFrameUBO", ShaderBindingKind.UniformBlock, GpuBindingRegistry.Ubo.LumOnFrame, ShaderStageKind.Vertex, ShaderStageKind.Fragment)]
    GpuUniformBuffer? LumOnFrame { get; }
    /// <summary>Declares the LumOnWorldProbeUBO UniformBlock slot.</summary>
    [ShaderBinding("LumOnWorldProbeUBO", ShaderBindingKind.UniformBlock, GpuBindingRegistry.Ubo.WorldProbe, ShaderStageKind.Vertex, ShaderStageKind.Fragment, Required = false)]
    GpuUniformBuffer? LumOnWorldProbe { get; }
    /// <summary>Declares the LumOnNearFieldUBO UniformBlock slot.</summary>
    [ShaderBinding("LumOnNearFieldUBO", ShaderBindingKind.UniformBlock, GpuBindingRegistry.Ubo.Material, ShaderStageKind.Vertex, ShaderStageKind.Fragment, Required = false)]
    CpuUniformBuffer LumOnNearField { get; }
    /// <summary>Declares the VgeLumOnProbeParamsUBO UniformBlock slot.</summary>
    [ShaderBinding("VgeLumOnProbeParamsUBO", ShaderBindingKind.UniformBlock, GpuBindingRegistry.Ubo.Object, ShaderStageKind.Vertex, ShaderStageKind.Fragment)]
    CpuUniformBuffer Parameters { get; }
    /// <summary>Declares the SurfaceLightingParams UniformBlock slot.</summary>
    [ShaderBinding("SurfaceLightingParams", ShaderBindingKind.UniformBlock, GpuBindingRegistry.Ubo.Lights, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Compute, Required = false)]
    CpuUniformBuffer SurfaceLightingParameters { get; }
    /// <summary>Declares the capturedMaterial Sampler slot.</summary>
    [ShaderBinding("capturedMaterial", ShaderBindingKind.Sampler, 16, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Compute, Required = false, TextureTarget = ShaderTextureTarget.Texture2DArray, Sampler = ShaderSamplerPolicy.NearestClamp)]
    GpuTexture? CapturedMaterial { get; }
    /// <summary>Declares the previousOutgoing Sampler slot.</summary>
    [ShaderBinding("previousOutgoing", ShaderBindingKind.Sampler, 17, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Compute, Required = false, TextureTarget = ShaderTextureTarget.Texture2DArray, Sampler = ShaderSamplerPolicy.NearestClamp)]
    GpuTexture? PreviousOutgoing { get; }
    /// <summary>Declares the surfacePages Sampler slot.</summary>
    [ShaderBinding("surfacePages", ShaderBindingKind.Sampler, 18, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Compute, Required = false, TextureTarget = ShaderTextureTarget.Texture2DArray, Sampler = ShaderSamplerPolicy.NearestClamp)]
    GpuTexture? SurfacePages { get; }
    /// <summary>Declares the SurfacePatches StorageBlock slot.</summary>
    [ShaderBinding("SurfacePatches", ShaderBindingKind.StorageBlock, 1, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Compute, Required = false)]
    GpuShaderStorageBuffer? SurfacePatches { get; }
    /// <summary>Declares the SurfaceSlots StorageBlock slot.</summary>
    [ShaderBinding("SurfaceSlots", ShaderBindingKind.StorageBlock, 2, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Compute, Required = false)]
    GpuShaderStorageBuffer? SurfaceSlots { get; }
    /// <summary>Declares the SurfaceReady StorageBlock slot.</summary>
    [ShaderBinding("SurfaceReady", ShaderBindingKind.StorageBlock, 3, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Compute, Required = false)]
    GpuShaderStorageBuffer? SurfaceReady { get; }
    #endregion
}
