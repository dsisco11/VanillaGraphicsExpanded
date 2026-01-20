using System;

using Cairo;

using VanillaGraphicsExpanded.ModSystems;
using VanillaGraphicsExpanded.PBR.Materials.Async;

using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.PBR.Materials.Diagnostics;

/// <summary>
/// Small in-game overlay that reports material atlas build progress while async builds are running.
/// </summary>
internal sealed class HudMaterialAtlasProgressPanel : HudElement
{
    private const string TrackerMpTextKey = "tracker-mp";
    private const string TrackerNdTextKey = "tracker-nd";
    private const string ProgressBarMpKey = "bar-mp";
    private const string ProgressBarNdKey = "bar-nd";

    private const int PanelWidth = 220;
    private const int PanelPadding = 6;
    private const int PanelMargin = 10;
    private const int LineHeight = 18;
    private const int Spacing = 4;
    private const int ProgressBarHeight = 10;

    private readonly MaterialAtlasSystem atlasSystem;

    private long tickListenerId;

    private int lastSeenGenerationId;
    private bool wasVisible;
    private float hideCountdownSeconds;

    private float mpProgress01;
    private float ndProgress01;

    public HudMaterialAtlasProgressPanel(ICoreClientAPI capi, MaterialAtlasSystem atlasSystem)
        : base(capi)
    {
        this.atlasSystem = atlasSystem ?? throw new ArgumentNullException(nameof(atlasSystem));

        tickListenerId = capi.Event.RegisterGameTickListener(OnGameTick, 200);

        Compose();
        TryClose();
    }

    public override string ToggleKeyCombinationCode => null!;

    private void Compose()
    {
        ElementBounds bgBounds = new ElementBounds()
            .WithSizing(ElementSizing.FitToChildren)
            .WithFixedPadding(PanelPadding);

        int y = 0;
        ElementBounds titleBounds = ElementBounds.Fixed(0, y, PanelWidth, LineHeight);
        y += LineHeight + Spacing;

        ElementBounds trackerMpBounds = ElementBounds.Fixed(0, y, PanelWidth, LineHeight);
        y += LineHeight + Spacing;

        ElementBounds barMpBounds = ElementBounds.Fixed(0, y, PanelWidth, ProgressBarHeight);
        y += ProgressBarHeight + Spacing;

        ElementBounds trackerNdBounds = ElementBounds.Fixed(0, y, PanelWidth, LineHeight);
        y += LineHeight + Spacing;

        ElementBounds barNdBounds = ElementBounds.Fixed(0, y, PanelWidth, ProgressBarHeight);

        bgBounds.WithChildren(titleBounds, trackerMpBounds, barMpBounds, trackerNdBounds, barNdBounds);

        ElementBounds dialogBounds = bgBounds
            .ForkBoundingParent()
            .WithAlignment(EnumDialogArea.RightBottom)
            .WithFixedPosition(-PanelMargin, -PanelMargin);

        SingleComposer?.Dispose();
        SingleComposer = capi.Gui
            .CreateCompo("vge-materialatlas-progress", dialogBounds)
            .AddGameOverlay(bgBounds, GuiStyle.DialogLightBgColor)
            .AddStaticText("Building Atlas", CairoFont.WhiteSmallText(), titleBounds)
            .AddDynamicText("Materials 0/0", CairoFont.WhiteSmallText(), trackerMpBounds, TrackerMpTextKey)
            .AddDynamicCustomDraw(barMpBounds, DrawMpProgressBar, ProgressBarMpKey)
            .AddDynamicText("Normals 0/0", CairoFont.WhiteSmallText(), trackerNdBounds, TrackerNdTextKey)
            .AddDynamicCustomDraw(barNdBounds, DrawNdProgressBar, ProgressBarNdKey)
            .Compose();
    }

