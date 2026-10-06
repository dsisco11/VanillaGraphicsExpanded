using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.Rendering.Shaders.Fixtures;

/// <summary>Exercises generated retained values through a packaged offline compute executable.</summary>
[ShaderProgram("Contract", "tests/uniform_state", 1)]
[ShaderStage("Contract", ShaderStageKind.Compute, "tests/uniform_state.csh")]
[ShaderUse("Contract", ShaderStageKind.Compute, nameof(Alternate), SpecializationId = 42)]
internal sealed partial class UniformStateComputeShader : GpuComputeShader, IUniformStateComputeBindings
{
    private readonly UniformStateBuffer inputs = new();
    #region Public API
    /// <summary>Selects a specialized executable without adding runtime binding values.</summary>
    [ShaderOption("UNIFORM_ALTERNATE", false)]
    internal static partial ShaderOption<bool> Alternate { get; }
    /// <summary>Adopts an executable loaded through the established compute preparation owner.</summary>
    internal UniformStateComputeShader(GpuComputePipeline pipeline) : base(pipeline)
    {
        inputs.SetWriteGuard(RequireInputMutation);
    }
    /// <summary>Retains the scalar until the next prepared publication.</summary>
    public float Scalar { get => inputs.Scalar; set => inputs.Scalar = value; }
    /// <summary>Copies desired array values without retaining caller-owned mutable storage.</summary>
    public float[] Values { get => inputs.Values; set => inputs.Values = value; }
    /// <summary>Retains the desired vector.</summary>
    public System.Numerics.Vector3 Vector { get => inputs.Vector; set => inputs.Vector = value; }
    /// <summary>Retains the desired matrix.</summary>
    public System.Numerics.Matrix4x4 Transform { get => inputs.Transform; set => inputs.Transform = value; }
    /// <summary>Retires the owned numeric block with the compute owner.</summary>
    public override void Dispose()
    {
        if (IsDisposed) return;
        RequireInputMutation();
        inputs.Dispose();
        base.Dispose();
    }
    #endregion
    #region Binding sources
    /// <summary>Supplies the owned block to generated validation and publication.</summary>
    CpuUniformBuffer IUniformStateComputeBindings.Inputs => inputs;
    #endregion
}
