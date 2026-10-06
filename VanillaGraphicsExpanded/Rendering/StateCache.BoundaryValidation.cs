using System;
using VanillaGraphicsExpanded.Rendering.Pipeline;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Enforces declared mutation coverage before managed drawing-state operations.</summary>
internal sealed partial class StateCache
{
    #region Public API
    /// <summary>Rejects a second engine entry before shader preparation or other setup can run.</summary>
    internal void RequireOutsideEngineBoundary()
    {
        if (activeBoundary is not null || resolvingBoundary)
            throw new InvalidOperationException("Nested engine boundaries are unsupported.");
    }

    /// <summary>Requires shared pass work to execute under its caller's active restoration contract.</summary>
    internal void RequireEngineBoundary(EngineBoundaryScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (!ReferenceEquals(activeBoundary, scope)) throw new InvalidOperationException("An active engine boundary is required.");
    }

    /// <summary>Rejects unsupported drawing-state commands while managed boundary authority is active.</summary>
    internal void RejectUnsupportedBoundaryMutation()
    {
        if (activeBoundary is not null || resolvingBoundary)
            throw new InvalidOperationException("Unsupported state mutation inside an engine boundary.");
    }
    #endregion

    #region Private
    /// <summary>Requires complete drawing-state coverage before supplemental state can change.</summary>
    private void ValidateCompleteMutation()
    {
        if (resolvingBoundary) throw new InvalidOperationException("Drawing state cannot change during boundary resolution.");
        if (activeBoundary is not null && !activeBoundary.Snapshot.Coverage.CompleteGraphics)
            throw new InvalidOperationException("Managed operation exceeds the declared complete graphics coverage.");
    }

    /// <summary>Checks a scalar or indexed setter without allocating coverage objects in managed draws.</summary>
    private void ValidateBoundaryMutation(DepthStateKnowledge depth = default,
        RasterizerStateKnowledge rasterizer = default, PrimitiveAssemblyStateKnowledge assembly = default,
        DynamicDrawStateKnowledge dynamic = default, BlendStateKnowledge blend = default,
        int index = -1, bool clearColor = false)
    {
        if (resolvingBoundary) throw new InvalidOperationException("Drawing state cannot change during boundary resolution.");
        if (activeBoundary is null) return;
        var snapshot = activeBoundary.Snapshot;
        var coverage = snapshot.Coverage;
        bool permitted = coverage.Depth.HasFlag(depth) && coverage.Rasterizer.HasFlag(rasterizer)
            && coverage.Assembly.HasFlag(assembly) && coverage.Dynamic.HasFlag(dynamic)
            && (!clearColor || coverage.ClearColor);
        if (blend != BlendStateKnowledge.None)
        {
            if (index >= 0)
                permitted &= index < snapshot.OutputCount && coverage.BlendAt(index).HasFlag(blend);
            else
                for (int i = 0; i < snapshot.OutputCount; i++) permitted &= coverage.BlendAt(i).HasFlag(blend);
        }
        if (!permitted) throw new InvalidOperationException("Managed operation exceeds the declared engine-boundary coverage.");
    }

    /// <summary>Checks the entire operation against entry coverage before native work or redundant-call suppression.</summary>
    private void ValidateBoundaryMutation(PipelineStateCoverage operation)
    {
        if (resolvingBoundary) throw new InvalidOperationException("Drawing state cannot change during boundary resolution.");
        if (activeBoundary is null) return;
        var snapshot = activeBoundary.Snapshot;
        if (!snapshot.Coverage.Contains(operation, snapshot.OutputCount))
            throw new InvalidOperationException("Managed operation exceeds the declared engine-boundary coverage.");
    }

    #endregion
}
