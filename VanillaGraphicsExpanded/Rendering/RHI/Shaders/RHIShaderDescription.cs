using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Freezes the legal stage set, authored binding interface and optional executable-cache input.</summary>
public sealed class RHIShaderDescription
{
    #region Public API
    /// <summary>Gets the executable's graphics or compute role.</summary>
    public ShaderKind Kind { get; }
    /// <summary>Gets the frozen stage inputs in deterministic stage order.</summary>
    public ImmutableArray<RHIShaderStageDescription> Stages { get; }
    /// <summary>Gets a read-only snapshot of the authored binding contract.</summary>
    public GpuBindingContract Bindings { get; }
    /// <summary>Gets the owner's optional, already selected executable cache input.</summary>
    public RHIShaderBinary? Binary { get; }
    /// <summary>Gets whether the owner requests later executable-cache extraction.</summary>
    public bool RetrievableBinary { get; }
    /// <summary>Gets the diagnostic label, which does not define executable behavior.</summary>
    public string? Label { get; }
    /// <summary>Validates supplied stage combinations without requiring a native context or reading assets.</summary>
    public RHIShaderDescription(ShaderKind kind, IEnumerable<RHIShaderStageDescription> stages,
        GpuBindingContract bindings, RHIShaderBinary? binary = null, bool retrievableBinary = false, string? label = null)
    {
        ArgumentNullException.ThrowIfNull(stages); ArgumentNullException.ThrowIfNull(bindings);
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        Stages = stages.OrderBy(value => value?.Stage).ToImmutableArray();
        if (Stages.IsEmpty || Stages.Any(value => value is null) || Stages.Select(value => value.Stage).Distinct().Count() != Stages.Length)
            throw new ArgumentException("Shader stages must be nonempty, nonnull and unique.", nameof(stages));
        var kinds = Stages.Select(value => value.Stage).ToHashSet();
        if (kind == ShaderKind.Compute ? Stages.Length != 1 || !kinds.Contains(ShaderStage.Compute)
            : !kinds.Contains(ShaderStage.Vertex) || kinds.Contains(ShaderStage.Compute)
                || kinds.Contains(ShaderStage.TessellationControl) != kinds.Contains(ShaderStage.TessellationEvaluation))
            throw new ArgumentException("Invalid stage combination for the executable kind.", nameof(stages));
        Kind = kind; Bindings = bindings.Snapshot(); _ = GpuBindingContract.Merge([Bindings]); Binary = binary;
        RetrievableBinary = retrievableBinary; Label = label;
    }
    #endregion
}
