using System;
using System.Runtime.CompilerServices;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.Rendering.Integration.Shadows;

/// <summary>Bounds queued native revision stamps without retaining discarded tessellated meshes.</summary>
internal sealed class NativeShadowTessellationStamps
{
    private readonly ConditionalWeakTable<TesselatedChunk, NativeShadowUpload> stamps = new();
    private readonly object gate = new();
    private int capacity;
    private int count;

    #region Public API
    /// <summary>Uses the finite pending-change allowance for outstanding native build stamps.</summary>
    internal NativeShadowTessellationStamps(int capacity)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        this.capacity = capacity;
    }

    /// <summary>Stops new stamp admission above a smaller allowance while retained native work drains.</summary>
    internal void Reconfigure(int allowance)
    {
        if (allowance <= 0) throw new ArgumentOutOfRangeException(nameof(allowance));
        lock (gate) capacity = allowance;
    }

    /// <summary>Declines unknown work on exhaustion rather than attaching an unbounded side table.</summary>
    internal bool TryAdd(TesselatedChunk chunk, NativeShadowUpload stamp)
    {
        lock (gate)
        {
            if (stamps.Remove(chunk)) count--;
            if (count >= capacity)
            {
                // Only pressure triggers a bounded recount; weak keys release native work discarded
                // by the engine before upload. Visits are bounded by the previously admitted
                // allowance while a smaller policy drains outstanding work.
                count = 0;
                foreach (var unused in stamps) count++;
                if (count >= capacity) return false;
            }
            stamps.Add(chunk, stamp); count++;
            return true;
        }
    }

    /// <summary>Transfers an upload stamp once, releasing its reserved bookkeeping slot.</summary>
    internal bool TryTake(TesselatedChunk chunk, out NativeShadowUpload? stamp)
    {
        lock (gate)
        {
            if (!stamps.TryGetValue(chunk, out stamp)) return false;
            stamps.Remove(chunk); count--;
            return true;
        }
    }

    /// <summary>Follows native part replacement exactly; missing or mixed-lifetime evidence stays unknown.</summary>
    internal void Merge(TesselatedChunk retained, TesselatedChunk incoming)
    {
        lock (gate)
        {
            TryTake(retained, out var prior);
            TryTake(incoming, out var next);
            if (prior == null && next == null) return;
            bool center = NativeShadowTessellationFields.Centers(incoming) != null;
            bool edge = NativeShadowTessellationFields.Edges(incoming) != null;
            if (!center && !edge) { if (prior != null) TryAdd(retained, prior); return; }
            var basis = prior ?? next!;
            bool sameOwner = prior == null || next == null || (ReferenceEquals(prior.Owner, next.Owner) && prior.Index == next.Index);
            bool sameChunk = prior != null && next != null && ReferenceEquals(prior.Chunk, next.Chunk);
            long centerRevision = center ? next?.CenterRevision ?? -1 : prior?.CenterRevision ?? -1;
            long edgeRevision = edge ? next?.EdgeRevision ?? -1 : prior?.EdgeRevision ?? -1;
            var chunk = center && edge && next != null ? next.Chunk : basis.Chunk;
            if (!sameOwner || (!sameChunk && !(center && edge))) centerRevision = edgeRevision = -1;
            TryAdd(retained, new(basis.Owner, basis.Index, centerRevision, edgeRevision, chunk));
        }
    }
    #endregion
}
