using System;
using System.Collections.Generic;
using VanillaGraphicsExpanded.PBR.Atmosphere.Celestial;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Integration;
using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.PBR.Atmosphere;

/// <summary>Owns sky scheduling and submits celestial and atmospheric passes as one ordered draw callback.</summary>
internal sealed class SkyRenderer : IRenderer
{
    private static SkyRenderer? active;
    private readonly ICoreClientAPI api;
    private readonly CelestialDrawPass celestial;
    private readonly MoonDrawPass moon;
    private readonly SunDrawPass sun;
    private readonly AtmosphereSkyPass atmosphere;
    private bool disposed;

    #region Public API
    #region Ownership
    /// <summary>Places the complete sky before subsequent opaque geometry, clouds and local effects.</summary>
    public double RenderOrder => .19;
    /// <summary>All sky geometry is camera relative.</summary>
    public int RenderRange => 1;
    /// <summary>Reports scheduled replacement ownership independently of per-pass readiness.</summary>
    internal static bool IsEnabled => active is { disposed: false };

    /// <summary>Composes independently recoverable passes and registers one preparation and one draw callback.</summary>
    internal SkyRenderer(ICoreClientAPI api, GBufferManager gbuffer)
    {
        this.api = api;
        try
        {
            celestial = new(api, gbuffer);
            moon = new(api, gbuffer);
            sun = new(api, gbuffer);
            atmosphere = new(api, gbuffer);
            api.Event.RegisterRenderer(this, EnumRenderStage.Before, "vge_sky_prepare");
            api.Event.RegisterRenderer(this, EnumRenderStage.Opaque, "vge_sky");
            active = this;
        }
        catch (Exception error)
        {
            try { Dispose(); }
            catch (Exception cleanup) { throw new AggregateException(error, cleanup); }
            throw;
        }
    }

    /// <summary>Withdraws scheduling before retiring each independent pass and its resource lifetimes.</summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (ReferenceEquals(active, this)) active = null;
        RenderingCleanup.Run(
            () => api.Event.UnregisterRenderer(this, EnumRenderStage.Before),
            () => api.Event.UnregisterRenderer(this, EnumRenderStage.Opaque),
            () => atmosphere?.Dispose(), () => sun?.Dispose(), () => moon?.Dispose(), () => celestial?.Dispose());
    }
    #endregion

    #region Preparation and submission
    /// <summary>Certifies atmospheric storage for the scene-color admission owner without submitting sky draws.</summary>
    internal bool PrepareFrame() => !disposed && atmosphere.PrepareFrame();

    /// <summary>Admits frame sources before storage freeze, then prepares and draws all sky passes in fixed order.</summary>
    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        if (disposed || !ReferenceEquals(active, this)) return;
        if (stage == EnumRenderStage.Before)
        {
            celestial.PrepareFrame();
            moon.PrepareFrame();
            sun.PrepareFrame();
        }
        else if (stage == EnumRenderStage.Opaque)
        {
            // Preparation can allocate or reload; it must finish before entering graphics authority.
            var draws = new List<SkyDraw>(4);
            if (celestial.PrepareDraw() is { } background) draws.Add(background);
            if (moon.PrepareDraw() is { } lunar) draws.Add(lunar);
            if (sun.PrepareDraw() is { } solar) draws.Add(solar);
            if (atmosphere.PrepareDraw() is { } sky) draws.Add(sky);
            SkyDraw.Run(draws);
        }
    }
    #endregion
    #endregion
}
