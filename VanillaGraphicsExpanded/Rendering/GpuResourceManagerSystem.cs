using System;
using System.Threading;

namespace VanillaGraphicsExpanded.Rendering;

internal static class GpuResourceManagerSystem
{
    private static GpuResourceManager? manager;

    public static bool IsInitialized => Volatile.Read(ref manager) is not null;

    public static bool IsRenderThread
    {
        get
        {
            var m = Volatile.Read(ref manager);
            return m is not null && m.IsRenderThread;
        }
    }

    public static void Initialize(GpuResourceManager instance)
    {
        ArgumentNullException.ThrowIfNull(instance);
        Interlocked.Exchange(ref manager, instance);
    }

    public static void Shutdown()
    {
        Interlocked.Exchange(ref manager, null);
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

    internal static void EnqueueBufferUpload(GpuBufferObject buffer)
    {
        var m = Volatile.Read(ref manager);
        m?.EnqueueBufferUpload(buffer);
    }
}
