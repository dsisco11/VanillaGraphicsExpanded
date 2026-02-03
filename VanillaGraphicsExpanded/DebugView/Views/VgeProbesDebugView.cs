using System;
using System.Linq;

using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.LumOn.WorldProbes;
using VanillaGraphicsExpanded.ModSystems;

using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

namespace VanillaGraphicsExpanded.DebugView;

public static partial class VgeBuiltInDebugViews
{
    private static DebugViewDefinition CreateProbesDebugView()
        => new(
            id: ProbesDebugViewId,
            name: "Probes",
            category: CategoryProbes,
            description: "Probe-related debug overlays (screen probes, probe atlas, world probes).",
            registerRenderer: ctx =>
            {
                ctx.Config.LumOn.DebugMode = ProbesDebugViewState.Instance.GetSelectedDebugModeOrDefault();
                return new ActionDisposable(() => ctx.Config.LumOn.DebugMode = LumOnDebugMode.Off);
            },
            activationMode: DebugViewActivationMode.Exclusive,
            availability: ctx =>
            {
                if (!ctx.Config.LumOn.Enabled)
                {
                    return DebugViewAvailability.Unavailable("LumOn is disabled in config.");
                }

                return DebugViewAvailability.Available();
            },
            createPanel: ctx => new ProbesDebugPanel(viewId: ProbesDebugViewId, ctx.Capi, ctx.Config));

    private sealed class ProbesDebugViewState : LumOnDebugViewStateBase
    {
        public static readonly ProbesDebugViewState Instance = new();

        private ProbeVizMode selectedMode = ProbeVizMode.ScreenProbeGrid;
        private bool worldProbes;

        private ProbesDebugViewState() : base(defaultMode: LumOnDebugMode.ProbeGrid)
        {
        }

        public ProbeVizMode GetSelectedProbeVizModeOrDefault()
        {
            ProbeVizMode mode = selectedMode;
            if (!Enum.IsDefined(typeof(ProbeVizMode), mode))
            {
                return ProbeVizMode.ScreenProbeGrid;
            }

            return mode;
        }

        public void SetSelectedProbeVizMode(ProbeVizMode mode)
        {
            if (!Enum.IsDefined(typeof(ProbeVizMode), mode))
            {
                return;
            }

            selectedMode = mode;
        }

        public bool GetWorldProbesEnabled() => worldProbes;

        public void SetWorldProbesEnabled(bool enabled) => worldProbes = enabled;

        public bool IsWorldToggleVisibleForCurrentMode()
        {
            ProbeModeMapping m = GetProbeModeMapping(GetSelectedProbeVizModeOrDefault());
            return m.World is not null;
        }

        public LumOnDebugMode GetSelectedDebugModeOrDefault()
        {
            ProbeModeMapping m = GetProbeModeMapping(GetSelectedProbeVizModeOrDefault());
            if (worldProbes && m.World is not null)
            {
                return m.World.Value;
            }

            return m.Screen;
        }
    }

    private enum ProbeVizMode
    {
        // Screen probes
        ScreenProbeGrid,
        ScreenProbeDepth,
        ScreenProbeNormal,
        TemporalWeight,
        TemporalRejection,
        ShCoefficients,
        InterpolationWeights,
        RadianceOverlay,
        GatherWeight,

        // Probe atlas
        ProbeAtlasMetaConfidence,
        ProbeAtlasTemporalAlpha,
        ProbeAtlasTemporalRejection,
        ProbeAtlasMetaFlags,
        ProbeAtlasTraceRadiance,
        ProbeAtlasCurrentRadiance,
        ProbeAtlasFilteredRadiance,
        ProbeAtlasGatherInputRadiance,
        ProbeAtlasHitDistance,
        ProbeAtlasFilterDelta,
        ProbeAtlasGatherInputSource,
        ProbeAtlasPisTraceMask,
        ProbePisEnergy,

        // World probes
        WorldProbeIrradianceCombined,
        WorldProbeIrradianceLevel,
        WorldProbeConfidence,
        WorldProbeShortRangeAoDirection,
        WorldProbeShortRangeAoConfidence,
        WorldProbeHitDistance,
        WorldProbeMetaFlagsHeatmap,
        WorldProbeBlendWeights,
        WorldProbeCrossLevelBlend,
        WorldProbeOrbsPoints,
        WorldProbeRawConfidences,

        // Symmetric where applicable (World vs Screen toggle)
        ContributionOnly,
    }

