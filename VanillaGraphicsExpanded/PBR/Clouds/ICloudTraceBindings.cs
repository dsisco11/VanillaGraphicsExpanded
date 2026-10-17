using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.PBR.Clouds;

/// <summary>Extends the physical volume inputs with explicit reduced-view scheduling.</summary>
[ShaderBindingSet(typeof(ICloudVolumeBindings),Defaults=true)]
internal interface ICloudTraceBindings : ICloudVolumeBindings
{
    #region Public API
    /// <summary>Supplies reprojection, lifecycle admission and exact reduced trace phases.</summary>
    [ShaderBinding("CloudTemporalParameters",ShaderBindingKind.UniformBlock,GpuBindingRegistry.Ubo.Object,ShaderStageKind.Fragment)]
    CpuUniformBuffer TemporalInputs { get; }
    /// <summary>Supplies cloud-owned scrambled PMJ offsets without depending on LumOn resource availability.</summary>
    [ShaderBinding("cloudPmjJitter",ShaderBindingKind.Sampler,7,ShaderStageKind.Fragment,TextureTarget=ShaderTextureTarget.Texture2D,Sampler=ShaderSamplerPolicy.NearestClamp)]
    GpuTexture? PmjJitter { set; }
    #endregion
}
