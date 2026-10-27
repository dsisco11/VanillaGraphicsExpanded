using VanillaGraphicsExpanded.Rendering.ShaderCompilation;
namespace VanillaGraphicsExpanded.Rendering.Shaders.Fixtures;

/// <summary>Exercises an interface-generated direct texture setter against a fixture-owned linked image.</summary>
internal partial class GeneratedTextureImageShader : GpuComputeProgram, IGeneratedTextureImageBindings
{
    #region Public API
    /// <summary>Owns a ready compute executable and retains generated binding values.</summary>
    internal GeneratedTextureImageShader(ComputeShaderAsset pipeline) : base(pipeline) { }
    #endregion
}
