using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.LumOn.Scene.Shaders;

/// <summary>Owns a shared compile-time GPU interface.</summary>
internal static partial class TraceGeometryBindingSet
{
    #region Private
    /// <summary>Declares the LumOnNearFieldUBO UniformBlock slot.</summary>
    [ShaderBinding("LumOnNearFieldUBO", ShaderBindingKind.UniformBlock, GpuBindingRegistry.Ubo.Material, ShaderStageKind.Fragment, ShaderStageKind.Compute)]
    private static partial ShaderUniformBlockBinding LumOnNearField { get; }
    /// <summary>Declares the nearFieldGeometry Sampler slot.</summary>
    [ShaderBinding("nearFieldGeometry", ShaderBindingKind.Sampler, 8, ShaderStageKind.Fragment, ShaderStageKind.Compute)]
    private static partial ShaderSamplerBinding NearFieldGeometry { get; }
    /// <summary>Declares the nearFieldRegions Sampler slot.</summary>
    [ShaderBinding("nearFieldRegions", ShaderBindingKind.Sampler, 9, ShaderStageKind.Fragment, ShaderStageKind.Compute)]
    private static partial ShaderSamplerBinding NearFieldRegions { get; }
    /// <summary>Declares the traceSceneLegacy Sampler slot.</summary>
    [ShaderBinding("traceSceneLegacy", ShaderBindingKind.Sampler, 10, ShaderStageKind.Fragment, ShaderStageKind.Compute, Required = false)]
    private static partial ShaderSamplerBinding TraceSceneLegacy { get; }
    /// <summary>Declares the traceSceneFaces Sampler slot.</summary>
    [ShaderBinding("traceSceneFaces", ShaderBindingKind.Sampler, 11, ShaderStageKind.Fragment, ShaderStageKind.Compute)]
    private static partial ShaderSamplerBinding TraceSceneFaces { get; }
    #endregion
}
