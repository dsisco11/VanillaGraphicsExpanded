using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.LumOn.Scene.Shaders;

/// <summary>Declares the GPU binding contract for TraceGeometryBindingSet.</summary>
internal interface ITraceGeometryInputs
{
    #region Public API
    /// <summary>Declares the LumOnNearFieldUBO UniformBlock slot.</summary>
    [ShaderBinding("LumOnNearFieldUBO", ShaderBindingKind.UniformBlock, GpuBindingRegistry.Ubo.Material, ShaderStageKind.Fragment, ShaderStageKind.Compute)]
    CpuUniformBuffer LumOnNearField { get; }
    /// <summary>Declares the nearFieldGeometry Sampler slot.</summary>
    [ShaderBinding("nearFieldGeometry", ShaderBindingKind.Sampler, 8, ShaderStageKind.Fragment, ShaderStageKind.Compute, TextureTarget = ShaderTextureTarget.Texture3D, Sampler = ShaderSamplerPolicy.NearestClamp, Required = false)]
    GpuTexture? NearFieldGeometry { get; }
    /// <summary>Declares the nearFieldRegions Sampler slot.</summary>
    [ShaderBinding("nearFieldRegions", ShaderBindingKind.Sampler, 9, ShaderStageKind.Fragment, ShaderStageKind.Compute, TextureTarget = ShaderTextureTarget.Texture3D, Sampler = ShaderSamplerPolicy.NearestClamp, Required = false)]
    GpuTexture? NearFieldRegions { get; }
    /// <summary>Declares the traceSceneLegacy Sampler slot.</summary>
    [ShaderBinding("traceSceneLegacy", ShaderBindingKind.Sampler, 10, ShaderStageKind.Fragment, ShaderStageKind.Compute, Required = false, TextureTarget = ShaderTextureTarget.Texture3D, Sampler = ShaderSamplerPolicy.NearestClamp)]
    GpuTexture? TraceSceneLegacy { get; }
    /// <summary>Declares the traceSceneFaces Sampler slot.</summary>
    [ShaderBinding("traceSceneFaces", ShaderBindingKind.Sampler, 11, ShaderStageKind.Fragment, ShaderStageKind.Compute, TextureTarget = ShaderTextureTarget.Texture2D, Sampler = ShaderSamplerPolicy.NearestClamp, Required = false)]
    GpuTexture? TraceSceneFaces { get; }
    #endregion
}
