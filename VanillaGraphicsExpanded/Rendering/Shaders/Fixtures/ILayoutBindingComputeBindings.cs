using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.Rendering.Shaders.Fixtures;

/// <summary>Declares the sampled input and image output of the layout binding executable.</summary>
internal interface ILayoutBindingComputeBindings : IOutputImageComputeBindings
{
    #region Public API
    /// <summary>Declares the unsigned occupancy sampler's fixed unit.</summary>
    [ShaderBinding("uOcc", ShaderBindingKind.Sampler, 3, ShaderStageKind.Compute, ShaderType = ShaderResourceType.UnsignedIntSampler3D)]
    ShaderSamplerBinding Input { get; }
    #endregion
}
