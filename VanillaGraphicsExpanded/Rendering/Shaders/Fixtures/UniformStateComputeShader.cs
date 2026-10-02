using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.Rendering.Shaders.Fixtures;

/// <summary>Exercises generated retained values through a packaged offline compute executable.</summary>
[ShaderProgram("Contract", "tests/uniform_state", 1)]
[ShaderStage("Contract", ShaderStageKind.Compute, "tests/uniform_state.csh")]
[ShaderUse("Contract", ShaderStageKind.Compute, nameof(Alternate), SpecializationId = 42)]
internal sealed partial class UniformStateComputeShader : GpuComputeShader, IUniformStateComputeBindings
{
    #region Public API
    /// <summary>Selects a specialized executable without adding runtime binding values.</summary>
    [ShaderOption("UNIFORM_ALTERNATE", false)]
    internal static partial ShaderOption<bool> Alternate { get; }
    /// <summary>Adopts an executable loaded through the established compute preparation owner.</summary>
    internal UniformStateComputeShader(GpuComputePipeline pipeline) : base(pipeline) { }
    #endregion
}
