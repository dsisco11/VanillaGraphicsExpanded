using System;
using System.Linq;

using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.Rendering.Profiling;

using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.DebugView;

public sealed class GuiDialogVgeDebugView : GuiDialog
{
    private readonly string[] lumonValues;
    private readonly string[] lumonNames;

    private readonly string[] profilerEnabledValues = ["On", "Off"];
    private readonly string[] profilerEnabledNames = ["On", "Off"];

    private readonly string[] profilerCategoryValues = ["All", "PBR", "LumOn", "Debug"];
    private readonly string[] profilerCategoryNames = ["All", "PBR", "LumOn", "Debug"];

    private readonly string[] profilerSortValues = ["Name", "LastMs", "AvgMs", "MaxMs"];
    private readonly string[] profilerSortNames = ["Name", "Last", "Avg", "Max"];

    private long profilerTickListenerId;

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

        profilerTickListenerId = capi.Event.RegisterGameTickListener(_ => RefreshProfilerText(), 200);

        // Ensure keyboard navigation works immediately (Up/Down cycles selection).
        SingleComposer?.FocusElement(0);
    }

    public override void OnGuiClosed()
    {
        base.OnGuiClosed();

        if (profilerTickListenerId != 0)
        {
            capi.Event.UnregisterGameTickListener(profilerTickListenerId);
            profilerTickListenerId = 0;
        }
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
        var fontSmall = CairoFont.WhiteSmallText();

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

                // GPU profiler
                .AddStaticText("GPU profiler", fontLabel, Label(rowY += 40))
                .AddInteractiveElement(
                    new GuiElementDropDownCycleOnArrow(
                        capi,
                        profilerEnabledValues,
                        profilerEnabledNames,
                        GlGpuProfiler.Instance.Enabled ? 0 : 1,
                        OnProfilerEnabledChanged,
                        Control(rowY),
                        fontSmall
                    ),
                    "profilerEnabled")

                .AddStaticText("GPU category", fontLabel, Label(rowY += 30))
                .AddInteractiveElement(
                    new GuiElementDropDownCycleOnArrow(
                        capi,
                        profilerCategoryValues,
                        profilerCategoryNames,
                        0,
                        OnProfilerSelectionChanged,
                        Control(rowY),
                        fontSmall
                    ),
                    "profilerCategory")

                .AddStaticText("GPU sort", fontLabel, Label(rowY += 30))
                .AddInteractiveElement(
                    new GuiElementDropDownCycleOnArrow(
                        capi,
                        profilerSortValues,
                        profilerSortNames,
                        1,
                        OnProfilerSelectionChanged,
                        Control(rowY),
                        fontSmall
                    ),
                    "profilerSort")

                .AddDynamicText(
                    "(Profiler will populate after a few frames.)",
                    fontSmall,
                    ElementBounds.Fixed(0, rowY + 35, labelW + 10 + controlW, 320),
                    "profilerText")
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

        RefreshProfilerText();
    }

    private void RefreshProfilerText()
    {
        if (SingleComposer is null)
        {
            return;
        }

        var dyn = SingleComposer.GetDynamicText("profilerText");

        string prefix = GetProfilerPrefix();
        string? prefixFilter = string.IsNullOrEmpty(prefix) ? null : prefix;
        var sort = GetProfilerSort();
        var entries = GlGpuProfiler.Instance.GetSnapshot(sort, prefixFilter, maxEntries: 64);

        string enabled = GlGpuProfiler.Instance.Enabled ? "On" : "Off";
        int w = capi.Render.FrameWidth;
        int h = capi.Render.FrameHeight;

        if (entries.Length == 0)
        {
            dyn.SetNewText($"GPU profiler: {enabled} @ {w}x{h}\n(no events yet)");
            return;
        }

        static string Ms(float v) => v <= 0f ? "-" : v.ToString("0.###");

        var lines = new string[entries.Length + 2];
        lines[0] = $"GPU profiler: {enabled} @ {w}x{h}  (showing {entries.Length})";
        lines[1] = "Event | last ms | avg ms | min ms | max ms | n";

        for (int i = 0; i < entries.Length; i++)
        {
            var e = entries[i];
            var s = e.Stats;
            lines[i + 2] = $"{e.Name} | {Ms(s.LastMs)} | {Ms(s.AvgMs)} | {Ms(s.MinMs)} | {Ms(s.MaxMs)} | {s.SampleCount}";
        }

        dyn.SetNewText(string.Join("\n", lines));
    }

    private string GetProfilerPrefix()
    {
        if (SingleComposer is null)
        {
            return string.Empty;
        }

        string? val = SingleComposer.GetDropDown("profilerCategory").SelectedValue;
        return val switch
        {
            "PBR" => "PBR.",
            "LumOn" => "LumOn.",
            "Debug" => "Debug.",
            _ => string.Empty
        };
    }

    private GpuProfileSort GetProfilerSort()
    {
        if (SingleComposer is null)
        {
            return GpuProfileSort.LastMs;
        }

        string? val = SingleComposer.GetDropDown("profilerSort").SelectedValue;
        return val switch
        {
            "Name" => GpuProfileSort.Name,
            "LastMs" => GpuProfileSort.LastMs,
            "AvgMs" => GpuProfileSort.AvgMs,
            "MaxMs" => GpuProfileSort.MaxMs,
            _ => GpuProfileSort.LastMs
        };
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

    private void OnProfilerEnabledChanged(string code, bool selected)
    {
        if (!selected)
        {
            return;
        }

        GlGpuProfiler.Instance.Enabled = code == "On";
        RefreshProfilerText();
    }

    private void OnProfilerSelectionChanged(string _code, bool selected)
    {
        if (!selected)
        {
            return;
        }

        RefreshProfilerText();
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
        LumOnDebugMode.MaterialBands => "Material Bands (hash of gMaterial) (Phase 7)",
        LumOnDebugMode.VgeNormalDepthAtlas => "VGE Normal+Depth Atlas (current page)",
        LumOnDebugMode.WorldProbeIrradianceCombined => "World-Probe Irradiance (combined) (Phase 18)",
        LumOnDebugMode.WorldProbeIrradianceLevel => "World-Probe Irradiance (selected level) (Phase 18)",
        LumOnDebugMode.WorldProbeConfidence => "World-Probe Confidence (Phase 18)",
        LumOnDebugMode.WorldProbeShortRangeAoDirection => "World-Probe ShortRangeAO Direction (Phase 18)",
        LumOnDebugMode.WorldProbeShortRangeAoConfidence => "World-Probe ShortRangeAO Confidence (Phase 18)",
        LumOnDebugMode.WorldProbeHitDistance => "World-Probe Hit Distance (normalized) (Phase 18)",
        LumOnDebugMode.WorldProbeMetaFlagsHeatmap => "World-Probe Meta Flags (heatmap) (Phase 18)",
        LumOnDebugMode.WorldProbeBlendWeights => "Blend Weights: screen vs world (Phase 18)",
        LumOnDebugMode.WorldProbeCrossLevelBlend => "Cross-Level Blend: selected L + weights (Phase 18)",
        LumOnDebugMode.WorldProbeOrbsPoints => "World-Probe Probes (orbs, GL_POINTS) (Phase 18)",
        LumOnDebugMode.PomMetrics => "POM Metrics (heatmap from gBufferNormal.w)",
        _ => mode.ToString()
    };
}
