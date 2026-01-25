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

    private readonly ConcurrentQueue<GpuBufferObject> bufferUploadQueue = new();
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

        // Phase 2: drive texture streaming uploads from the GPU manager tick.
        TextureStreamingSystem.TickOnRenderThread();

        // Phase 3: drain pending CPU-staged buffer uploads (glNamedBufferData/SubData + fallback binds).
        DrainBufferUploads();

        DrainDeletionQueue();
    }

    internal void EnqueueBufferUpload(GpuBufferObject buffer)
    {
        if (IsDisposed || buffer is null)
        {
            return;
        }

        bufferUploadQueue.Enqueue(buffer);
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

    public void EnqueueDeleteQuery(int queryId)
        => EnqueueDeletion(new GpuDeletionCommand(GpuDeletionKind.Query, queryId));

    public void EnqueueDeleteProgram(int programId)
        => EnqueueDeletion(new GpuDeletionCommand(GpuDeletionKind.Program, programId));

    public void EnqueueDeleteTransformFeedback(int transformFeedbackId)
        => EnqueueDeletion(new GpuDeletionCommand(GpuDeletionKind.TransformFeedback, transformFeedbackId));

    public void EnqueueDeleteSampler(int samplerId)
        => EnqueueDeletion(new GpuDeletionCommand(GpuDeletionKind.Sampler, samplerId));

    public void EnqueueDeleteProgramPipeline(int programPipelineId)
        => EnqueueDeletion(new GpuDeletionCommand(GpuDeletionKind.ProgramPipeline, programPipelineId));

    public void EnqueueDeleteShader(int shaderId)
        => EnqueueDeletion(new GpuDeletionCommand(GpuDeletionKind.Shader, shaderId));

    public void EnqueueDeleteSync(IntPtr sync)
        => EnqueueDeletion(new GpuDeletionCommand(GpuDeletionKind.Sync, (nint)sync));

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
                        GL.DeleteBuffer((int)command.Id);
                        break;
                    case GpuDeletionKind.VertexArray:
                        GL.DeleteVertexArray((int)command.Id);
                        break;
                    case GpuDeletionKind.Texture:
                        GL.DeleteTexture((int)command.Id);
                        break;
                    case GpuDeletionKind.Framebuffer:
                        GL.DeleteFramebuffer((int)command.Id);
                        break;
                    case GpuDeletionKind.Renderbuffer:
                        GL.DeleteRenderbuffer((int)command.Id);
                        break;
                    case GpuDeletionKind.Query:
                        GL.DeleteQuery((int)command.Id);
                        break;
                    case GpuDeletionKind.Program:
                        GL.DeleteProgram((int)command.Id);
                        break;
                    case GpuDeletionKind.TransformFeedback:
                        GL.DeleteTransformFeedback((int)command.Id);
                        break;
                    case GpuDeletionKind.Sampler:
                        GL.DeleteSampler((int)command.Id);
                        break;
                    case GpuDeletionKind.ProgramPipeline:
                        GL.DeleteProgramPipeline((int)command.Id);
                        break;
                    case GpuDeletionKind.Shader:
                        GL.DeleteShader((int)command.Id);
                        break;
                    case GpuDeletionKind.Sync:
                        GL.DeleteSync((IntPtr)command.Id);
                        break;
                }
            }
            catch
            {
                // Best-effort. Context might be gone during shutdown.
            }
        }
    }

    private void DrainBufferUploads()
    {
        while (bufferUploadQueue.TryDequeue(out GpuBufferObject? buffer))
        {
            try
            {
                if (buffer is null)
                {
                    continue;
                }

                buffer.DrainPendingUploadsOnRenderThread();
            }
            catch
            {
                // Best-effort: ignore GL errors during shutdown.
            }
        }
    }

    private readonly record struct GpuDeletionCommand(GpuDeletionKind Kind, nint Id);

    private enum GpuDeletionKind
    {
        Buffer = 0,
        VertexArray = 1,
        Texture = 2,
        Framebuffer = 3,
        Renderbuffer = 4,
        Query = 5,
        Program = 6,
        TransformFeedback = 7,
        Sampler = 8,
        ProgramPipeline = 9,
        Shader = 10,
        Sync = 11,
    }
}
