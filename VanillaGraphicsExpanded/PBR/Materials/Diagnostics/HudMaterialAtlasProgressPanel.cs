using System;

using VanillaGraphicsExpanded.ModSystems;
using VanillaGraphicsExpanded.PBR.Materials.Async;

using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.PBR.Materials.Diagnostics;

/// <summary>
/// Small in-game overlay that reports material atlas build progress while async builds are running.
/// </summary>
internal sealed class HudMaterialAtlasProgressPanel : HudElement
{
    private readonly MaterialAtlasSystem atlasSystem;

    private long tickListenerId;

    private int lastSeenGenerationId;
    private bool wasVisible;
    private float hideCountdownSeconds;

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
            .WithFixedPadding(GuiStyle.ElementToDialogPadding / 2);

        ElementBounds textBounds = ElementBounds.Fixed(0, 0, 360, 120);
        bgBounds.WithChildren(textBounds);

        ElementBounds dialogBounds = bgBounds
            .ForkBoundingParent()
            .WithAlignment(EnumDialogArea.None)
            .WithAlignment(EnumDialogArea.LeftTop)
            .WithFixedPosition(10, 10);

        SingleComposer?.Dispose();
        SingleComposer = capi.Gui
            .CreateCompo("vge-materialatlas-progress", dialogBounds)
            .AddGameOverlay(bgBounds, GuiStyle.DialogLightBgColor)
            .AddDynamicText("(material atlas)", CairoFont.WhiteSmallText(), textBounds, "text")
            .Compose();
    }

    private void OnGameTick(float dt)
    {
        if (!ConfigModSystem.Config.ShowMaterialAtlasProgressPanel)
        {
            HideImmediate();
            return;
        }

        if (!atlasSystem.TryGetAsyncBuildDiagnostics(out MaterialAtlasAsyncBuildDiagnostics diag) || diag.GenerationId <= 0)
        {
            HideImmediate();
            return;
        }

        bool isActive = !diag.IsComplete && !diag.IsCancelled && diag.TotalTiles > 0;

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

        int total = Math.Max(1, diag.TotalTiles);
        float pct = 100f * diag.CompletedTiles / total;

        string status = diag.IsCancelled ? "cancelled" : (diag.IsComplete ? "complete" : "building");

        string text =
            $"Material atlas ({status})\n" +
            $"Tiles: {diag.CompletedTiles}/{diag.TotalTiles} ({pct:0.0}%)  Overrides: {diag.OverridesApplied}/{diag.TotalOverrides}\n" +
            $"Queues: cpu={diag.PendingCpuJobs} done={diag.CompletedCpuResults} gpu={diag.PendingGpuUploads} ov={diag.PendingOverrideUploads}\n" +
            $"Frame: {diag.LastTickMs:0.###}ms  cpu+{diag.LastDispatchedCpuJobs} gpu+{diag.LastGpuUploads} ov+{diag.LastOverrideUploads}";

        SingleComposer.GetDynamicText("text").SetNewText(text);
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
