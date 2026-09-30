using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.PBR;

using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.ModSystems;

public sealed class PbrModSystem : ModSystem, IRenderer
{
    public double RenderOrder => -.6;
    public int RenderRange => 1;

    /// <summary>Captures a common subdivision metric before shadow and main rendering.</summary>
    public void OnRenderFrame(float deltaTime, EnumRenderStage stage) => PBR.Tessellation.TerrainDisplacementRuntime.CaptureView();
    private ICoreClientAPI? capi;
    private GBufferManager? gBufferManager;

    private DirectLightingBufferManager? directLightingBufferManager;
    private DirectLightingRenderer? directLightingRenderer;
    private PBRCompositeRenderer? pbrCompositeRenderer;
    private PBR.Liquids.LiquidRenderer? liquidRenderer;

    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Client;

    public override void StartClientSide(ICoreClientAPI api)
    {
        capi = api;
        PBR.Tessellation.TerrainDisplacementRuntime.Api = api;
        api.Event.RegisterRenderer(this, EnumRenderStage.Before, "vge_displacement_view");
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
        capi?.Event.UnregisterRenderer(this, EnumRenderStage.Before);
        PBR.Tessellation.TerrainDisplacementRuntime.Api = null;
        PBR.Tessellation.TerrainDisplacementRuntime.ResetHistory();
        base.Dispose();
        HarmonyPatches.PbrDrawRouteHook.Api = null;

        liquidRenderer?.Dispose();
        liquidRenderer = null;
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

        liquidRenderer ??= new PBR.Liquids.LiquidRenderer(capi);

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
