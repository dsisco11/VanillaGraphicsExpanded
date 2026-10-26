using VanillaGraphicsExpanded.Rendering.RHI;
using System;
using System.Collections.Concurrent;
using System.Threading;

using OpenTK.Graphics.OpenGL;

using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Drives resource uploads and deferred disposal on the engine render thread.</summary>
internal sealed partial class GpuResourceManager : IRenderer, IDisposable
{
    internal const EnumRenderStage Stage = EnumRenderStage.AfterFinalComposition;
    private const double RenderOrderValue = 0.9999;

    private readonly ConcurrentQueue<GpuBufferObject> bufferUploadQueue = new();
    private readonly ConcurrentQueue<GpuDeletionCommand> deletionQueue = new();
    private int renderThreadId;
    private int isDisposed;
    private (nint Handle, long Generation) context = Integration.RenderContextRegistry.Current();
    /// <summary>Reports whether terminal native cleanup can run on the originating render context.</summary>
    internal bool CanDrainNative => context != default
        ? context == Integration.RenderContextRegistry.Current()
        : IsRenderThread;
    /// <summary>Owns deferred managed-resource cleanup for this manager's context lifetime.</summary>
    internal GpuResourceDisposalQueue? ResourceDisposals { get; set; }

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

    /// <summary>Processes uploads and drains managed resources before queued GL handle deletions.</summary>
    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        if (stage != Stage || IsDisposed)
        {
            return;
        }

        EnsureRenderThreadId();
        if (context == default) context = Integration.RenderContextRegistry.Current();
        if (context != default && context != Integration.RenderContextRegistry.Current()) return;

        // Drive texture streaming uploads from the GPU manager tick.
        TextureStreamingSystem.TickOnRenderThread();

        // Drain pending CPU-staged buffer uploads before retiring queued resources.
        DrainBufferUploads();

        ResourceDisposals?.DrainPending();
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
        => EnqueueDeletion(new GpuDeletionCommand(GpuResourceKind.Buffer, bufferId));

    public void EnqueueDeleteVertexArray(int vertexArrayId)
        => EnqueueDeletion(new GpuDeletionCommand(GpuResourceKind.VertexArray, vertexArrayId));

    public void EnqueueDeleteTexture(int textureId)
        => EnqueueDeletion(new GpuDeletionCommand(GpuResourceKind.Texture, textureId));

    public void EnqueueDeleteFramebuffer(int framebufferId)
        => EnqueueDeletion(new GpuDeletionCommand(GpuResourceKind.Framebuffer, framebufferId));

    public void EnqueueDeleteRenderbuffer(int renderbufferId)
        => EnqueueDeletion(new GpuDeletionCommand(GpuResourceKind.Renderbuffer, renderbufferId));

    public void EnqueueDeleteQuery(int queryId)
        => EnqueueDeletion(new GpuDeletionCommand(GpuResourceKind.Query, queryId));

    public void EnqueueDeleteProgram(int programId)
        => EnqueueDeletion(new GpuDeletionCommand(GpuResourceKind.Program, programId));

    public void EnqueueDeleteTransformFeedback(int transformFeedbackId)
        => EnqueueDeletion(new GpuDeletionCommand(GpuResourceKind.TransformFeedback, transformFeedbackId));

    public void EnqueueDeleteSampler(int samplerId)
        => EnqueueDeletion(new GpuDeletionCommand(GpuResourceKind.Sampler, samplerId));

    public void EnqueueDeleteProgramPipeline(int programPipelineId)
        => EnqueueDeletion(new GpuDeletionCommand(GpuResourceKind.ProgramPipeline, programPipelineId));

    public void EnqueueDeleteShader(int shaderId)
        => EnqueueDeletion(new GpuDeletionCommand(GpuResourceKind.Shader, shaderId));

    public void EnqueueDeleteSync(IntPtr sync)
        => EnqueueDeletion(new GpuDeletionCommand(GpuResourceKind.Sync, (nint)sync));

    public void EnqueueDeleteSemaphore(int semaphoreId)
        => EnqueueDeletion(new GpuDeletionCommand(GpuResourceKind.Semaphore, semaphoreId));

    public void EnqueueDeleteMemoryObject(int memoryObjectId)
        => EnqueueDeletion(new GpuDeletionCommand(GpuResourceKind.MemoryObject, memoryObjectId));

    /// <summary>Closes deferred resource admission and drains cleanup when the render context is available.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref isDisposed, 1) != 0)
        {
            return;
        }

