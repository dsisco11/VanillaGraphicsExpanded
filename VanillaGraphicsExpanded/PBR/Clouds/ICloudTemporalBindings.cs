using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.PBR.Clouds;

/// <summary>Declares current/history payloads without native weather, lighting or atmosphere recomputation.</summary>
[ShaderBindingSet(typeof(ICloudHistoryBindings),Defaults=true)]
internal interface ICloudTemporalBindings : ICloudHistoryBindings
{
    #region Public API
    /// <summary>Supplies the complete current main-view camera for motion reprojection.</summary>
    [ShaderBinding("VgeFrameUBO",ShaderBindingKind.UniformBlock,GpuBindingRegistry.Ubo.Frame,ShaderStageKind.Fragment)]
    CpuUniformBuffer FrameInputs { get; }
    /// <summary>Supplies reprojection, lifecycle admission and exact reduced trace phases.</summary>
    [ShaderBinding("CloudTemporalParameters",ShaderBindingKind.UniformBlock,GpuBindingRegistry.Ubo.Object,ShaderStageKind.Fragment)]
    CpuUniformBuffer TemporalInputs { get; }
    /// <summary>Borrows the current trace block through a view disjoint from the history destination.</summary>
    [ShaderBinding("currentPayload",ShaderBindingKind.Sampler,0,ShaderStageKind.Fragment,TextureTarget=ShaderTextureTarget.Texture2DArray,Sampler=ShaderSamplerPolicy.NearestClamp)]
    GpuTextureView? CurrentPayload { set; }
    /// <summary>Checks unsampled reconstruction locations against current material depth.</summary>
    [ShaderBinding("sceneDepth",ShaderBindingKind.Sampler,1,ShaderStageKind.Fragment,TextureTarget=ShaderTextureTarget.Texture2D,Sampler=ShaderSamplerPolicy.NearestClamp)]
    int SceneDepth { set; }
    /// <summary>Retains opaque particle visibility independently of corrected material depth.</summary>
    [ShaderBinding("visibilityDepth",ShaderBindingKind.Sampler,6,ShaderStageKind.Fragment,TextureTarget=ShaderTextureTarget.Texture2D,Sampler=ShaderSamplerPolicy.NearestClamp)]
    int VisibilityDepth { set; }
    /// <summary>Rejects current/history support behind a newly exposed liquid clip.</summary>
    [ShaderBinding("liquidDepth",ShaderBindingKind.Sampler,2,ShaderStageKind.Fragment,TextureTarget=ShaderTextureTarget.Texture2D,Sampler=ShaderSamplerPolicy.NearestClamp)]
    int LiquidDepth { set; }
    #endregion
}