    private readonly record struct ProbeModeMapping(LumOnDebugMode Screen, LumOnDebugMode? World);

    private static ProbeModeMapping GetProbeModeMapping(ProbeVizMode mode) => mode switch
    {
        ProbeVizMode.ScreenProbeGrid => new(LumOnDebugMode.ProbeGrid, null),
        ProbeVizMode.ScreenProbeDepth => new(LumOnDebugMode.ProbeDepth, null),
        ProbeVizMode.ScreenProbeNormal => new(LumOnDebugMode.ProbeNormal, null),
        ProbeVizMode.TemporalWeight => new(LumOnDebugMode.TemporalWeight, null),
        ProbeVizMode.TemporalRejection => new(LumOnDebugMode.TemporalRejection, null),
        ProbeVizMode.ShCoefficients => new(LumOnDebugMode.ShCoefficients, null),
        ProbeVizMode.InterpolationWeights => new(LumOnDebugMode.InterpolationWeights, null),
        ProbeVizMode.RadianceOverlay => new(LumOnDebugMode.RadianceOverlay, null),
        ProbeVizMode.GatherWeight => new(LumOnDebugMode.GatherWeight, null),

        ProbeVizMode.ProbeAtlasMetaConfidence => new(LumOnDebugMode.ProbeAtlasMetaConfidence, null),
        ProbeVizMode.ProbeAtlasTemporalAlpha => new(LumOnDebugMode.ProbeAtlasTemporalAlpha, null),
        ProbeVizMode.ProbeAtlasTemporalRejection => new(LumOnDebugMode.ProbeAtlasTemporalRejection, null),
        ProbeVizMode.ProbeAtlasMetaFlags => new(LumOnDebugMode.ProbeAtlasMetaFlags, null),
        ProbeVizMode.ProbeAtlasTraceRadiance => new(LumOnDebugMode.ProbeAtlasTraceRadiance, null),
        ProbeVizMode.ProbeAtlasCurrentRadiance => new(LumOnDebugMode.ProbeAtlasCurrentRadiance, null),
        ProbeVizMode.ProbeAtlasFilteredRadiance => new(LumOnDebugMode.ProbeAtlasFilteredRadiance, null),
        ProbeVizMode.ProbeAtlasGatherInputRadiance => new(LumOnDebugMode.ProbeAtlasGatherInputRadiance, null),
        ProbeVizMode.ProbeAtlasHitDistance => new(LumOnDebugMode.ProbeAtlasHitDistance, null),
        ProbeVizMode.ProbeAtlasFilterDelta => new(LumOnDebugMode.ProbeAtlasFilterDelta, null),
        ProbeVizMode.ProbeAtlasGatherInputSource => new(LumOnDebugMode.ProbeAtlasGatherInputSource, null),
        ProbeVizMode.ProbeAtlasPisTraceMask => new(LumOnDebugMode.ProbeAtlasPisTraceMask, null),
        ProbeVizMode.ProbePisEnergy => new(LumOnDebugMode.ProbePisEnergy, null),

        ProbeVizMode.WorldProbeIrradianceCombined => new(LumOnDebugMode.WorldProbeIrradianceCombined, null),
        ProbeVizMode.WorldProbeIrradianceLevel => new(LumOnDebugMode.WorldProbeIrradianceLevel, null),
        ProbeVizMode.WorldProbeConfidence => new(LumOnDebugMode.WorldProbeConfidence, null),
        ProbeVizMode.WorldProbeShortRangeAoDirection => new(LumOnDebugMode.WorldProbeShortRangeAoDirection, null),
        ProbeVizMode.WorldProbeShortRangeAoConfidence => new(LumOnDebugMode.WorldProbeShortRangeAoConfidence, null),
        ProbeVizMode.WorldProbeHitDistance => new(LumOnDebugMode.WorldProbeHitDistance, null),
        ProbeVizMode.WorldProbeMetaFlagsHeatmap => new(LumOnDebugMode.WorldProbeMetaFlagsHeatmap, null),
        ProbeVizMode.WorldProbeBlendWeights => new(LumOnDebugMode.WorldProbeBlendWeights, null),
        ProbeVizMode.WorldProbeCrossLevelBlend => new(LumOnDebugMode.WorldProbeCrossLevelBlend, null),
        ProbeVizMode.WorldProbeOrbsPoints => new(LumOnDebugMode.WorldProbeOrbsPoints, null),
        ProbeVizMode.WorldProbeRawConfidences => new(LumOnDebugMode.WorldProbeRawConfidences, null),

        // Symmetric pair: screen-space vs world-probe contribution.
        ProbeVizMode.ContributionOnly => new(LumOnDebugMode.ScreenSpaceContributionOnly, LumOnDebugMode.WorldProbeContributionOnly),

        _ => new(LumOnDebugMode.ProbeGrid, null)
    };

