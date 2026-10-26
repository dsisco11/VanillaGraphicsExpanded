using System;
namespace VanillaGraphicsExpanded.Rendering.RHI;
/// <summary>Defines capability-gated external-object execution with explicit native handle ownership.</summary>
internal partial interface IRHIBackend
{
    /// <summary>Creates an optionally imported semaphore owner.</summary>
    RHICreationResult<GpuSemaphore> CreateSemaphore(in RHISemaphoreDescription description);
    /// <summary>Creates an optionally configured/imported memory owner.</summary>
    RHICreationResult<GpuMemoryObject> CreateMemoryObject(in RHIMemoryObjectDescription description);
    /// <summary>Orders an explicit external wait across supplied resources.</summary>
    void WaitSemaphore(GpuSemaphoreRef semaphore, ReadOnlySpan<GpuBufferRef> buffers, ReadOnlySpan<RHIExternalTextureTransition> textures);
    /// <summary>Signals external consumers after supplied resource writes.</summary>
    void SignalSemaphore(GpuSemaphoreRef semaphore, ReadOnlySpan<GpuBufferRef> buffers, ReadOnlySpan<RHIExternalTextureTransition> textures);
}
