using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Retains immutable framebuffer declarations separately from dynamic live-state knowledge.</summary>
internal sealed partial class StateCache
{
    private readonly ConcurrentDictionary<int, GpuFramebuffer> immutableFramebuffers = new();

    #region Internal API
    /// <summary>Rejects mutation through another managed wrapper of an immutable native framebuffer identity.</summary>
    internal void RequireMutableFramebufferConfiguration(int id)
    {
        if (immutableFramebuffers.ContainsKey(id))
            throw new InvalidOperationException("Immutable framebuffer configuration cannot change through an alias.");
    }
    /// <summary>Registers a completed immutable configuration so managed raw routing writes cannot alter it.</summary>
    internal void RegisterImmutableFramebuffer(GpuFramebuffer framebuffer)
    {
        if (!immutableFramebuffers.TryAdd(framebuffer.FboId, framebuffer))
            throw new InvalidOperationException("A immutable framebuffer identity is already registered.");
    }
    /// <summary>Withdraws only the matching immutable declaration during disposal or handle transfer.</summary>
    internal void UnregisterImmutableFramebuffer(int id, GpuFramebuffer framebuffer)
    {
        // Disposal may be queued from another thread; remove only this declaration atomically.
        ((ICollection<KeyValuePair<int, GpuFramebuffer>>)immutableFramebuffers)
            .Remove(new(id, framebuffer));
    }
    #endregion
}
