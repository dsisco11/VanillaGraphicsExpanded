using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Owns exactly-once, ordered cleanup followed by cache-backed drawing-state restoration.</summary>
internal sealed class EngineBoundaryScope : IDisposable
{
    private readonly StateCache cache;
    private readonly List<(EngineBoundaryCleanup Order, IDisposable Owner)> cleanup = new();
    private bool consumed;
    private bool executing;
    internal PipelineStateSnapshot Snapshot { get; }

    #region Public API
    /// <summary>Retains an independently owned snapshot after complete entry resolution.</summary>
    internal EngineBoundaryScope(StateCache cache, PipelineStateSnapshot snapshot) { this.cache = cache; Snapshot = snapshot; }

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
        AddCleanup(EngineBoundaryCleanup.Shader, program.UseScope());
    }

    /// <summary>Runs optional work and preserves its exception alongside any independent cleanup failures.</summary>
    internal void Run(Action operation)
    {
        ObjectDisposedException.ThrowIf(consumed, this);
        ArgumentNullException.ThrowIfNull(operation);
        if (executing) throw new InvalidOperationException("Boundary execution cannot be nested.");
        Exception? failure = null;
        executing = true;
        try { cache.RequireBoundaryContext(Snapshot); operation(); }
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
    /// <summary>Consumes first, attempts independent owners in order, and always releases boundary authority.</summary>
    private void Complete(Exception? operationFailure)
    {
        if (consumed)
        {
            if (operationFailure is not null) ExceptionDispatchInfo.Capture(operationFailure).Throw();
            return;
        }
        consumed = true;
        var failures = new List<Exception>();
        try
        {
            // Context checks precede every owner: never bind old names into a replacement context.
            foreach (var order in Enum.GetValues<EngineBoundaryCleanup>())
                for (int i = cleanup.Count - 1; i >= 0; i--)
                    if (cleanup[i].Order == order)
                        cache.AttemptBoundaryCleanup(Snapshot, cleanup[i].Owner.Dispose, failures,
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
