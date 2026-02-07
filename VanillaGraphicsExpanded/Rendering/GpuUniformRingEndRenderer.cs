using System;

using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.Rendering;

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
