using System;

namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Defines semantic buffer execution without native overload or binding-path selectors.</summary>
internal partial interface IRHIBackend
{
    /// <summary>Gets the existing context capability projection.</summary>
    RHICapabilities Capabilities { get; }
    /// <summary>Checks original context and thread before an operation starts.</summary>
    void RequireContext();
    /// <summary>Creates the requested existing owner and completes private metadata handoff.</summary>
    RHICreationResult<GpuBufferObject> CreateBuffer(in RHIBufferDescription description, RHIBufferInitialData initialData);
    /// <summary>Allocates storage under the owning abstraction's authority and publishes once.</summary>
    void AllocateBufferStorage(GpuBufferObject owner, in RHIBufferDescription description, ReadOnlySpan<byte> initialData);
    /// <summary>Writes an explicit buffer range without publishing storage metadata.</summary>
    void UploadBuffer(GpuBufferRef destination, nint offset, ReadOnlySpan<byte> bytes);
    /// <summary>Copies known in-bounds byte ranges in submission order.</summary>
    void CopyBuffer(GpuBufferRef source, nint sourceOffset, GpuBufferRef destination, nint destinationOffset, nint byteCount);
    /// <summary>Clears an explicit range with an unsigned byte value.</summary>
    void ClearBuffer(GpuBufferRef destination, nint offset, nint byteCount, byte value);
    /// <summary>Publishes a semantic generic role selection.</summary>
    void BindBuffer(RHIBufferRole role, GpuBufferRef buffer);
    /// <summary>Publishes a whole indexed selection.</summary>
    void BindBufferBase(RHIBufferRole role, int slot, GpuBufferRef buffer);
    /// <summary>Publishes a checked allocation-relative indexed range.</summary>
    void BindBufferRange(RHIBufferRole role, int slot, GpuBufferRef buffer, nint offset, nint size);
}
