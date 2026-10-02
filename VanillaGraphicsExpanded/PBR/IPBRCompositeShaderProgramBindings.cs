using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.PBR;

/// <summary>Declares the GPU binding contract for PBRCompositeShaderProgram.</summary>
[ShaderBindingSet(typeof(IShaderInterfaceLocations), Defaults = true)]
[ShaderBindingSet(typeof(IShaderIncludeBindings), Defaults = true)]
internal interface IPBRCompositeShaderProgramBindings
{
    #region Public API
    /// <summary>Declares the VgePbrCompositeParamsUBO UniformBlock slot.</summary>
    [ShaderBinding("VgePbrCompositeParamsUBO", ShaderBindingKind.UniformBlock, GpuBindingRegistry.Ubo.Object, ShaderStageKind.Vertex, ShaderStageKind.Fragment)]
    CpuUniformBuffer Parameters { get; }
    /// <summary>Declares the directDiffuse Sampler slot.</summary>
    [ShaderBinding("directDiffuse", ShaderBindingKind.Sampler, 0, ShaderStageKind.Vertex, ShaderStageKind.Fragment)]
    GpuTexture? DirectDiffuse { set; }
    /// <summary>Declares the directSpecular Sampler slot.</summary>
    [ShaderBinding("directSpecular", ShaderBindingKind.Sampler, 1, ShaderStageKind.Vertex, ShaderStageKind.Fragment)]
    GpuTexture? DirectSpecular { set; }
    /// <summary>Declares the emissive Sampler slot.</summary>
    [ShaderBinding("emissive", ShaderBindingKind.Sampler, 2, ShaderStageKind.Vertex, ShaderStageKind.Fragment)]
    GpuTexture? Emissive { set; }
    /// <summary>Allows missing or unpublished GI; the renderer sets indirect intensity to zero in that case.</summary>
    [ShaderBinding("indirectDiffuse", ShaderBindingKind.Sampler, 3, ShaderStageKind.Vertex, ShaderStageKind.Fragment, Required = false)]
    GpuTexture? IndirectDiffuse { set; }
    /// <summary>Declares the gBufferAlbedo Sampler slot.</summary>
    [ShaderBinding("gBufferAlbedo", ShaderBindingKind.Sampler, 4, ShaderStageKind.Vertex, ShaderStageKind.Fragment, TextureTarget = ShaderTextureTarget.Texture2D, Sampler = ShaderSamplerPolicy.NearestClamp)]
    int GBufferAlbedo { set; }
    /// <summary>Declares the gBufferMaterial Sampler slot.</summary>
    [ShaderBinding("gBufferMaterial", ShaderBindingKind.Sampler, 5, ShaderStageKind.Vertex, ShaderStageKind.Fragment, TextureTarget = ShaderTextureTarget.Texture2D, Sampler = ShaderSamplerPolicy.NearestClamp)]
    DynamicTexture2D? GBufferMaterial { set; }
    /// <summary>Declares the primaryDepth Sampler slot.</summary>
    [ShaderBinding("primaryDepth", ShaderBindingKind.Sampler, 6, ShaderStageKind.Vertex, ShaderStageKind.Fragment, TextureTarget = ShaderTextureTarget.Texture2D, Sampler = ShaderSamplerPolicy.NearestClamp)]
    int PrimaryDepth { set; }
    /// <summary>Declares the gBufferPosition Sampler slot.</summary>
    [ShaderBinding("gBufferPosition", ShaderBindingKind.Sampler, 11, ShaderStageKind.Vertex, ShaderStageKind.Fragment, TextureTarget = ShaderTextureTarget.Texture2D, Sampler = ShaderSamplerPolicy.NearestClamp)]
    int GBufferPosition { set; }
    /// <summary>Declares the gBufferNormal Sampler slot.</summary>
    [ShaderBinding("gBufferNormal", ShaderBindingKind.Sampler, 7, ShaderStageKind.Vertex, ShaderStageKind.Fragment, TextureTarget = ShaderTextureTarget.Texture2D, Sampler = ShaderSamplerPolicy.NearestClamp)]
    DynamicTexture2D? GBufferNormal { set; }
    /// <summary>Declares the gBufferEnvironment Sampler slot.</summary>
    [ShaderBinding("gBufferEnvironment", ShaderBindingKind.Sampler, 8, ShaderStageKind.Vertex, ShaderStageKind.Fragment, TextureTarget = ShaderTextureTarget.Texture2D, Sampler = ShaderSamplerPolicy.NearestClamp)]
    DynamicTexture2D? GBufferEnvironment { set; }
    /// <summary>Declares signed optical-depth and length accumulation for opaque receivers.</summary>
    [ShaderBinding("vge_waterOpticalDepth", ShaderBindingKind.Sampler, 12, ShaderStageKind.Fragment, TextureTarget = ShaderTextureTarget.Texture2D, Sampler = ShaderSamplerPolicy.NearestClamp, Required = false)]
    DynamicTexture2D? WaterOpticalDepth { set; }
    /// <summary>Declares signed scattering-source and boundary-count accumulation.</summary>
    [ShaderBinding("vge_waterSource", ShaderBindingKind.Sampler, 13, ShaderStageKind.Fragment, TextureTarget = ShaderTextureTarget.Texture2D, Sampler = ShaderSamplerPolicy.NearestClamp, Required = false)]
    DynamicTexture2D? WaterSource { set; }
    /// <summary>Declares a stable baked uniform location for the optical-depth sampler without relying on SPIR-V names.</summary>
    [ShaderBinding("vge_waterOpticalDepth", ShaderBindingKind.UniformLocation, 100, ShaderStageKind.Fragment)]
    ShaderUniformLocationBinding WaterOpticalDepthLocation { get; }
    /// <summary>Declares a stable baked uniform location for the scattering-source sampler.</summary>
    [ShaderBinding("vge_waterSource", ShaderBindingKind.UniformLocation, 101, ShaderStageKind.Fragment)]
    ShaderUniformLocationBinding WaterSourceLocation { get; }
    /// <summary>Declares the vge_atmosphereAerialRadiance Sampler slot.</summary>
    [ShaderBinding("vge_atmosphereAerialRadiance", ShaderBindingKind.Sampler, 9, ShaderStageKind.Vertex, ShaderStageKind.Fragment, TextureTarget = ShaderTextureTarget.Texture3D, Required = false)]
    DynamicTexture3D? AtmosphereAerialRadiance { set; }
    /// <summary>Declares the vge_atmosphereAerialAttenuation Sampler slot.</summary>
    [ShaderBinding("vge_atmosphereAerialAttenuation", ShaderBindingKind.Sampler, 10, ShaderStageKind.Vertex, ShaderStageKind.Fragment, TextureTarget = ShaderTextureTarget.Texture3D, Required = false)]
    DynamicTexture3D? AtmosphereAerialAttenuation { set; }
    #endregion
}
