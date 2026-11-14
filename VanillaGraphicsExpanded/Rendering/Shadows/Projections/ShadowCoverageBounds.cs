using System;
using VanillaGraphicsExpanded.Rendering.Shadows.Geometry;

namespace VanillaGraphicsExpanded.Rendering.Shadows.Projections;

/// <summary>Represents certified enclosures with exact chunk identities and local FP32 endpoints.</summary>
internal static class ShadowCoverageBounds
{
    #region Public API
    /// <summary>Rounds outward before selection or projection, so the encoded certificate covers exactly the rendered demand.</summary>
    internal static ShadowBounds Canonicalize(ShadowBounds bounds)
    {
        if (!bounds.IsValid) return bounds;
        return new(Round(bounds.MinX, false), Round(bounds.MinY, false), Round(bounds.MinZ, false),
            Round(bounds.MaxX, true), Round(bounds.MaxY, true), Round(bounds.MaxZ, true));
    }

    /// <summary>Packs six chunk/local endpoint pairs without narrowing an absolute world position.</summary>
    internal static void Write(Span<uint> words, ShadowBounds bounds)
    {
        ReadOnlySpan<double> values = [bounds.MinX, bounds.MinY, bounds.MinZ, bounds.MaxX, bounds.MaxY, bounds.MaxZ];
        for (int i = 0; i < values.Length; i++)
        {
            var (cell, local) = Split(values[i]);
            words[i * 2] = unchecked((uint)cell);
            words[i * 2 + 1] = BitConverter.SingleToUInt32Bits(local);
        }
    }

    /// <summary>Separates one native-world coordinate into an integer partition and bounded local remainder.</summary>
    internal static (int Cell, float Local) Split(double value)
    {
        double cell = Math.Floor(value / 32);
        // The closed upper edge of the last native partition is representable too.
        if (cell == (double)int.MaxValue + 1 && value == cell * 32) cell--;
        if (!double.IsFinite(value) || cell < int.MinValue || cell > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(value), "Shadow coverage exceeds native chunk addressing.");
        return ((int)cell, (float)(value - cell * 32));
    }
    #endregion

    #region Private
    /// <summary>Preserves closed containment despite rounding on either side of a partition boundary.</summary>
    private static double Round(double value, bool upper)
    {
        var (cell, local) = Split(value);
        double result = (double)cell * 32 + local;
        if (upper ? result < value : result > value)
            local = upper ? MathF.BitIncrement(local) : MathF.BitDecrement(local);
        return (double)cell * 32 + local;
    }
    #endregion
}
