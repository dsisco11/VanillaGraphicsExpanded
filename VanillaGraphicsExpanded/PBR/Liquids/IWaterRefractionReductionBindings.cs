using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.PBR.Liquids;

/// <summary>Declares the immutable full-resolution inputs for receiver reduction.</summary>
[ShaderBindingSet(typeof(IShaderInterfaceLocations), Defaults = true)]
[ShaderBindingSet(typeof(IShaderIncludeBindings), Defaults = true)]
internal interface IWaterRefractionReductionBindings
{
    #region Public API
    /// <summary>Supplies restored linear radiance with opaque receiver validity in alpha.</summary>
    [ShaderBinding("sourceColor", ShaderBindingKind.Sampler, 0, ShaderStageKind.Fragment, Sampler = ShaderSamplerPolicy.NearestClamp)]
    DynamicTexture2D? SourceColor { set; }

    /// <summary>Supplies matching hardware depth from the same composite draw.</summary>
    [ShaderBinding("sourceDepth", ShaderBindingKind.Sampler, 1, ShaderStageKind.Fragment, Sampler = ShaderSamplerPolicy.NearestClamp)]
    DynamicTexture2D? SourceDepth { set; }
    #endregion
}
