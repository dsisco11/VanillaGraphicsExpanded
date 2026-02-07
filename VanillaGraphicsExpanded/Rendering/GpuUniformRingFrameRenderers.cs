using System;

using Vintagestory.API.Client;

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

internal sealed class GpuUniformRingBeginRenderer : IRenderer
{
    private const double RenderOrderValue = -1000.0;

    private readonly GpuUniformRingFrameController controller;

    public GpuUniformRingBeginRenderer(GpuUniformRingFrameController controller)
    {
        this.controller = controller ?? throw new ArgumentNullException(nameof(controller));
    }

    public double RenderOrder => RenderOrderValue;
    public int RenderRange => 0;

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        if (stage == EnumRenderStage.Before)
        {
            controller.BeginFrame();
        }
    }

    public void Dispose() { }
}

internal sealed class GpuUniformRingEndRenderer : IRenderer
{
    private const double RenderOrderValue = 1000.0;

    private readonly GpuUniformRingFrameController controller;

    public GpuUniformRingEndRenderer(GpuUniformRingFrameController controller)
    {
        this.controller = controller ?? throw new ArgumentNullException(nameof(controller));
    }

    public double RenderOrder => RenderOrderValue;
    public int RenderRange => 0;

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        if (stage == EnumRenderStage.Done)
        {
            controller.EndFrame();
        }
    }

    public void Dispose() { }
}