    private sealed class ProbesDebugPanel : DebugViewPanelBase
    {
        private readonly string viewId;
        private readonly ICoreClientAPI capi;
        private readonly VgeConfig config;

        private GuiComposer? composer;
        private string? keyPrefix;
        private const string ClosestProbeTextKey = "closestprobe";
        private const string ClosestProbeLogButtonKey = "closestprobelog";
        private const string ProbeAtlasTemporalRejectionLegendKey = "probeatlas-temporalrej-legend";

        private readonly string[] values;
        private readonly string[] names;

        private bool lastToggleVisible;
        private bool lastLegendVisible;

        private static bool IsLegendVisible(ProbeVizMode mode) => mode switch
        {
            ProbeVizMode.ProbeAtlasTemporalRejection => true,
            _ => false
        };

        public ProbesDebugPanel(string viewId, ICoreClientAPI capi, VgeConfig config)
        {
            this.viewId = viewId;
            this.capi = capi;
            this.config = config;

            var modes = Enum.GetValues(typeof(ProbeVizMode)).Cast<ProbeVizMode>().ToArray();
            values = modes.Select(m => m.ToString()).ToArray();
            names = modes.Select(GetProbeVizModeDisplayName).ToArray();
        }

        public override void Compose(GuiComposer composer, ElementBounds bounds, string keyPrefix)
        {
            this.composer = composer;
            this.keyPrefix = keyPrefix;

            const double labelW = 140;
            const double rowH = 30;
            const double gap = 10;
            const double rowGapY = 8;

            double boundsW = bounds.fixedWidth > 0 ? bounds.fixedWidth : bounds.OuterWidth;
            double controlW = Math.Min(280, Math.Max(200, boundsW - labelW - gap));

            var fontLabel = CairoFont.WhiteDetailText();
            var fontSmall = CairoFont.WhiteSmallText();

            ProbeVizMode selectedMode = ProbesDebugViewState.Instance.GetSelectedProbeVizModeOrDefault();
            int selectedIndex = Array.IndexOf(values, selectedMode.ToString());
            if (selectedIndex < 0) selectedIndex = 0;

            ElementBounds labelMode = ElementBounds.Fixed(0, 0, labelW, rowH).WithParent(bounds);
            ElementBounds dropMode = ElementBounds.Fixed(labelW + gap, 0, controlW, rowH).WithParent(bounds);

            composer
                .AddStaticText("Mode", fontLabel, labelMode)
                .AddInteractiveElement(
                    new GuiElementDropDownCycleOnArrow(
                        capi,
                        values,
                        names,
                        selectedIndex,
                        OnModeChanged,
                        dropMode,
                        fontSmall),
                    $"{keyPrefix}-mode");

            bool toggleVisible = ProbesDebugViewState.Instance.IsWorldToggleVisibleForCurrentMode();
            lastToggleVisible = toggleVisible;

            bool legendVisible = IsLegendVisible(selectedMode);
            lastLegendVisible = legendVisible;

            double y = rowH + rowGapY;
            if (toggleVisible)
            {
                ElementBounds labelWorld = ElementBounds.Fixed(0, y, labelW, rowH).WithParent(bounds);
                ElementBounds ctrlWorld = ElementBounds.Fixed(labelW + gap, y, 30, rowH).WithParent(bounds);

                var sw = new GuiElementSwitch(capi, OnWorldToggled, ctrlWorld, size: 26, padding: 4);
                sw.SetValue(ProbesDebugViewState.Instance.GetWorldProbesEnabled());

                composer
                    .AddStaticText("World probes", fontLabel, labelWorld)
                    .AddInteractiveElement(sw, $"{keyPrefix}-world");

                y += rowH + rowGapY;
            }

            // Extra info for the orb view: show closest probe position in world-space.
            // (This helps diagnose why probes near the ground are disabled.)
            ElementBounds closestBounds = ElementBounds.Fixed(0, y, boundsW, rowH * 2).WithParent(bounds);
            composer.AddDynamicText("", fontSmall, closestBounds, $"{keyPrefix}-{ClosestProbeTextKey}");
            y += rowH * 2 + rowGapY;

            ElementBounds logBtnBounds = ElementBounds.Fixed(0, y, 200, rowH).WithParent(bounds);
            composer.AddSmallButton("Log closest probe", OnLogClosestProbeClicked, logBtnBounds, EnumButtonStyle.Small, $"{keyPrefix}-{ClosestProbeLogButtonKey}");

            y += rowH + rowGapY;

            if (legendVisible)
            {
                // Color key for the shader debug view: renderProbeAtlasTemporalRejectionDebug()
                // Keep this as VTML so the colors match the overlay without adding custom draw code.
                static string Line(string hex, string text) => $"<font color=\"{hex}\">■</font> {text}<br/>";

                string vtml =
                    "<b>Probe-Atlas Temporal Rejection</b><br/>" +
                    Line("#00ff00", "Valid history") +
                    Line("#ff0000", "Reprojection out of bounds") +
                    Line("#ffff00", "Velocity too large") +
                    Line("#ff8000", "Hit-distance delta reject") +
                    Line("#ff00ff", "Hit/miss classification mismatch") +
                    Line("#cc33cc", "Low history confidence") +
                    Line("#800080", "No valid history") +
                    Line("#0066ff", "Velocity invalid (fell back)");

                ElementBounds legendBounds = ElementBounds.Fixed(0, y, boundsW, rowH * 7).WithParent(bounds);
                composer.AddRichtext(vtml, fontSmall, legendBounds, $"{keyPrefix}-{ProbeAtlasTemporalRejectionLegendKey}");
            }

            RefreshClosestProbeText();
        }

