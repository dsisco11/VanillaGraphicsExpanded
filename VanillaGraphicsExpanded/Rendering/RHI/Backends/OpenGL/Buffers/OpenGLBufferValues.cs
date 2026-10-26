using System;
using Legacy = OpenTK.Graphics.OpenGL;
using Native = OpenTK.Graphics.OpenGL4;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Translates semantic buffer values into explicit native enum members.</summary>
internal static class OpenGLBufferValues
{
    #region Internal API
    /// <summary>Translates the declared role without relying on token-compatible enum values.</summary>
    internal static Legacy.BufferTarget Target(RHIBufferRole value) => value switch
    {
        RHIBufferRole.Vertex => Legacy.BufferTarget.ArrayBuffer,
        RHIBufferRole.Index => Legacy.BufferTarget.ElementArrayBuffer,
        RHIBufferRole.Uniform => Legacy.BufferTarget.UniformBuffer,
        RHIBufferRole.ShaderStorage => Legacy.BufferTarget.ShaderStorageBuffer,
        RHIBufferRole.AtomicCounter => Legacy.BufferTarget.AtomicCounterBuffer,
        RHIBufferRole.IndirectDraw => Legacy.BufferTarget.DrawIndirectBuffer,
        RHIBufferRole.IndirectDispatch => Legacy.BufferTarget.DispatchIndirectBuffer,
        RHIBufferRole.PixelPack => Legacy.BufferTarget.PixelPackBuffer,
        RHIBufferRole.PixelUnpack => Legacy.BufferTarget.PixelUnpackBuffer,
        RHIBufferRole.CopySource => Legacy.BufferTarget.CopyReadBuffer,
        RHIBufferRole.CopyDestination => Legacy.BufferTarget.CopyWriteBuffer,
        RHIBufferRole.TransformFeedback => Legacy.BufferTarget.TransformFeedbackBuffer,
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };
    /// <summary>Imports an existing wrapper's intended role at the resource integration boundary.</summary>
    internal static RHIBufferRole Role(Legacy.BufferTarget value) => value switch
    {
        Legacy.BufferTarget.ArrayBuffer => RHIBufferRole.Vertex,
        Legacy.BufferTarget.ElementArrayBuffer => RHIBufferRole.Index,
        Legacy.BufferTarget.UniformBuffer => RHIBufferRole.Uniform,
        Legacy.BufferTarget.ShaderStorageBuffer => RHIBufferRole.ShaderStorage,
        Legacy.BufferTarget.AtomicCounterBuffer => RHIBufferRole.AtomicCounter,
        Legacy.BufferTarget.DrawIndirectBuffer => RHIBufferRole.IndirectDraw,
        Legacy.BufferTarget.DispatchIndirectBuffer => RHIBufferRole.IndirectDispatch,
        Legacy.BufferTarget.PixelPackBuffer => RHIBufferRole.PixelPack,
        Legacy.BufferTarget.PixelUnpackBuffer => RHIBufferRole.PixelUnpack,
        Legacy.BufferTarget.CopyReadBuffer => RHIBufferRole.CopySource,
        Legacy.BufferTarget.CopyWriteBuffer => RHIBufferRole.CopyDestination,
        Legacy.BufferTarget.TransformFeedbackBuffer => RHIBufferRole.TransformFeedback,
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };
    /// <summary>Translates allocation usage into the core backend type.</summary>
    internal static Native.BufferUsageHint Usage(RHIBufferUsage value) => value switch
    {
        RHIBufferUsage.StaticDraw => Native.BufferUsageHint.StaticDraw,
        RHIBufferUsage.StaticRead => Native.BufferUsageHint.StaticRead,
        RHIBufferUsage.StaticCopy => Native.BufferUsageHint.StaticCopy,
        RHIBufferUsage.DynamicDraw => Native.BufferUsageHint.DynamicDraw,
        RHIBufferUsage.DynamicRead => Native.BufferUsageHint.DynamicRead,
        RHIBufferUsage.DynamicCopy => Native.BufferUsageHint.DynamicCopy,
        RHIBufferUsage.StreamDraw => Native.BufferUsageHint.StreamDraw,
        RHIBufferUsage.StreamRead => Native.BufferUsageHint.StreamRead,
        RHIBufferUsage.StreamCopy => Native.BufferUsageHint.StreamCopy,
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };
    /// <summary>Projects canonical legacy metadata or wrapper policy into semantic input.</summary>
    internal static RHIBufferUsage Usage(Legacy.BufferUsageHint value) => value switch
    {
        Legacy.BufferUsageHint.StaticDraw => RHIBufferUsage.StaticDraw,
        Legacy.BufferUsageHint.StaticRead => RHIBufferUsage.StaticRead,
        Legacy.BufferUsageHint.StaticCopy => RHIBufferUsage.StaticCopy,
        Legacy.BufferUsageHint.DynamicDraw => RHIBufferUsage.DynamicDraw,
        Legacy.BufferUsageHint.DynamicRead => RHIBufferUsage.DynamicRead,
        Legacy.BufferUsageHint.DynamicCopy => RHIBufferUsage.DynamicCopy,
        Legacy.BufferUsageHint.StreamDraw => RHIBufferUsage.StreamDraw,
        Legacy.BufferUsageHint.StreamRead => RHIBufferUsage.StreamRead,
        Legacy.BufferUsageHint.StreamCopy => RHIBufferUsage.StreamCopy,
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };
    /// <summary>Translates a semantic usage into the retained canonical metadata schema.</summary>
    internal static Legacy.BufferUsageHint MetadataUsage(RHIBufferUsage value) => value switch
    {
        RHIBufferUsage.StaticDraw => Legacy.BufferUsageHint.StaticDraw,
        RHIBufferUsage.StaticRead => Legacy.BufferUsageHint.StaticRead,
        RHIBufferUsage.StaticCopy => Legacy.BufferUsageHint.StaticCopy,
        RHIBufferUsage.DynamicDraw => Legacy.BufferUsageHint.DynamicDraw,
        RHIBufferUsage.DynamicRead => Legacy.BufferUsageHint.DynamicRead,
        RHIBufferUsage.DynamicCopy => Legacy.BufferUsageHint.DynamicCopy,
        RHIBufferUsage.StreamDraw => Legacy.BufferUsageHint.StreamDraw,
        RHIBufferUsage.StreamRead => Legacy.BufferUsageHint.StreamRead,
        RHIBufferUsage.StreamCopy => Legacy.BufferUsageHint.StreamCopy,
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };
    /// <summary>Translates each declared immutable-storage flag independently.</summary>
    internal static Native.BufferStorageFlags Storage(RHIStorageAccess value)
    {
        Native.BufferStorageFlags result = 0;
        if ((value & RHIStorageAccess.Read) != 0) result |= Native.BufferStorageFlags.MapReadBit;
        if ((value & RHIStorageAccess.Write) != 0) result |= Native.BufferStorageFlags.MapWriteBit;
        if ((value & RHIStorageAccess.Persistent) != 0) result |= Native.BufferStorageFlags.MapPersistentBit;
        if ((value & RHIStorageAccess.Coherent) != 0) result |= Native.BufferStorageFlags.MapCoherentBit;
        if ((value & RHIStorageAccess.DynamicUpdates) != 0) result |= Native.BufferStorageFlags.DynamicStorageBit;
        if ((value & RHIStorageAccess.ClientStorage) != 0) result |= Native.BufferStorageFlags.ClientStorageBit;
        return result;
    }
    /// <summary>Translates access flags for a named mapping.</summary>
    internal static Native.BufferAccessMask Map(RHIBufferMapAccess value)
    {
        Native.BufferAccessMask result = 0;
        if ((value & RHIBufferMapAccess.Read) != 0) result |= Native.BufferAccessMask.MapReadBit;
        if ((value & RHIBufferMapAccess.Write) != 0) result |= Native.BufferAccessMask.MapWriteBit;
        if ((value & RHIBufferMapAccess.InvalidateRange) != 0) result |= Native.BufferAccessMask.MapInvalidateRangeBit;
        if ((value & RHIBufferMapAccess.InvalidateBuffer) != 0) result |= Native.BufferAccessMask.MapInvalidateBufferBit;
        if ((value & RHIBufferMapAccess.ExplicitFlush) != 0) result |= Native.BufferAccessMask.MapFlushExplicitBit;
        if ((value & RHIBufferMapAccess.Unsynchronized) != 0) result |= Native.BufferAccessMask.MapUnsynchronizedBit;
        if ((value & RHIBufferMapAccess.Persistent) != 0) result |= Native.BufferAccessMask.MapPersistentBit;
        if ((value & RHIBufferMapAccess.Coherent) != 0) result |= Native.BufferAccessMask.MapCoherentBit;
        return result;
    }
    #endregion
}
