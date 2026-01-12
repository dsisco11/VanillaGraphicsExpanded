using VanillaGraphicsExpanded.HarmonyPatches;
using VanillaGraphicsExpanded.DebugView;
using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.ModSystems;
using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.Rendering.Profiling;
using VanillaGraphicsExpanded.SSGI;

using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.Client.NoObf;
using Microsoft.VisualBasic;

namespace VanillaGraphicsExpanded;

public sealed class VanillaGraphicsExpandedModSystem : ModSystem
{
    private ICoreClientAPI? capi;
    private GBufferManager? gBufferManager;
    private SSGIBufferManager? ssgiBufferManager;
    private SSGISceneCaptureRenderer? ssgiSceneCaptureRenderer;
    private SSGIRenderer? ssgiRenderer;
    private DirectLightingBufferManager? directLightingBufferManager;
    private DirectLightingRenderer? directLightingRenderer;
    private PBRCompositeRenderer? pbrCompositeRenderer;
    private GlGpuProfilerRenderer? gpuProfilerRenderer;
    private HarmonyLib.Harmony? harmony;

    // LumOn components
    private LumOnBufferManager? lumOnBufferManager;
    private LumOnRenderer? lumOnRenderer;
    private LumOnDebugRenderer? lumOnDebugRenderer;

    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Client;

    public override void StartPre(ICoreAPI api)
    {
        // Apply Harmony patches as early as possible, especially before shaders are loaded.
        harmony = new HarmonyLib.Harmony(Constants.ModId);
        harmony.PatchAll();
    }

    public override void AssetsLoaded(ICoreAPI api)
    {
        // Initialize the shader includes hook with dependencies
        ShaderIncludesHook.Initialize(api.Logger, api.Assets);

        // Initialize the shader imports system to load mod shader imports (shaders/includes)
        ShaderImportsSystem.Instance.Initialize(api);
    }

    public override void StartClientSide(ICoreClientAPI api)
    {
        capi = api;

        GlGpuProfiler.Instance.Initialize(api);
        gpuProfilerRenderer = new GlGpuProfilerRenderer(api);

        // Single, always-available debug view entry point.
        api.Input.RegisterHotKey(
            "vgedebugview",
            "VGE Debug View",
            GlKeys.F6,
            HotkeyType.DevTool);
        api.Input.SetHotKeyHandler("vgedebugview", VgeDebugViewManager.ToggleDialog);

        // Create G-buffer manager (Harmony hooks will call into this)
        gBufferManager = new GBufferManager(api);

        // Initialize LumOn based on config (loaded by ConfigModSystem).
        // Note: Legacy SSGI is intentionally not initialized while the new
        // direct-lighting + fog composite pipeline is being integrated.
        if (ConfigModSystem.Config.Enabled)
        {
            lumOnBufferManager = new LumOnBufferManager(api, ConfigModSystem.Config);
            lumOnRenderer = new LumOnRenderer(api, ConfigModSystem.Config, lumOnBufferManager, gBufferManager);
            api.Logger.Notification("[VGE] LumOn enabled - using Screen Probe Gather");
        }
        else
        {
            api.Logger.Notification("[VGE] LumOn disabled - direct lighting only (SSGI disabled)");
        }

        // Create direct lighting pass buffers + renderer (Opaque @ 9.0)
        directLightingBufferManager = new DirectLightingBufferManager(api);
        directLightingRenderer = new DirectLightingRenderer(api, gBufferManager, directLightingBufferManager);

        // Unified debug overlay (AfterBlit) driven by the VGE Debug View dialog.
        lumOnDebugRenderer = new LumOnDebugRenderer(api, ConfigModSystem.Config, lumOnBufferManager, gBufferManager, directLightingBufferManager);

        // Final composite (Opaque @ 11.0): direct + optional indirect + fog
        pbrCompositeRenderer = new PBRCompositeRenderer(
            api,
            gBufferManager,
            directLightingBufferManager,
            ConfigModSystem.Config,
            lumOnBufferManager);

        // Initialize the debug view manager (GUI)
        VgeDebugViewManager.Initialize(
            api,
            ConfigModSystem.Config);

        LoadShaders(api);
        api.Event.ReloadShader += () => LoadShaders(api);
    }

    public override void Dispose()
    {
        base.Dispose();
        try
        {
            VgeDebugViewManager.Dispose();

            gpuProfilerRenderer?.Dispose();
            gpuProfilerRenderer = null;

            GlGpuProfiler.Instance.Dispose();

            directLightingRenderer?.Dispose();
            directLightingRenderer = null;

            directLightingBufferManager?.Dispose();
            directLightingBufferManager = null;

            pbrCompositeRenderer?.Dispose();
            pbrCompositeRenderer = null;

            // Dispose LumOn components
            lumOnDebugRenderer?.Dispose();
            lumOnDebugRenderer = null;

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

            // Clear shader imports cache
            ShaderImportsSystem.Instance.Clear();
        }
        finally
        {
            // Unpatch Harmony patches
            harmony?.UnpatchAll(Constants.ModId);
            harmony = null;
        }
    }

    private static bool LoadShaders(ICoreClientAPI api)
    {
        // PBR direct lighting shader
        PBRDirectLightingShaderProgram.Register(api);

        // PBR final composite shader
        PBRCompositeShaderProgram.Register(api);

        // Legacy SSGI shaders
        SSGIShaderProgram.Register(api);
        SSGICompositeShaderProgram.Register(api);
        SSGIBlurShaderProgram.Register(api);
        SSGIDebugShaderProgram.Register(api);

        // LumOn shaders
        LumOnProbeAnchorShaderProgram.Register(api);
        LumOnHzbCopyShaderProgram.Register(api);
        LumOnHzbDownsampleShaderProgram.Register(api);
        LumOnProbeTraceShaderProgram.Register(api);
        LumOnScreenProbeAtlasTraceShaderProgram.Register(api);
        LumOnTemporalShaderProgram.Register(api);
        LumOnScreenProbeAtlasTemporalShaderProgram.Register(api);
        LumOnScreenProbeAtlasFilterShaderProgram.Register(api);
        LumOnScreenProbeAtlasProjectSh9ShaderProgram.Register(api);
        LumOnGatherShaderProgram.Register(api);
        LumOnProbeSh9GatherShaderProgram.Register(api);
        LumOnScreenProbeAtlasGatherShaderProgram.Register(api);
        LumOnUpsampleShaderProgram.Register(api);
        LumOnCombineShaderProgram.Register(api);
        LumOnDebugShaderProgram.Register(api);

        return true;
    }
}
