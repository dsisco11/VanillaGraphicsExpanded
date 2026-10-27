using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.LumOn;

/// <summary>Declares the GPU binding contract for LumOnCombineShaderProgram.</summary>
[ShaderBindingSet(typeof(VanillaGraphicsExpanded.PBR.Materials.Energy.IMicrofacetEnergyBindings), Defaults = true)]
[ShaderBindingSet(typeof(IShaderInterfaceLocations), Defaults = true)]
[ShaderBindingSet(typeof(IShaderIncludeBindings), Defaults = true)]
internal interface ILumOnCombineShaderProgramBindings : VanillaGraphicsExpanded.PBR.Materials.Energy.IMicrofacetEnergyBindings
{
    #region Public API
    /// <summary>Supplies common camera and frame values from the universal view snapshot.</summary>
    [ShaderBinding("VgeFrameUBO", ShaderBindingKind.UniformBlock, GpuBindingRegistry.Ubo.Frame, ShaderStageKind.Vertex, ShaderStageKind.Fragment, Required = false)]
    CpuUniformBuffer FrameInputs { get; }
    /// <summary>Declares the LumOnFrameUBO UniformBlock slot.</summary>
    [ShaderBinding("LumOnFrameUBO", ShaderBindingKind.UniformBlock, GpuBindingRegistry.Ubo.LumOnFrame, ShaderStageKind.Vertex, ShaderStageKind.Fragment)]
    GpuUniformBuffer? LumOnFrame { get; }
    /// <summary>Declares the VgeLumOnCombineParamsUBO UniformBlock slot.</summary>
    [ShaderBinding("VgeLumOnCombineParamsUBO", ShaderBindingKind.UniformBlock, GpuBindingRegistry.Ubo.Object, ShaderStageKind.Vertex, ShaderStageKind.Fragment)]
    CpuUniformBuffer Parameters { get; }
    /// <summary>Declares the sceneDirect Sampler slot.</summary>
    [ShaderBinding("sceneDirect", ShaderBindingKind.Sampler, 0, ShaderStageKind.Vertex, ShaderStageKind.Fragment)]
    GpuTextureRef SceneDirect { set; }
    /// <summary>Declares the indirectDiffuse Sampler slot.</summary>
    [ShaderBinding("indirectDiffuse", ShaderBindingKind.Sampler, 1, ShaderStageKind.Vertex, ShaderStageKind.Fragment)]
    GpuTextureRef IndirectDiffuse { set; }
    /// <summary>Declares the gBufferAlbedo Sampler slot.</summary>
    [ShaderBinding("gBufferAlbedo", ShaderBindingKind.Sampler, 2, ShaderStageKind.Vertex, ShaderStageKind.Fragment, TextureTarget = ShaderTextureTarget.Texture2D, Sampler = ShaderSamplerPolicy.NearestClamp)]
    GpuTextureRef GBufferAlbedo { set; }
    /// <summary>Declares the normal, material and environment surface array sampler.</summary>
    [ShaderBinding("gBufferSurface", ShaderBindingKind.Sampler, 3, ShaderStageKind.Vertex, ShaderStageKind.Fragment, TextureTarget = ShaderTextureTarget.Texture2DArray, Sampler = ShaderSamplerPolicy.NearestClamp)]
    GpuTextureRef GBufferSurface { set; }
    /// <summary>Declares the primaryDepth Sampler slot.</summary>
    [ShaderBinding("primaryDepth", ShaderBindingKind.Sampler, 4, ShaderStageKind.Vertex, ShaderStageKind.Fragment, TextureTarget = ShaderTextureTarget.Texture2D, Sampler = ShaderSamplerPolicy.NearestClamp)]
    GpuTextureRef PrimaryDepth { set; }
    /// <summary>Declares the normal, material and environment surface array sampler.</summary>

    #endregion
}
