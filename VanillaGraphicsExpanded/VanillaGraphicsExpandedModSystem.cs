using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded;

public sealed class VanillaGraphicsExpandedModSystem : ModSystem
{
    private PBROverlayRenderer? pbrOverlayRenderer;

    public override void StartClientSide(ICoreClientAPI api)
    {
        base.StartClientSide(api);

        pbrOverlayRenderer = new PBROverlayRenderer(api);
    }

    public override void Dispose()
    {
        base.Dispose();

        pbrOverlayRenderer?.Dispose();
        pbrOverlayRenderer = null;
    }
}
