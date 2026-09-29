using System;
using System.Collections.Generic;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Owns a set of GPU resources without imposing allocation, resize, or publication policy.</summary>
/// <remarks>Register only owned resources. Registered framebuffers must borrow their attachments; engine-owned handles stay outside the collection.</remarks>
internal sealed class GpuResourceCollection : IDisposable
{
    private readonly HashSet<GpuFramebuffer> framebuffers = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<GpuResource> resources = new(ReferenceEqualityComparer.Instance);
    private bool disposed;

    #region Ownership
    /// <summary>Transfers an explicitly owned resource to this collection; repeated registration is harmless.</summary>
    public T Own<T>(T resource) where T : GpuResource
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(resource);
        // Classify once on acquisition so each resource belongs to exactly one disposal set.
        if (resource is GpuFramebuffer framebuffer) framebuffers.Add(framebuffer);
        else resources.Add(resource);
        return resource;
    }
    #endregion

    #region Lifetime
    /// <summary>Releases framebuffers before their attachments, attempting every release even if one fails.</summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        List<Exception>? failures = null;
        // Framebuffers borrow collection-owned attachments. Retire all FBOs before any storage,
        // independently of registration order or changes to current/history roles.
        foreach (var framebuffer in framebuffers)
        {
            try { framebuffer.Dispose(); }
            catch (Exception error) { (failures ??= []).Add(error); }
        }
        framebuffers.Clear();
        foreach (var resource in resources)
        {
            try { resource.Dispose(); }
            catch (Exception error) { (failures ??= []).Add(error); }
        }
        resources.Clear();
        if (failures is not null) throw new AggregateException(failures);
    }
    #endregion
}
