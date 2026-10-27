using System;

namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Retains an exact compute realization without exposing disposal or executable ownership.</summary>
public readonly struct RHIComputePipelineRef : IEquatable<RHIComputePipelineRef>
{
    private readonly RHIComputePipeline? pipeline;
    #region Public API
    /// <summary>Reports absence independently of retirement.</summary>
    public bool IsEmpty => pipeline is null;
    /// <summary>Compares the retained managed realization identities.</summary>
    public bool Equals(RHIComputePipelineRef other) => ReferenceEquals(pipeline, other.pipeline);
    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is RHIComputePipelineRef other && Equals(other);
    /// <inheritdoc />
    public override int GetHashCode() => pipeline is null ? 0 : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(pipeline);
    /// <summary>Compares exact retained realization identities.</summary>
    public static bool operator ==(RHIComputePipelineRef left, RHIComputePipelineRef right) => left.Equals(right);
    /// <summary>Reports different retained realization identities.</summary>
    public static bool operator !=(RHIComputePipelineRef left, RHIComputePipelineRef right) => !left.Equals(right);
    #endregion
    #region Internal API
    /// <summary>Captures an optional realization without acquiring guards or native state.</summary>
    internal RHIComputePipelineRef(RHIComputePipeline? pipeline) { this.pipeline = pipeline; }
    /// <summary>Requires the original live realization before execution.</summary>
    internal RHIComputePipeline RequirePipeline()
    {
        var value = pipeline ?? throw new InvalidOperationException("A compute pipeline reference is required.");
        _ = value.RequireShader();
        return value;
    }
    #endregion
}
