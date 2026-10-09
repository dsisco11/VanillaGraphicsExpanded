using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.PBR;

/// <summary>Declares the GPU binding contract for PBRCompositeShaderProgram.</summary>
[ShaderBindingSet(typeof(IShaderInterfaceLocations), Defaults = true)]
[ShaderBindingSet(typeof(IShaderIncludeBindings), Defaults = true)]
internal interface IPBRCompositeShaderProgramBindings
{
    #region Public API
    /// <summary>Supplies the diffuse, specular and emissive radiance array.</summary>
    [ShaderBinding("directLighting", ShaderBindingKind.Sampler, 0, ShaderStageKind.Fragment, TextureTarget = ShaderTextureTarget.Texture2DArray, Sampler = ShaderSamplerPolicy.LinearClamp)]
    GpuTexture? DirectLighting { set; }

    /// <summary>Supplies current-frame occlusion for atmospheric in-scattering only.</summary>
    [ShaderBinding("vge_lightShaftOcclusion", ShaderBindingKind.Sampler, 1, ShaderStageKind.Fragment, TextureTarget = ShaderTextureTarget.Texture2D, Sampler = ShaderSamplerPolicy.LinearClamp, Required = false)]
    GpuTexture? LightShaftOcclusion { set; }
    /// <summary>Clean radiance retained before first-person framebuffer overwrites.</summary>
    [ShaderBinding("preOverlayColor", ShaderBindingKind.Sampler, 2, ShaderStageKind.Fragment, Required = false, Sampler = ShaderSamplerPolicy.NearestClamp)]
    DynamicTexture2D? PreOverlayColor { set; }
    /// <summary>Depth from the same pre-overlay lighting invocation.</summary>
    [ShaderBinding("preOverlayDepth", ShaderBindingKind.Sampler, 3, ShaderStageKind.Fragment, Required = false, Sampler = ShaderSamplerPolicy.NearestClamp)]
    DynamicTexture2D? PreOverlayDepth { set; }
    /// <summary>Publishes unattenuated opaque scene-linear radiance for refraction.</summary>
    [ShaderBinding("outRefractionColor", ShaderBindingKind.FragmentOutputLocation, 1, ShaderStageKind.Fragment)]
    ShaderFragmentOutputLocationBinding RefractionColorOutput { get; }
    /// <summary>Publishes matching full-precision hardware depth for refraction.</summary>
    [ShaderBinding("outRefractionDepth", ShaderBindingKind.FragmentOutputLocation, 2, ShaderStageKind.Fragment)]
    ShaderFragmentOutputLocationBinding RefractionDepthOutput { get; }
    /// <summary>Declares the VgePbrCompositeParamsUBO UniformBlock slot.</summary>
    [ShaderBinding("VgePbrCompositeParamsUBO", ShaderBindingKind.UniformBlock, GpuBindingRegistry.Ubo.Object, ShaderStageKind.Vertex, ShaderStageKind.Fragment)]
    CpuUniformBuffer Parameters { get; }
    /// <summary>Allows missing or unpublished GI; the renderer sets indirect intensity to zero in that case.</summary>
    [ShaderBinding("indirectDiffuse", ShaderBindingKind.Sampler, 4, ShaderStageKind.Vertex, ShaderStageKind.Fragment, Required = false)]
    GpuTexture? IndirectDiffuse { set; }
    /// <summary>Declares the gBufferAlbedo Sampler slot.</summary>
    [ShaderBinding("gBufferAlbedo", ShaderBindingKind.Sampler, 5, ShaderStageKind.Vertex, ShaderStageKind.Fragment, TextureTarget = ShaderTextureTarget.Texture2D, Sampler = ShaderSamplerPolicy.NearestClamp)]
    int GBufferAlbedo { set; }
    /// <summary>Declares the normal, material and environment surface array sampler.</summary>
    [ShaderBinding("gBufferSurface", ShaderBindingKind.Sampler, 6, ShaderStageKind.Vertex, ShaderStageKind.Fragment, TextureTarget = ShaderTextureTarget.Texture2DArray, Sampler = ShaderSamplerPolicy.NearestClamp)]
    GpuTexture? GBufferSurface { set; }
    /// <summary>Declares the primaryDepth Sampler slot.</summary>
    [ShaderBinding("primaryDepth", ShaderBindingKind.Sampler, 7, ShaderStageKind.Vertex, ShaderStageKind.Fragment, TextureTarget = ShaderTextureTarget.Texture2D, Sampler = ShaderSamplerPolicy.NearestClamp)]
    int PrimaryDepth { set; }
    /// <summary>Declares the gBufferPosition Sampler slot.</summary>
    [ShaderBinding("gBufferPosition", ShaderBindingKind.Sampler, 8, ShaderStageKind.Vertex, ShaderStageKind.Fragment, TextureTarget = ShaderTextureTarget.Texture2D, Sampler = ShaderSamplerPolicy.NearestClamp)]
    int GBufferPosition { set; }
    /// <summary>Declares signed optical-depth and length accumulation for opaque receivers.</summary>
    [ShaderBinding("vge_waterTransport", ShaderBindingKind.Sampler, 9, ShaderStageKind.Fragment, TextureTarget = ShaderTextureTarget.Texture2DArray, Sampler = ShaderSamplerPolicy.NearestClamp, Required = false)]
    GpuTexture? WaterTransport { set; }
    /// <summary>Declares the vge_atmosphereAerialRadiance Sampler slot.</summary>
    [ShaderBinding("vge_atmosphereAerialRadiance", ShaderBindingKind.Sampler, 10, ShaderStageKind.Vertex, ShaderStageKind.Fragment, TextureTarget = ShaderTextureTarget.Texture3D, Required = false)]
    DynamicTexture3D? AtmosphereAerialRadiance { set; }
    /// <summary>Declares the vge_atmosphereAerialAttenuation Sampler slot.</summary>
    [ShaderBinding("vge_atmosphereAerialAttenuation", ShaderBindingKind.Sampler, 11, ShaderStageKind.Vertex, ShaderStageKind.Fragment, TextureTarget = ShaderTextureTarget.Texture3D, Required = false)]
    DynamicTexture3D? AtmosphereAerialAttenuation { set; }
    /// <summary>Supplies current opaque ambient visibility and matching receiver depth.</summary>
    [ShaderBinding("ambientOcclusion", ShaderBindingKind.Sampler, 12, ShaderStageKind.Fragment, TextureTarget = ShaderTextureTarget.Texture2D, Sampler = ShaderSamplerPolicy.NearestClamp, Required = false)]
    GpuTexture? AmbientOcclusion { set; }
    #endregion
}
