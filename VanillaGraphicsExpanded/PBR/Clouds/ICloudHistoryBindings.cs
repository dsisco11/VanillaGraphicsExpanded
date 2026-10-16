using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.PBR.Clouds;

/// <summary>Declares intact physical OIT history and its unfiltered depth metadata.</summary>
internal interface ICloudHistoryBindings
{
    #region Public API
    /// <summary>Borrows physical history payload layer 0, without sampling a current draw attachment.</summary>
    [ShaderBinding("history0",ShaderBindingKind.Sampler,7,ShaderStageKind.Fragment,TextureTarget=ShaderTextureTarget.Texture2D,Sampler=ShaderSamplerPolicy.NearestClamp)]
    int History0 { set; }
    /// <summary>Borrows physical history payload layer 1, without sampling a current draw attachment.</summary>
    [ShaderBinding("history1",ShaderBindingKind.Sampler,8,ShaderStageKind.Fragment,TextureTarget=ShaderTextureTarget.Texture2D,Sampler=ShaderSamplerPolicy.NearestClamp)]
    int History1 { set; }
    /// <summary>Borrows physical history payload layer 2, without sampling a current draw attachment.</summary>
    [ShaderBinding("history2",ShaderBindingKind.Sampler,9,ShaderStageKind.Fragment,TextureTarget=ShaderTextureTarget.Texture2D,Sampler=ShaderSamplerPolicy.NearestClamp)]
    int History2 { set; }
    /// <summary>Borrows physical history payload layer 3, without sampling a current draw attachment.</summary>
    [ShaderBinding("history3",ShaderBindingKind.Sampler,10,ShaderStageKind.Fragment,TextureTarget=ShaderTextureTarget.Texture2D,Sampler=ShaderSamplerPolicy.NearestClamp)]
    int History3 { set; }
    /// <summary>Borrows physical history payload layer 4, without sampling a current draw attachment.</summary>
    [ShaderBinding("history4",ShaderBindingKind.Sampler,11,ShaderStageKind.Fragment,TextureTarget=ShaderTextureTarget.Texture2D,Sampler=ShaderSamplerPolicy.NearestClamp)]
    int History4 { set; }
    /// <summary>Borrows physical history payload layer 5, without sampling a current draw attachment.</summary>
    [ShaderBinding("history5",ShaderBindingKind.Sampler,12,ShaderStageKind.Fragment,TextureTarget=ShaderTextureTarget.Texture2D,Sampler=ShaderSamplerPolicy.NearestClamp)]
    int History5 { set; }
    /// <summary>Borrows physical history payload layer 6, without sampling a current draw attachment.</summary>
    [ShaderBinding("history6",ShaderBindingKind.Sampler,13,ShaderStageKind.Fragment,TextureTarget=ShaderTextureTarget.Texture2D,Sampler=ShaderSamplerPolicy.NearestClamp)]
    int History6 { set; }
    #endregion
}
