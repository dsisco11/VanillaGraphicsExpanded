using VanillaGraphicsExpanded.DebugView;

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
    private const int DEBUG_MODE_COUNT = 9; // 1-8 are valid debug modes
    
    // Overlay size (normalized 0-1, converted to NDC)
    private const float OVERLAY_SIZE = 0.25f; // 25% of screen

    #endregion

    #region Fields

    private int debugMode = 1;
    private bool isEnabled;

    #endregion

    #region Properties

    public override double RenderOrder => DEBUG_RENDER_ORDER;

    public bool DebugOverlayEnabled
    {
        get => isEnabled;
        set => isEnabled = value;
    }

    public int DebugOverlayMode
    {
        get => debugMode;
        set => debugMode = Clamp(value, 1, DEBUG_MODE_COUNT - 1);
    }

    #endregion

    #region Constructor

    public DebugOverlayRenderer(ICoreClientAPI capi, GBufferManager gBufferManager)
        : base(capi, gBufferManager,
            quadLeft: -OVERLAY_SIZE,      // Center horizontally: -0.25 to +0.25
            quadBottom: -OVERLAY_SIZE,    // Center vertically: -0.25 to +0.25
            quadSize: OVERLAY_SIZE * 2,   // Total size: 0.5 in NDC (25% of screen)
            renderStageName: "debugoverlay")
    {
        // Phase 19: Repurpose the old "cycle debug" hotkey to open the debug view GUI.
        capi.Input.RegisterHotKey(
            "vgedebugoverlay",
            "VGE Debug View",
            GlKeys.F6,
            HotkeyType.DevTool);
        capi.Input.SetHotKeyHandler("vgedebugoverlay", VgeDebugViewManager.ToggleDialog);
    }

    #endregion

    #region Virtual Overrides

    protected override bool ShouldRender() => isEnabled;

    protected override int GetDebugMode() => debugMode;

    #endregion

    private static int Clamp(int value, int minInclusive, int maxInclusive)
    {
        if (value < minInclusive) return minInclusive;
        if (value > maxInclusive) return maxInclusive;
        return value;
    }
}
