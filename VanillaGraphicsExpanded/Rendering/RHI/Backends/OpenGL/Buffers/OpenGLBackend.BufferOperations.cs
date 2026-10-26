using System;
using GL = OpenTK.Graphics.OpenGL4.GL;
using Native = OpenTK.Graphics.OpenGL4;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Executes core named buffer operations with manager-owned publication and no fallback.</summary>
internal sealed partial class OpenGLBackend
{
    #region Public API
    /// <inheritdoc />
    public unsafe void AllocateBufferStorage(GpuBufferObject owner, in RHIBufferDescription description, ReadOnlySpan<byte> initialData)
    {
        // Freeze value intent before native execution or callbacks can observe the caller again.
        var request = description;
        RequireContext();
        request.Validate();
        if (request.Mode == RHIStorageMode.Realization) throw new ArgumentException("Storage replacement requires an allocation request.");
        if (initialData.Length > request.Capacity) throw new ArgumentException("Initial bytes exceed requested capacity.");
        GpuResourceManager.RequireWrapperMetadataAccess(owner);
        if (GpuResourceManager.RequireBufferReferenceMetadata(owner).ImmutableStorage)
            throw new InvalidOperationException("Immutable buffer storage cannot be replaced in place.");
        owner.RequireMutableStorage();
        int nativeId = owner.BufferId;
        // The operation captures original identity and withdraws availability before dispatch.
        // Allocate first, then upload partial initial data, without reading beyond client memory.
        using var operation = new GpuResourceManager.MetadataOperation<GpuBufferMetadata>(owner, GpuMetadataChange.Storage);
        fixed (byte* bytes = initialData)
        {
            bool complete = initialData.Length == request.Capacity;
            if (request.Mode == RHIStorageMode.Mutable)
                GL.NamedBufferData(nativeId, request.Capacity, complete ? (nint)bytes : 0, OpenGLBufferValues.Usage(request.Usage));
            else
                GL.NamedBufferStorage(nativeId, request.Capacity, complete ? (nint)bytes : 0, OpenGLBufferValues.Storage(request.Access));
            RequireContext();
            operation.RequireLive();
            if (!complete && !initialData.IsEmpty)
            {
                InitializeBufferRange(nativeId, 0, initialData.Length, (nint)bytes,
                    request.Mode == RHIStorageMode.Mutable || (request.Access & RHIStorageAccess.DynamicUpdates) != 0, operation);
            }
        }
        RequireContext();
        var usage = request.Mode == RHIStorageMode.Immutable
            ? OpenTK.Graphics.OpenGL.BufferUsageHint.DynamicDraw : OpenGLBufferValues.MetadataUsage(request.Usage);
        operation.Commit(new(request.Capacity, usage, request.Mode == RHIStorageMode.Immutable,
            request.Mode == RHIStorageMode.Immutable ? LowerBufferDescription(request).StorageFlags : 0));
    }

    /// <inheritdoc />
    public unsafe void UploadBuffer(GpuBufferRef destination, nint offset, ReadOnlySpan<byte> bytes)
    {
        var owner = RequireRange(destination, offset, bytes.Length);
        var storage = owner.PublishedMetadata;
        if (storage.ImmutableStorage && (storage.StorageFlags & OpenTK.Graphics.OpenGL.BufferStorageFlags.DynamicStorageBit) == 0)
            throw new InvalidOperationException("Immutable storage does not allow client subdata updates.");
        cache.RequireResourceOperationAvailable(GpuResourceKind.Buffer, owner.ResourceId);
        fixed (byte* data = bytes) GL.NamedBufferSubData((int)owner.ResourceId, offset, bytes.Length, (nint)data);
    }

    /// <inheritdoc />
    public void CopyBuffer(GpuBufferRef source, nint sourceOffset, GpuBufferRef destination, nint destinationOffset, nint byteCount)
    {
        var from = RequireRange(source, sourceOffset, byteCount);
        var to = RequireRange(destination, destinationOffset, byteCount);
        if (from.ResourceId == to.ResourceId && byteCount != 0
            && sourceOffset < checked(destinationOffset + byteCount) && destinationOffset < checked(sourceOffset + byteCount))
            throw new ArgumentException("Copy ranges overlap in the same allocation.");
        cache.RequireResourceOperationAvailable(GpuResourceKind.Buffer, from.ResourceId);
        cache.RequireResourceOperationAvailable(GpuResourceKind.Buffer, to.ResourceId);
        GL.CopyNamedBufferSubData((int)from.ResourceId, (int)to.ResourceId, sourceOffset, destinationOffset, byteCount);
    }

