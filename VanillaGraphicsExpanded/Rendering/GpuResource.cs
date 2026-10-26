using VanillaGraphicsExpanded.Rendering.RHI;
using System;
using System.Threading;

using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Defines native resource identity and the shared disposal and handle-transfer lifecycle.</summary>
public abstract partial class GpuResource : IDisposable
{
    private int disposed;
    private int activePassReferences;
    private int lifetimeReferences;
    private readonly GpuResourceManager? deletionManager = GpuResourceManagerSystem.CurrentManager;
    private (nint Handle, long Generation) nativeContext = Integration.RenderContextRegistry.Current();
    protected virtual (nint Handle, long Generation) NativeProgramContext => default;

    /// <summary>Exposes the wrapper's native resource kind.</summary>
    internal GpuResourceKind Kind => ResourceKind;

    /// <summary>Gets the originating lifecycle service without storing any registry entry in the wrapper.</summary>
    internal GpuResourceManager? ResourceManager => deletionManager ?? GpuResourceManagerSystem.CurrentManager;

    /// <summary>Captures declaration-local guard counts when native association first becomes available.</summary>
    internal (int Pass, int Lifetime) UsageReferences => (Volatile.Read(ref activePassReferences), Volatile.Read(ref lifetimeReferences));

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

    /// <summary>Transfers the native handle only after active pass and deferred restoration borrowers release it.</summary>
    public virtual nint Detach()
    {
        if (!OwnsResource) throw new NotSupportedException("Borrowed resources cannot transfer ownership.");
        if (IsDisposed)
        {
            return 0;
        }
        RequireRetirement();
        if (ResourceId != 0 && !IsValid) throw new InvalidOperationException("The original resource allocation has retired.");
        var origin = NativeProgramContext != default ? NativeProgramContext : nativeContext;
        if (ResourceId != 0 && origin != default && (origin.Generation == 0 ? !IsCurrentResourceContext(origin) : !Integration.RenderContextRegistry.IsAlive(origin)))
            throw new InvalidOperationException("The original resource context has retired.");

        nint id = ResourceId;
        ResourceId = 0;
        Interlocked.Exchange(ref disposed, 1);
        ResourceManager?.UnregisterWrapper(this);
        OnDetached(id);
        return id;
    }

    public nint ReleaseHandle()
    {
        return Detach();
    }

    /// <summary>Retires storage exactly once after active pass and deferred restoration borrows have ended.</summary>
    public void Dispose()
    {
        // Detached or disposed wrappers no longer mutate the shared allocation. Other aliases
        // may subsequently borrow it, but those guards cannot make local cleanup non-idempotent.
        if (IsDisposed) return;
        // Local tools have no queue-owning context registration; retain ownership until their context is current.
        if (nativeContext != default && nativeContext.Generation == 0) RequireOriginalContext();
        RequireRetirement();
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        nint id = ResourceId;
        ResourceId = 0;

        OnBeforeDelete(id);

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
    /// <summary>Installs or clears identity during manager-owned creation without invoking wrapper policy.</summary>
    internal void SetCreationIdentity(nint id, (nint Handle, long Generation)? context = null)
    {
        ObjectDisposedException.ThrowIf(id != 0 && IsDisposed, this);
        if (id != 0 && ResourceId != 0)
            throw new InvalidOperationException("Creation requires an unpublished resource declaration.");
        ResourceId = id;
        if (id != 0) nativeContext = context ?? Integration.RenderContextRegistry.Current();
    }

    /// <summary>Rejects storage mutation or retirement while a pass or immutable framebuffer borrows its configuration.</summary>
    internal void RequireMutableStorage(bool allowRetired = false)
    {
        ResourceManager?.RequireResourceMutable(this, allowRetired);
        if (Volatile.Read(ref activePassReferences) != 0)
            throw new InvalidOperationException("End the render pass before changing or retiring its target storage.");
    }

    /// <summary>Retains a live resource for pass validation or immutable attachment configuration without transferring ownership.</summary>
    internal void RetainPassReference()
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        ResourceManager?.RetainResource(this, true);
        Interlocked.Increment(ref activePassReferences);
    }

    /// <summary>Releases a matching immediate pass reference without retiring the resource.</summary>
    internal void ReleasePassReference()
    {
        ResourceManager?.ReleaseResource(this, true);
        Interlocked.Decrement(ref activePassReferences);
    }

