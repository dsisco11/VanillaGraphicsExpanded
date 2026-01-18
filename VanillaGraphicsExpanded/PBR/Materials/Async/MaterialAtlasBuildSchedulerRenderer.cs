using System;

using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.PBR.Materials.Async;

internal sealed class MaterialAtlasBuildSchedulerRenderer : IRenderer
{
    private readonly MaterialAtlasBuildScheduler scheduler;

    public MaterialAtlasBuildSchedulerRenderer(MaterialAtlasBuildScheduler scheduler)
    {
        this.scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
    }

    public double RenderOrder => 0;

    public int RenderRange => 0;

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        scheduler.TickOnRenderThread();
    }

    public void Dispose()
    {
        // Scheduler is owned elsewhere.
    }
}
