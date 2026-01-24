using VanillaGraphicsExpanded.Rendering;

using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.ModSystems;

public sealed class GpuResourceManagerModSystem : ModSystem
{
    private ICoreClientAPI? capi;
    private GpuResourceManager? renderer;
    private bool rendererRegistered;

    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Client;

    public override void StartClientSide(ICoreClientAPI api)
    {
        capi = api;

        renderer ??= new GpuResourceManager();
        GpuResourceManagerSystem.Initialize(renderer);

        api.Event.RegisterRenderer(renderer, GpuResourceManager.Stage, "vge_gpu_resource_manager");
        rendererRegistered = true;

        api.Logger.Notification(
            "[VGE] GpuResourceManager registered ({0} @ {1})",
            GpuResourceManager.Stage,
            renderer.RenderOrder);
    }

    public override void Dispose()
    {
        base.Dispose();

        if (capi != null && rendererRegistered && renderer is not null)
        {
            try
            {
                capi.Event.UnregisterRenderer(renderer, GpuResourceManager.Stage);
            }
            catch
            {
                // Best-effort.
            }
        }

        renderer?.Dispose();
        renderer = null;
        rendererRegistered = false;

        // Texture streaming is driven by GpuResourceManager; dispose it when the manager is shutting down
        // to avoid recreating the singleton from other code paths.
        TextureStreamingSystem.Dispose();

        GpuResourceManagerSystem.Shutdown();

        capi = null;
    }
}
