using System;
using System.Numerics;

namespace VanillaGraphicsExpanded.PBR.Clouds.LightingVolume;

/// <summary>Defines a finite lighting lattice using unwrapped integer material keys.</summary>
internal sealed record LightingVolumeDomain
{
    internal int Width { get; }
    internal long X { get; }
    internal long Z { get; }
    internal double AnchorX { get; }
    internal double AnchorZ { get; }
    internal int Interior { get; }
    internal int Size => Interior + 2;
    internal const int Height = 34;
    internal int RegionSide => (Interior + 6) / 4;
    internal int Regions => RegionSide * 8 * RegionSide;
    internal int Voxels => checked(Size * Height * Size);
    internal long Bytes => checked(24L * Voxels + 32L * Width * Width + 128L * Regions + 65536);
    internal long RegionX => FloorDiv(X, 4);
    internal long RegionZ => FloorDiv(Z, 4);

    #region Public API
    /// <summary>Snaps native coverage while keeping dimensions fixed across half-cell native recentering.</summary>
    internal LightingVolumeDomain(int width, CloudPlacement placement)
    {
        if (width is < 1 or > 200 || !placement.IsFinite) throw new ArgumentOutOfRangeException(nameof(width));
        Width = width;
        AnchorX = 50.0 * ((long)placement.CenterX - placement.WindTileX - width / 2.0);
        AnchorZ = 50.0 * ((long)placement.CenterZ - placement.WindTileZ - width / 2.0);
        X = (long)Math.Floor(AnchorX / 100); Z = (long)Math.Floor(AnchorZ / 100);
        Interior = (width + 1) / 2 + 1;
    }

    /// <summary>Converts a camera-relative point into current native coordinates; wind is already in Offset.</summary>
    internal Vector3 NativePosition(Vector3 displacement, CloudPlacement placement) =>
        displacement - placement.Offset + new Vector3(25 * Width, 0, 25 * Width);

    /// <summary>Returns the bounded native-local position of the first guard node, avoiding absolute GPU floats.</summary>
    internal Vector3 FirstNode => new((float)(100 * X - 50 - AnchorX), -412.5f, (float)(100 * Z - 50 - AnchorZ));

    /// <summary>Retains only overlapping material nodes; owner/world/executable identity is checked by storage.</summary>
    internal bool Overlaps(LightingVolumeDomain other) => Width == other.Width &&
        Math.Abs(X - other.X) < Interior && Math.Abs(Z - other.Z) < Interior;

    /// <summary>Computes mathematical floor division for negative lattice keys.</summary>
    internal static long FloorDiv(long value, int divisor) => (long)Math.Floor(value / (double)divisor);
    #endregion
}
