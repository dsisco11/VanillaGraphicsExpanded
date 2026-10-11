using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Owns exactly-once, ordered cleanup followed by cache-backed drawing-state restoration.</summary>
internal sealed class EngineBoundaryScope : IDisposable
{
    private readonly StateCache cache;
    private readonly (nint Handle, long Generation) context;
    private readonly List<(EngineBoundaryCleanup Order, IDisposable Owner)> cleanup = new();
    private bool consumed;
    private bool executing;
    internal PipelineStateSnapshot Snapshot { get; }

    #region Public API
    /// <summary>Retains an independently owned snapshot after complete entry resolution.</summary>
    internal EngineBoundaryScope(StateCache cache, PipelineStateSnapshot snapshot)
    { this.cache = cache; Snapshot = snapshot; context = Integration.RenderContextRegistry.Current(); }

    /// <summary>Registers an existing scope for ordered cleanup without transferring resource ownership.</summary>
    internal void AddCleanup(EngineBoundaryCleanup order, IDisposable owner)
    {
        ObjectDisposedException.ThrowIf(consumed, this);
        ArgumentNullException.ThrowIfNull(owner);
        if (!Enum.IsDefined(order)) throw new ArgumentOutOfRangeException(nameof(order));
        cleanup.Add((order, owner));
    }

    /// <summary>Activates an already prepared shader and defers its existing ownership scope to ordered cleanup.</summary>
    internal void Activate(Shaders.GpuProgram program)
    {
        ObjectDisposedException.ThrowIf(consumed, this);
        if (!executing) throw new InvalidOperationException("Shader activation requires a running boundary.");
        if (program.RequiresPreparation || program.IsRetired)
            throw new InvalidOperationException("Boundary shaders must be prepared before entry.");
        AddCleanup(EngineBoundaryCleanup.Shader, program.UseScope(false));
    }

    /// <summary>Runs optional work and preserves its exception alongside any independent cleanup failures.</summary>
    internal void Run(Action operation)
    {
        ObjectDisposedException.ThrowIf(consumed, this);
        ArgumentNullException.ThrowIfNull(operation);
        RequireOriginatingContext();
        if (executing) throw new InvalidOperationException("Boundary execution cannot be nested.");
        Exception? failure = null;
        executing = true;
        try { operation(); }
        catch (Exception error) { failure = error; }
        finally { executing = false; }
        Complete(failure);
    }

    /// <summary>Restores registered owners and drawing state once; repeated disposal issues no native calls.</summary>
    public void Dispose()
    {
        if (executing) throw new InvalidOperationException("Boundary cleanup belongs after the operation.");
        Complete(null);
    }
    #endregion

    #region Private
    /// <summary>Rejects cross-context work before any owner cleanup or native snapshot restoration.</summary>
    private void RequireOriginatingContext()
    {
        if (context.Handle == 0 || context != Integration.RenderContextRegistry.Current())
            throw new EngineBoundaryRestoreException("Engine boundary requires its originating live render context.",
                new InvalidOperationException("Cross-context boundary execution or restoration was rejected."));
    }

    /// <summary>Consumes first, attempts independent owners in order, and always releases boundary authority.</summary>
    private void Complete(Exception? operationFailure)
    {
        if (consumed)
        {
            if (operationFailure is not null) ExceptionDispatchInfo.Capture(operationFailure).Throw();
            return;
        }
        // Reject before consuming cleanup owners so a live context switch can return and retry.
        try { RequireOriginatingContext(); }
        catch (EngineBoundaryRestoreException restoration)
        {
            if (operationFailure is not null) throw new AggregateException(operationFailure, restoration);
            throw;
        }
        consumed = true;
        var failures = new List<Exception>();
        try
        {
            // Attempt each independent owner before restoring drawing state.
            foreach (var order in Enum.GetValues<EngineBoundaryCleanup>())
                for (int i = cleanup.Count - 1; i >= 0; i--)
                    if (cleanup[i].Order == order)
                        cache.AttemptBoundaryCleanup(cleanup[i].Owner.Dispose, failures,
                            order == EngineBoundaryCleanup.Shader);
            cache.RestoreBoundaryState(Snapshot, failures);
        }
        finally { cache.ReleaseEngineBoundary(this); }
        if (failures.Count != 0)
        {
            var restoration = new EngineBoundaryRestoreException("Engine boundary restoration failed.", new AggregateException(failures));
            if (operationFailure is not null) throw new AggregateException(operationFailure, restoration);
            throw restoration;
        }
        if (operationFailure is not null) ExceptionDispatchInfo.Capture(operationFailure).Throw();
    }
    #endregion
}
