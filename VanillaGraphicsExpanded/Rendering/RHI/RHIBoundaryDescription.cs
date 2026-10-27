using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Freezes the complete permitted pipeline set and operation footprint before any native mutation.</summary>
public sealed class RHIBoundaryDescription
{
    #region Public API
    /// <summary>Gets the diagnostic interruption name.</summary>
    public string Name { get; }
    /// <summary>Gets exact permitted graphics realizations without following asset reloads.</summary>
    public ImmutableArray<RHIGraphicsPipelineRef> Graphics { get; }
    /// <summary>Gets exact permitted compute realizations.</summary>
    public ImmutableArray<RHIComputePipelineRef> Compute { get; }
    /// <summary>Gets explicitly admitted helper effects beyond the pipelines' own footprints.</summary>
    public BoundaryOperations Operations { get; }
    /// <summary>Gets the integration caller's proof that nonqueryable conditional rendering is inactive.</summary>
    public bool ConditionalRenderingInactive { get; }
    /// <summary>Copies declarations using CPU-only supplied shape checks; execution resolves current lifetime and cached state.</summary>
    public RHIBoundaryDescription(string name, bool conditionalRenderingInactive,
        IEnumerable<RHIGraphicsPipelineRef>? graphics = null, IEnumerable<RHIComputePipelineRef>? compute = null,
        BoundaryOperations operations = BoundaryOperations.None)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if ((operations & ~BoundaryOperations.RenderPasses) != 0) throw new ArgumentOutOfRangeException(nameof(operations));
        Name = name; ConditionalRenderingInactive = conditionalRenderingInactive; Operations = operations;
        Graphics = graphics?.Distinct().ToImmutableArray() ?? [];
        Compute = compute?.Distinct().ToImmutableArray() ?? [];
        if (Graphics.Any(value => value.IsEmpty) || Compute.Any(value => value.IsEmpty))
            throw new ArgumentException("An admitted pipeline reference cannot be empty.");
    }
    #endregion
}
