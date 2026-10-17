using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.PBR.Clouds;

/// <summary>Declares physical radiance/transmission, bucket transmission and unfiltered depth history.</summary>
internal interface ICloudHistoryBindings
{
    #region Public API
    /// <summary>Borrows previous integrated physical radiance and overall transmission without sampling a current draw attachment.</summary>
    [ShaderBinding("history0",ShaderBindingKind.Sampler,7,ShaderStageKind.Fragment,TextureTarget=ShaderTextureTarget.Texture2D,Sampler=ShaderSamplerPolicy.NearestClamp)]
    int History0 { set; }
    /// <summary>Borrows previous three bucket transmissions without sampling a current draw attachment.</summary>
    [ShaderBinding("history1",ShaderBindingKind.Sampler,8,ShaderStageKind.Fragment,TextureTarget=ShaderTextureTarget.Texture2D,Sampler=ShaderSamplerPolicy.NearestClamp)]
    int History1 { set; }
    /// <summary>Borrows previous unfiltered cloud and foreground depth metadata without sampling a current draw attachment.</summary>
    [ShaderBinding("history2",ShaderBindingKind.Sampler,9,ShaderStageKind.Fragment,TextureTarget=ShaderTextureTarget.Texture2D,Sampler=ShaderSamplerPolicy.NearestClamp)]
    int History2 { set; }
    #endregion
}