    private void OnGameTick(float dt)
    {
        if (!ConfigModSystem.Config.MaterialAtlas.ShowMaterialAtlasProgressPanel)
        {
            HideImmediate();
            return;
        }

        if (!atlasSystem.TryGetAsyncBuildDiagnostics(out MaterialAtlasAsyncBuildDiagnostics diag) || diag.GenerationId <= 0)
        {
            HideImmediate();
            return;
        }

        int totalWork = Math.Max(0, diag.TotalTiles) + Math.Max(0, diag.TotalNormalDepthJobs);
        bool isActive = !diag.IsComplete && !diag.IsCancelled && totalWork > 0;

        if (diag.GenerationId != lastSeenGenerationId)
        {
            lastSeenGenerationId = diag.GenerationId;
            hideCountdownSeconds = 0;
        }

        if (isActive)
        {
            hideCountdownSeconds = 0;
            EnsureVisible();
            UpdateText(diag);
            return;
        }

        // Completed/cancelled: keep the panel visible briefly so it doesn't "blink".
        if (wasVisible && hideCountdownSeconds <= 0)
        {
            hideCountdownSeconds = 2.0f;
        }

        if (hideCountdownSeconds > 0)
        {
            hideCountdownSeconds -= dt;
            EnsureVisible();
            UpdateText(diag);
        }
        else
        {
            HideImmediate();
        }
    }

    private void UpdateText(in MaterialAtlasAsyncBuildDiagnostics diag)
    {
        if (SingleComposer is null)
        {
            return;
        }

        int mpTotal = Math.Max(0, diag.TotalTiles);
        int mpDone = Math.Clamp(diag.CompletedTiles, 0, mpTotal);

        int ndTotal = Math.Max(0, diag.TotalNormalDepthJobs);
        int ndDone = Math.Clamp(diag.CompletedNormalDepthJobs, 0, ndTotal);

        mpProgress01 = mpTotal > 0 ? (float)mpDone / mpTotal : 0f;
        ndProgress01 = ndTotal > 0 ? (float)ndDone / ndTotal : 0f;

        SingleComposer.GetDynamicText(TrackerMpTextKey).SetNewText($"MP {mpDone}/{mpTotal}");
        SingleComposer.GetDynamicText(TrackerNdTextKey).SetNewText($"ND {ndDone}/{ndTotal}");
        SingleComposer.GetCustomDraw(ProgressBarMpKey).Redraw();
        SingleComposer.GetCustomDraw(ProgressBarNdKey).Redraw();
    }

    private void DrawMpProgressBar(Context ctx, ImageSurface surface, ElementBounds currentBounds)
        => DrawProgressBar(ctx, surface, currentBounds, mpProgress01);

    private void DrawNdProgressBar(Context ctx, ImageSurface surface, ElementBounds currentBounds)
        => DrawProgressBar(ctx, surface, currentBounds, ndProgress01);

    private static void DrawProgressBar(Context ctx, ImageSurface surface, ElementBounds currentBounds, float progress01)
    {
        int w = Math.Max(1, currentBounds.OuterWidthInt);
        int h = Math.Max(1, currentBounds.OuterHeightInt);

        ctx.Operator = Operator.Source;
        ctx.SetSourceRGBA(0, 0, 0, 0);
        ctx.Paint();
        ctx.Operator = Operator.Over;

        const double borderAlpha = 0.45;
        const double bgAlpha = 0.20;
        const double fillAlpha = 0.75;

        // Background
        ctx.SetSourceRGBA(1, 1, 1, bgAlpha);
        ctx.Rectangle(0, 0, w, h);
        ctx.Fill();

        // Fill
        int innerW = Math.Max(0, w - 2);
        int innerH = Math.Max(0, h - 2);
        int fillW = (int)Math.Round(innerW * Math.Clamp(progress01, 0f, 1f));

        if (fillW > 0 && innerH > 0)
        {
            ctx.SetSourceRGBA(0.25, 0.85, 0.35, fillAlpha);
            ctx.Rectangle(1, 1, fillW, innerH);
            ctx.Fill();
        }

        // Border
        ctx.SetSourceRGBA(0, 0, 0, borderAlpha);
        ctx.LineWidth = 1;
        ctx.Rectangle(0.5, 0.5, w - 1, h - 1);
        ctx.Stroke();
    }

    private void EnsureVisible()
    {
        if (!wasVisible)
        {
            TryOpen(withFocus: false);
            wasVisible = true;
        }
    }

    private void HideImmediate()
    {
        hideCountdownSeconds = 0;

        if (wasVisible)
        {
            TryClose();
            wasVisible = false;
        }
    }

    public override void Dispose()
    {
        if (tickListenerId != 0)
        {
            capi.Event.UnregisterGameTickListener(tickListenerId);
            tickListenerId = 0;
        }

        base.Dispose();
    }
}
