using VanillaGraphicsExpanded.Harmony;
using VanillaGraphicsExpanded.PBR;

using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded;

public sealed class VanillaGraphicsExpandedModSystem : ModSystem
{
    private ICoreClientAPI? capi;
    private GBufferRenderer? gBufferRenderer;
    private PBROverlayRenderer? pbrOverlayRenderer;
    private DebugOverlayRenderer? debugOverlayRenderer;
    private HarmonyLib.Harmony? harmony;

    public override void StartPre(ICoreAPI api)
    {
        // Apply Harmony patches as early as possible, especially before shaders are loaded.
        harmony = new HarmonyLib.Harmony(Mod.Info.ModID);
        harmony.PatchAll();

        // Initialize the shader includes hook with dependencies
        ShaderIncludesHook.Initialize(api.Logger, api.Assets);
    }

    public override void AssetsLoaded(ICoreAPI api)
    {
        base.AssetsLoaded(api);

        // Initialize the shader imports system to load mod's shaderincludes
        ShaderImportsSystem.Instance.Initialize(api);
    }

    public override void StartClientSide(ICoreClientAPI api)
    {
        base.StartClientSide(api);
        capi = api;

        // Create G-buffer renderer first (runs at Before stage)
        gBufferRenderer = new GBufferRenderer(api);

        // Create PBR overlay renderer (runs at AfterBlit stage)
        pbrOverlayRenderer = new PBROverlayRenderer(api, gBufferRenderer);

        // Create debug overlay renderer (runs after PBR overlay)
        debugOverlayRenderer = new DebugOverlayRenderer(api, gBufferRenderer);

        // Register hotkey to toggle PBR overlay (F7)
        api.Input.RegisterHotKey(
            "vgetoggle",
            "VGE Toggle PBR Overlay",
            GlKeys.F7,
            HotkeyType.DevTool);
        api.Input.SetHotKeyHandler("vgetoggle", OnTogglePbrOverlay);
    }

    private bool OnTogglePbrOverlay(KeyCombination keyCombination)
    {
        if (pbrOverlayRenderer is null)
        {
            return false;
        }

        pbrOverlayRenderer.Enabled = !pbrOverlayRenderer.Enabled;

        string status = pbrOverlayRenderer.Enabled ? "enabled" : "disabled";
        capi?.TriggerIngameError(this, "vge", $"[VGE] PBR overlay {status}");

        return true;
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

            gBufferRenderer?.Dispose();
            gBufferRenderer = null;

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
}