        public override bool WantsGameTick => true;

        public override void OnGameTick(float dt)
        {
            _ = dt;
            RefreshClosestProbeText();
        }

        private void RefreshClosestProbeText()
        {
            if (composer is null || string.IsNullOrWhiteSpace(keyPrefix))
            {
                return;
            }

            GuiElementDynamicText? dyn;
            try
            {
                dyn = composer.GetDynamicText($"{keyPrefix}-{ClosestProbeTextKey}");
            }
            catch
            {
                return;
            }

            dyn.SetNewText(ComputeClosestProbeText());
        }

        private bool OnLogClosestProbeClicked()
        {
            string msg = ComputeClosestProbeText();
            if (string.IsNullOrWhiteSpace(msg))
            {
                msg = "(closest probe unavailable)";
            }

            capi.Logger.Notification("[VGE] {0}", msg.Replace("\n", " | "));
            return true;
        }

        private string ComputeClosestProbeText()
        {
            if (config.LumOn.DebugMode != LumOnDebugMode.WorldProbeOrbsPoints)
            {
                return string.Empty;
            }

            // Prefer selected block (under crosshair) as the target; fall back to camera.
            Vec3d targetWorld = capi.World?.Player?.Entity?.CameraPos ?? new Vec3d();
            try
            {
                var sel = capi.World?.Player?.CurrentBlockSelection;
                if (sel?.Position is not null)
                {
                    var p = sel.Position;
                    // Clamp hit position slightly inside the block to avoid selecting a probe from the
                    // adjacent cell when HitPosition lies exactly on a face (0 or 1.0 components).
                    const double hitEps = 1e-3;
                    var hp = sel.HitPosition;
                    double hx = Math.Clamp(hp.X, hitEps, 1.0 - hitEps);
                    double hy = Math.Clamp(hp.Y, hitEps, 1.0 - hitEps);
                    double hz = Math.Clamp(hp.Z, hitEps, 1.0 - hitEps);
                    targetWorld = new Vec3d(p.X + hx, p.Y + hy, p.Z + hz);
                }
            }
            catch
            {
                // Ignore selection query failures.
            }

            var wpMs = capi.ModLoader.GetModSystem<WorldProbeModSystem>();
            var bm = wpMs.GetClipmapBufferManagerOrNull();
            if (bm is null || bm.Resources is null)
            {
                return "Closest world probe: (clipmap not initialized)";
            }

            if (!bm.TryGetRuntimeParams(out var camPosWorld, out _, out float baseSpacing, out int levels, out int resolution, out var origins, out _))
            {
                return "Closest world probe: (runtime params missing)";
            }

            levels = Math.Clamp(levels, 1, 8);
            resolution = Math.Max(1, resolution);
            double baseSpacingD = Math.Max(1e-6, baseSpacing);

            double bestD2 = double.PositiveInfinity;
            int bestLevel = 0;
            Vec3i bestIndex = new();
            Vec3d bestPos = new();

            // Match shader behavior: choose the finest level whose extents contain the target.
            int selectedLevel = -1;
            Vec3d selectedOriginAbs = new();
            Vec3d selectedLocal = new();

            for (int level = 0; level < levels; level++)
            {
                double spacing = baseSpacingD * (1 << level);
                if (spacing <= 0) continue;

                var oRel = (level < origins.Length) ? origins[level] : default;
                Vec3d originAbs = new(
                    camPosWorld.X + oRel.X,
                    camPosWorld.Y + oRel.Y,
                    camPosWorld.Z + oRel.Z);

                Vec3d local = LumOnClipmapTopology.WorldToLocal(targetWorld, originAbs, spacing);
                bool inside =
                    local.X >= 0 && local.Y >= 0 && local.Z >= 0 &&
                    local.X < resolution && local.Y < resolution && local.Z < resolution;

                if (inside)
                {
                    selectedLevel = level;
                    selectedOriginAbs = originAbs;
                    selectedLocal = local;
                    break;
                }
            }

            if (selectedLevel < 0)
            {
                // Target outside all extents (e.g. far selection): fall back to coarsest with clamping.
                selectedLevel = Math.Max(0, levels - 1);
                double spacing = baseSpacingD * (1 << selectedLevel);
                var oRel = (selectedLevel < origins.Length) ? origins[selectedLevel] : default;
                selectedOriginAbs = new Vec3d(
                    camPosWorld.X + oRel.X,
                    camPosWorld.Y + oRel.Y,
                    camPosWorld.Z + oRel.Z);
                selectedLocal = LumOnClipmapTopology.WorldToLocal(targetWorld, selectedOriginAbs, Math.Max(1e-6, spacing));
            }

            if (selectedLevel >= 0)
            {
                double spacing = baseSpacingD * (1 << selectedLevel);

                var idx = new Vec3i(
                    (int)Math.Floor(selectedLocal.X),
                    (int)Math.Floor(selectedLocal.Y),
                    (int)Math.Floor(selectedLocal.Z));
                idx.X = Math.Clamp(idx.X, 0, resolution - 1);
                idx.Y = Math.Clamp(idx.Y, 0, resolution - 1);
                idx.Z = Math.Clamp(idx.Z, 0, resolution - 1);

                Vec3d probeCenter = LumOnClipmapTopology.IndexToProbeCenterWorld(idx, selectedOriginAbs, spacing);

                double dx = probeCenter.X - targetWorld.X;
                double dy = probeCenter.Y - targetWorld.Y;
                double dz = probeCenter.Z - targetWorld.Z;
                bestD2 = (dx * dx) + (dy * dy) + (dz * dz);
                bestLevel = selectedLevel;
                bestIndex = idx;
                bestPos = probeCenter;
            }

            if (double.IsInfinity(bestD2))
            {
                return "Closest world probe: (unavailable)";
            }

            double dist = Math.Sqrt(bestD2);
            return
                $"Closest world probe:\n" +
                $"target=({targetWorld.X:0.###},{targetWorld.Y:0.###},{targetWorld.Z:0.###})\n" +
                $"L{bestLevel} idx=({bestIndex.X},{bestIndex.Y},{bestIndex.Z})  pos=({bestPos.X:0.###},{bestPos.Y:0.###},{bestPos.Z:0.###})  d={dist:0.###}";
        }

