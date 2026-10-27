using System;

namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Identifies one fixed compute executable; diagnostic labels do not affect behavior identity.</summary>
public sealed class RHIComputePipelineDescription : IEquatable<RHIComputePipelineDescription>
{
    #region Public API
    /// <summary>Gets the exact executable generation selected by this immutable description.</summary>
    public RHIShaderRef Shader { get; }
    /// <summary>Gets the optional diagnostic label.</summary>
    public string? Label { get; }
    #region Construction
    /// <summary>Captures supplied reference identity without native queries or metadata inspection.</summary>
    public RHIComputePipelineDescription(RHIShaderRef shader, string? label = null)
    {
        if (shader.IsEmpty) throw new ArgumentException("A compute shader reference is required.", nameof(shader));
        Shader = shader; Label = label;
    }
    #endregion
    #region Identity
    /// <summary>Compares behavior and exact executable generation independently of diagnostic names.</summary>
    public bool Equals(RHIComputePipelineDescription? other) => other is not null && Shader == other.Shader;
    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is RHIComputePipelineDescription other && Equals(other);
    /// <summary>Compares immutable behavior descriptions, including absent values.</summary>
    public static bool operator ==(RHIComputePipelineDescription? left, RHIComputePipelineDescription? right)
        => ReferenceEquals(left, right) || left is not null && left.Equals(right);
    /// <summary>Reports different executable behavior independently of labels.</summary>
    public static bool operator !=(RHIComputePipelineDescription? left, RHIComputePipelineDescription? right) => !(left == right);
    /// <inheritdoc />
    public override int GetHashCode() => Shader.GetHashCode();
    #endregion
    #endregion
}
