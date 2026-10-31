using VanillaGraphicsExpanded.Rendering.RHI;
using System;
using System.Threading;


namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Defines native resource identity and the shared disposal and handle-transfer lifecycle.</summary>
public abstract partial class GpuResource : IDisposable
{
    private int disposed;
    private int disposalRequested;
    private int ownerThread = Environment.CurrentManagedThreadId;
    private RHIResourceManager? deletionManager = RHIResourceManagerSystem.CurrentManager;
    private (nint Handle, long Generation) nativeContext = Integration.RenderContextRegistry.Current();
    protected virtual (nint Handle, long Generation) NativeProgramContext => default;

    /// <summary>Exposes the wrapper's native resource kind.</summary>
    internal GpuResourceKind Kind => ResourceKind;

    /// <summary>Gets the originating lifecycle service without storing any registry entry in the wrapper.</summary>
    internal RHIResourceManager? ResourceManager => deletionManager ?? RHIResourceManagerSystem.CurrentManager;


    #region Public API
    /// <summary>Reports whether this wrapper has been disposed.</summary>
    public bool IsDisposed => Volatile.Read(ref disposed) != 0;

    /// <summary>Reports whether this wrapper retains a nonzero native identity and has not been disposed.</summary>
    public bool IsValid => ResourceId != 0 && !IsDisposed && (ResourceManager?.IsResourceLive(this) ?? true);

    /// <summary>
    /// Sets a debug label for this resource (best-effort; typically only active in debug builds).
    /// </summary>
    public abstract void SetDebugName(string? debugName);

    /// <summary>Gets the current native resource ID, or zero when the ID has been retired.</summary>
    /// <remarks>Reading the ID does not transfer ownership. Only resource implementations can replace it.</remarks>
    public abstract nint ResourceId { get; protected set; }

    protected abstract GpuResourceKind ResourceKind { get; }

    protected virtual bool OwnsResource => true;

    /// <summary>Transfers the native handle at the owner's legal release boundary.</summary>
    public virtual nint Detach()
    {
        ValidateOwnerThread();
        if (!OwnsResource) throw new NotSupportedException("Borrowed resources cannot transfer ownership.");
        if (IsDisposed)
        {
            return 0;
        }
        if (ResourceId != 0 && !IsValid) throw new InvalidOperationException("The original resource allocation has retired.");
        var origin = NativeProgramContext != default ? NativeProgramContext : nativeContext;
        if (ResourceId != 0 && origin != default && (origin.Generation == 0 ? !Integration.RenderContextRegistry.IsCurrentResourceContext(origin) : !Integration.RenderContextRegistry.IsAlive(origin)))
            throw new InvalidOperationException("The original resource context has retired.");

        nint id = ResourceId;
        ResourceId = 0;
        Interlocked.Exchange(ref disposed, 1);
        InvalidatePreparedConsumers();
        ResourceManager?.UnregisterWrapper(this);
        OnDetached(id);
        return id;
    }

    public nint ReleaseHandle()
    {
        return Detach();
    }

    /// <summary>Requests complete owner-thread retirement, or retires immediately on that thread after use has ended.</summary>
    /// <remarks>Worker requests preserve identity until the originating cleanup boundary. Owners must withdraw
    /// future work and finish or discard recordings before that boundary; queuing does not establish GPU completion.</remarks>
    public void Dispose()
    {
        // Retirement is idempotent; consumers never acquire exclusion over the owner.
        if (IsDisposed) return;
        // A worker requests retirement; it must not withdraw identity or invoke owner callbacks.
        // The originating manager drains the entire transition after rendering, not just its GL delete.
        if (Environment.CurrentManagedThreadId != ownerThread && deletionManager?.ResourceDisposals is { } queue)
        {
            if (Interlocked.Exchange(ref disposalRequested, 1) == 0) queue.Enqueue(this);
            return;
        }
        ValidateOwnerThread();
        // Local tools have no queue-owning context registration; retain ownership until their context is current.
        if (nativeContext != default && nativeContext.Generation == 0) RequireOriginalContext();
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        nint id = ResourceId;
        ResourceId = 0;

        InvalidatePreparedConsumers();
        OnBeforeDelete(id);
        if (id != 0 && OwnsResource && ResourceManager?.GetResourceRegistration(this) is null)
            RHIResourceManager.CompleteLocalRetirement(this);

        if (id != 0 && OwnsResource)
        {
            var origin = NativeProgramContext != default ? NativeProgramContext : nativeContext;
            if (ResourceManager is not { } manager || !manager.TryScheduleResourceRetirement(this, id, origin)) RetireResource(ResourceKind, id, origin, deletionManager);
        }

        ResourceManager?.UnregisterWrapper(this);
        OnAfterDelete();
    }

    #endregion

    #region Internal API
    /// <summary>Reacts to a manager-committed transition after affected snapshots are coherent.</summary>
    internal virtual void OnResourceChanged(in RHIResourceChange change) { }

