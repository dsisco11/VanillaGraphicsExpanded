using System;
using VanillaGraphicsExpanded.Rendering.Shadows.Configuration;
using VanillaGraphicsExpanded.Rendering.Shadows.Pages;
using VanillaGraphicsExpanded.Rendering.Shadows.Projections;

namespace VanillaGraphicsExpanded.Rendering.Shadows.Sampling;

/// <summary>Supplies current illumination identity and bounded reuse policy without replacing the rendered mapping.</summary>
internal sealed class ShadowTemporalQuery
{
    private readonly ShadowTemporalSettings policy;
    private readonly double now;
    internal ShadowProjectionSnapshot Current { get; }

    #region Public API
    /// <summary>Freezes current source data and a monotonic render-clock sample for GPU eligibility checks.</summary>
    internal ShadowTemporalQuery(ShadowProjectionSnapshot current, double now, ShadowTemporalSettings settings)
    {
        ArgumentNullException.ThrowIfNull(current); ArgumentNullException.ThrowIfNull(settings);
        if (!double.IsFinite(now) || now < 0) throw new ArgumentOutOfRangeException(nameof(now));
        Current = current; this.now = now; policy = settings.Freeze();
    }

    /// <summary>Prepares precise relative motion before narrowing, bound to the exact rendered mapping certified on GPU.</summary>
    internal void WriteTo(Span<uint> destination, ShadowProjectionSnapshot rendered)
    {
        if (destination.Length < 146) throw new ArgumentException("Temporal sampling requires 146 words.", nameof(destination));
        if (rendered.Source != Current.Source || rendered.IsPoint != Current.IsPoint)
            throw new ArgumentException("Temporal motion requires the same source lifetime and projection kind.", nameof(rendered));
        ShadowClock.Write(destination, 128, now);
        ShadowClock.Write(destination, 130, policy.MaxRenderedAgeSeconds, duration: true);
        ShadowClock.Write(destination, 132, policy.MaxStaleAgeSeconds, duration: true);
        ShadowPageRequest.Put(destination, 138, Current.Generation);
        var delta = Current.PositionOrForward - rendered.PositionOrForward;
        double distanceSquared = Numerics.Vector3d.Dot(delta, delta);
        double cosine = Numerics.Vector3d.Dot(Current.PositionOrForward, rendered.PositionOrForward);
        // Decide the precise inclusive policy boundary at setup, before FP32 demotion.
        // The shader accepts this result only after matching the immutable rendered mapping.
        bool allowed = Current.IsPoint
            ? distanceSquared <= policy.MaxPointTranslationMetres * policy.MaxPointTranslationMetres
            : distanceSquared == 0 || cosine >= Math.Cos(policy.MaxDirectionalAngleDegrees * Math.PI / 180);
        destination[134] = allowed ? 1u : 0u;
        ShadowCoordinates.Vector(destination, 140, new((float)delta.X, (float)delta.Y, (float)delta.Z));
        // The current generation is the coherent illumination identity across cube faces.
        destination[143] = destination[144] = destination[145] = 0;
    }
    #endregion

}
