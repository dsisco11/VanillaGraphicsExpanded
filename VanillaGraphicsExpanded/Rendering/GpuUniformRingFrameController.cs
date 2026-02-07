using System;

namespace VanillaGraphicsExpanded.Rendering;

internal sealed class GpuUniformRingFrameController : IDisposable
{
    private readonly GpuUniformRingBuffer ring;
    private int frameIndex;

    public GpuUniformRingFrameController(GpuUniformRingBuffer ring)
    {
        this.ring = ring ?? throw new ArgumentNullException(nameof(ring));
    }

    public void BeginFrame()
    {
        ring.BeginFrame(frameIndex++);
        GpuUniformRingSystem.SetCurrent(ring);
    }

    public void EndFrame()
    {
        ring.EndFrame();
        GpuUniformRingSystem.ClearCurrent();
    }

    public void Dispose()
    {
        GpuUniformRingSystem.ClearCurrent();
        ring.Dispose();
    }
}
