using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.PBR.SceneColor;

/// <summary>Declares exact texel inputs for particle visibility and underlying material reconstruction.</summary>
[ShaderBindingSet(typeof(IShaderInterfaceLocations), Defaults = true)]
[ShaderBindingSet(typeof(IShaderIncludeBindings), Defaults = true)]
internal interface ISceneColorParticleShaderProgramBindings
{
    #region Public API
    /// <summary>Samples the final engine visibility depth.</summary>
    [ShaderBinding("visibilityDepth", ShaderBindingKind.Sampler, 0, ShaderStageKind.Fragment, TextureTarget = ShaderTextureTarget.Texture2D, Sampler = ShaderSamplerPolicy.NearestClamp)]
    int VisibilityDepth { set; }
    /// <summary>Samples material depth from before particle submission.</summary>
    [ShaderBinding("beforeDepth", ShaderBindingKind.Sampler, 1, ShaderStageKind.Fragment, TextureTarget = ShaderTextureTarget.Texture2D, Sampler = ShaderSamplerPolicy.NearestClamp)]
    int BeforeDepth { set; }
    /// <summary>Samples visibility depth immediately after particle submission.</summary>
    [ShaderBinding("afterDepth", ShaderBindingKind.Sampler, 2, ShaderStageKind.Fragment, TextureTarget = ShaderTextureTarget.Texture2D, Sampler = ShaderSamplerPolicy.NearestClamp)]
    int AfterDepth { set; }
    /// <summary>Samples premultiplied particle radiance and accumulated coverage.</summary>
    [ShaderBinding("particleColor", ShaderBindingKind.Sampler, 3, ShaderStageKind.Fragment, TextureTarget = ShaderTextureTarget.Texture2D, Sampler = ShaderSamplerPolicy.NearestClamp)]
    int ParticleColor { set; }
    #endregion
}
