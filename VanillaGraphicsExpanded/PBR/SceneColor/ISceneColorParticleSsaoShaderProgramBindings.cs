using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.PBR.SceneColor;

/// <summary>Declares visibility and isolated engine metadata inputs for particle SSAO restoration.</summary>
[ShaderBindingSet(typeof(IShaderInterfaceLocations), Defaults = true)]
[ShaderBindingSet(typeof(IShaderIncludeBindings), Defaults = true)]
internal interface ISceneColorParticleSsaoShaderProgramBindings
{
    #region Public API
    /// <summary>Samples the current engine depth without attaching it to the output target.</summary>
    [ShaderBinding("visibilityDepth", ShaderBindingKind.Sampler, 0, ShaderStageKind.Fragment, TextureTarget = ShaderTextureTarget.Texture2D, Sampler = ShaderSamplerPolicy.NearestClamp)]
    int VisibilityDepth { set; }
    /// <summary>Samples the material depth before particle submission.</summary>
    [ShaderBinding("beforeDepth", ShaderBindingKind.Sampler, 1, ShaderStageKind.Fragment, TextureTarget = ShaderTextureTarget.Texture2D, Sampler = ShaderSamplerPolicy.NearestClamp)]
    int BeforeDepth { set; }
    /// <summary>Samples visibility immediately after particle submission.</summary>
    [ShaderBinding("afterDepth", ShaderBindingKind.Sampler, 2, ShaderStageKind.Fragment, TextureTarget = ShaderTextureTarget.Texture2D, Sampler = ShaderSamplerPolicy.NearestClamp)]
    int AfterDepth { set; }
    /// <summary>Samples the original cube shader's SSAO normal output.</summary>
    [ShaderBinding("particleNormal", ShaderBindingKind.Sampler, 3, ShaderStageKind.Fragment, TextureTarget = ShaderTextureTarget.Texture2D, Sampler = ShaderSamplerPolicy.NearestClamp)]
    DynamicTexture2D? ParticleNormal { set; }
    /// <summary>Samples the original cube shader's SSAO view-position output.</summary>
    [ShaderBinding("particlePosition", ShaderBindingKind.Sampler, 4, ShaderStageKind.Fragment, TextureTarget = ShaderTextureTarget.Texture2D, Sampler = ShaderSamplerPolicy.NearestClamp)]
    DynamicTexture2D? ParticlePosition { set; }
    #endregion
}
