using VanillaGraphicsExpanded.HarmonyPatches;
using VanillaGraphicsExpanded.PBR;

using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded;

public sealed class VanillaGraphicsExpandedModSystem : ModSystem
{
    private ICoreClientAPI? capi;
    private GBufferManager? gBufferManager;
    private PBROverlayRenderer? pbrOverlayRenderer;
    private DebugOverlayRenderer? debugOverlayRenderer;
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

        // Create PBR overlay renderer (runs at AfterBlit stage)
        pbrOverlayRenderer = new PBROverlayRenderer(api, gBufferManager);

        // Create debug overlay renderer (runs after PBR overlay)
        debugOverlayRenderer = new DebugOverlayRenderer(api, gBufferManager);
        // Load custom shaders
        LoadShaders(api);
        api.Event.ReloadShader += () => LoadShaders(api);
    }

    public override void Dispose()
    {
        base.Dispose();
        try
        {
            debugOverlayRenderer?.Dispose();
            debugOverlayRenderer = null;

            pbrOverlayRenderer?.Dispose();
            pbrOverlayRenderer = null;

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
        return true;
    }
}
