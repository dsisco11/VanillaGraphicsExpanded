using System;

using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.Rendering.Profiling;

public sealed class GlGpuProfilerRenderer : IRenderer, IDisposable
{
    private const double RenderOrderValue = -1000.0;

    private readonly ICoreClientAPI capi;

    public double RenderOrder => RenderOrderValue;

    public int RenderRange => 0;

    public GlGpuProfilerRenderer(ICoreClientAPI capi)
    {
        this.capi = capi;
        capi.Event.RegisterRenderer(this, EnumRenderStage.Before, "vge_gl_gpu_profiler");
    }

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        if (stage != EnumRenderStage.Before)
        {
            return;
        }

        GlGpuProfiler.Instance.BeginFrame(capi.Render.FrameWidth, capi.Render.FrameHeight);
    }

    public void Dispose()
    {
        capi.Event.UnregisterRenderer(this, EnumRenderStage.Before);
    }
}
