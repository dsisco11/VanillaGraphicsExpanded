using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using VanillaGraphicsExpanded.Rendering.RHI;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Validates client payloads and preserves upload policy while semantic commands own execution.</summary>
internal static class GpuBufferUploader
{
    #region Internal API
    /// <summary>Allocates mutable storage, optionally initializing it from stable client memory.</summary>
    internal static unsafe void Allocate(GpuBufferObject buffer, int byteCount, nint data, RHIBufferUsage usage)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(byteCount);
        var description = new RHIBufferDescription
        { Role = buffer.Role, Mode = RHIStorageMode.Mutable, Capacity = byteCount, Usage = usage };
        ReadOnlySpan<byte> bytes = data == 0 ? default : new((void*)data, byteCount);
        RHICommandContext.Current.AllocateBufferStorage(buffer, description, bytes);
    }

    /// <summary>Checks managed payload representation and extent before allocating storage.</summary>
    internal static void Allocate<T>(GpuBufferObject buffer, int byteCount, T[] data, RHIBufferUsage usage) where T : struct
    {
        ArgumentNullException.ThrowIfNull(data);
        if (RuntimeHelpers.IsReferenceOrContainsReferences<T>()) throw new ArgumentException("Buffer data must contain no managed references.");
        var bytes = MemoryMarshal.AsBytes(data.AsSpan());
        if (byteCount < 0 || byteCount > bytes.Length) throw new ArgumentOutOfRangeException(nameof(byteCount));
        var description = new RHIBufferDescription
        { Role = buffer.Role, Mode = RHIStorageMode.Mutable, Capacity = byteCount, Usage = usage };
        RHICommandContext.Current.AllocateBufferStorage(buffer, description, bytes[..byteCount]);
    }

    /// <summary>Uploads stable client memory without changing capacity or publishing metadata.</summary>
    internal static unsafe void Upload(GpuBufferObject buffer, int offset, int byteCount, nint data)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(byteCount);
        if (byteCount != 0 && data == 0) throw new ArgumentException("Upload data is missing.");
        RHICommandContext.Current.UploadBuffer(buffer, offset, new ReadOnlySpan<byte>((void*)data, byteCount));
    }

    /// <summary>Checks and uploads a managed payload without a temporary binding.</summary>
    internal static void Upload<T>(GpuBufferObject buffer, int offset, int byteCount, T[] data) where T : struct
    {
        ArgumentNullException.ThrowIfNull(data);
        if (RuntimeHelpers.IsReferenceOrContainsReferences<T>()) throw new ArgumentException("Buffer data must contain no managed references.");
        var bytes = MemoryMarshal.AsBytes(data.AsSpan());
        if (byteCount < 0 || byteCount > bytes.Length) throw new ArgumentOutOfRangeException(nameof(byteCount));
        RHICommandContext.Current.UploadBuffer(buffer, offset, bytes[..byteCount]);
    }
    #endregion
}