    /// <inheritdoc />
    public unsafe void ClearBuffer(GpuBufferRef destination, nint offset, nint byteCount, byte value)
    {
        var owner = RequireRange(destination, offset, byteCount);
        cache.RequireResourceOperationAvailable(GpuResourceKind.Buffer, owner.ResourceId);
        GL.ClearNamedBufferSubData((int)owner.ResourceId, Native.PixelInternalFormat.R8ui, offset, byteCount,
            Native.PixelFormat.RedInteger, Native.PixelType.UnsignedByte, (nint)(&value));
    }
    #endregion

    #region Internal API
    /// <summary>Initializes a private name for the existing manager creation transaction.</summary>
    internal GpuBufferMetadata? InitializeBuffer(in GpuBufferInitialization initialization, out nint id, nint data = 0, int byteCount = 0, nint offset = 0)
    {
        RequireContext();
        initialization.Validate();
        if (byteCount < 0 || offset < 0 || offset > initialization.Capacity || byteCount > initialization.Capacity-offset
            || byteCount != 0 && data == 0) throw new ArgumentException("Invalid initial buffer data.");
        id = 0;
        GL.CreateBuffers(1, out int name);
        id = name;
        RequireContext();
        if (name == 0) return null;
        if (initialization.Mode == GpuBufferStorageMode.Realization)
            return new(0, OpenTK.Graphics.OpenGL.BufferUsageHint.StaticDraw);
        bool complete = offset == 0 && byteCount == initialization.Capacity;
        if (initialization.Mode == GpuBufferStorageMode.Mutable)
            GL.NamedBufferData(name, initialization.Capacity, complete ? data : 0, OpenGLBufferValues.Usage(OpenGLBufferValues.Usage(initialization.Usage)));
        else
            GL.NamedBufferStorage(name, initialization.Capacity, complete ? data : 0, StorageAccess(initialization.StorageFlags));
        RequireContext();
        if (!complete && byteCount != 0) InitializeBufferRange(name, offset, byteCount, data,
            initialization.Mode == GpuBufferStorageMode.Mutable || (initialization.StorageFlags & OpenTK.Graphics.OpenGL.BufferStorageFlags.DynamicStorageBit) != 0);
        RequireContext();
        return new(initialization.Capacity, initialization.Mode == GpuBufferStorageMode.Mutable
            ? initialization.Usage : OpenTK.Graphics.OpenGL.BufferUsageHint.DynamicDraw,
            initialization.Mode == GpuBufferStorageMode.Immutable, initialization.Mode == GpuBufferStorageMode.Immutable ? initialization.StorageFlags : 0);
    }

    /// <summary>Maps a current allocation using named access; failure never retries another path.</summary>
    internal nint MapBuffer(GpuBufferRef buffer, nint offset, nint byteCount, RHIBufferMapAccess access)
    {
        var owner = RequireRange(buffer, offset, byteCount);
        const RHIBufferMapAccess supported = RHIBufferMapAccess.Read | RHIBufferMapAccess.Write | RHIBufferMapAccess.InvalidateRange
            | RHIBufferMapAccess.InvalidateBuffer | RHIBufferMapAccess.ExplicitFlush | RHIBufferMapAccess.Unsynchronized
            | RHIBufferMapAccess.Persistent | RHIBufferMapAccess.Coherent;
        if (byteCount == 0 || (access & ~supported) != 0 || (access & (RHIBufferMapAccess.Read | RHIBufferMapAccess.Write)) == 0
            || (access & RHIBufferMapAccess.Read) != 0 && (access & (RHIBufferMapAccess.InvalidateRange | RHIBufferMapAccess.InvalidateBuffer | RHIBufferMapAccess.Unsynchronized)) != 0
            || (access & RHIBufferMapAccess.ExplicitFlush) != 0 && (access & RHIBufferMapAccess.Write) == 0
            || (access & RHIBufferMapAccess.Coherent) != 0 && (access & RHIBufferMapAccess.Persistent) == 0)
            throw new ArgumentException("Invalid mapping access or extent.");
        var storage = owner.PublishedMetadata;
        if (storage.ImmutableStorage)
        {
            var required = (access & RHIBufferMapAccess.Read) != 0 ? OpenTK.Graphics.OpenGL.BufferStorageFlags.MapReadBit : 0;
            if ((access & RHIBufferMapAccess.Write) != 0) required |= OpenTK.Graphics.OpenGL.BufferStorageFlags.MapWriteBit;
            if ((access & RHIBufferMapAccess.Persistent) != 0) required |= OpenTK.Graphics.OpenGL.BufferStorageFlags.MapPersistentBit;
            if ((access & RHIBufferMapAccess.Coherent) != 0) required |= OpenTK.Graphics.OpenGL.BufferStorageFlags.MapCoherentBit;
            if ((storage.StorageFlags & required) != required) throw new InvalidOperationException("Mapping access exceeds immutable storage permissions.");
        }
        else if ((access & (RHIBufferMapAccess.Persistent | RHIBufferMapAccess.Coherent)) != 0)
            throw new InvalidOperationException("Persistent mapping requires immutable storage.");
        cache.RequireResourceOperationAvailable(GpuResourceKind.Buffer, owner.ResourceId);
        return GL.MapNamedBufferRange((int)owner.ResourceId, offset, byteCount, OpenGLBufferValues.Map(access));
    }

