using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System;

using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.Rendering;

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
            OpenGlStateCache.Current.BeginFrame();
            controller.BeginFrame();
        }
    }

    public void Dispose() { }
}