    /// <summary>Prevents handle retirement while allowing storage or attachment mutation outside an active pass.</summary>
    internal void RetainLifetimeReference()
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        ResourceManager?.RetainResource(this, false);
        Interlocked.Increment(ref lifetimeReferences);
    }

    /// <summary>Ends a native-handle borrow without disposing or mutating the resource.</summary>
    internal void ReleaseLifetimeReference()
    {
        ResourceManager?.ReleaseResource(this, false);
        Interlocked.Decrement(ref lifetimeReferences);
    }
    #endregion

    #region Protected API
    /// <summary>Preserves native names needed by deferred restoration independently of active image mutation guards.</summary>
    protected void RequireRetirement()
    {
        if (OwnsResource) ResourceManager?.RequireResourceRetirement(this);
        if (Volatile.Read(ref activePassReferences) != 0)
            throw new InvalidOperationException("End the render pass before retiring its target storage.");
        if (Volatile.Read(ref lifetimeReferences) != 0)
            throw new InvalidOperationException("End the graphics boundary before retiring its borrowed resource.");
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
        (nint Handle, long Generation) origin, GpuResourceManager? manager)
    {
        // Context-free CPU declarations retain the existing service policy. Native owners
        // abandon dead namespaces or defer to their captured manager while another context is current.
        if (origin == default) { DeleteOrEnqueue(kind, id); return; }
        if (origin.Generation == 0)
        {
            // An unregistered tool has no foreign-context cleanup service. Never delete a name in another namespace.
            if (IsCurrentResourceContext(origin))
            {
                if (kind == GpuResourceKind.Buffer) RHICommandContext.Current.OpenGL.DeleteBuffer((int)id);
                else if (kind == GpuResourceKind.Texture) RHICommandContext.Current.OpenGL.DeleteTexture((int)id);
                else if (kind == GpuResourceKind.Renderbuffer) RHICommandContext.Current.OpenGL.DeleteRenderbuffer((int)id);
                else if (kind == GpuResourceKind.Sampler) RHICommandContext.Current.OpenGL.DeleteSampler((int)id);
                else if (kind == GpuResourceKind.Query) RHICommandContext.Current.OpenGL.DeleteQuery((int)id);
                else if (kind == GpuResourceKind.Sync) RHICommandContext.Current.OpenGL.DeleteSync(id);
                else if (kind == GpuResourceKind.TransformFeedback) RHICommandContext.Current.OpenGL.DeleteTransformFeedback((int)id);
                else if (kind == GpuResourceKind.Semaphore) RHICommandContext.Current.OpenGL.DeleteSemaphore((int)id);
                else if (kind == GpuResourceKind.MemoryObject) RHICommandContext.Current.OpenGL.DeleteMemoryObject((int)id);
                else DeleteOrEnqueue(kind, id);
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
        if (GpuResourceManagerSystem.IsInitialized
            && GpuResourceManagerSystem.IsRenderThreadKnown
            && !GpuResourceManagerSystem.IsRenderThread)
        {
            switch (kind)
            {
                case GpuResourceKind.Buffer:
                    GpuResourceManagerSystem.EnqueueDeleteBuffer((int)id);
                    return;
                case GpuResourceKind.VertexArray:
                    GpuResourceManagerSystem.EnqueueDeleteVertexArray((int)id);
                    return;
                case GpuResourceKind.Texture:
                    GpuResourceManagerSystem.EnqueueDeleteTexture((int)id);
                    return;
                case GpuResourceKind.Framebuffer:
                    GpuResourceManagerSystem.EnqueueDeleteFramebuffer((int)id);
                    return;
                case GpuResourceKind.Renderbuffer:
                    GpuResourceManagerSystem.EnqueueDeleteRenderbuffer((int)id);
                    return;
                case GpuResourceKind.Query:
                    GpuResourceManagerSystem.EnqueueDeleteQuery((int)id);
                    return;
                case GpuResourceKind.Program:
                    GpuResourceManagerSystem.EnqueueDeleteProgram((int)id);
                    return;
                case GpuResourceKind.TransformFeedback:
                    GpuResourceManagerSystem.EnqueueDeleteTransformFeedback((int)id);
                    return;
                case GpuResourceKind.Sampler:
                    GpuResourceManagerSystem.EnqueueDeleteSampler((int)id);
                    return;
                case GpuResourceKind.ProgramPipeline:
                    GpuResourceManagerSystem.EnqueueDeleteProgramPipeline((int)id);
                    return;
                case GpuResourceKind.Shader:
                    GpuResourceManagerSystem.EnqueueDeleteShader((int)id);
                    return;
                case GpuResourceKind.Sync:
                    GpuResourceManagerSystem.EnqueueDeleteSync((IntPtr)id);
                    return;
                case GpuResourceKind.Semaphore:
                    GpuResourceManagerSystem.EnqueueDeleteSemaphore((int)id);
                    return;
                case GpuResourceKind.MemoryObject:
                    GpuResourceManagerSystem.EnqueueDeleteMemoryObject((int)id);
                    return;
            }
        }

        try
        {
            switch (kind)
            {
                case GpuResourceKind.Buffer:
                    RHICommandContext.Current.OpenGL.DeleteBuffer((int)id);
                    break;
                case GpuResourceKind.VertexArray:
                    StateCache.Current.DeleteVertexArray((int)id);
                    break;
                case GpuResourceKind.Texture:
                    RHICommandContext.Current.OpenGL.DeleteTexture((int)id);
                    break;
                case GpuResourceKind.Framebuffer:
                    StateCache.Current.DeleteFramebuffer((int)id);
                    break;
                case GpuResourceKind.Renderbuffer:
                    RHICommandContext.Current.OpenGL.DeleteRenderbuffer((int)id);
                    break;
                case GpuResourceKind.Query:
                    RHICommandContext.Current.OpenGL.DeleteQuery((int)id);
                    break;
                case GpuResourceKind.Program:
                    StateCache.Current.DeleteProgram((int)id);
                    break;
                case GpuResourceKind.TransformFeedback:
                    RHICommandContext.Current.OpenGL.DeleteTransformFeedback((int)id);
                    break;
                case GpuResourceKind.Sampler:
                    RHICommandContext.Current.OpenGL.DeleteSampler((int)id);
                    break;
                case GpuResourceKind.ProgramPipeline:
                    StateCache.Current.DeleteProgramPipeline((int)id);
                    break;
                case GpuResourceKind.Shader:
                    StateCache.Current.DeleteShader((int)id);
                    break;
                case GpuResourceKind.Sync:
                    RHICommandContext.Current.OpenGL.DeleteSync((IntPtr)id);
                    break;
                case GpuResourceKind.Semaphore:
                    RHICommandContext.Current.OpenGL.DeleteSemaphore((int)id);
                    break;
                case GpuResourceKind.MemoryObject:
                    RHICommandContext.Current.OpenGL.DeleteMemoryObject((int)id);
                    break;
            }
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
