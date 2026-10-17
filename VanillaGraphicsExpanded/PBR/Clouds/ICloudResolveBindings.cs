using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.PBR.Clouds;

/// <summary>Declares reduced physical cloud payloads, foreground clips and final scene encoding without density inputs.</summary>
[ShaderBindingSet(typeof(CameraExposure.IPhotometricFrameBindings),Defaults=true)]
[ShaderBindingSet(typeof(ICloudHistoryBindings),Defaults=true)]
internal interface ICloudResolveBindings : CameraExposure.IPhotometricFrameBindings,ICloudHistoryBindings
{
    #region Public API
    /// <summary>Supplies the current camera for foreground distance reconstruction.</summary>
    [ShaderBinding("VgeFrameUBO",ShaderBindingKind.UniformBlock,GpuBindingRegistry.Ubo.Frame,ShaderStageKind.Fragment)]
    CpuUniformBuffer FrameInputs { get; }
    /// <summary>Supplies reduced dimensions and the current sampling phase.</summary>
    [ShaderBinding("CloudTemporalParameters",ShaderBindingKind.UniformBlock,GpuBindingRegistry.Ubo.Object,ShaderStageKind.Fragment)]
    CpuUniformBuffer TemporalInputs { get; }
    /// <summary>Rejects reduced samples incompatible with current opaque geometry.</summary>
    [ShaderBinding("sceneDepth",ShaderBindingKind.Sampler,1,ShaderStageKind.Fragment,TextureTarget=ShaderTextureTarget.Texture2D,Sampler=ShaderSamplerPolicy.NearestClamp)]
    int SceneDepth { set; }
    /// <summary>Rejects reduced samples incompatible with the current liquid surface.</summary>
    [ShaderBinding("liquidDepth",ShaderBindingKind.Sampler,2,ShaderStageKind.Fragment,TextureTarget=ShaderTextureTarget.Texture2D,Sampler=ShaderSamplerPolicy.NearestClamp)]
    int LiquidDepth { set; }
    #endregion
}
