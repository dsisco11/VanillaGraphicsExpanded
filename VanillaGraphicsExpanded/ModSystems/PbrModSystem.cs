using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.PBR;

using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.ModSystems;

public sealed class PbrModSystem : ModSystem
{
    private ICoreClientAPI? capi;
    private GBufferManager? gBufferManager;

    private DirectLightingBufferManager? directLightingBufferManager;
    private DirectLightingRenderer? directLightingRenderer;
    private PBRCompositeRenderer? pbrCompositeRenderer;

    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Client;

    public override void StartClientSide(ICoreClientAPI api)
    {
        capi = api;
        ConfigModSystem.Config.Sanitize();

        EnsureInitializedIfReady("startup");
    }

    internal void SetDependencies(ICoreClientAPI api, GBufferManager gBufferManager)
    {
        capi ??= api;
        this.gBufferManager = gBufferManager;

        EnsureInitializedIfReady("dependencies ready");
    }

    public override void Dispose()
    {
        base.Dispose();

        directLightingRenderer?.Dispose();
        directLightingRenderer = null;

        directLightingBufferManager?.Dispose();
        directLightingBufferManager = null;

        pbrCompositeRenderer?.Dispose();
        pbrCompositeRenderer = null;

        gBufferManager = null;
        capi = null;
    }

    private void EnsureInitializedIfReady(string reason)
    {
        if (capi is null || gBufferManager is null)
        {
            return;
        }

        directLightingBufferManager ??= new DirectLightingBufferManager(capi);
        directLightingRenderer ??= new DirectLightingRenderer(capi, gBufferManager, directLightingBufferManager);

        var lumOnSystem = capi.ModLoader.GetModSystem<LumOnModSystem>();
        lumOnSystem.SetDependencies(capi, gBufferManager, directLightingBufferManager);
        var lumOnBuffers = lumOnSystem.GetLumOnBufferManagerOrNull();

        pbrCompositeRenderer ??= new PBRCompositeRenderer(
            capi,
            gBufferManager,
            directLightingBufferManager,
            ConfigModSystem.Config,
            lumOnBuffers);

        capi.Logger.Debug("[VGE] PbrModSystem ensured ({0})", reason);
    }
}
