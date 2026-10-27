using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.PBR.Materials.Energy;

/// <summary>Supplies immutable directional-albedo data to explicit material-response consumers.</summary>
internal interface IMicrofacetEnergyBindings
{
    #region Public API
    /// <summary>Borrows the complete lookup only when the linked shader evaluates material energy.</summary>
    [ShaderBinding("vge_microfacetEnergy", ShaderBindingKind.Sampler, GpuBindingRegistry.Sampler.MicrofacetEnergy,
        ShaderStageKind.Fragment, ShaderStageKind.Compute, Required = false,
        TextureTarget = ShaderTextureTarget.Texture2D, Sampler = ShaderSamplerPolicy.LinearClamp)]
    GpuTextureRef MicrofacetEnergy => MicrofacetEnergyLookup.Prepare();
    #endregion
}
