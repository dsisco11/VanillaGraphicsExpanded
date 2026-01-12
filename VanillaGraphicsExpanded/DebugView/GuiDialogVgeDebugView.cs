using System;
using System.Linq;

using VanillaGraphicsExpanded.LumOn;

using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.DebugView;

public sealed class GuiDialogVgeDebugView : GuiDialog
{
    private readonly string[] lumonValues;
    private readonly string[] lumonNames;

    public GuiDialogVgeDebugView(ICoreClientAPI capi)
        : base(capi)
    {
        var lumonModes = Enum.GetValues(typeof(LumOnDebugMode)).Cast<LumOnDebugMode>().ToArray();
        lumonValues = lumonModes.Select(m => m.ToString()).ToArray();
        lumonNames = lumonModes.Select(GetLumOnDebugModeDisplayName).ToArray();

        Compose();
    }

    public override string ToggleKeyCombinationCode => null!;

    public override void OnGuiOpened()
    {
        base.OnGuiOpened();
        RefreshFromRuntime();

        // Ensure keyboard navigation works immediately (Up/Down cycles selection).
        SingleComposer?.FocusElement(0);
    }

    private void Compose()
    {
        ElementBounds bgBounds = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
        bgBounds.BothSizing = ElementSizing.FitToChildren;

        ElementBounds dialogBounds = ElementStdBounds
            .AutosizedMainDialog
            .WithAlignment(EnumDialogArea.CenterMiddle)
            .WithFixedAlignmentOffset(GuiStyle.DialogToScreenPadding, 0);

        var rowY = 30.0;
        const double rowH = 30.0;
        const double labelW = 140.0;
        const double controlW = 260.0;

        ElementBounds Label(double y) => ElementBounds.Fixed(0, y, labelW, rowH);
        ElementBounds Control(double y) => ElementBounds.Fixed(labelW + 10, y, controlW, rowH);

        var fontLabel = CairoFont.WhiteDetailText();

        SingleComposer = capi.Gui
            .CreateCompo("vge-debug-view", dialogBounds)
            .AddShadedDialogBG(bgBounds, true)
            .AddDialogTitleBar("VGE Debug View", OnTitleBarClose)
            .BeginChildElements(bgBounds)
                .AddStaticText("Debug mode", fontLabel, Label(rowY))
                .AddInteractiveElement(
                    new GuiElementDropDownCycleOnArrow(
                        capi,
                        lumonValues,
                        lumonNames,
                        GetLumOnSelectedIndex(lumonValues, VgeDebugViewManager.GetMode()),
                        OnLumOnModeChanged,
                        Control(rowY),
                        CairoFont.WhiteSmallText()
                    ),
                    "lumonmode")
            .EndChildElements()
            .Compose();

        RefreshFromRuntime();
    }

    private void RefreshFromRuntime()
    {
        if (SingleComposer is null)
        {
            return;
        }

        var current = VgeDebugViewManager.GetMode();
        SingleComposer.GetDropDown("lumonmode").SetSelectedIndex(GetLumOnSelectedIndex(lumonValues, current));
    }

    private void OnTitleBarClose()
    {
        TryClose();
    }

    private void OnLumOnModeChanged(string code, bool selected)
    {
        if (!selected)
        {
            return;
        }

        if (Enum.TryParse(code, out LumOnDebugMode mode))
        {
            // Selecting any non-Off mode implies preview on.
            VgeDebugViewManager.SetMode(mode);
        }
    }

    private static int GetLumOnSelectedIndex(string[] values, LumOnDebugMode current)
    {
        int idx = Array.IndexOf(values, current.ToString());
        return idx < 0 ? 0 : idx;
    }

    private static string GetLumOnDebugModeDisplayName(LumOnDebugMode mode) => mode switch
    {
        LumOnDebugMode.Off => "Off (normal)",
        LumOnDebugMode.ProbeGrid => "Probe Grid",
        LumOnDebugMode.ProbeDepth => "Probe Depth",
        LumOnDebugMode.ProbeNormal => "Probe Normals",
        LumOnDebugMode.SceneDepth => "Scene Depth",
        LumOnDebugMode.SceneNormal => "Scene Normals",
        LumOnDebugMode.TemporalWeight => "Temporal Weight",
        LumOnDebugMode.TemporalRejection => "Temporal Rejection",
        LumOnDebugMode.ShCoefficients => "SH Coefficients",
        LumOnDebugMode.InterpolationWeights => "Interpolation Weights",
        LumOnDebugMode.RadianceOverlay => "Radiance Overlay",
        LumOnDebugMode.GatherWeight => "Gather Weight (diagnostic)",
        LumOnDebugMode.ProbeAtlasMetaConfidence => "Probe-Atlas Meta Confidence",
        LumOnDebugMode.ProbeAtlasTemporalAlpha => "Probe-Atlas Temporal Alpha",
        LumOnDebugMode.ProbeAtlasMetaFlags => "Probe-Atlas Meta Flags",
        LumOnDebugMode.ProbeAtlasFilteredRadiance => "Probe-Atlas Filtered Radiance",
        LumOnDebugMode.ProbeAtlasFilterDelta => "Probe-Atlas Filter Delta",
        LumOnDebugMode.ProbeAtlasGatherInputSource => "Probe-Atlas Gather Input Source",
        LumOnDebugMode.CompositeAO => "Composite AO (Phase 15)",
        LumOnDebugMode.CompositeIndirectDiffuse => "Composite Indirect Diffuse (Phase 15)",
        LumOnDebugMode.CompositeIndirectSpecular => "Composite Indirect Specular (Phase 15)",
        LumOnDebugMode.CompositeMaterial => "Composite Material (Phase 15)",
        LumOnDebugMode.DirectDiffuse => "DirectDiffuse (Phase 16)",
        LumOnDebugMode.DirectSpecular => "DirectSpecular (Phase 16)",
        LumOnDebugMode.DirectEmissive => "Direct Emissive (Phase 16)",
        LumOnDebugMode.DirectTotal => "Direct Total (diffuse+spec) (Phase 16)",
        _ => mode.ToString()
    };
}
