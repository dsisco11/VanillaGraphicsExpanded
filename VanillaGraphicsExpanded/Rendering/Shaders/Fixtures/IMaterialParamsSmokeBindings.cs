using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.Rendering.Shaders.Fixtures;

/// <summary>Declares the material texture read by the offline smoke executable.</summary>
internal interface IMaterialParamsSmokeBindings
{
    #region Public API
    /// <summary>Declares the existing material parameter texture unit.</summary>
    [ShaderBinding("materialParams", ShaderBindingKind.Sampler, 0, ShaderStageKind.Fragment, ShaderType = ShaderResourceType.Sampler2D)]
    ShaderSamplerBinding MaterialParams { get; }
    #endregion
}
