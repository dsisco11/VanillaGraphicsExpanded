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
    /// <summary>Reads current physical trace layer 0 independently of previous history.</summary>
    [ShaderBinding("current0",ShaderBindingKind.Sampler,0,ShaderStageKind.Fragment,TextureTarget=ShaderTextureTarget.Texture2D,Sampler=ShaderSamplerPolicy.NearestClamp)]
    int Current0 { set; }
    /// <summary>Reads current physical trace layer 1 independently of previous history.</summary>
    [ShaderBinding("current1",ShaderBindingKind.Sampler,1,ShaderStageKind.Fragment,TextureTarget=ShaderTextureTarget.Texture2D,Sampler=ShaderSamplerPolicy.NearestClamp)]
    int Current1 { set; }
    /// <summary>Reads current physical trace layer 2 independently of previous history.</summary>
    [ShaderBinding("current2",ShaderBindingKind.Sampler,2,ShaderStageKind.Fragment,TextureTarget=ShaderTextureTarget.Texture2D,Sampler=ShaderSamplerPolicy.NearestClamp)]
    int Current2 { set; }
    /// <summary>Reads current physical trace layer 3 independently of previous history.</summary>
    [ShaderBinding("current3",ShaderBindingKind.Sampler,3,ShaderStageKind.Fragment,TextureTarget=ShaderTextureTarget.Texture2D,Sampler=ShaderSamplerPolicy.NearestClamp)]
    int Current3 { set; }
    /// <summary>Reads current physical trace layer 4 independently of previous history.</summary>
    [ShaderBinding("current4",ShaderBindingKind.Sampler,4,ShaderStageKind.Fragment,TextureTarget=ShaderTextureTarget.Texture2D,Sampler=ShaderSamplerPolicy.NearestClamp)]
    int Current4 { set; }
    /// <summary>Reads current physical trace layer 5 independently of previous history.</summary>
    [ShaderBinding("current5",ShaderBindingKind.Sampler,5,ShaderStageKind.Fragment,TextureTarget=ShaderTextureTarget.Texture2D,Sampler=ShaderSamplerPolicy.NearestClamp)]
    int Current5 { set; }
    #endregion
}
