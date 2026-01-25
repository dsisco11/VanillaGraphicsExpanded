using System;

using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>
/// RAII wrapper around an EXT semaphore object (GL_EXT_semaphore).
/// Deletion is deferred to <see cref="GpuResourceManager"/> when available.
/// </summary>
internal sealed class GpuSemaphore : GpuResource, IDisposable
{
    private int semaphoreId;

    protected override nint ResourceId
    {
        get => semaphoreId;
        set => semaphoreId = (int)value;
    }

    protected override GpuResourceKind ResourceKind => GpuResourceKind.Semaphore;

    /// <summary>
    /// Returns <c>true</c> when the current OpenGL context reports EXT semaphore support.
    /// </summary>
    public static bool IsSupported => GlExtensions.Supports("GL_EXT_semaphore");

    /// <summary>
    /// Gets the underlying OpenGL semaphore id.
    /// </summary>
    public int SemaphoreId => semaphoreId;

    /// <summary>
    /// Returns <c>true</c> when the semaphore has a non-zero id and has not been disposed.
    /// </summary>
    public new bool IsValid => semaphoreId != 0 && !IsDisposed;

    private GpuSemaphore(int semaphoreId)
    {
        this.semaphoreId = semaphoreId;
    }

    /// <summary>
    /// Creates a new semaphore object via <c>glGenSemaphoresEXT</c>.
    /// </summary>
    public static GpuSemaphore Create()
    {
        if (!IsSupported)
        {
            throw new NotSupportedException("GL_EXT_semaphore is not supported by the current context.");
        }

        int id = GL.Ext.GenSemaphore();
        if (id == 0)
        {
            throw new InvalidOperationException("glGenSemaphoresEXT returned 0.");
        }

        return new GpuSemaphore(id);
    }

    /// <summary>
    /// Sets a debug label for this resource (no-op; KHR_debug does not standardize labels for semaphores).
    /// </summary>
    public override void SetDebugName(string? debugName)
    {
    }

    /// <summary>
    /// Waits on this semaphore via <c>glWaitSemaphoreEXT</c>.
    /// </summary>
    public void Wait(
        ReadOnlySpan<int> bufferIds,
        ReadOnlySpan<int> textureIds,
        ReadOnlySpan<TextureLayout> textureLayouts)
    {
        if (!IsValid)
        {
            return;
        }

        if (textureIds.Length != textureLayouts.Length)
        {
            throw new ArgumentException("textureIds and textureLayouts must have the same length.");
        }

        int[] buffers = bufferIds.IsEmpty ? Array.Empty<int>() : bufferIds.ToArray();
        int[] textures = textureIds.IsEmpty ? Array.Empty<int>() : textureIds.ToArray();
        TextureLayout[] layouts = textureLayouts.IsEmpty ? Array.Empty<TextureLayout>() : textureLayouts.ToArray();

        GL.Ext.WaitSemaphore(semaphoreId, buffers.Length, buffers, textures.Length, textures, layouts);
    }

    /// <summary>
    /// Signals this semaphore via <c>glSignalSemaphoreEXT</c>.
    /// </summary>
    public void Signal(
        ReadOnlySpan<int> bufferIds,
        ReadOnlySpan<int> textureIds,
        ReadOnlySpan<TextureLayout> textureLayouts)
    {
        if (!IsValid)
        {
            return;
        }

        if (textureIds.Length != textureLayouts.Length)
        {
            throw new ArgumentException("textureIds and textureLayouts must have the same length.");
        }

        int[] buffers = bufferIds.IsEmpty ? Array.Empty<int>() : bufferIds.ToArray();
        int[] textures = textureIds.IsEmpty ? Array.Empty<int>() : textureIds.ToArray();
        TextureLayout[] layouts = textureLayouts.IsEmpty ? Array.Empty<TextureLayout>() : textureLayouts.ToArray();

        GL.Ext.SignalSemaphore(semaphoreId, buffers.Length, buffers, textures.Length, textures, layouts);
    }

    /// <summary>
    /// Imports a semaphore from a file descriptor via <c>glImportSemaphoreFdEXT</c>.
    /// </summary>
    public void ImportFromFd(ExternalHandleType handleType, int fd)
    {
        if (!IsValid)
        {
            return;
        }

        GL.Ext.ImportSemaphoreF(semaphoreId, handleType, fd);
    }
}

