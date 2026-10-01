using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.Rendering.Shaders.Fixtures;

/// <summary>Declares the direct texture image API exercised by the linked compute fixture.</summary>
internal interface IGeneratedTextureImageBindings
{
    #region Public API
    /// <summary>Binds the fixture image with the default texture view.</summary>
    [ShaderBinding("outImg", ShaderBindingKind.Image, 0, ShaderStageKind.Compute)]
    GpuTexture Image { set; }
    #endregion
}
