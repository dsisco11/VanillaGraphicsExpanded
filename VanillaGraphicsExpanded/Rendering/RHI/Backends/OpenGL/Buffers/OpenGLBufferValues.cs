using System;
using Legacy = OpenTK.Graphics.OpenGL;
using Native = OpenTK.Graphics.OpenGL4;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Translates semantic buffer values into explicit native enum members.</summary>
internal static class OpenGLBufferValues
{
    #region Internal API
    /// <summary>Translates the declared role without relying on token-compatible enum values.</summary>
    internal static Legacy.BufferTarget Target(BufferRole value) => value switch
    {
        BufferRole.Vertex => Legacy.BufferTarget.ArrayBuffer,
        BufferRole.Index => Legacy.BufferTarget.ElementArrayBuffer,
        BufferRole.Uniform => Legacy.BufferTarget.UniformBuffer,
        BufferRole.ShaderStorage => Legacy.BufferTarget.ShaderStorageBuffer,
        BufferRole.AtomicCounter => Legacy.BufferTarget.AtomicCounterBuffer,
        BufferRole.IndirectDraw => Legacy.BufferTarget.DrawIndirectBuffer,
        BufferRole.IndirectDispatch => Legacy.BufferTarget.DispatchIndirectBuffer,
        BufferRole.PixelPack => Legacy.BufferTarget.PixelPackBuffer,
        BufferRole.PixelUnpack => Legacy.BufferTarget.PixelUnpackBuffer,
        BufferRole.CopySource => Legacy.BufferTarget.CopyReadBuffer,
        BufferRole.CopyDestination => Legacy.BufferTarget.CopyWriteBuffer,
        BufferRole.TransformFeedback => Legacy.BufferTarget.TransformFeedbackBuffer,
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };
    /// <summary>Imports an existing wrapper's intended role at the resource integration boundary.</summary>
    internal static BufferRole Role(Legacy.BufferTarget value) => value switch
    {
        Legacy.BufferTarget.ArrayBuffer => BufferRole.Vertex,
        Legacy.BufferTarget.ElementArrayBuffer => BufferRole.Index,
        Legacy.BufferTarget.UniformBuffer => BufferRole.Uniform,
        Legacy.BufferTarget.ShaderStorageBuffer => BufferRole.ShaderStorage,
        Legacy.BufferTarget.AtomicCounterBuffer => BufferRole.AtomicCounter,
        Legacy.BufferTarget.DrawIndirectBuffer => BufferRole.IndirectDraw,
        Legacy.BufferTarget.DispatchIndirectBuffer => BufferRole.IndirectDispatch,
        Legacy.BufferTarget.PixelPackBuffer => BufferRole.PixelPack,
        Legacy.BufferTarget.PixelUnpackBuffer => BufferRole.PixelUnpack,
        Legacy.BufferTarget.CopyReadBuffer => BufferRole.CopySource,
        Legacy.BufferTarget.CopyWriteBuffer => BufferRole.CopyDestination,
        Legacy.BufferTarget.TransformFeedbackBuffer => BufferRole.TransformFeedback,
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };
    /// <summary>Translates allocation usage into the core backend type.</summary>
    internal static Native.BufferUsageHint Usage(BufferUsage value) => value switch
    {
        BufferUsage.StaticDraw => Native.BufferUsageHint.StaticDraw,
        BufferUsage.StaticRead => Native.BufferUsageHint.StaticRead,
        BufferUsage.StaticCopy => Native.BufferUsageHint.StaticCopy,
        BufferUsage.DynamicDraw => Native.BufferUsageHint.DynamicDraw,
        BufferUsage.DynamicRead => Native.BufferUsageHint.DynamicRead,
        BufferUsage.DynamicCopy => Native.BufferUsageHint.DynamicCopy,
        BufferUsage.StreamDraw => Native.BufferUsageHint.StreamDraw,
        BufferUsage.StreamRead => Native.BufferUsageHint.StreamRead,
        BufferUsage.StreamCopy => Native.BufferUsageHint.StreamCopy,
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };
    /// <summary>Projects canonical legacy metadata or wrapper policy into semantic input.</summary>
    internal static BufferUsage Usage(Legacy.BufferUsageHint value) => value switch
    {
        Legacy.BufferUsageHint.StaticDraw => BufferUsage.StaticDraw,
        Legacy.BufferUsageHint.StaticRead => BufferUsage.StaticRead,
        Legacy.BufferUsageHint.StaticCopy => BufferUsage.StaticCopy,
        Legacy.BufferUsageHint.DynamicDraw => BufferUsage.DynamicDraw,
        Legacy.BufferUsageHint.DynamicRead => BufferUsage.DynamicRead,
        Legacy.BufferUsageHint.DynamicCopy => BufferUsage.DynamicCopy,
        Legacy.BufferUsageHint.StreamDraw => BufferUsage.StreamDraw,
        Legacy.BufferUsageHint.StreamRead => BufferUsage.StreamRead,
        Legacy.BufferUsageHint.StreamCopy => BufferUsage.StreamCopy,
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };
    /// <summary>Translates a semantic usage into the retained canonical metadata schema.</summary>
    internal static Legacy.BufferUsageHint MetadataUsage(BufferUsage value) => value switch
    {
        BufferUsage.StaticDraw => Legacy.BufferUsageHint.StaticDraw,
        BufferUsage.StaticRead => Legacy.BufferUsageHint.StaticRead,
        BufferUsage.StaticCopy => Legacy.BufferUsageHint.StaticCopy,
        BufferUsage.DynamicDraw => Legacy.BufferUsageHint.DynamicDraw,
        BufferUsage.DynamicRead => Legacy.BufferUsageHint.DynamicRead,
        BufferUsage.DynamicCopy => Legacy.BufferUsageHint.DynamicCopy,
        BufferUsage.StreamDraw => Legacy.BufferUsageHint.StreamDraw,
        BufferUsage.StreamRead => Legacy.BufferUsageHint.StreamRead,
        BufferUsage.StreamCopy => Legacy.BufferUsageHint.StreamCopy,
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };
    /// <summary>Translates each declared immutable-storage flag independently.</summary>
    internal static Native.BufferStorageFlags Storage(StorageAccess value)
    {
        Native.BufferStorageFlags result = 0;
        if ((value & StorageAccess.Read) != 0) result |= Native.BufferStorageFlags.MapReadBit;
        if ((value & StorageAccess.Write) != 0) result |= Native.BufferStorageFlags.MapWriteBit;
        if ((value & StorageAccess.Persistent) != 0) result |= Native.BufferStorageFlags.MapPersistentBit;
        if ((value & StorageAccess.Coherent) != 0) result |= Native.BufferStorageFlags.MapCoherentBit;
        if ((value & StorageAccess.DynamicUpdates) != 0) result |= Native.BufferStorageFlags.DynamicStorageBit;
        if ((value & StorageAccess.ClientStorage) != 0) result |= Native.BufferStorageFlags.ClientStorageBit;
        if ((value & StorageAccess.Sparse) != 0) result |= (Native.BufferStorageFlags)Native.ArbSparseBuffer.SparseStorageBitArb;
        return result;
    }
    /// <summary>Translates access flags for a named mapping.</summary>
    internal static Native.BufferAccessMask Map(BufferMapAccess value)
    {
        Native.BufferAccessMask result = 0;
        if ((value & BufferMapAccess.Read) != 0) result |= Native.BufferAccessMask.MapReadBit;
        if ((value & BufferMapAccess.Write) != 0) result |= Native.BufferAccessMask.MapWriteBit;
        if ((value & BufferMapAccess.InvalidateRange) != 0) result |= Native.BufferAccessMask.MapInvalidateRangeBit;
        if ((value & BufferMapAccess.InvalidateBuffer) != 0) result |= Native.BufferAccessMask.MapInvalidateBufferBit;
        if ((value & BufferMapAccess.ExplicitFlush) != 0) result |= Native.BufferAccessMask.MapFlushExplicitBit;
        if ((value & BufferMapAccess.Unsynchronized) != 0) result |= Native.BufferAccessMask.MapUnsynchronizedBit;
        if ((value & BufferMapAccess.Persistent) != 0) result |= Native.BufferAccessMask.MapPersistentBit;
        if ((value & BufferMapAccess.Coherent) != 0) result |= Native.BufferAccessMask.MapCoherentBit;
        return result;
    }
    #endregion
}
