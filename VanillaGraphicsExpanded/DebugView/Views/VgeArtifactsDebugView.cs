using System;

using VanillaGraphicsExpanded.PBR.Materials;

using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.DebugView;

public static partial class VgeBuiltInDebugViews
{
    private static DebugViewDefinition CreateArtifactsView()
        => new(
            id: ArtifactsViewId,
            name: "Artifacts",
            category: CategoryPbr,
            description: "Progress and stats for async cache artifact generators.",
            registerRenderer: _ => new ActionDisposable(() => { }),
            activationMode: DebugViewActivationMode.Toggle,
            createPanel: ctx => new ArtifactsPanel(ctx.Capi));

    private sealed class ArtifactsPanel : DebugViewPanelBase
    {
        private readonly ICoreClientAPI capi;

        private GuiComposer? composer;

        private long lastQueued;
        private long lastInFlight;
        private long lastCompleted;
        private long lastErrors;

        public ArtifactsPanel(ICoreClientAPI capi)
        {
            this.capi = capi;
        }

        public override bool WantsGameTick => true;

        public override void Compose(GuiComposer composer, ElementBounds bounds, string keyPrefix)
        {
            this.composer = composer;

            const double rowH = 22;
            const double gapY = 6;
            const double w = 460;

            var font = CairoFont.WhiteSmallText();

            long queued = 0;
            long inflight = 0;
            long completed = 0;
            long errors = 0;

            if (PbrMaterialRegistry.Instance.TryGetBaseColorRegenStatsSnapshot(out var stats))
            {
                queued = stats.Queued;
                inflight = stats.InFlight;
                completed = stats.Completed;
                errors = stats.Errors;
            }

            lastQueued = queued;
            lastInFlight = inflight;
            lastCompleted = completed;
            lastErrors = errors;

            ElementBounds b0 = ElementBounds.Fixed(0, 0, w, rowH).WithParent(bounds);
            ElementBounds b1 = ElementBounds.Fixed(0, rowH + gapY, w, rowH).WithParent(bounds);
            ElementBounds b2 = ElementBounds.Fixed(0, (rowH + gapY) * 2, w, rowH).WithParent(bounds);
            ElementBounds b3 = ElementBounds.Fixed(0, (rowH + gapY) * 3, w, rowH).WithParent(bounds);

            composer
                .AddStaticText("BaseColor (ArtifactScheduler)", font, b0, key: $"{keyPrefix}-title")
                .AddStaticText($"Queued: {queued}", font, b1, key: $"{keyPrefix}-q")
                .AddStaticText($"InFlight: {inflight}", font, b2, key: $"{keyPrefix}-f")
                .AddStaticText($"Completed: {completed}  Errors: {errors}", font, b3, key: $"{keyPrefix}-c");
        }

        public override void OnGameTick(float dt)
        {
            long queued = 0;
            long inflight = 0;
            long completed = 0;
            long errors = 0;

            if (PbrMaterialRegistry.Instance.TryGetBaseColorRegenStatsSnapshot(out var stats))
            {
                queued = stats.Queued;
                inflight = stats.InFlight;
                completed = stats.Completed;
                errors = stats.Errors;
            }

            if (queued != lastQueued || inflight != lastInFlight || completed != lastCompleted || errors != lastErrors)
            {
                lastQueued = queued;
                lastInFlight = inflight;
                lastCompleted = completed;
                lastErrors = errors;

                try
                {
                    composer?.ReCompose();
                }
                catch
                {
                    // Ignore UI refresh failures.
                }
            }
        }
    }
}
