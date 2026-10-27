using System;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Rejects unrelated reentry into synchronous resource operations without collecting metadata.</summary>
internal sealed partial class OpenGlStateCache
{
    private ResourceOperationGuard? resourceOperation;

    #region Internal API
    /// <summary>Captures a temporary execution guard, independent of candidate construction and publication.</summary>
    internal sealed class ResourceOperationGuard(GpuResourceHandle handle, ResourceOperationGuard? previous)
    {
        internal readonly GpuResourceHandle Handle = handle;
        internal readonly (nint, long) Context = Integration.RenderContextRegistry.Current();
        internal ResourceOperationGuard? Previous = previous;
        internal bool Cancelled;
        internal bool Deleted;
    }

    /// <summary>Rejects an unrelated mutation before dispatch; participating steps receive their owner explicitly.</summary>
    internal void RequireResourceOperationAvailable(GpuResourceKind kind, nint id, ResourceOperationGuard? participant = null)
    {
        for (var item = resourceOperation; item is not null; item = item.Previous)
            if (!ReferenceEquals(item, participant) && !item.Deleted && item.Context == Integration.RenderContextRegistry.Current() && item.Handle == new GpuResourceHandle(kind, id))
                throw new InvalidOperationException("A resource operation is already in progress.");
    }

    /// <summary>Registers only an active execution guard; it stores no metadata, readers or recipients.</summary>
    internal ResourceOperationGuard EnterResourceOperation(GpuResourceHandle handle)
    {
        RequireResourceOperationAvailable(handle.Kind, handle.Id);
        return resourceOperation = new(handle, resourceOperation);
    }

    /// <summary>Removes the exact operation guard, including during exceptional unwind.</summary>
    internal void ExitResourceOperation(ResourceOperationGuard guard)
    {
        if (ReferenceEquals(resourceOperation, guard)) resourceOperation = guard.Previous;
        else for (var item = resourceOperation; item is not null; item = item.Previous)
            if (ReferenceEquals(item.Previous, guard)) { item.Previous = guard.Previous; break; }
    }

    /// <summary>Cancels completion after deletion without allowing a reused name to revive the old operation.</summary>
    internal void CancelResourceOperation(GpuResourceKind kind, nint id)
    {
        for (var item = resourceOperation; item is not null; item = item.Previous)
            if (item.Context == Integration.RenderContextRegistry.Current() && item.Handle == new GpuResourceHandle(kind, id)) { item.Cancelled = true; item.Deleted = true; }
    }
    #endregion
}
