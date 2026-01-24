using System;
using System.Collections.Concurrent;
using System.Threading;

using OpenTK.Graphics.OpenGL;

using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.Rendering;

internal sealed class GpuResourceManager : IRenderer, IDisposable
{
    internal const EnumRenderStage Stage = EnumRenderStage.AfterFinalComposition;
    private const double RenderOrderValue = 0.9999;

    private readonly ConcurrentQueue<GpuDeletionCommand> deletionQueue = new();
    private int renderThreadId;
    private int isDisposed;

    public double RenderOrder => RenderOrderValue;
    public int RenderRange => 0;

    public int RenderThreadId => Volatile.Read(ref renderThreadId);
    public bool IsDisposed => Volatile.Read(ref isDisposed) != 0;

    public bool IsRenderThread
    {
        get
        {
            int tid = Volatile.Read(ref renderThreadId);
            return tid != 0 && Environment.CurrentManagedThreadId == tid;
        }
    }

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        if (stage != Stage || IsDisposed)
        {
            return;
        }

        EnsureRenderThreadId();
        DrainDeletionQueue();
    }

    public void EnqueueDeleteBuffer(int bufferId)
        => EnqueueDeletion(new GpuDeletionCommand(GpuDeletionKind.Buffer, bufferId));

    public void EnqueueDeleteVertexArray(int vertexArrayId)
        => EnqueueDeletion(new GpuDeletionCommand(GpuDeletionKind.VertexArray, vertexArrayId));

    public void EnqueueDeleteTexture(int textureId)
        => EnqueueDeletion(new GpuDeletionCommand(GpuDeletionKind.Texture, textureId));

    public void EnqueueDeleteFramebuffer(int framebufferId)
        => EnqueueDeletion(new GpuDeletionCommand(GpuDeletionKind.Framebuffer, framebufferId));

    public void EnqueueDeleteRenderbuffer(int renderbufferId)
        => EnqueueDeletion(new GpuDeletionCommand(GpuDeletionKind.Renderbuffer, renderbufferId));

    public void Dispose()
    {
        if (Interlocked.Exchange(ref isDisposed, 1) != 0)
        {
            return;
        }

        // Best-effort: if we're disposing on the render thread, drain immediately.
        // If we aren't on the render thread (or the context is gone), leave the queue.
        if (IsRenderThread)
        {
            DrainDeletionQueue();
        }
    }

    private void EnsureRenderThreadId()
    {
        if (Volatile.Read(ref renderThreadId) == 0)
        {
            Interlocked.CompareExchange(ref renderThreadId, Environment.CurrentManagedThreadId, 0);
        }
    }

    private void EnqueueDeletion(GpuDeletionCommand command)
    {
        if (IsDisposed || command.Id == 0)
        {
            return;
        }

        deletionQueue.Enqueue(command);
    }

    private void DrainDeletionQueue()
    {
        while (deletionQueue.TryDequeue(out GpuDeletionCommand command))
        {
            try
            {
                switch (command.Kind)
                {
                    case GpuDeletionKind.Buffer:
                        GL.DeleteBuffer(command.Id);
                        break;
                    case GpuDeletionKind.VertexArray:
                        GL.DeleteVertexArray(command.Id);
                        break;
                    case GpuDeletionKind.Texture:
                        GL.DeleteTexture(command.Id);
                        break;
                    case GpuDeletionKind.Framebuffer:
                        GL.DeleteFramebuffer(command.Id);
                        break;
                    case GpuDeletionKind.Renderbuffer:
                        GL.DeleteRenderbuffer(command.Id);
                        break;
                }
            }
            catch
            {
                // Best-effort. Context might be gone during shutdown.
            }
        }
    }

    private readonly record struct GpuDeletionCommand(GpuDeletionKind Kind, int Id);

    private enum GpuDeletionKind
    {
        Buffer = 0,
        VertexArray = 1,
        Texture = 2,
        Framebuffer = 3,
        Renderbuffer = 4,
    }
}

