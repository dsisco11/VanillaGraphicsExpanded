using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Retains resources for render-thread disposal within one graphics-manager lifetime.</summary>
internal sealed class GpuResourceDisposalQueue
{
    private readonly object gate = new();
    private readonly Queue<GpuResource> pending = new();
    private bool closed;

    #region Public API
    /// <summary>Reports whether this context lifetime has ended.</summary>
    public bool IsClosed { get { lock (gate) return closed; } }

    /// <summary>Queues cleanup without issuing GL calls, including when called by a finalizer.</summary>
    public void Enqueue(GpuResource resource)
    {
        lock (gate)
        {
            // A closed context owns reclamation of its remaining allocations. Never carry
            // their integer names into another context, where those names may be reused.
            if (closed) return;
            pending.Enqueue(resource);
        }
    }

    /// <summary>Disposes pending resources on the caller's current render context.</summary>
    public void DrainPending()
    {
        while (true)
        {
            GpuResource resource;
            lock (gate)
            {
                if (closed || !pending.TryDequeue(out resource!)) return;
            }
            try { resource.Dispose(); }
            catch (Exception error) { Debug.WriteLine($"[GpuResourceDisposalQueue] {error}"); }
        }
    }

    /// <summary>Closes admission and either drains on the render thread or abandons context-owned allocations.</summary>
    public void Close(bool canDispose)
    {
        GpuResource[] remaining;
        lock (gate)
        {
            if (closed) return;
            closed = true;
            remaining = pending.ToArray();
            pending.Clear();
        }
        if (!canDispose) return;
        foreach (var resource in remaining)
        {
            // One failing managed cleanup must not strand the remainder after admission closes.
            try { resource.Dispose(); }
            catch (Exception error) { Debug.WriteLine($"[GpuResourceDisposalQueue] {error}"); }
        }
    }
    #endregion
}
