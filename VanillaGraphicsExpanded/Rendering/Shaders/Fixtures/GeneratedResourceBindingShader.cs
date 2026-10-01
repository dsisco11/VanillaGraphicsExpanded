using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.Rendering.Shaders.Fixtures;

/// <summary>Exercises generated resource setters against a fixture-owned linked program.</summary>
internal partial class GeneratedResourceBindingShader : GpuProgram
{
    #region Public API
    /// <summary>Binds the input texture at the compute fixture's declared unit.</summary>
    [ShaderBinding("uOcc", ShaderBindingKind.Sampler, 3, ShaderStageKind.Compute)]
    public partial GpuTexture Input { set; }

    /// <summary>Binds the output image while preserving its explicit view parameters.</summary>
    [ShaderBinding("outImg", ShaderBindingKind.Image, 0, ShaderStageKind.Compute)]
    public partial GpuTextureBinding Output { set; }

    /// <summary>Exercises an optional resource absent from the linked fixture.</summary>
    [ShaderBinding("unusedTexture", ShaderBindingKind.Sampler, 7, ShaderStageKind.Compute, Required = false)]
    public partial GpuTexture Unused { set; }
    #endregion
}
