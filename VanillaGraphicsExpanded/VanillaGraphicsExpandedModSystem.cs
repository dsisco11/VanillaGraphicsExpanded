using VanillaGraphicsExpanded.HarmonyPatches;
using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.SSGI;

using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded;

public sealed class VanillaGraphicsExpandedModSystem : ModSystem
{
    private ICoreClientAPI? capi;
    private GBufferManager? gBufferManager;
    private SSGIBufferManager? ssgiBufferManager;
    private SSGISceneCaptureRenderer? ssgiSceneCaptureRenderer;
    private SSGIRenderer? ssgiRenderer;
    private PBROverlayRenderer? pbrOverlayRenderer;
    private HarmonyLib.Harmony? harmony;

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

        // Create SSGI buffer manager
        ssgiBufferManager = new SSGIBufferManager(api);

        // Create SSGI scene capture renderer (runs at end of Opaque stage)
        // This captures lit geometry before OIT/post-processing for SSGI to sample
        ssgiSceneCaptureRenderer = new SSGISceneCaptureRenderer(api, ssgiBufferManager);

        // Create SSGI renderer (runs at AfterBlit stage, before PBR overlay)
        ssgiRenderer = new SSGIRenderer(api, ssgiBufferManager);

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
            harmony?.UnpatchAll(Mod.Info.ModID);
            harmony = null;
        }
    }

    private static bool LoadShaders(ICoreClientAPI api)
    {
        PBROverlayShaderProgram.Register(api);
        SSGIShaderProgram.Register(api);
        SSGICompositeShaderProgram.Register(api);
        SSGIBlurShaderProgram.Register(api);
        SSGIDebugShaderProgram.Register(api);
        return true;
    }
}
