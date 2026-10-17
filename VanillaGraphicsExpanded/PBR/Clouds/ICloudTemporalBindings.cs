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
    /// <summary>Reads current integrated physical radiance and overall transmission independently of previous history.</summary>
    [ShaderBinding("current0",ShaderBindingKind.Sampler,0,ShaderStageKind.Fragment,TextureTarget=ShaderTextureTarget.Texture2D,Sampler=ShaderSamplerPolicy.NearestClamp)]
    int Current0 { set; }
    /// <summary>Reads current three bucket transmissions independently of previous history.</summary>
    [ShaderBinding("current1",ShaderBindingKind.Sampler,1,ShaderStageKind.Fragment,TextureTarget=ShaderTextureTarget.Texture2D,Sampler=ShaderSamplerPolicy.NearestClamp)]
    int Current1 { set; }
    /// <summary>Reads current unfiltered cloud and foreground depth metadata independently of previous history.</summary>
    [ShaderBinding("current2",ShaderBindingKind.Sampler,2,ShaderStageKind.Fragment,TextureTarget=ShaderTextureTarget.Texture2D,Sampler=ShaderSamplerPolicy.NearestClamp)]
    int Current2 { set; }
    #endregion
}
