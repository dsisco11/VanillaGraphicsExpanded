using System;

using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>
/// RAII wrapper around an EXT memory object (GL_EXT_memory_object).
/// Deletion is deferred to <see cref="GpuResourceManager"/> when available.
/// </summary>
internal sealed class GpuMemoryObject : GpuResource<GpuMemoryObjectMetadata>, IDisposable
{
    private int memoryObjectId;

    /// <inheritdoc />
    public override nint ResourceId
    {
        get => memoryObjectId;
        protected set => memoryObjectId = (int)value;
    }

    protected override GpuResourceKind ResourceKind => GpuResourceKind.MemoryObject;

    /// <summary>
    /// Returns <c>true</c> when the current OpenGL context reports EXT memory object support.
    /// </summary>
    public static bool IsSupported => GpuSupport.Graphics.SupportsExtMemoryObject;

    /// <summary>
    /// Gets the underlying OpenGL memory object id.
    /// </summary>
    public int MemoryObjectId => memoryObjectId;

    /// <summary>
    /// Returns <c>true</c> when the memory object has a non-zero id and has not been disposed.
    /// </summary>
    public new bool IsValid => base.IsValid;

    /// <summary>Declares an unpublished wrapper for manager-owned creation.</summary>
    private GpuMemoryObject() { }

    /// <summary>
    /// Creates a new memory object via <c>glCreateMemoryObjectsEXT</c>.
    /// </summary>
    public static GpuMemoryObject Create()
    {
        var value = new GpuMemoryObject();
        try
        {
            if (!GpuResourceManager.CreateMemoryObject(new GpuMemoryObjectInitialization(), value, out _).HasValue)
                throw new InvalidOperationException("MemoryObject creation failed.");
            return value;
        }
        catch
        {
            // The manager retires provisional allocation; dispose a completed handoff too.
            value.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Sets a debug label for this resource (no-op; KHR_debug does not standardize labels for memory objects).
    /// </summary>
    public override void SetDebugName(string? debugName)
    {
    }

    /// <summary>
    /// Sets an integer parameter via <c>glMemoryObjectParameterivEXT</c>.
    /// </summary>
    public void SetParameter(MemoryObjectParameterName pname, int value)
    {
        if (!IsValid)
        {
            return;
        }

        GL.Ext.MemoryObjectParameter(memoryObjectId, pname, ref value);
    }

    /// <summary>
    /// Imports memory from a file descriptor via <c>glImportMemoryFdEXT</c>.
    /// </summary>
    public void ImportFromFd(ulong sizeBytes, ExternalHandleType handleType, int fd)
    {
        if (!IsValid)
        {
            return;
        }

        GL.Ext.ImportMemoryF(memoryObjectId, unchecked((long)sizeBytes), handleType, fd);
    }
}
