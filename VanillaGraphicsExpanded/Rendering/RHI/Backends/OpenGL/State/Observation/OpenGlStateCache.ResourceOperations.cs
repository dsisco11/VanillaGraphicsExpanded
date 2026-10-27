using System;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Rejects unrelated reentry into synchronous resource operations without collecting metadata.</summary>
internal sealed partial class OpenGlStateCache : IRHIResourceOperationExecution
{
    private ResourceOperationGuard? resourceOperation;

    /// <summary>Uses registered identity or the physical namespace after local RHI admission; pure registry tools need no native admission.</summary>
    private (nint Handle, long Generation) ResourceOperationContext
    {
        get
        {
            var registered = Integration.RenderContextRegistry.Current();
            return registered != default || rhiNativeContext == 0 ? registered
                : Integration.RenderContextRegistry.CaptureResourceContext();
        }
    }

    #region Explicit RHI execution contract
    /// <inheritdoc />
    void IRHIResourceOperationExecution.RequireResourceOperationAvailable(GpuResourceKind kind, nint id, IRHIResourceOperation? participant)
        => RequireResourceOperationAvailable(kind, id, participant);
    /// <inheritdoc />
    IRHIResourceOperation IRHIResourceOperationExecution.EnterResourceOperation(GpuResourceHandle handle)
        => EnterResourceOperation(handle);
    /// <inheritdoc />
    void IRHIResourceOperationExecution.CancelResourceOperation(GpuResourceKind kind, nint id)
        => CancelResourceOperation(kind, id);
    #endregion

    #region Internal API
    /// <summary>Captures a temporary execution guard, independent of candidate construction and publication.</summary>
    internal sealed class ResourceOperationGuard(OpenGlStateCache owner, GpuResourceHandle handle, ResourceOperationGuard? previous) : IRHIResourceOperation
    {
        /// <inheritdoc />
        public GpuResourceHandle Handle { get; } = handle;
        internal readonly (nint, long) Context = owner.ResourceOperationContext;
        internal ResourceOperationGuard? Previous = previous;
        /// <inheritdoc />
        public bool Cancelled { get; internal set; }
        /// <inheritdoc />
        public bool Deleted { get; internal set; }
        private bool disposed;

        #region Public API
        /// <inheritdoc />
        public void Interrupt() => Cancelled = true;
        /// <summary>Releases only the original cache exclusion, even after context replacement.</summary>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            owner.ExitResourceOperation(this);
        }
        #endregion
    }

    /// <summary>Rejects an unrelated mutation before dispatch; participating steps receive their owner explicitly.</summary>
    internal void RequireResourceOperationAvailable(GpuResourceKind kind, nint id, IRHIResourceOperation? participant = null)
    {
        var context = ResourceOperationContext;
        for (var item = resourceOperation; item is not null; item = item.Previous)
            if (!ReferenceEquals(item, participant) && !item.Deleted && item.Context == context && item.Handle == new GpuResourceHandle(kind, id))
                throw new InvalidOperationException("A resource operation is already in progress.");
    }

    /// <summary>Registers only an active execution guard; it stores no metadata, readers or recipients.</summary>
    internal ResourceOperationGuard EnterResourceOperation(GpuResourceHandle handle)
    {
        RequireResourceOperationAvailable(handle.Kind, handle.Id);
        return resourceOperation = new(this, handle, resourceOperation);
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
        var context = ResourceOperationContext;
        for (var item = resourceOperation; item is not null; item = item.Previous)
            if (item.Context == context && item.Handle == new GpuResourceHandle(kind, id)) { item.Cancelled = true; item.Deleted = true; }
    }
    #endregion
}
