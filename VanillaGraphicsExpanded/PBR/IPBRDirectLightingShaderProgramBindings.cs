using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.PBR;

/// <summary>Declares the GPU binding contract for PBRDirectLightingShaderProgram.</summary>
[ShaderBindingSet(typeof(VanillaGraphicsExpanded.PBR.Materials.Energy.IMicrofacetEnergyBindings), Defaults = true)]
[ShaderBindingSet(typeof(IShaderInterfaceLocations), Defaults = true)]
[ShaderBindingSet(typeof(IShaderIncludeBindings), Defaults = true)]
[ShaderBindingSet(typeof(VanillaGraphicsExpanded.PBR.CameraExposure.IPhotometricFrameBindings), Defaults = true)]
[ShaderBindingSet(typeof(Shadows.ISurfaceShadowBindings), Defaults = true)]
internal interface IPBRDirectLightingShaderProgramBindings : Shadows.ISurfaceShadowBindings, VanillaGraphicsExpanded.PBR.CameraExposure.IPhotometricFrameBindings, VanillaGraphicsExpanded.PBR.Materials.Energy.IMicrofacetEnergyBindings
{
    #region Public API
    /// <summary>Supplies the shared dynamic-light list without effect-specific copies.</summary>
    [ShaderBinding("VgeLightsUBO", ShaderBindingKind.UniformBlock, GpuBindingRegistry.Ubo.Lights, ShaderStageKind.Fragment)]
    CpuUniformBuffer LightsInputs { get; }
    /// <summary>Supplies the shared camera snapshot.</summary>
    [ShaderBinding("VgeFrameUBO", ShaderBindingKind.UniformBlock, GpuBindingRegistry.Ubo.Frame, ShaderStageKind.Fragment)]
    CpuUniformBuffer FrameInputs { get; }
    /// <summary>Declares the primaryScene Sampler slot.</summary>
    [ShaderBinding("primaryScene", ShaderBindingKind.Sampler, 0, ShaderStageKind.Vertex, ShaderStageKind.Fragment, TextureTarget = ShaderTextureTarget.Texture2D, Sampler = ShaderSamplerPolicy.LinearClamp)]
    GpuTextureRef PrimaryScene { set; }
    /// <summary>Declares the primaryDepth Sampler slot.</summary>
    [ShaderBinding("primaryDepth", ShaderBindingKind.Sampler, 1, ShaderStageKind.Vertex, ShaderStageKind.Fragment, TextureTarget = ShaderTextureTarget.Texture2D, Sampler = ShaderSamplerPolicy.NearestClamp)]
    GpuTextureRef PrimaryDepth { set; }
    /// <summary>Declares the gBufferPosition Sampler slot.</summary>
    [ShaderBinding("gBufferPosition", ShaderBindingKind.Sampler, 2, ShaderStageKind.Vertex, ShaderStageKind.Fragment, TextureTarget = ShaderTextureTarget.Texture2D, Sampler = ShaderSamplerPolicy.NearestClamp)]
    GpuTextureRef GBufferPosition { set; }
    /// <summary>Declares the normal, material and environment surface array sampler.</summary>
    [ShaderBinding("gBufferSurface", ShaderBindingKind.Sampler, 3, ShaderStageKind.Vertex, ShaderStageKind.Fragment, TextureTarget = ShaderTextureTarget.Texture2DArray, Sampler = ShaderSamplerPolicy.NearestClamp)]
    GpuTextureRef GBufferSurface { set; }
    #endregion
}
