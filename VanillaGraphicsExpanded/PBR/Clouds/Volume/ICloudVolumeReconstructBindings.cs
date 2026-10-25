using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.PBR.CameraExposure;

namespace VanillaGraphicsExpanded.PBR.Clouds.Volume;

/// <summary>Declares compact continuous cloud results separately from exact depth/completion metadata.</summary>
[ShaderBindingSet(typeof(IPhotometricFrameBindings),Defaults=true)]
internal interface ICloudVolumeReconstructBindings:IPhotometricFrameBindings
{
    #region Public API
    /// <summary>Borrows the shared frame publication for normalized full-view coordinates.</summary>
    [ShaderBinding("VgeFrameUBO",ShaderBindingKind.UniformBlock,GpuBindingRegistry.Ubo.Frame,ShaderStageKind.Fragment)]
    CpuUniformBuffer Camera {get;}
    /// <summary>Borrows the same temporal parameters for diagnostic display without another UBO owner.</summary>
    [ShaderBinding("CloudTemporalParameters",ShaderBindingKind.UniformBlock,14,ShaderStageKind.Fragment)]
    CpuUniformBuffer TemporalInputs {get;}
    /// <summary>Reads effective prior weight, update status, rejection reason and frames since observation.</summary>
    [ShaderBinding("reconstructDiagnostics",ShaderBindingKind.Sampler,11,ShaderStageKind.Fragment,Sampler=ShaderSamplerPolicy.NearestClamp)]
    GpuTextureRef Diagnostics {set;}
    /// <summary>Hardware-filters premultiplied integrated radiance and opacity together.</summary>
    [ShaderBinding("reconstructCloud",ShaderBindingKind.Sampler,0,ShaderStageKind.Fragment,Sampler=ShaderSamplerPolicy.LinearClamp)]
    GpuTextureRef Cloud {set;}
    /// <summary>Hardware-filters all three bucket transmissions before native product expansion.</summary>
    [ShaderBinding("reconstructBins",ShaderBindingKind.Sampler,1,ShaderStageKind.Fragment,Sampler=ShaderSamplerPolicy.LinearClamp)]
    GpuTextureRef Bins {set;}
    /// <summary>Reads exact trace clip depths and whole-ray validity.</summary>
    [ShaderBinding("reconstructMetadata",ShaderBindingKind.Sampler,2,ShaderStageKind.Fragment,Sampler=ShaderSamplerPolicy.NearestClamp)]
    GpuTextureRef Metadata {set;}
    /// <summary>Reads the destination material depth.</summary>
    [ShaderBinding("reconstructMaterialDepth",ShaderBindingKind.Sampler,3,ShaderStageKind.Fragment,Sampler=ShaderSamplerPolicy.NearestClamp)]
    GpuTextureRef MaterialDepth {set;}
    /// <summary>Reads foreground particle visibility depth.</summary>
    [ShaderBinding("reconstructVisibilityDepth",ShaderBindingKind.Sampler,4,ShaderStageKind.Fragment,Sampler=ShaderSamplerPolicy.NearestClamp)]
    GpuTextureRef VisibilityDepth {set;}
    /// <summary>Reads liquid depth independently of material correction.</summary>
    [ShaderBinding("reconstructLiquidDepth",ShaderBindingKind.Sampler,5,ShaderStageKind.Fragment,Sampler=ShaderSamplerPolicy.NearestClamp)]
    GpuTextureRef LiquidDepth {set;}
    #endregion
}
