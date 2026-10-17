using System;
using System.Numerics;
using VanillaGraphicsExpanded.PBR.Clouds;

namespace VanillaGraphicsExpanded.Tests;

/// <summary>Models conservative interval reduction on the CPU for contract checks; runtime reduction stays on the GPU.</summary>
internal readonly record struct CloudHeightInterval(float Lower, float Upper)
{
    #region Public API
    /// <summary>Gets the reduction identity for invalid, unoccupied or logically padded native columns.</summary>
    internal static CloudHeightInterval Empty => new(float.PositiveInfinity, float.NegativeInfinity);
    /// <summary>Identifies an empty interval without rejecting conservative unbounded endpoints.</summary>
    internal bool IsEmpty => Lower > Upper;

    /// <summary>Matches CloudColumn/CloudOccupied admission and converts native BA tile units to world units.</summary>
    internal static CloudHeightInterval FromColumn(Vector4 column)
    {
        // The shader rejects nonfinite values in every component, including the unused green channel.
        // Extinction zero is valid map data but contributes no occupied interval; no positive cutoff is used.
        if (!float.IsFinite(column.X) || !float.IsFinite(column.Y) || !float.IsFinite(column.Z)
            || !float.IsFinite(column.W) || column.X <= 0 || column.W <= column.Z) return Empty;
        return new(column.Z * CloudHierarchyLayout.CellSize, column.W * CloudHierarchyLayout.CellSize);
    }

    /// <summary>Encloses both child intervals, retaining gaps as conservative false positives.</summary>
    internal CloudHeightInterval Enclose(CloudHeightInterval other) =>
        new(MathF.Min(Lower, other.Lower), MathF.Max(Upper, other.Upper));
    #endregion
}
