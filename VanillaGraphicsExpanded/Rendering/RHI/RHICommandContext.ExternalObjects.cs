using System;
namespace VanillaGraphicsExpanded.Rendering.RHI;
/// <summary>Provides explicit optional external-object creation and synchronization.</summary>
public sealed partial class RHICommandContext
{
    #region Public API
    /// <summary>Creates an owned semaphore, optionally importing the explicitly described handle.</summary>
    public RHICreationResult<GpuSemaphore> CreateSemaphore(in RHISemaphoreDescription description) => backend.CreateSemaphore(description);
    /// <summary>Creates an owned memory object without inferring platform-handle ownership.</summary>
    public RHICreationResult<GpuMemoryObject> CreateMemoryObject(in RHIMemoryObjectDescription description) => backend.CreateMemoryObject(description);
    /// <summary>Waits on external work for the explicitly supplied resource transitions.</summary>
    public void WaitSemaphore(GpuSemaphoreRef semaphore, ReadOnlySpan<GpuBufferRef> buffers, ReadOnlySpan<RHIExternalTextureTransition> textures) => backend.WaitSemaphore(semaphore, buffers, textures);
    /// <summary>Signals external consumers for the explicitly supplied resource transitions.</summary>
    public void SignalSemaphore(GpuSemaphoreRef semaphore, ReadOnlySpan<GpuBufferRef> buffers, ReadOnlySpan<RHIExternalTextureTransition> textures) => backend.SignalSemaphore(semaphore, buffers, textures);
    #endregion
}
