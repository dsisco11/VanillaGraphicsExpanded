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
        if (initialData.Offset < 0 || initialData.Offset > request.Capacity
            || initialData.Bytes.Length > request.Capacity - initialData.Offset
            || request.Mode == RHIStorageMode.Realization && !initialData.Bytes.IsEmpty)
            throw new ArgumentException("Initial content exceeds the requested allocation.");
        var usage = request.Mode == RHIStorageMode.Mutable ? request.Usage : RHIBufferUsage.StaticDraw;
        GpuBufferObject owner = request.Role switch
        {
            RHIBufferRole.Index => new GpuEbo(usage, request.Label),
            RHIBufferRole.Uniform => new GpuUniformBuffer(usage, request.Label),
            RHIBufferRole.ShaderStorage => new GpuShaderStorageBuffer(usage, request.Label),
            RHIBufferRole.AtomicCounter => new GpuAtomicCounterBuffer(usage, request.Label),
            RHIBufferRole.IndirectDraw or RHIBufferRole.IndirectDispatch => new GpuIndirectBuffer(usage, request.Label, request.Role),
            RHIBufferRole.PixelPack => new GpuPixelPackBuffer(usage, request.Label),
            RHIBufferRole.PixelUnpack => new GpuPixelUnpackBuffer(usage, request.Label),
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
                RHIStorageMode.Realization => GpuBufferStorageMode.Realization,
                RHIStorageMode.Mutable => GpuBufferStorageMode.Mutable,
                RHIStorageMode.Immutable => GpuBufferStorageMode.Immutable,
                _ => throw new ArgumentOutOfRangeException(nameof(description))
            }
        };
        if (description.Mode == RHIStorageMode.Realization) return request;
        request.Capacity = description.Capacity;
        if (description.Mode == RHIStorageMode.Mutable) request.Usage = OpenGLBufferValues.MetadataUsage(description.Usage);
        else
        {
            OpenTK.Graphics.OpenGL.BufferStorageFlags flags = 0;
            if ((description.Access & RHIStorageAccess.Read) != 0) flags |= OpenTK.Graphics.OpenGL.BufferStorageFlags.MapReadBit;
            if ((description.Access & RHIStorageAccess.Write) != 0) flags |= OpenTK.Graphics.OpenGL.BufferStorageFlags.MapWriteBit;
            if ((description.Access & RHIStorageAccess.Persistent) != 0) flags |= OpenTK.Graphics.OpenGL.BufferStorageFlags.MapPersistentBit;
            if ((description.Access & RHIStorageAccess.Coherent) != 0) flags |= OpenTK.Graphics.OpenGL.BufferStorageFlags.MapCoherentBit;
            if ((description.Access & RHIStorageAccess.DynamicUpdates) != 0) flags |= OpenTK.Graphics.OpenGL.BufferStorageFlags.DynamicStorageBit;
            if ((description.Access & RHIStorageAccess.ClientStorage) != 0) flags |= OpenTK.Graphics.OpenGL.BufferStorageFlags.ClientStorageBit;
            request.StorageFlags = flags;
        }
        return request;
    }
    #endregion
}
