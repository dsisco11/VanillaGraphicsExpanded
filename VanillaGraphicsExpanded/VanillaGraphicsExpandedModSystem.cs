using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded;

public sealed class VanillaGraphicsExpandedModSystem : ModSystem
{
    private ICoreClientAPI? capi;
    private GBufferRenderer? gBufferRenderer;
    private PBROverlayRenderer? pbrOverlayRenderer;
    private DebugOverlayRenderer? debugOverlayRenderer;

    public override void StartPre(ICoreAPI api)
    {
        base.StartPre(api);
        
        // Apply Harmony patches early, before shaders are loaded
        ShaderPatches.Apply(api);
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
            return false;
            
        pbrOverlayRenderer.Enabled = !pbrOverlayRenderer.Enabled;
        
        var status = pbrOverlayRenderer.Enabled ? "enabled" : "disabled";
        capi?.TriggerIngameError(this, "vge", $"[VGE] PBR overlay {status}");
        
        return true;
    }

    public override void Dispose()
    {
        base.Dispose();

        debugOverlayRenderer?.Dispose();
        debugOverlayRenderer = null;
        
        pbrOverlayRenderer?.Dispose();
        pbrOverlayRenderer = null;
        
        gBufferRenderer?.Dispose();
        gBufferRenderer = null;
        
        ShaderPatches.Unpatch(null!);
    }
}
