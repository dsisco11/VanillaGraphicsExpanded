using System;

using Cairo;

using VanillaGraphicsExpanded;

using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.LumOn.Diagnostics;

/// <summary>
/// Persistent in-game overlay that shows LumOn debug counters while enabled.
/// Visibility is controlled by <see cref="LumOnModSystem"/>.
/// </summary>
internal sealed class HudLumOnStatsPanel : HudElement
{
    private const int PanelWidth = 520;
    private const int PanelPadding = 6;
    private const int PanelMargin = 10;
    private const int LineHeight = 18;
    private const int Spacing = 3;

    private const string Line0Key = "line0";
    private const string Line1Key = "line1";
    private const string Line2Key = "line2";
    private const string Line3Key = "line3";
    private const string Line4Key = "line4";
    private const string Line5Key = "line5";

    private readonly LumOnModSystem lumOn;

    private long tickListenerId;

    private string[] lastLines = Array.Empty<string>();

    public HudLumOnStatsPanel(ICoreClientAPI capi, LumOnModSystem lumOn)
        : base(capi)
    {
        this.lumOn = lumOn ?? throw new ArgumentNullException(nameof(lumOn));

        tickListenerId = capi.Event.RegisterGameTickListener(OnGameTick, 200);

        Compose();
        TryClose();
    }

    public override string ToggleKeyCombinationCode => null!;

    public void Show()
    {
        TryOpen(withFocus: false);
    }

    public void Hide()
    {
        TryClose();
    }

    private void Compose()
    {
        ElementBounds bgBounds = new ElementBounds()
            .WithSizing(ElementSizing.FitToChildren)
            .WithFixedPadding(PanelPadding);

        int y = 0;
        ElementBounds line0Bounds = ElementBounds.Fixed(0, y, PanelWidth, LineHeight);
        y += LineHeight + Spacing;
        ElementBounds line1Bounds = ElementBounds.Fixed(0, y, PanelWidth, LineHeight);
        y += LineHeight + Spacing;
        ElementBounds line2Bounds = ElementBounds.Fixed(0, y, PanelWidth, LineHeight);
        y += LineHeight + Spacing;
        ElementBounds line3Bounds = ElementBounds.Fixed(0, y, PanelWidth, LineHeight);
        y += LineHeight + Spacing;
        ElementBounds line4Bounds = ElementBounds.Fixed(0, y, PanelWidth, LineHeight);
        y += LineHeight + Spacing;
        ElementBounds line5Bounds = ElementBounds.Fixed(0, y, PanelWidth, LineHeight);

        bgBounds.WithChildren(line0Bounds, line1Bounds, line2Bounds, line3Bounds, line4Bounds, line5Bounds);

        ElementBounds dialogBounds = bgBounds
            .ForkBoundingParent()
            .WithAlignment(EnumDialogArea.RightTop)
            .WithFixedPosition(-PanelMargin, PanelMargin);

        SingleComposer?.Dispose();
        SingleComposer = capi.Gui
            .CreateCompo("vge-lumon-stats", dialogBounds)
            .AddGameOverlay(bgBounds, GuiStyle.DialogLightBgColor)
            .AddDynamicText("LumOn: (initializing)", CairoFont.WhiteSmallText(), line0Bounds, Line0Key)
            .AddDynamicText(string.Empty, CairoFont.WhiteSmallText(), line1Bounds, Line1Key)
            .AddDynamicText(string.Empty, CairoFont.WhiteSmallText(), line2Bounds, Line2Key)
            .AddDynamicText(string.Empty, CairoFont.WhiteSmallText(), line3Bounds, Line3Key)
            .AddDynamicText(string.Empty, CairoFont.WhiteSmallText(), line4Bounds, Line4Key)
            .AddDynamicText(string.Empty, CairoFont.WhiteSmallText(), line5Bounds, Line5Key)
            .Compose();
    }

    private void OnGameTick(float dt)
    {
        if (!IsOpened() || SingleComposer is null)
        {
            return;
        }

        string[] lines = lumOn.GetLumOnStatsOverlayLines();
        if (AreSameLines(lastLines, lines))
        {
            return;
        }

        lastLines = lines;

        SingleComposer.GetDynamicText(Line0Key).SetNewText(GetLine(lines, 0));
        SingleComposer.GetDynamicText(Line1Key).SetNewText(GetLine(lines, 1));
        SingleComposer.GetDynamicText(Line2Key).SetNewText(GetLine(lines, 2));
        SingleComposer.GetDynamicText(Line3Key).SetNewText(GetLine(lines, 3));
        SingleComposer.GetDynamicText(Line4Key).SetNewText(GetLine(lines, 4));
        SingleComposer.GetDynamicText(Line5Key).SetNewText(GetLine(lines, 5));
    }

    private static bool AreSameLines(string[] a, string[] b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++)
        {
            if (!string.Equals(a[i], b[i], StringComparison.Ordinal)) return false;
        }
        return true;
    }

    private static string GetLine(string[] lines, int index)
        => (uint)index < (uint)lines.Length ? (lines[index] ?? string.Empty) : string.Empty;

    public override void Dispose()
    {
        base.Dispose();

        if (tickListenerId != 0)
        {
            capi.Event.UnregisterGameTickListener(tickListenerId);
            tickListenerId = 0;
        }

        SingleComposer?.Dispose();
        SingleComposer = null;
    }
}