    /// <summary>Installs or clears identity during manager-owned creation without invoking wrapper policy.</summary>
    internal void SetCreationIdentity(nint id, (nint Handle, long Generation)? context = null)
    {
        ObjectDisposedException.ThrowIf(id != 0 && IsDisposed, this);
        if (id != 0 && ResourceId != 0)
            throw new InvalidOperationException("Creation requires an unpublished resource declaration.");
        // Explicit owner replacement can suppress the initiating wrapper's metadata callback.
        // Withdraw derived selections at the identity handoff; consumers refresh only after completion.
        if (ResourceId != id) InvalidatePreparedConsumers();
        ResourceId = id;
        if (id != 0)
        {
            nativeContext = context ?? Integration.RenderContextRegistry.Current();
            ownerThread = Environment.CurrentManagedThreadId;
        }
    }

    /// <summary>Diagnoses owner mutation of a retired original allocation in development builds.</summary>
    [System.Diagnostics.Conditional("DEBUG")]
    internal void RequireMutableStorage(bool allowRetired = false)
    {
        ValidateOwnerThread();
        ObjectDisposedException.ThrowIf(!allowRetired && (IsDisposed || ResourceId != 0 && !IsValid), this);
    }
    #endregion

    protected virtual void OnDetached(nint id)
    {
    }

    protected virtual void OnBeforeDelete(nint id)
    {
    }

    protected virtual void OnAfterDelete()
    {
    }

    /// <summary>Retires a resource only in its originating namespace, independently of other resource owners.</summary>
    internal static void RetireResource(GpuResourceKind kind, nint id,
        (nint Handle, long Generation) origin, RHIResourceManager? manager)
    {
        // Context-free CPU declarations retain the existing service policy. Native owners
        // abandon dead namespaces or defer to their captured manager while another context is current.
        if (origin == default) { DeleteOrEnqueue(kind, id); return; }
        if (origin.Generation == 0)
        {
            // An unregistered tool has no foreign-context cleanup service. Never delete a name in another namespace.
            if (Integration.RenderContextRegistry.IsCurrentResourceContext(origin))
            {
                RHICommandContext.Current.RetireResource(new(kind, id));
            }
            return;
        }
        if (!Integration.RenderContextRegistry.IsAlive(origin)) return;
        if (origin != Integration.RenderContextRegistry.Current())
        {
            manager?.EnqueueResourceDeletion(kind, id, origin);
            return;
        }
        DeleteOrEnqueue(kind, id);
    }

    /// <summary>Retires an externally owned handle through the same deletion policy as owned resources.</summary>
    internal static void DeleteOrEnqueue(GpuResourceKind kind, nint id)
    {
        // If the manager is initialized and we're not on the render thread, enqueue deletion.
        // Otherwise, do immediate deletion (legacy behavior).
        if (RHIResourceManagerSystem.IsInitialized
            && RHIResourceManagerSystem.IsRenderThreadKnown
            && !RHIResourceManagerSystem.IsRenderThread)
        {
            switch (kind)
            {
                case GpuResourceKind.Buffer:
                    RHIResourceManagerSystem.EnqueueDeleteBuffer((int)id);
                    return;
                case GpuResourceKind.VertexArray:
                    RHIResourceManagerSystem.EnqueueDeleteVertexArray((int)id);
                    return;
                case GpuResourceKind.Texture:
                    RHIResourceManagerSystem.EnqueueDeleteTexture((int)id);
                    return;
                case GpuResourceKind.Framebuffer:
                    RHIResourceManagerSystem.EnqueueDeleteFramebuffer((int)id);
                    return;
                case GpuResourceKind.Renderbuffer:
                    RHIResourceManagerSystem.EnqueueDeleteRenderbuffer((int)id);
                    return;
                case GpuResourceKind.Query:
                    RHIResourceManagerSystem.EnqueueDeleteQuery((int)id);
                    return;
                case GpuResourceKind.Program:
                    RHIResourceManagerSystem.EnqueueDeleteProgram((int)id);
                    return;
                case GpuResourceKind.TransformFeedback:
                    RHIResourceManagerSystem.EnqueueDeleteTransformFeedback((int)id);
                    return;
                case GpuResourceKind.Sampler:
                    RHIResourceManagerSystem.EnqueueDeleteSampler((int)id);
                    return;
                case GpuResourceKind.ProgramPipeline:
                    RHIResourceManagerSystem.EnqueueDeleteProgramPipeline((int)id);
                    return;
                case GpuResourceKind.Shader:
                    RHIResourceManagerSystem.EnqueueDeleteShader((int)id);
                    return;
                case GpuResourceKind.Sync:
                    RHIResourceManagerSystem.EnqueueDeleteSync((IntPtr)id);
                    return;
                case GpuResourceKind.Semaphore:
                    RHIResourceManagerSystem.EnqueueDeleteSemaphore((int)id);
                    return;
                case GpuResourceKind.MemoryObject:
                    RHIResourceManagerSystem.EnqueueDeleteMemoryObject((int)id);
                    return;
            }
        }

        try
        {
            RHICommandContext.Current.RetireResource(new(kind, id));
        }
        catch
        {
            // Best-effort: context may be gone during shutdown.
        }
    }
}

public enum GpuResourceKind
{
    Buffer = 0,
    VertexArray = 1,
    Texture = 2,
    Framebuffer = 3,
    Renderbuffer = 4,
    Query = 5,
    Sync = 6,
    Program = 7,
    TransformFeedback = 8,
    Sampler = 9,
    ProgramPipeline = 10,
    Shader = 11,
    Semaphore = 12,
    MemoryObject = 13,
}
