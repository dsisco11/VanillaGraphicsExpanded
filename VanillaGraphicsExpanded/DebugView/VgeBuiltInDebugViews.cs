using System;
using System.Linq;

using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.Rendering.Profiling;

using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

namespace VanillaGraphicsExpanded.DebugView;

public static class VgeBuiltInDebugViews
{
    private const string CategoryLumOn = "LumOn";
    private const string CategoryProfiling = "Profiling";
    private const string CategoryGBuffer = "GBuffer";

    private const string LumOnDebugViewId = "vge.lumon.debug";
    private const string GpuProfilerViewId = "vge.profiling.gpu";
    private const string GpuDebugGroupsViewId = "vge.profiling.gpuDebugGroups";
    private const string GBufferOverlayViewId = "vge.gbuffer.overlay";

    public static void RegisterAll(
        ICoreClientAPI capi,
        GBufferManager gBufferManager)
    {
        if (capi is null) throw new ArgumentNullException(nameof(capi));
        if (gBufferManager is null) throw new ArgumentNullException(nameof(gBufferManager));

        DebugViewRegistry.Instance.LogWarning ??= msg => capi.Logger.Warning(msg);
        DebugViewController.Instance.LogWarning ??= msg => capi.Logger.Warning(msg);

        DebugViewRegistry.Instance.Register(CreateLumOnDebugView());
        DebugViewRegistry.Instance.Register(CreateGpuProfilerView());
        DebugViewRegistry.Instance.Register(CreateGpuDebugGroupsView());
        DebugViewRegistry.Instance.Register(CreateGBufferOverlayView(gBufferManager));
    }

