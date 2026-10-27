using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Retains immutable framebuffer declarations separately from dynamic live-state knowledge.</summary>
internal sealed partial class StateCache
{
    private readonly ConcurrentDictionary<((nint Handle, long Generation) Context, int Id), GpuFramebuffer> immutableFramebuffers = new();

    #region Internal API
    /// <summary>Rejects mutation through another managed wrapper of an immutable native framebuffer identity.</summary>
    internal void RequireMutableFramebufferConfiguration(int id)
    {
        if (TryGetImmutableFramebuffer(id, out var immutable))
            throw new InvalidOperationException($"Immutable framebuffer {id} ({immutable.GetType().Name}, live={immutable.IsValid}, disposed={immutable.IsDisposed}, managerRetired={immutable.ResourceManager?.IsMetadataRegistryRetired}) configuration cannot change through an alias.");
    }
    /// <summary>Registers a completed immutable configuration so managed raw routing writes cannot alter it.</summary>
    internal void RegisterImmutableFramebuffer(GpuFramebuffer framebuffer)
    {
        _ = TryGetImmutableFramebuffer(framebuffer.FboId, out var existing);
        if (!immutableFramebuffers.TryAdd((framebuffer.OriginalContext, framebuffer.FboId), framebuffer))
            throw new InvalidOperationException($"Immutable framebuffer identity {framebuffer.FboId} is already registered to {existing?.GetType().Name}.");
    }
    /// <summary>Withdraws only the matching immutable declaration during disposal or handle transfer.</summary>
    internal void UnregisterImmutableFramebuffer(int id, GpuFramebuffer framebuffer)
    {
        // Disposal may be queued from another thread; remove only this declaration atomically.
        ((ICollection<KeyValuePair<((nint Handle, long Generation) Context, int Id), GpuFramebuffer>>)immutableFramebuffers)
            .Remove(new((framebuffer.OriginalContext, id), framebuffer));
    }
    #endregion
    #region Private
    /// <summary>Returns only a live original declaration, removing retired associations without native queries.</summary>
    private bool TryGetImmutableFramebuffer(int id, out GpuFramebuffer framebuffer)
    {
        if (!immutableFramebuffers.TryGetValue((GpuResource.CaptureCreationContext(GpuResourceKind.Framebuffer), id), out framebuffer!)) return false;
        if (framebuffer.IsValid) return true;
        // Manager retirement can precede queued wrapper disposal. Such a declaration cannot
        // authorize suppression or block a new allocation that reuses the numeric name.
        UnregisterImmutableFramebuffer(id, framebuffer);
        framebuffer = null!;
        return false;
    }
    #endregion
}
