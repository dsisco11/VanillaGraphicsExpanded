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
        HarmonyPatches.PbrDrawRouteHook.Api = api;
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
        HarmonyPatches.PbrDrawRouteHook.Api = null;

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


        pbrCompositeRenderer ??= new PBRCompositeRenderer(
            capi,
            gBufferManager,
            directLightingBufferManager,
            ConfigModSystem.Config,
            lumOnSystem.GetLumOnBufferManagerOrNull,
            () => PbrShaderLightingMode.LumOnEnabled);

        capi.Logger.Debug("[VGE] PbrModSystem ensured ({0})", reason);
    }
}
