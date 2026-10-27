using System;
using System.Linq;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.ProgramBinaries;
using VanillaGraphicsExpanded.Rendering.RHI;

namespace VanillaGraphicsExpanded.Rendering.ShaderCompilation;

/// <summary>Projects asset-selected, digest-validated code into immutable executable requests without native execution.</summary>
internal static class ShaderExecutableInputs
{
    #region Internal API
    /// <summary>Freezes captured stage payloads and retains cache decisions above the native creation boundary.</summary>
    internal static RHIShaderDescription Create(ShaderLoadPlan plan, PreparedProgramBinary inputs, RHIShaderBinary? binary = null)
    {
        var stages = plan.Stages.Select(selection => CreateStage(selection, inputs.Read(selection.BinaryPath))).ToArray();
        var bindings = GpuBindingContract.Merge(plan.Stages.Select(selection => selection.Stage.Bindings));
        bool compute = stages.Length == 1 && stages[0].Stage == ShaderStage.Compute;
        return new(compute ? ShaderKind.Compute : ShaderKind.Graphics, stages, bindings, binary,
            retrievableBinary: inputs.Key is not null, label: plan.Settings.Contract.Identity);
    }
    /// <summary>Copies one explicitly selected module and preserves typed specialization values without touching native state.</summary>
    internal static RHIShaderStageDescription CreateStage(ShaderStageSelection selection, ReadOnlySpan<byte> bytes)
        => new(Stage(selection.Stage.Kind), bytes, selection.Stage.EntryPoint,
            selection.Specializations.Select(value => Specialization(value.Id, value.Value)));
    #endregion

    #region Private
    /// <summary>Maps catalog stage policy without exposing it as a native API token.</summary>
    private static ShaderStage Stage(ShaderStageKind kind) => kind switch
    {
        ShaderStageKind.Vertex => ShaderStage.Vertex,
        ShaderStageKind.Fragment => ShaderStage.Fragment,
        ShaderStageKind.Geometry => ShaderStage.Geometry,
        ShaderStageKind.TessellationControl => ShaderStage.TessellationControl,
        ShaderStageKind.TessellationEvaluation => ShaderStage.TessellationEvaluation,
        ShaderStageKind.Compute => ShaderStage.Compute,
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };
    /// <summary>Retains the source option's exact scalar representation in the backend-neutral request.</summary>
    private static RHIShaderSpecialization Specialization(uint id, ShaderScalar value) => value.Type switch
    {
        ShaderScalarType.Bool => new(id, value.Bits != 0),
        ShaderScalarType.Int => new(id, unchecked((int)value.Bits)),
        ShaderScalarType.UInt => new(id, value.Bits),
        ShaderScalarType.Float => new(id, BitConverter.UInt32BitsToSingle(value.Bits)),
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };
    #endregion
}
