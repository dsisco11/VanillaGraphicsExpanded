using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.Rendering.Shaders.Fixtures;

/// <summary>Declares the generated resource API exercised by the linked compute fixture.</summary>
internal interface IGeneratedResourceBindings
{
    #region Public API
    /// <summary>Binds the input texture at the compute fixture's declared unit.</summary>
    [ShaderBinding("uOcc", ShaderBindingKind.Sampler, 3, ShaderStageKind.Compute)]
    GpuTexture Input { set; }

    /// <summary>Binds the output image while preserving its explicit view parameters.</summary>
    [ShaderBinding("outImg", ShaderBindingKind.Image, 0, ShaderStageKind.Compute)]
    GpuTextureBinding Output { set; }

    /// <summary>Exercises an optional resource absent from the linked fixture.</summary>
    [ShaderBinding("unusedTexture", ShaderBindingKind.Sampler, 7, ShaderStageKind.Compute, Required = false)]
    GpuTexture Unused { set; }
    #endregion
}
