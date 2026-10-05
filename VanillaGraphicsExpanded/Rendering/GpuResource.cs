using System;
using System.Threading;

using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Defines native resource identity and the shared disposal and handle-transfer lifecycle.</summary>
public abstract class GpuResource : IDisposable
{
    private int disposed;

    public bool IsDisposed => Volatile.Read(ref disposed) != 0;

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

    public virtual nint Detach()
    {
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

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        nint id = ResourceId;
        ResourceId = 0;

        OnBeforeDelete(id);

        if (id != 0 && OwnsResource)
        {
            DeleteOrEnqueue(ResourceKind, id);
        }

        OnAfterDelete();
    }

    protected virtual void OnDetached(nint id)
    {
    }

    protected virtual void OnBeforeDelete(nint id)
    {
    }

    protected virtual void OnAfterDelete()
    {
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
                    GL.DeleteRenderbuffer((int)id);
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
