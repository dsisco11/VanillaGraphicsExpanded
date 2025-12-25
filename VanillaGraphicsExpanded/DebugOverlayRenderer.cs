using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded;

/// <summary>
/// Renders a small debug overlay at the center of the screen showing various G-buffer visualizations.
/// Inherits from PBROverlayRenderer to reuse shader setup and rendering logic.
/// </summary>
public sealed class DebugOverlayRenderer : PBROverlayRenderer
{
    #region Constants

    private const double DEBUG_RENDER_ORDER = 0.96; // After PBR overlay
    private const int DEBUG_MODE_COUNT = 7;
    
    // Overlay size (normalized 0-1, converted to NDC)
    private const float OVERLAY_SIZE = 0.25f; // 25% of screen

    #endregion

    #region Fields

    private int debugMode = 1;
    private bool isEnabled;

    #endregion

    #region Properties

    public override double RenderOrder => DEBUG_RENDER_ORDER;

    #endregion

    #region Constructor

    public DebugOverlayRenderer(ICoreClientAPI capi, GBufferRenderer gBufferRenderer)
        : base(capi, gBufferRenderer,
            quadLeft: -OVERLAY_SIZE,      // Center horizontally: -0.25 to +0.25
            quadBottom: -OVERLAY_SIZE,    // Center vertically: -0.25 to +0.25
            quadSize: OVERLAY_SIZE * 2,   // Total size: 0.5 in NDC (25% of screen)
            renderStageName: "debugoverlay")
    {
        // Register hotkey for debug mode cycling (F6 forward, Shift+F6 backward)
        capi.Input.RegisterHotKey(
            "vgedebugoverlay",
            "VGE Debug Overlay",
            GlKeys.F6,
            HotkeyType.DevTool);
        capi.Input.SetHotKeyHandler("vgedebugoverlay", OnDebugHotkey);

        capi.Input.RegisterHotKey(
            "vgedebugoverlayprev",
            "VGE Debug Overlay (Previous)",
            GlKeys.F6,
            HotkeyType.DevTool,
            shiftPressed: true);
        capi.Input.SetHotKeyHandler("vgedebugoverlayprev", OnDebugHotkeyPrev);
    }

    #endregion

    #region Virtual Overrides

    protected override bool ShouldRender() => isEnabled;

    protected override int GetDebugMode() => debugMode;

    #endregion

    #region Hotkey Handling

    private bool OnDebugHotkey(KeyCombination keyCombination)
    {
        if (!isEnabled)
        {
            isEnabled = true;
            debugMode = 1; // Start at 1 (Normals), 0 is PBR output
        }
        else
        {
            debugMode++;
            if (debugMode >= DEBUG_MODE_COUNT)
            {
                isEnabled = false;
                debugMode = 1;
            }
        }

        ShowDebugModeMessage();
        return true;
    }

    private bool OnDebugHotkeyPrev(KeyCombination keyCombination)
    {
        if (!isEnabled)
        {
            isEnabled = true;
            debugMode = DEBUG_MODE_COUNT - 1; // Start at last mode when cycling backwards
        }
        else
        {
            debugMode--;
            if (debugMode < 1)
            {
                isEnabled = false;
                debugMode = 1;
            }
        }

        ShowDebugModeMessage();
        return true;
    }

    private void ShowDebugModeMessage()
    {
        if (isEnabled)
        {
            // Debug modes match pbroverlay.fsh: 1=normals (blurred), 2=roughness, 3=metallic, 4=worldPos, 5=depth, 6=normals (raw)
            string modeName = debugMode switch
            {
                1 => "Normals (Blurred)",
                2 => "Roughness",
                3 => "Metallic",
                4 => "World Position",
                5 => "Depth",
                6 => "Normals (Raw)",
                _ => "Unknown"
            };
            capi.TriggerIngameError(this, "vgedebug", $"Debug: {modeName}");
        }
        else
        {
            capi.TriggerIngameError(this, "vgedebug", "Debug overlay disabled");
        }
    }

    #endregion
}
