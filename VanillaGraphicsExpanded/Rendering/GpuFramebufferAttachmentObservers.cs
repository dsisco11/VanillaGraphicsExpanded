using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Shares resize notifications between image wrappers without retaining resources or attachments.</summary>
internal static class GpuFramebufferAttachmentObservers
{
    private static readonly ConditionalWeakTable<GpuResource, List<WeakReference<GpuFramebufferAttachment>>> observers = new();

    #region Public API
    /// <summary>Registers an image on the render thread for changes made through any wrapper of its storage.</summary>
    public static void Register(GpuResource resource, GpuFramebufferAttachment attachment)
    {
        var list = observers.GetOrCreateValue(resource);
        list.RemoveAll(reference => !reference.TryGetTarget(out _));
        list.Add(new(attachment));
    }

    /// <summary>Publishes a storage change to every live image wrapper on the render thread.</summary>
    public static void NotifyChanged(GpuResource resource)
    {
        if (!observers.TryGetValue(resource, out var list)) return;
        // Snapshot allows callbacks to create or replace image wrappers safely.
        foreach (var reference in list.ToArray())
            if (reference.TryGetTarget(out var attachment)) attachment.NotifyChanged();
    }
    #endregion
}
