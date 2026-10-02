using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.Rendering.Shaders.Fixtures;

/// <summary>Declares the fixed image output shared by small offline compute fixtures.</summary>
internal interface IOutputImageComputeBindings
{
    #region Public API
    /// <summary>Declares the unsigned 3D image output unit.</summary>
    [ShaderBinding("outImg", ShaderBindingKind.Image, 0, ShaderStageKind.Compute, ShaderType = ShaderResourceType.UnsignedIntImage3D)]
    ShaderImageBinding Output { get; }
    #endregion
}
