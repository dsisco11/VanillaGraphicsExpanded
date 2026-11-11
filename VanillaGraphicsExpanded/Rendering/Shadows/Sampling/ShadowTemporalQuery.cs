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

    /// <summary>Packs limits separately from the retained projection and never changes its source lifetime.</summary>
    internal void WriteTo(Span<uint> destination)
    {
        if (destination.Length < 146) throw new ArgumentException("Temporal sampling requires 146 words.", nameof(destination));
        Number(destination, 128, now);
        Number(destination, 130, policy.MaxRenderedAgeSeconds);
        Number(destination, 132, policy.MaxStaleAgeSeconds);
        Number(destination, 134, policy.MaxPointTranslationMetres);
        Number(destination, 136, Math.Cos(policy.MaxDirectionalAngleDegrees * Math.PI / 180));
        ShadowPageRequest.Put(destination, 138, Current.Generation);
        Number(destination, 140, Current.PositionOrForward.X);
        Number(destination, 142, Current.PositionOrForward.Y);
        Number(destination, 144, Current.PositionOrForward.Z);
    }
    #endregion

    #region Private
    /// <summary>Preserves double clock and motion precision in the shared GPU word representation.</summary>
    private static void Number(Span<uint> words, int offset, double value) =>
        ShadowPageRequest.Put(words, offset, BitConverter.DoubleToInt64Bits(value));
    #endregion
}
