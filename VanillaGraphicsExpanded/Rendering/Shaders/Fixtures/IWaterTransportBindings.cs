using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.Rendering.Shaders.Fixtures;

/// <summary>Supplies independent atmospheric tables to the water composition fixture.</summary>
internal interface IWaterTransportBindings
{
    #region Public API
    /// <summary>Supplies packed Rayleigh and Mie source radiance.</summary>
    [ShaderBinding("vge_atmosphereAerialRadiance", ShaderBindingKind.Sampler, 5, ShaderStageKind.Fragment,
        TextureTarget = ShaderTextureTarget.Texture3D, Sampler = ShaderSamplerPolicy.Default)]
    DynamicTexture3D AerialRadiance { set; }
    /// <summary>Supplies independent RGB atmospheric transmission loss.</summary>
    [ShaderBinding("vge_atmosphereAerialAttenuation", ShaderBindingKind.Sampler, 6, ShaderStageKind.Fragment,
        TextureTarget = ShaderTextureTarget.Texture3D, Sampler = ShaderSamplerPolicy.Default)]
    DynamicTexture3D AerialAttenuation { set; }
    #endregion
}
