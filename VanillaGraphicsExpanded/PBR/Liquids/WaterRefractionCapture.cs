using System;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using HarmonyLib;
using VanillaGraphicsExpanded.ModSystems;
using VanillaGraphicsExpanded.Rendering;
using Vintagestory.API.Client;
using Vintagestory.GameContent;

namespace VanillaGraphicsExpanded.PBR.Liquids;

/// <summary>Captures coherent world lighting and depth without changing any engine mesh submission.</summary>
internal sealed class WaterRefractionCapture : IRenderer
{
    private static WaterRefractionCapture? active;
    private static readonly AccessTools.FieldRef<EntityPlayerShapeRenderer, RenderMode> ReadMode =
        AccessTools.FieldRefAccess<EntityPlayerShapeRenderer, RenderMode>("renderMode");
    private static readonly Func<EntityPlayerShapeRenderer, bool> IsSelf =
        AccessTools.MethodDelegate<Func<EntityPlayerShapeRenderer, bool>>(
            AccessTools.PropertyGetter(typeof(EntityPlayerShapeRenderer), "IsSelf"));
    private readonly ICoreClientAPI api;
    private readonly DirectLightingRenderer direct;
    private readonly PBRCompositeRenderer composite;
    private readonly WaterRefractionScene scene = new();
    private DirectLightingTargets? lighting;
    private bool attempted;
    private bool warned;
    public double RenderOrder => 0;
    public int RenderRange => int.MaxValue;

    #region Public API
    /// <summary>Registers frame invalidation and lends the captured pair to the final composite owner.</summary>
    internal WaterRefractionCapture(ICoreClientAPI api, DirectLightingRenderer direct, PBRCompositeRenderer composite)
    {
        this.api = api;
        this.direct = direct;
        this.composite = composite;
        composite.PreOverlayScene = scene;
        active = this;
        api.Event.RegisterRenderer(this, EnumRenderStage.Before, "vge_refraction_capture_reset");
        api.Event.LeaveWorld += Retire;
    }

    /// <summary>Runs before the original local hand projection; the Harmony prefix never suppresses that method.</summary>
    internal static void BeforeOverlay(EntityPlayerShapeRenderer renderer, bool shadowPass)
    {
        var capture = active;
        if (capture is null || capture.attempted || !ShouldCapture(ConfigModSystem.Config.WaterRefractionEnabled,
            capture.api.Render.CurrentRenderStage, shadowPass, IsSelf(renderer), ReadMode(renderer))) return;
        // Optional snapshot failure must not prevent the original engine overlay from submitting.
        try { capture.Capture(); }
        catch (Exception error)
        {
            capture.scene.Invalidate();
            if (EngineBoundaryRestoreException.IsRestorationFailure(error)) throw;
            if (!capture.warned) capture.api.Logger.Warning("[VGE] Pre-overlay refraction capture unavailable: {0}", error.Message);
            capture.warned = true;
        }
    }

    /// <summary>Restricts capture to the actual engine hand-mode world overwrite boundary.</summary>
    internal static bool ShouldCapture(bool enabled, EnumRenderStage stage, bool shadowPass, bool self, RenderMode mode)
        => enabled && stage == EnumRenderStage.Opaque && !shadowPass && self && mode == RenderMode.FirstPerson;

    /// <summary>Withdraws previous-frame publication, including when no overlay submits this frame.</summary>
    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        attempted = false;
        scene.Invalidate();
        if (!ConfigModSystem.Config.WaterRefractionEnabled) Retire();
    }

    /// <summary>Unregisters callbacks and releases isolated resources without touching engine targets.</summary>
    public void Dispose()
    {
        api.Event.UnregisterRenderer(this, EnumRenderStage.Before);
        api.Event.LeaveWorld -= Retire;
        composite.PreOverlayScene = null;
        Retire();
        if (ReferenceEquals(active, this)) active = null;
    }
    #endregion

    #region Private
    /// <summary>Evaluates the existing PBR passes into owned storage, restoring the caller's draw state even on failure.</summary>
    private void Capture()
    {
        StateCache.Current.RequireOutsideEngineBoundary();
        attempted = true;
        scene.Invalidate();
        // Unknown resources retain capture. Only current, entirely empty liquid
        // pools prove that no interface can consume the pre-overlay receiver pair.
        // Keep allocations across dry frames so returning water reuses them.
        if (LiquidMeshSource.TryGet(api, out var source) && !source.MayHaveLiquidGeometry()) return;
        int width = api.Render.FrameWidth, height = api.Render.FrameHeight;
        if (width <= 0 || height <= 0) return;
        var directPipeline = direct.PrepareBoundaryPipeline();
        var compositePrograms = composite.PrepareBoundaryPrograms(capture: true);
        if (directPipeline is null || compositePrograms is null) return;
        try
        {
            GraphicsCommandContext.TryRunShared("WaterRefraction.PreOverlay", [directPipeline],
                compositePrograms, true, (commands, scope) =>
                {
                    // Allocation belongs inside the same preservation contract as both draws.
                    if (lighting?.IsValid != true || lighting.DirectDiffuse.Width != width || lighting.DirectDiffuse.Height != height)
                    {
                        scene.Dispose();
                        lighting?.Dispose();
                        lighting = new DirectLightingTargets(width, height);
                    }
                    if (direct.RenderLightingWithinBoundary(commands, directPipeline, lighting))
                        composite.RenderCompositeWithinBoundary(scope, EnumRenderStage.Opaque, scene, lighting);
                });
        }
        catch { scene.Invalidate(); throw; }
    }

    /// <summary>Retires the color/depth pair and its lighting scratch together on world or option boundaries.</summary>
    private void Retire()
    {
        scene.Dispose();
        lighting?.Dispose();
        lighting = null;
        attempted = false;
        warned = false;
    }
    #endregion
}
