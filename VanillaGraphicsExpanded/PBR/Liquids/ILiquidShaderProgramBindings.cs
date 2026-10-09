using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.PBR.Liquids;

/// <summary>Declares the GPU binding contract for LiquidShaderProgram.</summary>
[ShaderBindingSet(typeof(IShaderInterfaceLocations), Defaults = true)]
[ShaderBindingSet(typeof(IShaderIncludeBindings), Defaults = true)]
internal interface ILiquidShaderProgramBindings
{
    #region Public API
    /// <summary>Supplies current-frame occlusion for atmospheric in-scattering only.</summary>
    [ShaderBinding("vge_lightShaftOcclusion", ShaderBindingKind.Sampler, 11, ShaderStageKind.Fragment, TextureTarget = ShaderTextureTarget.Texture2D, Sampler = ShaderSamplerPolicy.LinearClamp, Required = false)]
    GpuTexture? LightShaftOcclusion { set; }
    /// <summary>Declares optional immutable opaque radiance.</summary>
    [ShaderBinding("vge_refractionColor", ShaderBindingKind.Sampler, 9, ShaderStageKind.Fragment, TextureTarget = ShaderTextureTarget.Texture2D, Sampler = ShaderSamplerPolicy.NearestClamp, Required = false)]
    DynamicTexture2D? RefractionColorTexture { set; }
    /// <summary>Declares optional immutable opaque depth.</summary>
    [ShaderBinding("vge_refractionDepth", ShaderBindingKind.Sampler, 10, ShaderStageKind.Fragment, TextureTarget = ShaderTextureTarget.Texture2D, Sampler = ShaderSamplerPolicy.NearestClamp, Required = false)]
    DynamicTexture2D? RefractionDepthTexture { set; }
    /// <summary>Supplies the universal camera snapshot shared with other world passes.</summary>
    [ShaderBinding("VgeFrameUBO", ShaderBindingKind.UniformBlock, GpuBindingRegistry.Ubo.Frame, ShaderStageKind.Vertex, ShaderStageKind.Fragment)]
    CpuUniformBuffer FrameInputs { get; }
    /// <summary>Declares the VgeLiquidFrameParams UniformBlock slot.</summary>
    [ShaderBinding("VgeLiquidFrameParams", ShaderBindingKind.UniformBlock, GpuBindingRegistry.Ubo.ShaderInputs, ShaderStageKind.Vertex, ShaderStageKind.Fragment)]
    CpuUniformBuffer FrameParameters { get; }
    /// <summary>Declares the VgeLiquidDrawParams UniformBlock slot.</summary>
    [ShaderBinding("VgeLiquidDrawParams", ShaderBindingKind.UniformBlock, GpuBindingRegistry.Ubo.Object, ShaderStageKind.Vertex, ShaderStageKind.Fragment)]
    CpuUniformBuffer DrawParameters { get; }
    /// <summary>Declares the VgeLiquidWaveParams UniformBlock slot.</summary>
    [ShaderBinding("VgeLiquidWaveParams", ShaderBindingKind.UniformBlock, 15, ShaderStageKind.Vertex, ShaderStageKind.Fragment)]
    CpuUniformBuffer WaveParameters { get; }
    /// <summary>Declares the terrainTex Sampler slot.</summary>
    [ShaderBinding("terrainTex", ShaderBindingKind.Sampler, 0, ShaderStageKind.Vertex, ShaderStageKind.Fragment, TextureTarget = ShaderTextureTarget.Texture2D, Sampler = ShaderSamplerPolicy.Default)]
    int TerrainTexture { set; }
    /// <summary>Declares the depthTex Sampler slot.</summary>
    [ShaderBinding("depthTex", ShaderBindingKind.Sampler, 1, ShaderStageKind.Vertex, ShaderStageKind.Fragment, TextureTarget = ShaderTextureTarget.Texture2D, Sampler = ShaderSamplerPolicy.NearestClamp)]
    int DepthTexture { set; }
    /// <summary>Declares the vge_materialParamsTex Sampler slot.</summary>
    [ShaderBinding("vge_materialParamsTex", ShaderBindingKind.Sampler, 2, ShaderStageKind.Vertex, ShaderStageKind.Fragment, TextureTarget = ShaderTextureTarget.Texture2D, Sampler = ShaderSamplerPolicy.NearestClamp)]
    Texture2D? MaterialParamsTexture { set; }
    /// <summary>Declares the optional current-generation medium index image.</summary>
    [ShaderBinding("vge_waterMediumIndices", ShaderBindingKind.Sampler, 7, ShaderStageKind.Fragment, TextureTarget = ShaderTextureTarget.Texture2D, Sampler = ShaderSamplerPolicy.NearestClamp, Required = false)]
    Texture2D? WaterMediumIndicesTexture { set; }
    /// <summary>Declares the optional current-generation coefficient table.</summary>
    [ShaderBinding("vge_waterMediumRecords", ShaderBindingKind.Sampler, 8, ShaderStageKind.Fragment, TextureTarget = ShaderTextureTarget.Texture2D, Sampler = ShaderSamplerPolicy.NearestClamp, Required = false)]
    Texture2D? WaterMediumRecordsTexture { set; }
    /// <summary>Declares the shadowMapNear Sampler slot.</summary>
    [ShaderBinding("shadowMapNear", ShaderBindingKind.Sampler, 3, ShaderStageKind.Vertex, ShaderStageKind.Fragment, TextureTarget = ShaderTextureTarget.Texture2D, Sampler = ShaderSamplerPolicy.ShadowCompareLinearClamp, Required = false)]
    int ShadowMapNear { set; }
    /// <summary>Declares the shadowMapFar Sampler slot.</summary>
    [ShaderBinding("shadowMapFar", ShaderBindingKind.Sampler, 4, ShaderStageKind.Vertex, ShaderStageKind.Fragment, TextureTarget = ShaderTextureTarget.Texture2D, Sampler = ShaderSamplerPolicy.ShadowCompareLinearClamp, Required = false)]
    int ShadowMapFar { set; }
    /// <summary>Declares the vge_atmosphereAerialRadiance Sampler slot.</summary>
    [ShaderBinding("vge_atmosphereAerialRadiance", ShaderBindingKind.Sampler, 5, ShaderStageKind.Vertex, ShaderStageKind.Fragment, TextureTarget = ShaderTextureTarget.Texture3D, Sampler = ShaderSamplerPolicy.Default)]
    DynamicTexture3D? AerialRadianceTexture { set; }
    /// <summary>Declares the vge_atmosphereAerialAttenuation Sampler slot.</summary>
    [ShaderBinding("vge_atmosphereAerialAttenuation", ShaderBindingKind.Sampler, 6, ShaderStageKind.Vertex, ShaderStageKind.Fragment, TextureTarget = ShaderTextureTarget.Texture3D, Sampler = ShaderSamplerPolicy.Default)]
    DynamicTexture3D? AerialAttenuationTexture { set; }
    #endregion
}