        private void OnModeChanged(string code, bool selected)
        {
            if (!selected)
            {
                return;
            }

            if (!Enum.TryParse(code, out ProbeVizMode mode))
            {
                return;
            }

            bool prevToggleVisible = ProbesDebugViewState.Instance.IsWorldToggleVisibleForCurrentMode();
            bool prevLegendVisible = IsLegendVisible(ProbesDebugViewState.Instance.GetSelectedProbeVizModeOrDefault());
            ProbesDebugViewState.Instance.SetSelectedProbeVizMode(mode);
            bool nextToggleVisible = ProbesDebugViewState.Instance.IsWorldToggleVisibleForCurrentMode();
            bool nextLegendVisible = IsLegendVisible(mode);

            if (string.Equals(DebugViewController.Instance.ActiveExclusiveViewId, viewId, StringComparison.Ordinal))
            {
                config.LumOn.DebugMode = ProbesDebugViewState.Instance.GetSelectedDebugModeOrDefault();
            }

            if (prevToggleVisible != nextToggleVisible || lastToggleVisible != nextToggleVisible
                || prevLegendVisible != nextLegendVisible || lastLegendVisible != nextLegendVisible)
            {
                lastToggleVisible = nextToggleVisible;
                lastLegendVisible = nextLegendVisible;
                try
                {
                    composer?.ReCompose();
                }
                catch
                {
                    // Ignore UI refresh failures.
                }
            }

            RefreshClosestProbeText();
        }

