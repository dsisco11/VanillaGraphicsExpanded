using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.PBR.Clouds;

/// <summary>Declares one disjoint array view containing radiance, bucket transmission and depth history.</summary>
internal interface ICloudHistoryBindings
{
    #region Public API
    /// <summary>Borrows a complete readable payload block while excluding every attached write layer.</summary>
    [ShaderBinding("historyPayload",ShaderBindingKind.Sampler,7,ShaderStageKind.Fragment,TextureTarget=ShaderTextureTarget.Texture2DArray,Sampler=ShaderSamplerPolicy.NearestClamp)]
    GpuTextureView? HistoryPayload { set; }
    #endregion
}
