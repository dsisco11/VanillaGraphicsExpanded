using System;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Coordinates transient and retained uniform storage through the existing frame lifecycle.</summary>
internal sealed class RHIUniformRingFrameController : IDisposable
{
    private readonly GpuUniformRingBuffer ring;
    private int frameIndex;
    private bool disposed;

    #region Public API
    /// <summary>Adopts the allocator and its context-bound storage policy.</summary>
    public RHIUniformRingFrameController(GpuUniformRingBuffer ring)
    {
        this.ring = ring ?? throw new ArgumentNullException(nameof(ring));
    }

    /// <summary>Begins and exposes a new allocation epoch within the renderer lifetime.</summary>
    public void BeginFrame()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ring.BeginFrame(frameIndex++);
        RHIUniformRingSystem.SetCurrent(ring);
    }

    /// <summary>Closes publication before detaching the allocator, including failed fence creation.</summary>
    public void EndFrame()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        try { ring.EndFrame(); }
        finally { RHIUniformRingSystem.ClearCurrent(); }
    }

    /// <summary>Retires both allocation policies and prevents further frame publication.</summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        RHIUniformRingSystem.ClearCurrent();
        ring.Dispose();
    }
    #endregion
}