        private void OnWorldToggled(bool on)
        {
            ProbesDebugViewState.Instance.SetWorldProbesEnabled(on);

            if (string.Equals(DebugViewController.Instance.ActiveExclusiveViewId, viewId, StringComparison.Ordinal))
            {
                config.LumOn.DebugMode = ProbesDebugViewState.Instance.GetSelectedDebugModeOrDefault();
            }

            try
            {
                composer?.ReCompose();
            }
            catch
            {
                // Ignore UI refresh failures.
            }

            RefreshClosestProbeText();
        }

        private static string GetProbeVizModeDisplayName(ProbeVizMode mode) => mode switch
        {
            ProbeVizMode.ScreenProbeGrid => "Probe Grid",
            ProbeVizMode.ScreenProbeDepth => "Probe Depth",
            ProbeVizMode.ScreenProbeNormal => "Probe Normals",
            ProbeVizMode.TemporalWeight => "Temporal Weight",
            ProbeVizMode.TemporalRejection => "Temporal Rejection",
            ProbeVizMode.ShCoefficients => "SH Coefficients",
            ProbeVizMode.InterpolationWeights => "Interpolation Weights",
            ProbeVizMode.RadianceOverlay => "Radiance Overlay",
            ProbeVizMode.GatherWeight => "Gather Weight (diagnostic)",
            ProbeVizMode.ProbeAtlasMetaConfidence => "Probe-Atlas Meta Confidence",
            ProbeVizMode.ProbeAtlasTemporalAlpha => "Probe-Atlas Temporal Alpha",
            ProbeVizMode.ProbeAtlasTemporalRejection => "Probe-Atlas Temporal Rejection",
            ProbeVizMode.ProbeAtlasMetaFlags => "Probe-Atlas Meta Flags",
            ProbeVizMode.ProbeAtlasTraceRadiance => "Probe-Atlas Trace Radiance",
            ProbeVizMode.ProbeAtlasCurrentRadiance => "Probe-Atlas Current Radiance",
            ProbeVizMode.ProbeAtlasFilteredRadiance => "Probe-Atlas Filtered Radiance",
            ProbeVizMode.ProbeAtlasGatherInputRadiance => "Probe-Atlas Gather Input Radiance",
            ProbeVizMode.ProbeAtlasHitDistance => "Probe-Atlas Hit Distance",
            ProbeVizMode.ProbeAtlasFilterDelta => "Probe-Atlas Filter Delta",
            ProbeVizMode.ProbeAtlasGatherInputSource => "Probe-Atlas Gather Input Source",
            ProbeVizMode.ProbeAtlasPisTraceMask => "Probe-Atlas PIS Trace Mask",
            ProbeVizMode.ProbePisEnergy => "Probe PIS Energy",
            ProbeVizMode.WorldProbeIrradianceCombined => "World-Probe Irradiance (combined)",
            ProbeVizMode.WorldProbeIrradianceLevel => "World-Probe Irradiance (selected level)",
            ProbeVizMode.WorldProbeConfidence => "World-Probe Confidence",
            ProbeVizMode.WorldProbeShortRangeAoDirection => "World-Probe ShortRangeAO Direction",
            ProbeVizMode.WorldProbeShortRangeAoConfidence => "World-Probe ShortRangeAO Confidence",
            ProbeVizMode.WorldProbeHitDistance => "World-Probe Hit Distance (normalized)",
            ProbeVizMode.WorldProbeMetaFlagsHeatmap => "World-Probe Meta Flags (heatmap)",
            ProbeVizMode.WorldProbeBlendWeights => "Blend Weights: screen vs world",
            ProbeVizMode.WorldProbeCrossLevelBlend => "Cross-Level Blend: selected L + weights",
            ProbeVizMode.WorldProbeOrbsPoints => "World-Probe Probes (orbs, GL_POINTS)",
            ProbeVizMode.WorldProbeRawConfidences => "World-Probe Raw Confidences",
            ProbeVizMode.ContributionOnly => "Contribution Only",
            _ => mode.ToString()
        };
    }
}
