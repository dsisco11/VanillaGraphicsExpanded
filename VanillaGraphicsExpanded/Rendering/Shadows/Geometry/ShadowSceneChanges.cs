using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using VanillaGraphicsExpanded.Rendering.Shadows.Lights;

namespace VanillaGraphicsExpanded.Rendering.Shadows.Geometry;

/// <summary>Bounds retained change history; lost or unknown extents advance a broad invalidation epoch.</summary>
internal sealed class ShadowSceneChanges
{
    private readonly Queue<ShadowSceneChange> changes = new();
    private readonly int capacity;
    private long sequence, epoch = 1, discardedThrough;
    private int pending;
    private readonly object gate = new();

    #region Public API
    /// <summary>Requires a finite history allowance from the owning policy.</summary>
    internal ShadowSceneChanges(int capacity)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        this.capacity = capacity;
    }

    /// <summary>Withdraws certification while a scene owner is changing its represented state.</summary>
    internal void BeginChange() { lock (gate) pending = checked(pending + 1); }

    /// <summary>Publishes creation, removal or prior/current motion and relevant content revisions.</summary>
    internal void CompleteChange(ShadowSourceKey source, ShadowBounds? prior, ShadowBounds? current,
        long geometryRevision, long materialRevision, long deformationRevision)
    {
        lock (gate)
        {
            if (pending == 0) throw new InvalidOperationException("No scene change is pending.");
            sequence = checked(sequence + 1);
            // Unknown coverage and overflow invalidate all dependencies before old evidence is dropped.
            if (!source.IsKnown || (prior == null && current == null) || prior is { IsValid: false }
                || current is { IsValid: false } || changes.Count == capacity)
            { epoch = checked(epoch + 1); discardedThrough = sequence; changes.Clear(); }
            else changes.Enqueue(new(sequence, source, prior, current, geometryRevision, materialRevision, deformationRevision));
            pending--;
        }
    }

    /// <summary>Captures bounded history and distinguishes missing history from no relevant changes.</summary>
    internal ShadowChangeBatch Capture(long afterSequence, long previousEpoch)
    {
        lock (gate)
        {
            bool lost = afterSequence < discardedThrough || afterSequence > sequence || previousEpoch != epoch;
            var result = ImmutableArray.CreateBuilder<ShadowSceneChange>();
            foreach (var change in changes) if (change.Sequence > afterSequence) result.Add(change);
            return new(sequence, epoch, pending == 0, lost, result.ToImmutable());
        }
    }
    #endregion
}
