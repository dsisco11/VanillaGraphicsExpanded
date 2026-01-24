using System;
using System.Threading;

using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

public abstract class GpuResource : IDisposable
{
    private int disposed;

    public bool IsDisposed => Volatile.Read(ref disposed) != 0;

    public bool IsValid => ResourceId != 0 && !IsDisposed;

    protected abstract int ResourceId { get; set; }

    protected abstract GpuResourceKind ResourceKind { get; }

    public virtual int Detach()
    {
        if (IsDisposed)
        {
            return 0;
        }

        int id = ResourceId;
        ResourceId = 0;
        Interlocked.Exchange(ref disposed, 1);
        OnDetached(id);
        return id;
    }

    public int ReleaseHandle()
    {
        return Detach();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        int id = ResourceId;
        ResourceId = 0;

        OnBeforeDelete(id);

        if (id != 0)
        {
            DeleteOrEnqueue(ResourceKind, id);
        }

        OnAfterDelete();
    }

    protected virtual void OnDetached(int id)
    {
    }

    protected virtual void OnBeforeDelete(int id)
    {
    }

    protected virtual void OnAfterDelete()
    {
    }

    private static void DeleteOrEnqueue(GpuResourceKind kind, int id)
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
                    GpuResourceManagerSystem.EnqueueDeleteBuffer(id);
                    return;
                case GpuResourceKind.VertexArray:
                    GpuResourceManagerSystem.EnqueueDeleteVertexArray(id);
                    return;
                case GpuResourceKind.Texture:
                    GpuResourceManagerSystem.EnqueueDeleteTexture(id);
                    return;
                case GpuResourceKind.Framebuffer:
                    GpuResourceManagerSystem.EnqueueDeleteFramebuffer(id);
                    return;
                case GpuResourceKind.Renderbuffer:
                    GpuResourceManagerSystem.EnqueueDeleteRenderbuffer(id);
                    return;
            }
        }

        try
        {
            switch (kind)
            {
                case GpuResourceKind.Buffer:
                    GL.DeleteBuffer(id);
                    break;
                case GpuResourceKind.VertexArray:
                    GL.DeleteVertexArray(id);
                    break;
                case GpuResourceKind.Texture:
                    GL.DeleteTexture(id);
                    break;
                case GpuResourceKind.Framebuffer:
                    GL.DeleteFramebuffer(id);
                    break;
                case GpuResourceKind.Renderbuffer:
                    GL.DeleteRenderbuffer(id);
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
}
