using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.Rendering.Shaders.Fixtures;

/// <summary>Exercises a direct texture setter against a fixture-owned linked image.</summary>
internal partial class GeneratedTextureImageShader : GpuProgram
{
    #region Public API
    /// <summary>Binds the fixture image with the default texture view.</summary>
    [ShaderBinding("outImg", ShaderBindingKind.Image, 0, ShaderStageKind.Compute)]
    public partial GpuTexture Image { set; }
    #endregion
}
