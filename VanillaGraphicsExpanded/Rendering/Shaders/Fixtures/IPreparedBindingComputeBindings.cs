using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.Rendering.Shaders.Fixtures;

/// <summary>Declares array extents and independent image and sampler units for preparation validation.</summary>
internal interface IPreparedBindingComputeBindings
{
    #region Public API
    /// <summary>Declares two consecutive sampled texture units.</summary>
    [ShaderBinding("inputs", ShaderBindingKind.Sampler, 7, ShaderStageKind.Compute, ArrayLength = 2, ShaderType = ShaderResourceType.Sampler2D)]
    ShaderSamplerBinding Inputs { get; }
    /// <summary>Declares an image using the same numeric slot in its independent namespace.</summary>
    [ShaderBinding("outputImage", ShaderBindingKind.Image, 7, ShaderStageKind.Compute, ShaderType = ShaderResourceType.Image2D)]
    ShaderImageBinding Output { get; }
    /// <summary>Declares an unused required sampler whose inactivity remains legal.</summary>
    [ShaderBinding("unused", ShaderBindingKind.Sampler, 10, ShaderStageKind.Compute)]
    ShaderSamplerBinding Unused { get; }
    #endregion
}
