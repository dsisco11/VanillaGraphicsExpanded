using System;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Coordinates semantic execution on the originating rendering context.</summary>
public sealed partial class RHICommandContext
{
    [ThreadStatic] private static RHICommandContext? current;
    private readonly IRHIBackend backend;

    #region Public API
    /// <summary>Gets the current context's admitted execution service, including context-local tools.</summary>
    public static RHICommandContext Current
    {
        get
        {
            if (current is null || !((OpenGLBackend)current.backend).IsCurrentContext)
                current = new(new OpenGLBackend(StateCache.Current));
            current.backend.RequireContext();
            return current;
        }
    }
    /// <summary>Gets semantic cached limits without new native discovery.</summary>
    public RHICapabilities Capabilities => backend.Capabilities;
    /// <summary>Uploads stable client bytes immediately; no recording storage is allocated.</summary>
    public void UploadBuffer(GpuBufferRef destination, nint offset, ReadOnlySpan<byte> bytes) => backend.UploadBuffer(destination, offset, bytes);
    /// <summary>Copies explicitly selected byte ranges without changing surrounding selectors.</summary>
    public void CopyBuffer(GpuBufferRef source, nint sourceOffset, GpuBufferRef destination, nint destinationOffset, nint byteCount) =>
        backend.CopyBuffer(source, sourceOffset, destination, destinationOffset, byteCount);
    /// <summary>Clears an explicitly selected byte range.</summary>
    public void ClearBuffer(GpuBufferRef destination, nint offset, nint byteCount, byte value = 0) => backend.ClearBuffer(destination, offset, byteCount, value);
    #endregion

    #region Internal API
    /// <summary>Provides native services only to existing manager and integration boundaries.</summary>
    internal OpenGLBackend OpenGL => (OpenGLBackend)backend;
    #region Owner-authorized buffer operations
    /// <summary>Admits owner-authorized storage replacement through the same backend transaction.</summary>
    internal void AllocateBufferStorage(GpuBufferObject owner, in RHIBufferDescription description, ReadOnlySpan<byte> initialData = default) =>
        backend.AllocateBufferStorage(owner, description, initialData);
    /// <summary>Maps an owner-selected CPU range without exposing pointers in recorded commands.</summary>
    internal nint MapBuffer(GpuBufferRef buffer, nint offset, nint byteCount, BufferMapAccess access) => OpenGL.MapBuffer(buffer, offset, byteCount, access);
    /// <summary>Flushes an explicit mapped range under originating-context validation.</summary>
    internal void FlushMappedBuffer(GpuBufferRef buffer, nint offset, nint byteCount) => OpenGL.FlushMappedBuffer(buffer, offset, byteCount);
    /// <summary>Ends immediate mapping of an existing owner.</summary>
    internal bool UnmapBuffer(GpuBufferRef buffer) => OpenGL.UnmapBuffer(buffer);
    /// <summary>Allocates immutable owner storage using an explicit access request.</summary>
    internal void AllocateImmutableBuffer(GpuBufferObject owner, nint capacity, StorageAccess access)
    {
        var description = new RHIBufferDescription { Role = owner.Role, Mode = ResourceStorageMode.Immutable, Capacity = capacity, Access = access };
        backend.AllocateBufferStorage(owner, description, default);
    }
    #endregion
    /// <summary>Creates the thin composition root without another cache or registry.</summary>
    internal RHICommandContext(IRHIBackend backend) => this.backend = backend;
    #endregion
}