        // Drain only on the live originating context. Otherwise abandon queued names rather than
        // carrying them into a replacement native namespace after terminal manager shutdown.
        bool canDrain = CanDrainNative;
        ResourceDisposals?.Close(canDrain);
        if (canDrain)
        {
            DrainDeletionQueue();
        }
        RetireMetadataRegistry();
        while (deletionQueue.TryDequeue(out _)) { }
        while (bufferUploadQueue.TryDequeue(out _)) { }
    }

    private void EnsureRenderThreadId()
    {
        if (Volatile.Read(ref renderThreadId) == 0)
        {
            Interlocked.CompareExchange(ref renderThreadId, Environment.CurrentManagedThreadId, 0);
        }
    }

    /// <summary>Queues a resource name with the namespace captured by its owner.</summary>
    internal void EnqueueResourceDeletion(GpuResourceKind kind, nint id, (nint Handle, long Generation) origin)
        => EnqueueDeletion(new GpuDeletionCommand(kind, id, origin));

    /// <summary>Defers an owning engine adapter cleanup without executing it in another native namespace.</summary>
    internal void EnqueueContextCleanup(Action cleanup, (nint Handle, long Generation) origin)
    {
        ArgumentNullException.ThrowIfNull(cleanup);
        EnqueueDeletion(new GpuDeletionCommand(default, 0, origin, cleanup));
    }

    private void EnqueueDeletion(GpuDeletionCommand command)
    {
        if (IsDisposed || command.Id == 0 && command.Cleanup is null)
        {
            return;
        }

        if (command.Cleanup is null && command.Registration is null
            && metadataRegistry.TryGetValue(new(command.Kind, command.Id), out var original))
        {
            if (original.State == GpuResourceRegistrationState.Live) RetireResource(original);
            command = command with { Registration = original };
        }
        deletionQueue.Enqueue(command.Context == default ? command with { Context = context } : command);
    }

    private void DrainDeletionQueue()
    {
        var deferred = new System.Collections.Generic.List<GpuDeletionCommand>();
        while (deletionQueue.TryDequeue(out GpuDeletionCommand command))
        {
            if (command.Context != default && command.Context != Integration.RenderContextRegistry.Current())
            {
                if (Integration.RenderContextRegistry.IsAlive(command.Context)) deferred.Add(command);
                continue;
            }
            try
            {
                if (command.Registration is { } original &&
                    (!metadataRegistry.TryGetValue(original.Handle, out var current) || !ReferenceEquals(current, original))) continue;
                if (command.Cleanup is { } cleanup) { cleanup(); continue; }
                switch (command.Kind)
                {
                    case GpuResourceKind.Buffer:
                        RHICommandContext.Current.OpenGL.DeleteBuffer((int)command.Id);
                        break;
                    case GpuResourceKind.VertexArray:
                        StateCache.Current.DeleteVertexArray((int)command.Id);
                        break;
                    case GpuResourceKind.Texture:
                        RHICommandContext.Current.OpenGL.DeleteTexture((int)command.Id);
                        break;
                    case GpuResourceKind.Framebuffer:
                        StateCache.Current.DeleteFramebuffer((int)command.Id);
                        break;
                    case GpuResourceKind.Renderbuffer:
                        RHICommandContext.Current.OpenGL.DeleteRenderbuffer((int)command.Id);
                        break;
                    case GpuResourceKind.Query:
                        StateCache.Current.DeleteQuery((int)command.Id);
                        break;
                    case GpuResourceKind.Program:
                        StateCache.Current.DeleteProgram((int)command.Id);
                        break;
                    case GpuResourceKind.TransformFeedback:
                        StateCache.Current.DeleteTransformFeedback((int)command.Id);
                        break;
                    case GpuResourceKind.Sampler:
                        RHICommandContext.Current.OpenGL.DeleteSampler((int)command.Id);
                        break;
                    case GpuResourceKind.ProgramPipeline:
                        StateCache.Current.DeleteProgramPipeline((int)command.Id);
                        break;
                    case GpuResourceKind.Shader:
                        StateCache.Current.DeleteShader((int)command.Id);
                        break;
                    case GpuResourceKind.Sync:
                        StateCache.Current.DeleteSync((IntPtr)command.Id);
                        break;
                    case GpuResourceKind.Semaphore:
                        StateCache.Current.DeleteSemaphore((int)command.Id);
                        break;
                    case GpuResourceKind.MemoryObject:
                        StateCache.Current.DeleteMemoryObject((int)command.Id);
                        break;
                }
            }
            catch
            {
                // Best-effort. Context might be gone during shutdown.
            }
        }
        foreach (var command in deferred) deletionQueue.Enqueue(command);
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

    private readonly record struct GpuDeletionCommand(GpuResourceKind Kind, nint Id, (nint Handle, long Generation) Context = default, Action? Cleanup = null, GpuResourceRegistration? Registration = null);

}
