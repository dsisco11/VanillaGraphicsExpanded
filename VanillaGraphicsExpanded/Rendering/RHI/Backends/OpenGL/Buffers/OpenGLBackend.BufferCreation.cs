using System;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Creates existing buffer owners through the private manager transaction.</summary>
internal sealed partial class OpenGLBackend
{
    #region Public API
    /// <inheritdoc />
    public RHICreationResult<GpuBufferObject> CreateBuffer(in RHIBufferDescription description, RHIBufferInitialData initialData)
    {
        // Freeze value intent before native execution or callbacks can observe the caller again.
        var request = description;
        RequireContext();
        request.Validate();
        if (request.Access.HasFlag(StorageAccess.Sparse) && !Capabilities.SparseBuffers) throw new NotSupportedException("Sparse buffers are unavailable.");
        if (request.Access.HasFlag(StorageAccess.Sparse) && !initialData.Bytes.IsEmpty)
            throw new ArgumentException("Sparse storage starts uncommitted; commit pages before uploading content.");
        if (initialData.Offset < 0 || initialData.Offset > request.Capacity
            || initialData.Bytes.Length > request.Capacity - initialData.Offset
            || request.Mode == ResourceStorageMode.Realization && !initialData.Bytes.IsEmpty)
            throw new ArgumentException("Initial content exceeds the requested allocation.");
        var usage = request.Mode == ResourceStorageMode.Mutable ? request.Usage : BufferUsage.StaticDraw;
        GpuBufferObject owner = request.Role switch
        {
            BufferRole.Index => new GpuEbo(usage, request.Label),
            BufferRole.Uniform => new GpuUniformBuffer(usage, request.Label),
            BufferRole.ShaderStorage => new GpuShaderStorageBuffer(usage, request.Label),
            BufferRole.AtomicCounter => new GpuAtomicCounterBuffer(usage, request.Label),
            BufferRole.IndirectDraw or BufferRole.IndirectDispatch => new GpuIndirectBuffer(usage, request.Label, request.Role),
            BufferRole.PixelPack => new GpuPixelPackBuffer(usage, request.Label),
            BufferRole.PixelUnpack => new GpuPixelUnpackBuffer(usage, request.Label),
            _ => new GpuVbo(OpenGLBufferValues.Target(request.Role), usage, request.Label)
        };
        try
        {
            var initialization = LowerBufferDescription(request);
            if (!GpuResourceManager.CreateBuffer(initialization, owner, initialData, out _).HasValue)
            {
                owner.Dispose();
                return new(null, "Native buffer initialization did not complete.");
            }
            owner.SetDebugName(request.Label);
            return new(owner, null);
        }
        catch
        {
            // The manager has already cleared provisional identity and retired only this candidate.
            // Local declaration cleanup must never replace the initialization exception.
            try { owner.Dispose(); } catch { }
            throw;
        }
    }
    #endregion

    #region Private
    /// <summary>Preserves omitted versus assigned fields when lowering to private initialization.</summary>
    private static GpuBufferInitialization LowerBufferDescription(in RHIBufferDescription description)
    {
        var request = new GpuBufferInitialization
        {
            Mode = description.Mode switch
            {
                ResourceStorageMode.Realization => GpuBufferStorageMode.Realization,
                ResourceStorageMode.Mutable => GpuBufferStorageMode.Mutable,
                ResourceStorageMode.Immutable => GpuBufferStorageMode.Immutable,
                _ => throw new ArgumentOutOfRangeException(nameof(description))
            }
        };
        if (description.Mode == ResourceStorageMode.Realization) return request;
        request.Capacity = description.Capacity;
        if (description.Mode == ResourceStorageMode.Mutable) request.Usage = OpenGLBufferValues.MetadataUsage(description.Usage);
        else
        {
            OpenTK.Graphics.OpenGL.BufferStorageFlags flags = 0;
            if ((description.Access & StorageAccess.Read) != 0) flags |= OpenTK.Graphics.OpenGL.BufferStorageFlags.MapReadBit;
            if ((description.Access & StorageAccess.Write) != 0) flags |= OpenTK.Graphics.OpenGL.BufferStorageFlags.MapWriteBit;
            if ((description.Access & StorageAccess.Persistent) != 0) flags |= OpenTK.Graphics.OpenGL.BufferStorageFlags.MapPersistentBit;
            if ((description.Access & StorageAccess.Coherent) != 0) flags |= OpenTK.Graphics.OpenGL.BufferStorageFlags.MapCoherentBit;
            if ((description.Access & StorageAccess.DynamicUpdates) != 0) flags |= OpenTK.Graphics.OpenGL.BufferStorageFlags.DynamicStorageBit;
            if ((description.Access & StorageAccess.ClientStorage) != 0) flags |= OpenTK.Graphics.OpenGL.BufferStorageFlags.ClientStorageBit;
            if ((description.Access & StorageAccess.Sparse) != 0) flags |= (OpenTK.Graphics.OpenGL.BufferStorageFlags)OpenTK.Graphics.OpenGL.ArbSparseBuffer.SparseStorageBitArb;
            request.StorageFlags = flags;
        }
        return request;
    }
    #endregion
}
