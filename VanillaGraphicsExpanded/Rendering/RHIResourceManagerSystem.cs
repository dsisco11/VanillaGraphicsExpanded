using System;
using System.Threading;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Publishes the active render-thread resource manager and its deferred cleanup lifetime.</summary>
internal static class RHIResourceManagerSystem
{
    private static RHIResourceManager? manager;
    internal static RHIResourceManager? CurrentManager => Volatile.Read(ref manager);
    /// <summary>Notifies renderer composition when a retired context receives a replacement service.</summary>
    internal static event Action<RHIResourceManager>? ManagerChanged;

    /// <summary>Publishes the single service before native observation, without backend or capability admission.</summary>
    internal static RHIResourceManager EnsureCurrentContext()
    {
        if (Integration.RenderContextRegistry.Current() == default)
            throw new InvalidOperationException("Resource observation requires a registered context.");
        var current = CurrentManager;
        if (current is { CanObserveNativeMetadata: true }) return current;
        if (current is not null && !current.IsMetadataRegistryRetired)
            throw new InvalidOperationException("The live resource manager belongs to another context.");
        if (current is not null) { Shutdown(); current.Dispose(); }
        current = new RHIResourceManager();
        Initialize(current);
        ManagerChanged?.Invoke(current);
        return current;
    }
    private static RHIResourceDisposalQueue resourceDisposals = new();

    /// <summary>Captures the disposal service for the resource's original context lifetime.</summary>
    internal static RHIResourceDisposalQueue CaptureDisposalQueue() => Volatile.Read(ref resourceDisposals);

    public static bool IsInitialized => Volatile.Read(ref manager) is not null;

    public static int RenderThreadId
    {
        get
        {
            var m = Volatile.Read(ref manager);
            return m?.RenderThreadId ?? 0;
        }
    }

    public static bool IsRenderThreadKnown => RenderThreadId != 0;

    public static bool IsRenderThread
    {
        get
        {
            var m = Volatile.Read(ref manager);
            return m is not null && m.IsRenderThread;
        }
    }

    /// <summary>Initializes one manager lifetime, retaining pre-initialization cleanup and rejecting live replacement.</summary>
    public static void Initialize(RHIResourceManager instance)
    {
        ArgumentNullException.ThrowIfNull(instance);
        var previous = Volatile.Read(ref manager);
        if (previous is not null && !ReferenceEquals(previous, instance))
            throw new InvalidOperationException("Shut down the previous GPU resource manager before initializing another.");
        if (instance.IsDisposed) throw new ObjectDisposedException(nameof(instance));
        if (resourceDisposals.IsClosed) resourceDisposals = new RHIResourceDisposalQueue();
        instance.ResourceDisposals = resourceDisposals;
        Interlocked.Exchange(ref manager, instance);
    }

    /// <summary>Closes admission for this lifetime so late finalizers cannot enter a later manager's queue.</summary>
    public static void Shutdown()
    {
        var previous = Interlocked.Exchange(ref manager, null);
        resourceDisposals.Close(previous?.CanDrainNative == true);
    }

    public static void EnqueueDeleteBuffer(int bufferId)
    {
        var m = Volatile.Read(ref manager);
        m?.EnqueueDeleteBuffer(bufferId);
    }

    public static void EnqueueDeleteVertexArray(int vertexArrayId)
    {
        var m = Volatile.Read(ref manager);
        m?.EnqueueDeleteVertexArray(vertexArrayId);
    }

    public static void EnqueueDeleteTexture(int textureId)
    {
        var m = Volatile.Read(ref manager);
        m?.EnqueueDeleteTexture(textureId);
    }

    public static void EnqueueDeleteFramebuffer(int framebufferId)
    {
        var m = Volatile.Read(ref manager);
        m?.EnqueueDeleteFramebuffer(framebufferId);
    }

    public static void EnqueueDeleteRenderbuffer(int renderbufferId)
    {
        var m = Volatile.Read(ref manager);
        m?.EnqueueDeleteRenderbuffer(renderbufferId);
    }

    public static void EnqueueDeleteQuery(int queryId)
    {
        var m = Volatile.Read(ref manager);
        m?.EnqueueDeleteQuery(queryId);
    }

    public static void EnqueueDeleteProgram(int programId)
    {
        var m = Volatile.Read(ref manager);
        m?.EnqueueDeleteProgram(programId);
    }

    public static void EnqueueDeleteTransformFeedback(int transformFeedbackId)
    {
        var m = Volatile.Read(ref manager);
        m?.EnqueueDeleteTransformFeedback(transformFeedbackId);
    }

    public static void EnqueueDeleteSampler(int samplerId)
    {
        var m = Volatile.Read(ref manager);
        m?.EnqueueDeleteSampler(samplerId);
    }

    public static void EnqueueDeleteProgramPipeline(int programPipelineId)
    {
        var m = Volatile.Read(ref manager);
        m?.EnqueueDeleteProgramPipeline(programPipelineId);
    }

    public static void EnqueueDeleteShader(int shaderId)
    {
        var m = Volatile.Read(ref manager);
        m?.EnqueueDeleteShader(shaderId);
    }

    public static void EnqueueDeleteSync(IntPtr sync)
    {
        var m = Volatile.Read(ref manager);
        m?.EnqueueDeleteSync(sync);
    }

    public static void EnqueueDeleteSemaphore(int semaphoreId)
    {
        var m = Volatile.Read(ref manager);
        m?.EnqueueDeleteSemaphore(semaphoreId);
    }

    public static void EnqueueDeleteMemoryObject(int memoryObjectId)
    {
        var m = Volatile.Read(ref manager);
        m?.EnqueueDeleteMemoryObject(memoryObjectId);
    }

    internal static void EnqueueBufferUpload(GpuBufferObject buffer)
    {
        var m = Volatile.Read(ref manager);
        m?.EnqueueBufferUpload(buffer);
    }
}
