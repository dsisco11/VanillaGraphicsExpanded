using System;
using VanillaGraphicsExpanded.Rendering.Integration;
using VanillaGraphicsExpanded.Rendering.Pipeline;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Resolves a declared engine contract before publishing an active mutation boundary.</summary>
internal sealed partial class StateCache
{
    private EngineBoundaryScope? activeBoundary;
    private bool resolvingBoundary;
    /// <summary>Retains the last optional entry failure for the adapter's diagnostics.</summary>
    internal Exception? BoundaryEntryFailure { get; private set; }
    /// <summary>Counts boundary mutable-state value reads; native error-status checks are excluded.</summary>
    internal long BoundaryQueries { get; private set; }
    /// <summary>Counts native error-status checks separately from state reads and mutation calls.</summary>
    internal long BoundaryErrorChecks { get; private set; }

    #region Public API
    /// <summary>Returns no token when incoming state cannot be resolved; never changes native drawing state.</summary>
    internal bool TryBeginEngineBoundary(EngineBoundaryDeclaration declaration, out EngineBoundaryScope? scope,
        EngineBoundaryResources? resources = null)
    {
        ArgumentNullException.ThrowIfNull(declaration);
        scope = null;
        // Nesting is a programming error, not an optional-rendering readiness failure.
        if (activeBoundary is not null || resolvingBoundary) throw new InvalidOperationException("Nested engine boundaries are unsupported.");
        BoundaryEntryFailure = null;
        resolvingBoundary = true;
        try
        {
            GpuSupport.EnsureCurrentContext();
            int count = MaxDrawBuffers;
            declaration.Coverage.ValidateOutputCount(count);
            ResolveBoundaryState(declaration.Coverage, count);
            // Resolve immutable limits here, not during the managed draw's first setter.
            if (declaration.Coverage.Dynamic.HasFlag(DynamicDrawStateKnowledge.Viewport)) EnsureViewportLimits();
            if (declaration.Coverage.Assembly.HasFlag(PrimitiveAssemblyStateKnowledge.PatchVertices)) EnsurePatchLimit();
            var snapshot = new PipelineStateSnapshot(declaration.Coverage,
                depth, rasterizer, assembly, dynamicState, clearColor, blend);
            scope = new EngineBoundaryScope(this, snapshot);
            if (resources is not null) CaptureBoundaryBindings(scope, resources);
            activeBoundary = scope;
            return true;
        }
        catch (Exception error) when (error is not OutOfMemoryException && !EngineBoundaryRestoreException.IsRestorationFailure(error))
        {
            // A failed query may have resolved earlier fields, but no partial token escapes.
            BoundaryEntryFailure = error;
            scope = null;
            return false;
        }
        finally { resolvingBoundary = false; }
    }

    /// <summary>Releases the entry token after its owning restoration operation has finished.</summary>
    /// <remarks>Does not restore native state. Only the cache restoration owner may use this handoff.</remarks>
    internal void ReleaseEngineBoundary(EngineBoundaryScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (!ReferenceEquals(activeBoundary, scope)) throw new InvalidOperationException("Boundary is not active on this cache.");
        activeBoundary = null;
        boundaryRetiredResources.Clear();
    }

    #endregion
}
