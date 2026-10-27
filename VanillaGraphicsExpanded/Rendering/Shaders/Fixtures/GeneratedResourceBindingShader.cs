using VanillaGraphicsExpanded.Rendering.ShaderCompilation;
namespace VanillaGraphicsExpanded.Rendering.Shaders.Fixtures;

/// <summary>Exercises interface-generated resource setters against a fixture-owned linked program.</summary>
internal partial class GeneratedResourceBindingShader : GpuComputeProgram, IGeneratedResourceBindings
{
    #region Public API
    /// <summary>Owns a ready compute executable and retains generated binding values.</summary>
    internal GeneratedResourceBindingShader(ComputeShaderAsset pipeline) : base(pipeline) { }
    #endregion
}
