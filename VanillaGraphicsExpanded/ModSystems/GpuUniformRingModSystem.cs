using VanillaGraphicsExpanded.Rendering;

using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.ModSystems;

/// <summary>
/// Installs a per-frame UBO ring allocator and exposes it via <see cref="GpuUniformRingSystem"/>.
/// </summary>
public sealed class GpuUniformRingModSystem : ModSystem
{
    private ICoreClientAPI? capi;
    private GpuUniformRingFrameController? controller;
    private GpuUniformRingBeginRenderer? beginRenderer;
    private GpuUniformRingEndRenderer? endRenderer;
    private bool registered;

    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Client;

    public override void StartClientSide(ICoreClientAPI api)
    {
        capi = api;

        // Conservative defaults; can be made configurable later.
        var ring = new GpuUniformRingBuffer(
            pageSizeBytes: 2 * 1024 * 1024,
            pageCount: 3,
            preferPersistent: true,
            coherent: true,
            debugName: "VGE.UboRing");

        controller = new GpuUniformRingFrameController(ring);
        beginRenderer = new GpuUniformRingBeginRenderer(controller);
        endRenderer = new GpuUniformRingEndRenderer(controller);

        api.Event.RegisterRenderer(beginRenderer, EnumRenderStage.Before, "vge_ubo_ring_begin");
        api.Event.RegisterRenderer(endRenderer, EnumRenderStage.Done, "vge_ubo_ring_end");
        registered = true;

        api.Logger.Notification("[VGE] UBO ring registered (Before/Done)");
    }

    public override void Dispose()
    {
        base.Dispose();

        if (capi != null && registered)
        {
            try { capi.Event.UnregisterRenderer(beginRenderer, EnumRenderStage.Before); } catch { }
            try { capi.Event.UnregisterRenderer(endRenderer, EnumRenderStage.Done); } catch { }
        }

        beginRenderer = null;
        endRenderer = null;

        controller?.Dispose();
        controller = null;

        capi = null;
        registered = false;
    }
}
