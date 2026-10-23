using System;
using System.Threading;

using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Defines native resource identity and the shared disposal and handle-transfer lifecycle.</summary>
public abstract class GpuResource : IDisposable
{
    private int disposed;
    private int activePassReferences;
    private int lifetimeReferences;
    private readonly GpuResourceManager? deletionManager = GpuResourceManagerSystem.CurrentManager;
    private readonly (nint Handle, long Generation) nativeContext = Integration.RenderContextRegistry.Current();
    protected virtual (nint Handle, long Generation) NativeProgramContext => default;

    /// <summary>Exposes the wrapper's native resource kind.</summary>
    internal GpuResourceKind Kind => ResourceKind;

    #region Public API
    /// <summary>Reports whether this wrapper has been disposed.</summary>
    public bool IsDisposed => Volatile.Read(ref disposed) != 0;

    /// <summary>Reports whether this wrapper retains a nonzero native identity and has not been disposed.</summary>
    public bool IsValid => ResourceId != 0 && !IsDisposed;

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
        RequireRetirement();
        if (IsDisposed)
        {
            return 0;
        }

        nint id = ResourceId;
        ResourceId = 0;
        Interlocked.Exchange(ref disposed, 1);
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
            RetireResource(ResourceKind, id, origin, deletionManager);
        }

        OnAfterDelete();
    }

    #endregion

    #region Internal API
    /// <summary>Rejects storage mutation or retirement while a pass or immutable framebuffer borrows its configuration.</summary>
    internal void RequireMutableStorage()
    {
        if (Volatile.Read(ref activePassReferences) != 0)
            throw new InvalidOperationException("End the render pass before changing or retiring its target storage.");
    }

    /// <summary>Retains a live resource for pass validation or immutable attachment configuration without transferring ownership.</summary>
    internal void RetainPassReference()
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        Interlocked.Increment(ref activePassReferences);
    }

    /// <summary>Releases a matching immediate pass reference without retiring the resource.</summary>
    internal void ReleasePassReference() => Interlocked.Decrement(ref activePassReferences);

    /// <summary>Prevents handle retirement while allowing storage or attachment mutation outside an active pass.</summary>
    internal void RetainLifetimeReference()
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        Interlocked.Increment(ref lifetimeReferences);
    }

    /// <summary>Ends a native-handle borrow without disposing or mutating the resource.</summary>
    internal void ReleaseLifetimeReference() => Interlocked.Decrement(ref lifetimeReferences);
    #endregion

    #region Private
    /// <summary>Preserves native names needed by deferred restoration independently of active image mutation guards.</summary>
    private void RequireRetirement()
    {
        RequireMutableStorage();
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
    private static void RetireResource(GpuResourceKind kind, nint id,
        (nint Handle, long Generation) origin, GpuResourceManager? manager)
    {
        // Context-free CPU declarations retain the existing service policy. Native owners
        // abandon dead namespaces or defer to their captured manager while another context is current.
        if (origin == default) { DeleteOrEnqueue(kind, id); return; }
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
                    StateCache.Current.DeleteBuffer((int)id);
                    break;
                case GpuResourceKind.VertexArray:
                    StateCache.Current.DeleteVertexArray((int)id);
                    break;
                case GpuResourceKind.Texture:
                    StateCache.Current.DeleteTexture((int)id);
                    break;
                case GpuResourceKind.Framebuffer:
                    StateCache.Current.DeleteFramebuffer((int)id);
                    break;
                case GpuResourceKind.Renderbuffer:
                    StateCache.Current.DeleteRenderbuffer((int)id);
                    break;
                case GpuResourceKind.Query:
                    GL.DeleteQuery((int)id);
                    break;
                case GpuResourceKind.Program:
                    StateCache.Current.DeleteProgram((int)id);
                    break;
                case GpuResourceKind.TransformFeedback:
                    GL.DeleteTransformFeedback((int)id);
                    break;
                case GpuResourceKind.Sampler:
                    StateCache.Current.DeleteSampler((int)id);
                    break;
                case GpuResourceKind.ProgramPipeline:
                    GL.DeleteProgramPipeline((int)id);
                    break;
                case GpuResourceKind.Shader:
                    GL.DeleteShader((int)id);
                    break;
                case GpuResourceKind.Sync:
                    GL.DeleteSync((IntPtr)id);
                    break;
                case GpuResourceKind.Semaphore:
                    GL.Ext.DeleteSemaphore((int)id);
                    break;
                case GpuResourceKind.MemoryObject:
                    GL.Ext.DeleteMemoryObject((int)id);
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
