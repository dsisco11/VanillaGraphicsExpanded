using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.Rendering;

using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.ModSystems;

/// <summary>Composes shared PBR renderers and their client resource lifetimes.</summary>
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
    private PBR.Liquids.LiquidDepthRenderer? liquidDepthRenderer;
    private PBR.Liquids.WaterVolumeRenderer? waterVolumeRenderer;
    private PBR.Liquids.WaterRefractionCapture? waterRefractionCapture;
    private PBR.SceneColor.SceneColorParticleCapture? particleCapture;
    private PBR.Atmosphere.AtmosphereSkyRenderer? skyRenderer;
    private PBR.Atmosphere.Celestial.CelestialRenderer? nightSky;
    private PBR.Atmosphere.Celestial.MoonRenderer? moon;
    private PBR.Atmosphere.Celestial.SunRenderer? sun;
    private PBR.SceneColor.SceneColorPipeline? sceneColor;
    private PBR.CameraExposure.CameraExposureRenderer? cameraExposure;
    private PBR.SceneColor.SceneLightingPublicationRenderer? sceneLighting;
    private PBR.Postprocessing.PostprocessPipeline? postprocessing;
    private PBR.Postprocessing.LightShaftOcclusionRenderer? lightShaftOcclusion;
    private PBR.Postprocessing.AmbientOcclusionRenderer? ambientOcclusion;
    private DepthHierarchyRenderer? depthHierarchy;
    private DepthHierarchyEnablement? depthHierarchyEnablement;
    private PipelineEnablement? pipelineEnablement;

    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Client;

    public override void StartClientSide(ICoreClientAPI api)
    {
        capi = api;
        PBR.Atmosphere.Celestial.NativeNightSkyDrawRegistration.Disable(api);
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

    /// <summary>Withdraws engine admission and attempts every independent renderer before shared buffers.</summary>
    public override void Dispose()
    {
        var api = capi; capi = null; gBufferManager = null;
        PBR.Tessellation.TerrainDisplacementRuntime.Api = null;
        PBR.Tessellation.TerrainDisplacementRuntime.ResetHistory();
        HarmonyPatches.PbrDrawRouteHook.Api = null;
        Rendering.Integration.RenderingCleanup.Run(
            () => api?.Event.UnregisterRenderer(this, EnumRenderStage.Before),
            () => { var owner = ambientOcclusion; ambientOcclusion = null; owner?.Dispose(); },
            () => { var owner = depthHierarchyEnablement; depthHierarchyEnablement = null; owner?.Dispose(); },
            () => { var owner = depthHierarchy; depthHierarchy = null; owner?.Dispose(); },
            () => { var owner = lightShaftOcclusion; lightShaftOcclusion = null; owner?.Dispose(); },
            () => { var owner = postprocessing; postprocessing = null; owner?.Dispose(); },
            () => { var owner = sceneLighting; sceneLighting = null; owner?.Dispose(); },
            () => { var owner = cameraExposure; cameraExposure = null; owner?.Dispose(); },
            () => { var owner = pipelineEnablement; pipelineEnablement = null; owner?.Dispose(); },
            () => { var owner = sceneColor; sceneColor = null; owner?.Dispose(); },
            () => { var owner = liquidRenderer; liquidRenderer = null; owner?.Dispose(); },
            () => { var owner = skyRenderer; skyRenderer = null; owner?.Dispose(); },
            () => { var owner = nightSky; nightSky = null; owner?.Dispose(); },
            () => { var owner = moon; moon = null; owner?.Dispose(); },
            () => { var owner = sun; sun = null; owner?.Dispose(); },
            () => { var owner = particleCapture; particleCapture = null; owner?.Dispose(); },
            () => { var owner = waterRefractionCapture; waterRefractionCapture = null; owner?.Dispose(); },
            () => { var owner = waterVolumeRenderer; waterVolumeRenderer = null; owner?.Dispose(); },
            () => { var owner = liquidDepthRenderer; liquidDepthRenderer = null; owner?.Dispose(); },
            () => { var owner = directLightingRenderer; directLightingRenderer = null; owner?.Dispose(); },
            () => { var owner = pbrCompositeRenderer; pbrCompositeRenderer = null; owner?.Dispose(); },
            () => { var owner = directLightingBufferManager; directLightingBufferManager = null; owner?.Dispose(); },
            () => PBR.Materials.Energy.MicrofacetEnergyLookup.Retire(),
            () => base.Dispose());
    }

    private void EnsureInitializedIfReady(string reason)
    {
        if (capi is null || gBufferManager is null)
        {
            return;
        }

        PBR.Materials.Energy.MicrofacetEnergyLookup.Prepare();
        directLightingBufferManager ??= new DirectLightingBufferManager(capi);
        skyRenderer ??= new PBR.Atmosphere.AtmosphereSkyRenderer(capi, gBufferManager);
        nightSky ??= new PBR.Atmosphere.Celestial.CelestialRenderer(capi, gBufferManager);
        moon ??= new PBR.Atmosphere.Celestial.MoonRenderer(capi, gBufferManager);
        sun ??= new PBR.Atmosphere.Celestial.SunRenderer(capi, gBufferManager);
        particleCapture ??= new PBR.SceneColor.SceneColorParticleCapture(capi, gBufferManager);
        directLightingRenderer ??= new DirectLightingRenderer(capi, gBufferManager, directLightingBufferManager);

        liquidDepthRenderer ??= new PBR.Liquids.LiquidDepthRenderer(capi);
        liquidRenderer ??= new PBR.Liquids.LiquidRenderer(capi, () => pbrCompositeRenderer?.RefractionScene);
        waterVolumeRenderer ??= new PBR.Liquids.WaterVolumeRenderer(capi);

        var lumOnSystem = capi.ModLoader.GetModSystem<LumOnModSystem>();
        lumOnSystem.SetDependencies(capi, gBufferManager, directLightingBufferManager);


        pbrCompositeRenderer ??= new PBRCompositeRenderer(
            capi,
            gBufferManager,
            directLightingBufferManager,
            ConfigModSystem.Config,
            lumOnSystem.GetLumOnBufferManagerOrNull,
            () => PbrShaderLightingMode.LumOnEnabled);
        sceneColor ??= new PBR.SceneColor.SceneColorPipeline(capi, pbrCompositeRenderer,
            directLightingRenderer, directLightingBufferManager, skyRenderer, particleCapture);
        pipelineEnablement ??= new PipelineEnablement(capi);
        cameraExposure ??= new PBR.CameraExposure.CameraExposureRenderer(capi,pipelineEnablement);
        sceneLighting ??= new PBR.SceneColor.SceneLightingPublicationRenderer(capi);
        postprocessing ??= new PBR.Postprocessing.PostprocessPipeline(capi, pipelineEnablement);
        lightShaftOcclusion ??= new PBR.Postprocessing.LightShaftOcclusionRenderer(capi, pipelineEnablement);
        depthHierarchy ??= new DepthHierarchyRenderer(capi);
        depthHierarchyEnablement ??= new DepthHierarchyEnablement(pipelineEnablement,depthHierarchy);
        ambientOcclusion ??= new PBR.Postprocessing.AmbientOcclusionRenderer(capi, gBufferManager,pipelineEnablement);
        waterRefractionCapture ??= new PBR.Liquids.WaterRefractionCapture(capi, directLightingRenderer, pbrCompositeRenderer);

        capi.Logger.Debug("[VGE] PbrModSystem ensured ({0})", reason);
    }
}