    /// <summary>Flushes the original mapped owner under current identity checks.</summary>
    internal void FlushMappedBuffer(GpuBufferRef buffer, nint offset, nint byteCount)
    {
        var owner = RequireRange(buffer, offset, byteCount);
        GL.FlushMappedNamedBufferRange((int)owner.ResourceId, offset, byteCount);
    }

    /// <summary>Unmaps the original resource without binding or retry.</summary>
    internal bool UnmapBuffer(GpuBufferRef buffer)
    {
        var owner = RequireRange(buffer, 0, 0);
        return GL.UnmapNamedBuffer((int)owner.ResourceId);
    }

    /// <summary>Reads explicit buffer bytes for an existing synchronous diagnostic consumer.</summary>
    internal unsafe void ReadBuffer(GpuBufferRef buffer, nint offset, Span<byte> bytes)
    {
        var owner = RequireRange(buffer, offset, bytes.Length);
        fixed (byte* pointer = bytes) GL.GetNamedBufferSubData((int)owner.ResourceId, offset, bytes.Length, (nint)pointer);
    }
    #endregion

    #region Private
    /// <summary>Copies partial initial content into any storage mode without requiring dynamic CPU updates.</summary>
    private void InitializeBufferRange(int destination, nint offset, int byteCount, nint data, bool clientUpdates, GpuResourceManager.MetadataOperation<GpuBufferMetadata>? operation = null)
    {
        RequireContext();
        operation?.RequireLive();
        if (clientUpdates)
        {
            GL.NamedBufferSubData(destination, offset, byteCount, data);
            RequireContext();
            operation?.RequireLive();
            return;
        }
        var cleanupOrigin = origin == default ? (nativeContext, 0L) : origin;
        var manager = GpuResourceManagerSystem.CurrentManager;
        int staging = 0;
        bool completed = false;
        try
        {
            GL.CreateBuffers(1, out staging);
            RequireContext();
            operation?.RequireLive();
            if (staging == 0) throw new InvalidOperationException("Initial-content staging creation failed.");
            GL.NamedBufferData(staging, byteCount, data, Native.BufferUsageHint.StreamDraw);
            RequireContext();
            operation?.RequireLive();
            GL.CopyNamedBufferSubData(staging, destination, 0, offset, byteCount);
            RequireContext();
            operation?.RequireLive();
            completed = true;
        }
        finally
        {
            // Native deferred destruction retains already consumed staging until the copy completes.
            // A cleanup error must not replace the original initialization failure.
            if (staging != 0) { try { GpuResource.RetireResource(GpuResourceKind.Buffer, staging, cleanupOrigin, manager); } catch when (!completed) { } }
        }
    }
    /// <summary>Checks known bounds and original usability without querying or recovering metadata.</summary>
    private GpuResource<GpuBufferMetadata> RequireRange(GpuBufferRef buffer, nint offset, nint byteCount)
    {
        RequireContext();
        var owner = buffer.RequireResource();
        var data = GpuResourceManager.RequireBufferReferenceMetadata(owner);
        if (offset < 0 || byteCount < 0 || offset > data.Capacity || byteCount > data.Capacity - offset)
            throw new ArgumentOutOfRangeException(nameof(byteCount), "Buffer range exceeds available storage.");
        return owner;
    }

    /// <summary>Translates retained initialization flags explicitly at the private native boundary.</summary>
    private static Native.BufferStorageFlags StorageAccess(OpenTK.Graphics.OpenGL.BufferStorageFlags flags)
    {
        Native.BufferStorageFlags result = 0;
        if ((flags & OpenTK.Graphics.OpenGL.BufferStorageFlags.MapReadBit) != 0) result |= Native.BufferStorageFlags.MapReadBit;
        if ((flags & OpenTK.Graphics.OpenGL.BufferStorageFlags.MapWriteBit) != 0) result |= Native.BufferStorageFlags.MapWriteBit;
        if ((flags & OpenTK.Graphics.OpenGL.BufferStorageFlags.MapPersistentBit) != 0) result |= Native.BufferStorageFlags.MapPersistentBit;
        if ((flags & OpenTK.Graphics.OpenGL.BufferStorageFlags.MapCoherentBit) != 0) result |= Native.BufferStorageFlags.MapCoherentBit;
        if ((flags & OpenTK.Graphics.OpenGL.BufferStorageFlags.DynamicStorageBit) != 0) result |= Native.BufferStorageFlags.DynamicStorageBit;
        if ((flags & OpenTK.Graphics.OpenGL.BufferStorageFlags.ClientStorageBit) != 0) result |= Native.BufferStorageFlags.ClientStorageBit;
        return result;
    }
    #endregion
}
