using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded;

public sealed class VanillaGraphicsExpandedModSystem : ModSystem
{
    private GBufferRenderer? gBufferRenderer;
    private PBROverlayRenderer? pbrOverlayRenderer;

    public override void StartClientSide(ICoreClientAPI api)
    {
        base.StartClientSide(api);

        // Create G-buffer renderer first (runs at Before stage)
        gBufferRenderer = new GBufferRenderer(api);
        
        // Create PBR overlay renderer (runs at AfterBlit stage)
        pbrOverlayRenderer = new PBROverlayRenderer(api, gBufferRenderer);
    }

    public override void Dispose()
    {
        base.Dispose();

        pbrOverlayRenderer?.Dispose();
        pbrOverlayRenderer = null;
        
        gBufferRenderer?.Dispose();
        gBufferRenderer = null;
    }
}
