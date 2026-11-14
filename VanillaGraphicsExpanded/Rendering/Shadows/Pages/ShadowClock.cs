using System;

namespace VanillaGraphicsExpanded.Rendering.Shadows.Pages;

/// <summary>Transports monotonic time as integer nanoseconds without a GPU floating point epoch.</summary>
internal static class ShadowClock
{
    #region Public API
    /// <summary>Encodes a nonnegative render-clock sample; sub-nanosecond input is rounded to the clock resolution.</summary>
    internal static void Write(Span<uint> words, int offset, double seconds, bool duration = false)
    {
        const ulong ticksPerSecond = 1_000_000_000;
        if (!double.IsFinite(seconds) || seconds < 0 || seconds >= 18_446_744_073)
            throw new ArgumentOutOfRangeException(nameof(seconds));
        // Split before scaling: a large uptime must not erase the fractional part.
        double whole = Math.Floor(seconds);
        double fraction = (seconds - whole) * ticksPerSecond;
        ulong ticks = checked((ulong)whole * ticksPerSecond + (ulong)(duration ? Math.Floor(fraction) : Math.Round(fraction)));
        words[offset] = (uint)ticks; words[offset + 1] = (uint)(ticks >> 32);
    }
    #endregion
}