    private static DebugViewDefinition CreateLumOnDebugView()
        => new(
            id: LumOnDebugViewId,
            name: "Debug Overlay",
            category: CategoryLumOn,
            description: "Select and render a LumOn fullscreen debug visualization (AfterBlit).",
            registerRenderer: ctx =>
            {
                LumOnDebugMode mode = LumOnDebugViewState.GetSelectedModeOrDefault();
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
            createPanel: ctx => new LumOnDebugPanel(viewId: LumOnDebugViewId, ctx.Capi, ctx.Config));

    private static DebugViewDefinition CreateGpuProfilerView()
        => new(
            id: GpuProfilerViewId,
            name: "GPU Profiler",
            category: CategoryProfiling,
            description: "View GlGpuProfiler snapshot (with prefix filter and sort).",
            registerRenderer: _ =>
            {
                GlGpuProfiler.Instance.Enabled = true;
                return new ActionDisposable(() => GlGpuProfiler.Instance.Enabled = false);
            },
            activationMode: DebugViewActivationMode.Toggle,
            createPanel: ctx => new GpuProfilerPanel(viewId: GpuProfilerViewId, ctx.Capi));

    private static DebugViewDefinition CreateGpuDebugGroupsView()
        => new(
            id: GpuDebugGroupsViewId,
            name: "GPU Debug Groups",
            category: CategoryProfiling,
            description: "Emit GL debug groups for GlGpuProfiler scopes (useful in RenderDoc/Nsight).",
            registerRenderer: _ =>
            {
                GlGpuProfiler.Instance.EmitDebugGroups = true;
                return new ActionDisposable(() => GlGpuProfiler.Instance.EmitDebugGroups = false);
            },
            activationMode: DebugViewActivationMode.Toggle);

    private static DebugViewDefinition CreateGBufferOverlayView(GBufferManager gBufferManager)
        => new(
            id: GBufferOverlayViewId,
            name: "Overlay",
            category: CategoryGBuffer,
            description: "Fullscreen overlay that blits a selected GBuffer/Primary attachment (AfterBlit).",
            registerRenderer: ctx =>
            {
                var renderer = new VgeGBufferOverlayRenderer(ctx.Capi, gBufferManager);
                return renderer;
            },
            activationMode: DebugViewActivationMode.Exclusive,
            createPanel: ctx => new GBufferOverlayPanel(viewId: GBufferOverlayViewId, ctx.Capi));


    private static class LumOnDebugViewState
    {
        private static LumOnDebugMode selectedMode = LumOnDebugMode.ProbeGrid;

        public static LumOnDebugMode GetSelectedModeOrDefault()
        {
            LumOnDebugMode mode = selectedMode;
            if (!Enum.IsDefined(typeof(LumOnDebugMode), mode) || mode == LumOnDebugMode.Off)
            {
                return LumOnDebugMode.ProbeGrid;
            }

            return mode;
        }

        public static void SetSelectedMode(LumOnDebugMode mode)
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

        private readonly string[] values;
        private readonly string[] names;

        public LumOnDebugPanel(string viewId, ICoreClientAPI capi, VgeConfig config)
        {
            this.viewId = viewId;
            this.capi = capi;
            this.config = config;

            var modes = Enum.GetValues(typeof(LumOnDebugMode)).Cast<LumOnDebugMode>()
                .Where(m => m != LumOnDebugMode.Off)
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

            int selectedIndex = Array.IndexOf(values, LumOnDebugViewState.GetSelectedModeOrDefault().ToString());
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

            LumOnDebugViewState.SetSelectedMode(mode);

            if (string.Equals(DebugViewController.Instance.ActiveExclusiveViewId, viewId, StringComparison.Ordinal))
            {
                config.LumOn.DebugMode = mode;
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
            LumOnDebugMode.VelocityMagnitude => "Velocity Magnitude (Phase 14)",
            LumOnDebugMode.VelocityValidity => "Velocity Validity (Phase 14)",
            LumOnDebugMode.VelocityPrevUv => "Velocity Prev UV (Phase 14)",
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
            LumOnDebugMode.WorldProbeRawConfidences => "World-Probe Raw Confidences (Phase 18)",
            LumOnDebugMode.WorldProbeContributionOnly => "Contribution Only: world-probe (Phase 18)",
            LumOnDebugMode.ScreenSpaceContributionOnly => "Contribution Only: screen-space (Phase 18)",
            _ => mode.ToString()
        };
    }

    private sealed class GpuProfilerPanel : DebugViewPanelBase
    {
        private readonly string viewId;
        private readonly ICoreClientAPI capi;

        private readonly string[] enabledValues = ["On", "Off"];
        private readonly string[] enabledNames = ["On", "Off"];

        private readonly string[] categoryValues = ["All", "PBR", "LumOn", "Debug"];
        private readonly string[] categoryNames = ["All", "PBR", "LumOn", "Debug"];

        private readonly string[] sortValues = ["Name", "LastMs", "AvgMs", "MaxMs"];
        private readonly string[] sortNames = ["Name", "Last", "Avg", "Max"];

        private GuiComposer? composer;
        private string? keyPrefix;

        public GpuProfilerPanel(string viewId, ICoreClientAPI capi)
        {
            this.viewId = viewId;
            this.capi = capi;
        }

        public override bool WantsGameTick => true;

        public override void Compose(GuiComposer composer, ElementBounds bounds, string keyPrefix)
        {
            this.composer = composer;
            this.keyPrefix = keyPrefix;

            const double rowH = 30;
            const double labelW = 120;
            const double gap = 10;
            double boundsW = bounds.fixedWidth > 0 ? bounds.fixedWidth : bounds.OuterWidth;
            double boundsH = bounds.fixedHeight > 0 ? bounds.fixedHeight : bounds.OuterHeight;
            double controlW = Math.Min(280, Math.Max(200, boundsW - labelW - gap));

            var fontLabel = CairoFont.WhiteDetailText();
            var fontSmall = CairoFont.WhiteSmallText();

            ElementBounds labelEnabled = ElementBounds.Fixed(0, 0, labelW, rowH).WithParent(bounds);
            ElementBounds ctrlEnabled = ElementBounds.Fixed(labelW + gap, 0, controlW, rowH).WithParent(bounds);

            ElementBounds labelCat = ElementBounds.Fixed(0, rowH + 8, labelW, rowH).WithParent(bounds);
            ElementBounds ctrlCat = ElementBounds.Fixed(labelW + gap, rowH + 8, controlW, rowH).WithParent(bounds);

            ElementBounds labelSort = ElementBounds.Fixed(0, (rowH + 8) * 2, labelW, rowH).WithParent(bounds);
            ElementBounds ctrlSort = ElementBounds.Fixed(labelW + gap, (rowH + 8) * 2, controlW, rowH).WithParent(bounds);

            ElementBounds textBounds = ElementBounds.Fixed(0, (rowH + 8) * 3 + 8, boundsW, Math.Max(60, boundsH - ((rowH + 8) * 3 + 8))).WithParent(bounds);

            int enabledIndex = DebugViewController.Instance.IsActive(viewId) ? 0 : 1;

            composer
                .AddStaticText("Enabled", fontLabel, labelEnabled)
                .AddInteractiveElement(
                    new GuiElementDropDownCycleOnArrow(
                        capi,
                        enabledValues,
                        enabledNames,
                        enabledIndex,
                        OnEnabledChanged,
                        ctrlEnabled,
                        fontSmall),
                    $"{keyPrefix}-enabled")

                .AddStaticText("Category", fontLabel, labelCat)
                .AddInteractiveElement(
                    new GuiElementDropDownCycleOnArrow(
                        capi,
                        categoryValues,
                        categoryNames,
                        0,
                        OnSelectionChanged,
                        ctrlCat,
                        fontSmall),
                    $"{keyPrefix}-cat")

                .AddStaticText("Sort", fontLabel, labelSort)
                .AddInteractiveElement(
                    new GuiElementDropDownCycleOnArrow(
                        capi,
                        sortValues,
                        sortNames,
                        1,
                        OnSelectionChanged,
                        ctrlSort,
                        fontSmall),
                    $"{keyPrefix}-sort")

                .AddDynamicText("(Profiler will populate after a few frames.)", fontSmall, textBounds, $"{keyPrefix}-text");
        }

        public override void OnOpened()
        {
            RefreshText();
        }

        public override void OnGameTick(float dt)
        {
            RefreshText();
        }

        private void OnEnabledChanged(string code, bool selected)
        {
            if (!selected)
            {
                return;
            }

            bool wantsOn = code == "On";
            bool isOn = DebugViewController.Instance.IsActive(viewId);
            if (wantsOn != isOn)
            {
                _ = DebugViewController.Instance.TryActivate(viewId, out _);
            }

            RefreshText();
        }

        private void OnSelectionChanged(string _code, bool selected)
        {
            if (!selected)
            {
                return;
            }

            RefreshText();
        }

        private void RefreshText()
        {
            if (composer is null || string.IsNullOrWhiteSpace(keyPrefix))
            {
                return;
            }

            try
            {
                var dyn = composer.GetDynamicText($"{keyPrefix}-text");

                string prefix = GetPrefix();
                string? prefixFilter = string.IsNullOrEmpty(prefix) ? null : prefix;
                var sort = GetSort();
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
            catch
            {
                // Ignore UI refresh errors.
            }
        }

        private string GetPrefix()
        {
            if (composer is null || string.IsNullOrWhiteSpace(keyPrefix))
            {
                return string.Empty;
            }

            string? val = composer.GetDropDown($"{keyPrefix}-cat").SelectedValue;
            return val switch
            {
                "PBR" => "PBR.",
                "LumOn" => "LumOn.",
                "Debug" => "Debug.",
                _ => string.Empty
            };
        }

        private GpuProfileSort GetSort()
        {
            if (composer is null || string.IsNullOrWhiteSpace(keyPrefix))
            {
                return GpuProfileSort.LastMs;
            }

            string? val = composer.GetDropDown($"{keyPrefix}-sort").SelectedValue;
            return val switch
            {
                "Name" => GpuProfileSort.Name,
                "LastMs" => GpuProfileSort.LastMs,
                "AvgMs" => GpuProfileSort.AvgMs,
                "MaxMs" => GpuProfileSort.MaxMs,
                _ => GpuProfileSort.LastMs
            };
        }
    }

    private static class GBufferOverlayViewState
    {
        public static GBufferOverlayMode Mode { get; set; } = GBufferOverlayMode.Normals;
    }

    private sealed class GBufferOverlayPanel : DebugViewPanelBase
    {
        private readonly string viewId;
        private readonly ICoreClientAPI capi;

        private readonly string[] values = Enum.GetNames(typeof(GBufferOverlayMode));
        private readonly string[] names = Enum.GetNames(typeof(GBufferOverlayMode));

        public GBufferOverlayPanel(string viewId, ICoreClientAPI capi)
        {
            this.viewId = viewId;
            this.capi = capi;
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

            int selectedIndex = Array.IndexOf(values, GBufferOverlayViewState.Mode.ToString());
            if (selectedIndex < 0) selectedIndex = 0;

            composer
                .AddStaticText("Attachment", fontLabel, labelBounds)
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

            if (!Enum.TryParse(code, out GBufferOverlayMode mode))
            {
                return;
            }

            GBufferOverlayViewState.Mode = mode;
        }
    }

    public enum GBufferOverlayMode
    {
        Normals = 0,
        Material = 1,
        Depth = 2,
        PrimaryColor = 3
    }

    private sealed class VgeGBufferOverlayRenderer : IRenderer, IDisposable
    {
        private const double RenderOrderValue = 1.0;
        private const int RenderRangeValue = 1;

        private readonly ICoreClientAPI capi;
        private readonly GBufferManager gBufferManager;

        private MeshRef? quadMeshRef;

        public double RenderOrder => RenderOrderValue;
        public int RenderRange => RenderRangeValue;

        public VgeGBufferOverlayRenderer(ICoreClientAPI capi, GBufferManager gBufferManager)
        {
            this.capi = capi;
            this.gBufferManager = gBufferManager;

            var quadMesh = QuadMeshUtil.GetCustomQuadModelData(-1, -1, 0, 2, 2);
            quadMesh.Rgba = null;
            quadMeshRef = capi.Render.UploadMesh(quadMesh);

            capi.Event.RegisterRenderer(this, EnumRenderStage.AfterBlit, "vge_gbuffer_overlay");
        }

        public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
        {
            if (stage != EnumRenderStage.AfterBlit)
            {
                return;
            }

            if (quadMeshRef is null)
            {
                return;
            }

            int textureId = GetTextureId();
            if (textureId == 0)
            {
                return;
            }

            bool prevDepthTest = OpenTK.Graphics.OpenGL.GL.IsEnabled(OpenTK.Graphics.OpenGL.EnableCap.DepthTest);
            bool prevBlend = OpenTK.Graphics.OpenGL.GL.IsEnabled(OpenTK.Graphics.OpenGL.EnableCap.Blend);
            bool prevDepthMask = OpenTK.Graphics.OpenGL.GL.GetBoolean(OpenTK.Graphics.OpenGL.GetPName.DepthWritemask);
            int prevActiveTexture = OpenTK.Graphics.OpenGL.GL.GetInteger(OpenTK.Graphics.OpenGL.GetPName.ActiveTexture);

            var blitShader = capi.Render.GetEngineShader(EnumShaderProgram.Blit);
            blitShader.Use();

            try
            {
                capi.Render.GLDepthMask(false);
                OpenTK.Graphics.OpenGL.GL.Disable(OpenTK.Graphics.OpenGL.EnableCap.DepthTest);
                capi.Render.GlToggleBlend(false);

                OpenTK.Graphics.OpenGL.GL.ActiveTexture(OpenTK.Graphics.OpenGL.TextureUnit.Texture0);
                OpenTK.Graphics.OpenGL.GL.BindTexture(OpenTK.Graphics.OpenGL.TextureTarget.Texture2D, textureId);
                blitShader.BindTexture2D("scene", textureId, 0);

                capi.Render.RenderMesh(quadMeshRef);
            }
            finally
            {
                blitShader.Stop();

                if (prevDepthTest) OpenTK.Graphics.OpenGL.GL.Enable(OpenTK.Graphics.OpenGL.EnableCap.DepthTest);
                else OpenTK.Graphics.OpenGL.GL.Disable(OpenTK.Graphics.OpenGL.EnableCap.DepthTest);

                capi.Render.GLDepthMask(prevDepthMask);
                capi.Render.GlToggleBlend(prevBlend);
                OpenTK.Graphics.OpenGL.GL.ActiveTexture((OpenTK.Graphics.OpenGL.TextureUnit)prevActiveTexture);
            }
        }

        private int GetTextureId()
        {
            return GBufferOverlayViewState.Mode switch
            {
                GBufferOverlayMode.Normals => gBufferManager.NormalTextureId,
                GBufferOverlayMode.Material => gBufferManager.MaterialTextureId,
                GBufferOverlayMode.Depth => capi.Render.FrameBuffers[(int)EnumFrameBuffer.Primary].DepthTextureId,
                GBufferOverlayMode.PrimaryColor => capi.Render.FrameBuffers[(int)EnumFrameBuffer.Primary].ColorTextureIds[0],
                _ => 0
            };
        }

        public void Dispose()
        {
            capi.Event.UnregisterRenderer(this, EnumRenderStage.AfterBlit);

            if (quadMeshRef is not null)
            {
                capi.Render.DeleteMesh(quadMeshRef);
                quadMeshRef = null;
            }
        }
    }
}
