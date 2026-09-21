using System;
using System.Linq;

using VanillaGraphicsExpanded.LumOn;

using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.DebugView;

public static partial class VgeBuiltInDebugViews
{
    private static DebugViewDefinition CreateLumOnModeSelectorDebugView(
        string id,
        string name,
        string category,
        string description,
        ILumOnDebugViewState viewState,
        LumOnDebugMode[] allowedModes)
        => new(
            id: id,
            name: name,
            category: category,
            description: description,
            registerRenderer: ctx =>
            {
                viewState.SetSelectedMode(ctx.Config.Debug.DebugViews.ActiveExclusiveLumOnDebugMode ?? ctx.Config.LumOn.DebugMode);
                LumOnDebugMode mode = viewState.GetSelectedModeOrDefault();
                ctx.Config.LumOn.DebugMode = mode;
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
            createPanel: ctx => new LumOnDebugPanel(
                viewId: id,
                capi: ctx.Capi,
                config: ctx.Config,
                viewState: viewState,
                allowedModes: allowedModes));

    internal interface ILumOnDebugViewState
    {
        LumOnDebugMode GetSelectedModeOrDefault();
        void SetSelectedMode(LumOnDebugMode mode);
    }

    internal abstract class LumOnDebugViewStateBase : ILumOnDebugViewState
    {
        private readonly LumOnDebugMode defaultMode;
        private LumOnDebugMode selectedMode;

        protected LumOnDebugViewStateBase(LumOnDebugMode defaultMode)
        {
            this.defaultMode = defaultMode;
            selectedMode = defaultMode;
        }

        public LumOnDebugMode GetSelectedModeOrDefault()
        {
            LumOnDebugMode mode = selectedMode;
            if (!Enum.IsDefined(typeof(LumOnDebugMode), mode) || mode == LumOnDebugMode.Off)
            {
                return defaultMode;
            }

            return mode;
        }

        public void SetSelectedMode(LumOnDebugMode mode)
        {
            if (!Enum.IsDefined(typeof(LumOnDebugMode), mode) || mode == LumOnDebugMode.Off)
            {
                return;
            }

            selectedMode = mode;
        }
    }

    private sealed class LumOnDebugPanel : DebugViewPanelBase
    {
        private readonly string viewId;
        private readonly ICoreClientAPI capi;
        private readonly VgeConfig config;

        private readonly ILumOnDebugViewState viewState;

        private readonly string[] values;
        private readonly string[] names;

        public LumOnDebugPanel(
            string viewId,
            ICoreClientAPI capi,
            VgeConfig config,
            ILumOnDebugViewState viewState,
            LumOnDebugMode[] allowedModes)
        {
            this.viewId = viewId;
            this.capi = capi;
            this.config = config;
            this.viewState = viewState;

            var modes = (allowedModes ?? Array.Empty<LumOnDebugMode>())
                .Where(m => m != LumOnDebugMode.Off)
                .Distinct()
                .ToArray();

            values = modes.Select(m => m.ToString()).ToArray();
            names = modes.Select(GetLumOnDebugModeDisplayName).ToArray();
        }

        public override void Compose(GuiComposer composer, ElementBounds bounds, string keyPrefix)
        {
            const double labelW = 140;
            const double rowH = 30;
            const double gap = 10;

            double boundsW = bounds.fixedWidth > 0 ? bounds.fixedWidth : bounds.OuterWidth;
            double controlW = Math.Min(280, Math.Max(200, boundsW - labelW - gap));

            ElementBounds labelBounds = ElementBounds.Fixed(0, 0, labelW, rowH).WithParent(bounds);
            ElementBounds dropBounds = ElementBounds.Fixed(labelW + gap, 0, controlW, rowH).WithParent(bounds);

            var fontLabel = CairoFont.WhiteDetailText();
            var fontSmall = CairoFont.WhiteSmallText();

            int selectedIndex = Array.IndexOf(values, viewState.GetSelectedModeOrDefault().ToString());
            if (selectedIndex < 0) selectedIndex = 0;

            composer
                .AddStaticText("Mode", fontLabel, labelBounds)
                .AddInteractiveElement(
                    new GuiElementDropDownCycleOnArrow(
                        capi,
                        values,
                        names,
                        selectedIndex,
                        OnModeChanged,
                        dropBounds,
                        fontSmall),
                    $"{keyPrefix}-mode");
        }

        private void OnModeChanged(string code, bool selected)
        {
            if (!selected)
            {
                return;
            }

            if (!Enum.TryParse(code, out LumOnDebugMode mode))
            {
                return;
            }

            viewState.SetSelectedMode(mode);

            if (string.Equals(DebugViewController.Instance.ActiveExclusiveViewId, viewId, StringComparison.Ordinal))
            {
                config.LumOn.DebugMode = mode;
                DebugViewController.Instance.NotifyExclusiveModeChanged();
            }
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
            LumOnDebugMode.RadianceOverlay => "Indirect Lighting Output",
            LumOnDebugMode.GatherWeight => "Gather Weight (diagnostic)",
            LumOnDebugMode.LocalTraceGeometry => "Local-Tracing Geometry",
            LumOnDebugMode.ProbeAtlasTraceOutcome => "Probe-Atlas Trace Outcome",
            LumOnDebugMode.ProbeAtlasMetaConfidence => "Probe-Atlas Meta Confidence",
            LumOnDebugMode.ProbeAtlasTemporalAlpha => "Probe-Atlas Temporal Alpha",
            LumOnDebugMode.ProbeAtlasMetaFlags => "Probe-Atlas Meta Flags",
            LumOnDebugMode.ProbeAtlasTraceRadiance => "Probe-Atlas Trace Radiance",
            LumOnDebugMode.ProbeAtlasCurrentRadiance => "Probe-Atlas Current Radiance",
            LumOnDebugMode.ProbeAtlasFilteredRadiance => "Probe-Atlas Filtered Radiance",
            LumOnDebugMode.ProbeAtlasGatherInputRadiance => "Probe-Atlas Gather Input Radiance",
            LumOnDebugMode.ProbeAtlasHitDistance => "Probe-Atlas Hit Distance",
            LumOnDebugMode.ProbeAtlasFilterDelta => "Probe-Atlas Filter Delta",
            LumOnDebugMode.ProbeAtlasGatherInputSource => "Probe-Atlas Gather Input Source",
            LumOnDebugMode.ProbeAtlasTemporalRejection => "Probe-Atlas Temporal Rejection",
            LumOnDebugMode.CompositeAO => "Composite AO",
            LumOnDebugMode.CompositeIndirectDiffuse => "Composite Indirect Diffuse",
            LumOnDebugMode.CompositeIndirectSpecular => "Composite Indirect Specular",
            LumOnDebugMode.CompositeMaterial => "Composite Material",
            LumOnDebugMode.DirectDiffuse => "Direct Diffuse",
            LumOnDebugMode.DirectSpecular => "Direct Specular",
            LumOnDebugMode.DirectEmissive => "Direct Emissive",
            LumOnDebugMode.DirectTotal => "Direct Total (diffuse+spec)",
            LumOnDebugMode.VelocityMagnitude => "Velocity Magnitude",
            LumOnDebugMode.VelocityValidity => "Velocity Validity",
            LumOnDebugMode.VelocityPrevUv => "Velocity Prev UV",
            LumOnDebugMode.MaterialBands => "Material Bands (hash of gMaterial)",
            LumOnDebugMode.VgeNormalDepthAtlas => "VGE Normal+Depth Atlas (current page)",
            LumOnDebugMode.WorldProbeIrradianceCombined => "World-Probe Irradiance (combined)",
            LumOnDebugMode.WorldProbeIrradianceLevel => "World-Probe Irradiance (selected level)",
            LumOnDebugMode.WorldProbeConfidence => "World-Probe Confidence",
            LumOnDebugMode.WorldProbeShortRangeAoDirection => "World-Probe ShortRangeAO Direction",
            LumOnDebugMode.WorldProbeShortRangeAoConfidence => "World-Probe ShortRangeAO Confidence",
            LumOnDebugMode.WorldProbeHitDistance => "World-Probe Hit Distance (normalized)",
            LumOnDebugMode.WorldProbeMetaFlagsHeatmap => "World-Probe Meta Flags (heatmap)",
            LumOnDebugMode.WorldProbeBlendWeights => "Blend Weights: screen vs world",
            LumOnDebugMode.WorldProbeCrossLevelBlend => "Cross-Level Blend: selected L + weights",
            LumOnDebugMode.WorldProbeOrbsPoints => "World-Probe Probes (orbs, GL_POINTS)",
            LumOnDebugMode.PomMetrics => "POM Metrics (heatmap from gBufferNormal.w)",
            LumOnDebugMode.WorldProbeRawConfidences => "World-Probe Raw Confidences",
            LumOnDebugMode.WorldProbeImportance => "World-Probe Importance (orbs; blue = low, red = high)",
            LumOnDebugMode.WorldProbeLightingEffect => "World-Probe Lighting Effect",
            LumOnDebugMode.WorldProbeSuppressedLighting => "Lighting With World Radiance Zeroed",
            LumOnDebugMode.LumonScenePageReady => "LumonScene: Page Ready",
            LumOnDebugMode.LumonScenePatchUv => "LumonScene: Patch UV",
            LumOnDebugMode.LumonSceneIrradiance => "LumonScene: Irradiance",
            LumOnDebugMode.LumonSceneMaterial => "LumonScene: Material Albedo",
            LumOnDebugMode.LumonSceneMaterialRoughness => "LumonScene: Material Roughness",
            LumOnDebugMode.LumonSceneMaterialAtlasAll => "LumonScene: Material Atlas Albedo (all layers)",
            LumOnDebugMode.LumonSceneMaterialAtlasAllRoughness => "LumonScene: Material Atlas Roughness (all layers)",
            LumOnDebugMode.LumonSceneMaterialAtlasAllNormals => "LumonScene: Material Atlas Normals (all layers)",
            LumOnDebugMode.LumonSceneChunkSlot => "LumonScene: ChunkSlot",
            LumOnDebugMode.LumonSceneSlotGeneration => "LumonScene: Slot Generation",
            LumOnDebugMode.LumonScenePageTableOccupancy => "LumonScene: PageTable Occupancy",
            LumOnDebugMode.TraceSceneBoundsL0 => "TraceScene: Bounds (L0)",
            LumOnDebugMode.TraceSceneOccupancyL0 => "TraceScene: Occupancy (L0)",
            LumOnDebugMode.TraceScenePayloadL0 => "TraceScene: Payload (L0)",
            LumOnDebugMode.TraceSceneDdaDistanceL0 => "TraceScene: Voxel DDA Distance (SDF)",
            LumOnDebugMode.LumOnScenesOverview => "LumOn Scenes Overview",
            _ => mode.ToString()
        };
    }
}
