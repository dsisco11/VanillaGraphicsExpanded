using System;

namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Provides typed owner factories over one backend creation transaction.</summary>
public sealed partial class RHICommandContext
{
    #region Public API
    /// <summary>Creates the requested existing buffer abstraction and returns it only after handoff.</summary>
    public RHICreationResult<GpuBufferObject> CreateBuffer(in RHIBufferDescription description, RHIBufferInitialData initialData = default) =>
        backend.CreateBuffer(description, initialData);
    /// <summary>Creates a typed uniform-buffer owner with frozen initialization intent.</summary>
    public RHICreationResult<GpuUniformBuffer> CreateUniformBuffer(in RHIBufferDescription description, RHIBufferInitialData initialData = default) =>
        CreateTypedBuffer<GpuUniformBuffer>(description, initialData, RHIBufferRole.Uniform);
    #endregion

    #region Internal API
    /// <summary>Creates a typed Vertex owner without exposing an unpublished recipient.</summary>
    internal RHICreationResult<GpuVbo> CreateVertexBuffer(in RHIBufferDescription description, RHIBufferInitialData initialData = default) =>
        CreateTypedBuffer<GpuVbo>(description, initialData, RHIBufferRole.Vertex);
    /// <summary>Creates a typed Index owner without exposing an unpublished recipient.</summary>
    internal RHICreationResult<GpuEbo> CreateIndexBuffer(in RHIBufferDescription description, RHIBufferInitialData initialData = default) =>
        CreateTypedBuffer<GpuEbo>(description, initialData, RHIBufferRole.Index);
    /// <summary>Creates a typed ShaderStorage owner without exposing an unpublished recipient.</summary>
    internal RHICreationResult<GpuShaderStorageBuffer> CreateShaderStorageBuffer(in RHIBufferDescription description, RHIBufferInitialData initialData = default) =>
        CreateTypedBuffer<GpuShaderStorageBuffer>(description, initialData, RHIBufferRole.ShaderStorage);
    /// <summary>Creates a typed AtomicCounter owner without exposing an unpublished recipient.</summary>
    internal RHICreationResult<GpuAtomicCounterBuffer> CreateAtomicCounterBuffer(in RHIBufferDescription description, RHIBufferInitialData initialData = default) =>
        CreateTypedBuffer<GpuAtomicCounterBuffer>(description, initialData, RHIBufferRole.AtomicCounter);
    /// <summary>Creates a typed IndirectDraw owner without exposing an unpublished recipient.</summary>
    internal RHICreationResult<GpuIndirectBuffer> CreateIndirectBuffer(in RHIBufferDescription description, RHIBufferInitialData initialData = default) =>
        CreateTypedBuffer<GpuIndirectBuffer>(description, initialData, RHIBufferRole.IndirectDraw);
    /// <summary>Creates a typed PixelPack owner without exposing an unpublished recipient.</summary>
    internal RHICreationResult<GpuPixelPackBuffer> CreatePixelPackBuffer(in RHIBufferDescription description, RHIBufferInitialData initialData = default) =>
        CreateTypedBuffer<GpuPixelPackBuffer>(description, initialData, RHIBufferRole.PixelPack);
    /// <summary>Creates a typed PixelUnpack owner without exposing an unpublished recipient.</summary>
    internal RHICreationResult<GpuPixelUnpackBuffer> CreatePixelUnpackBuffer(in RHIBufferDescription description, RHIBufferInitialData initialData = default) =>
        CreateTypedBuffer<GpuPixelUnpackBuffer>(description, initialData, RHIBufferRole.PixelUnpack);
    #endregion

    #region Private
    /// <summary>Checks the typed role before dispatch and preserves the common success/failure contract.</summary>
    private RHICreationResult<T> CreateTypedBuffer<T>(in RHIBufferDescription description, RHIBufferInitialData initialData, RHIBufferRole role) where T : GpuBufferObject
    {
        if (description.Role != role) throw new ArgumentException("Description role does not match the typed factory.");
        var result = backend.CreateBuffer(description, initialData);
        return result.TryGetResource(out var owner) ? new((T)owner!, null) : new(null, result.Diagnostics);
    }
    #endregion
}
