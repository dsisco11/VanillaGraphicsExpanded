using VanillaGraphicsExpanded.HarmonyPatches;
using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.SSGI;

using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded;

public sealed class VanillaGraphicsExpandedModSystem : ModSystem
{
    private const string LumOnConfigFile = "vanillagraphicsexpanded-lumon.json";

    private ICoreClientAPI? capi;
    private GBufferManager? gBufferManager;
    private SSGIBufferManager? ssgiBufferManager;
    private SSGISceneCaptureRenderer? ssgiSceneCaptureRenderer;
    private SSGIRenderer? ssgiRenderer;
    private PBROverlayRenderer? pbrOverlayRenderer;
    private HarmonyLib.Harmony? harmony;

    // LumOn components
    private LumOnConfig? lumOnConfig;
    private LumOnBufferManager? lumOnBufferManager;
    private LumOnRenderer? lumOnRenderer;

    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Client;

    public override void StartPre(ICoreAPI api)
    {
        // Apply Harmony patches as early as possible, especially before shaders are loaded.
        harmony = new HarmonyLib.Harmony(Mod.Info.ModID);
        harmony.PatchAll();
    }

    public override void AssetsLoaded(ICoreAPI api)
    {
        // Initialize the shader includes hook with dependencies
        ShaderIncludesHook.Initialize(api.Logger, api.Assets);

        // Initialize the shader imports system to load mod's shaderincludes
        ShaderImportsSystem.Instance.Initialize(api);
    }

    public override void StartClientSide(ICoreClientAPI api)
    {
        capi = api;

        // Create G-buffer manager (Harmony hooks will call into this)
        gBufferManager = new GBufferManager(api);

        // Load LumOn configuration
        lumOnConfig = api.LoadModConfig<LumOnConfig>(LumOnConfigFile);
        if (lumOnConfig == null)
        {
            lumOnConfig = new LumOnConfig();
            api.StoreModConfig(lumOnConfig, LumOnConfigFile);
            api.Logger.Notification("[VGE] Created default LumOn config");
        }

        // Initialize LumOn or legacy SSGI based on config
        if (lumOnConfig.Enabled)
        {
            // LumOn path - new Screen Probe Gather system
            lumOnBufferManager = new LumOnBufferManager(api, lumOnConfig);
            lumOnRenderer = new LumOnRenderer(api, lumOnConfig, lumOnBufferManager, gBufferManager);
            api.Logger.Notification("[VGE] LumOn enabled - using Screen Probe Gather");
        }
        else
        {
            // Legacy SSGI path
            ssgiBufferManager = new SSGIBufferManager(api);
            ssgiSceneCaptureRenderer = new SSGISceneCaptureRenderer(api, ssgiBufferManager);
            ssgiRenderer = new SSGIRenderer(api, ssgiBufferManager);
            api.Logger.Notification("[VGE] LumOn disabled - using legacy SSGI");
        }

        // Create PBR overlay renderer (runs at AfterBlit stage)
        pbrOverlayRenderer = new DebugOverlayRenderer(api, gBufferManager);

        LoadShaders(api);
        api.Event.ReloadShader += () => LoadShaders(api);
    }

    public override void Dispose()
    {
        base.Dispose();
        try
        {
            pbrOverlayRenderer?.Dispose();
            pbrOverlayRenderer = null;

            // Dispose LumOn components
            lumOnRenderer?.Dispose();
            lumOnRenderer = null;

            lumOnBufferManager?.Dispose();
            lumOnBufferManager = null;

            // Dispose legacy SSGI components
            ssgiRenderer?.Dispose();
            ssgiRenderer = null;

            ssgiSceneCaptureRenderer?.Dispose();
            ssgiSceneCaptureRenderer = null;

            ssgiBufferManager?.Dispose();
            ssgiBufferManager = null;

            gBufferManager?.Dispose();
            gBufferManager = null;

            // Save LumOn config on dispose
            if (capi != null && lumOnConfig != null)
            {
                capi.StoreModConfig(lumOnConfig, LumOnConfigFile);
            }

            // Clear shader imports cache
            ShaderImportsSystem.Instance.Clear();
        }
        finally
        {
            // Unpatch Harmony patches
            harmony?.UnpatchAll(Mod.Info.ModID);
            harmony = null;
        }
    }

    private static bool LoadShaders(ICoreClientAPI api)
    {
        // PBR overlay shader
        PBROverlayShaderProgram.Register(api);

        // Legacy SSGI shaders
        SSGIShaderProgram.Register(api);
        SSGICompositeShaderProgram.Register(api);
        SSGIBlurShaderProgram.Register(api);
        SSGIDebugShaderProgram.Register(api);

        // LumOn shaders
        LumOnProbeAnchorShaderProgram.Register(api);
        LumOnProbeTraceShaderProgram.Register(api);
        LumOnTemporalShaderProgram.Register(api);
        LumOnGatherShaderProgram.Register(api);
        LumOnUpsampleShaderProgram.Register(api);

        return true;
    }
}
