using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.PBR.Liquids;

/// <summary>Declares the GPU binding contract for LiquidShaderProgram.</summary>
[ShaderBindingSet(typeof(IShaderInterfaceLocations), Defaults = true)]
[ShaderBindingSet(typeof(IShaderIncludeBindings), Defaults = true)]
internal interface ILiquidShaderProgramBindings
{
    #region Public API
    /// <summary>Declares the VgeLiquidFrameParams UniformBlock slot.</summary>
    [ShaderBinding("VgeLiquidFrameParams", ShaderBindingKind.UniformBlock, GpuBindingRegistry.Ubo.Frame, ShaderStageKind.Vertex, ShaderStageKind.Fragment)]
    GpuUniformBuffer FrameParameters { set; }
    /// <summary>Declares the VgeLiquidDrawParams UniformBlock slot.</summary>
    [ShaderBinding("VgeLiquidDrawParams", ShaderBindingKind.UniformBlock, GpuBindingRegistry.Ubo.Object, ShaderStageKind.Vertex, ShaderStageKind.Fragment)]
    GpuUniformBuffer DrawParameters { set; }
    /// <summary>Declares the VgeLiquidWaveParams UniformBlock slot.</summary>
    [ShaderBinding("VgeLiquidWaveParams", ShaderBindingKind.UniformBlock, 15, ShaderStageKind.Vertex, ShaderStageKind.Fragment)]
    GpuUniformBuffer WaveParameters { set; }
    /// <summary>Declares the terrainTex Sampler slot.</summary>
    [ShaderBinding("terrainTex", ShaderBindingKind.Sampler, 0, ShaderStageKind.Vertex, ShaderStageKind.Fragment)]
    int TerrainTexture { set; }
    /// <summary>Declares the depthTex Sampler slot.</summary>
    [ShaderBinding("depthTex", ShaderBindingKind.Sampler, 1, ShaderStageKind.Vertex, ShaderStageKind.Fragment)]
    int DepthTexture { set; }
    /// <summary>Declares the vge_materialParamsTex Sampler slot.</summary>
    [ShaderBinding("vge_materialParamsTex", ShaderBindingKind.Sampler, 2, ShaderStageKind.Vertex, ShaderStageKind.Fragment)]
    int MaterialParamsTexture { set; }
    /// <summary>Declares the shadowMapNear Sampler slot.</summary>
    [ShaderBinding("shadowMapNear", ShaderBindingKind.Sampler, 3, ShaderStageKind.Vertex, ShaderStageKind.Fragment)]
    int ShadowMapNear { set; }
    /// <summary>Declares the shadowMapFar Sampler slot.</summary>
    [ShaderBinding("shadowMapFar", ShaderBindingKind.Sampler, 4, ShaderStageKind.Vertex, ShaderStageKind.Fragment)]
    int ShadowMapFar { set; }
    /// <summary>Declares the vge_atmosphereAerialRadiance Sampler slot.</summary>
    [ShaderBinding("vge_atmosphereAerialRadiance", ShaderBindingKind.Sampler, 5, ShaderStageKind.Vertex, ShaderStageKind.Fragment)]
    int AerialRadianceTexture { set; }
    /// <summary>Declares the vge_atmosphereAerialAttenuation Sampler slot.</summary>
    [ShaderBinding("vge_atmosphereAerialAttenuation", ShaderBindingKind.Sampler, 6, ShaderStageKind.Vertex, ShaderStageKind.Fragment)]
    int AerialAttenuationTexture { set; }
    /// <summary>Declares the terrainTex UniformLocation slot.</summary>
    [ShaderBinding("terrainTex", ShaderBindingKind.UniformLocation, 100, ShaderStageKind.Vertex, ShaderStageKind.Fragment)]
    ShaderUniformLocationBinding TerrainTexLocation { get; }
    /// <summary>Declares the depthTex UniformLocation slot.</summary>
    [ShaderBinding("depthTex", ShaderBindingKind.UniformLocation, 101, ShaderStageKind.Vertex, ShaderStageKind.Fragment)]
    ShaderUniformLocationBinding DepthTexLocation { get; }
    /// <summary>Declares the vge_materialParamsTex UniformLocation slot.</summary>
    [ShaderBinding("vge_materialParamsTex", ShaderBindingKind.UniformLocation, 102, ShaderStageKind.Vertex, ShaderStageKind.Fragment)]
    ShaderUniformLocationBinding MaterialParamsTexLocation { get; }
    #endregion
}
