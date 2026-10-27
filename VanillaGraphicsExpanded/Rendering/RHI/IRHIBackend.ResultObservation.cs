namespace VanillaGraphicsExpanded.Rendering.RHI;
/// <summary>Provides immediate mapping and captured-result observation on the original execution service.</summary>
internal partial interface IRHIBackend
{
    /// <summary>Maps a checked CPU-accessible allocation interval.</summary>
    nint MapBuffer(GpuBufferRef buffer, nint offset, nint byteCount, BufferMapAccess access);
    /// <summary>Flushes the explicitly selected mapped interval.</summary>
    void FlushMappedBuffer(GpuBufferRef buffer, nint offset, nint byteCount);
    /// <summary>Releases an immediate mapping and reports storage integrity.</summary>
    bool UnmapBuffer(GpuBufferRef buffer);
    /// <summary>Observes only the captured issued query generation.</summary>
    bool TryReadQuery(GpuQuery owner, long revision, bool wait, out ulong result);
}
