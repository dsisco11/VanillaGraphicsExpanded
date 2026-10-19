using System;
using System.Numerics;

namespace VanillaGraphicsExpanded.PBR.Clouds.Volume;

/// <summary>Defines stable material cells, circular slots and the finite cloud coverage they represent.</summary>
internal sealed record CloudVolumeDomain
{
    internal const double HorizontalCell = 19.53125;
    internal const int Height = 66, ScratchBytes = 1048576;
    internal const long ResidentLimit = 512L * 1024 * 1024, PeakLimit = 2 * ResidentLimit;
    internal int Width { get; }
    internal int Size { get; }
    internal long FirstX { get; }
    internal long FirstZ { get; }
    internal double AnchorX { get; }
    internal double AnchorZ { get; }
    internal double Bottom { get; }
    internal double Band { get; }
    internal float VerticalCell => (float)(Band / 64);
    internal int SlotX => Mod(FirstX, Size);
    internal int SlotZ => Mod(FirstZ, Size);
    internal int BlocksX => (Size + 7) / 8;
    internal int BlocksY => (Height + 3) / 4;
    internal int Blocks => checked(BlocksX * BlocksY * BlocksX);
    internal int Voxels => checked(Size * Height * Size);
    internal long Bytes => checked(22L * Voxels + 16L * Width * Width + 68L * Blocks + ScratchBytes);
    internal long CopyBytes => 16L * Voxels;

    #region Public API
    /// <summary>Chooses a snapped band containing the full envelope, retaining its previous extent where possible.</summary>
    internal CloudVolumeDomain(int width, CloudPlacement placement, double minimum, double maximum,
        CloudVolumeDomain? previous = null)
    {
        if (width is < 1 or > 200 || !placement.IsFinite || !double.IsFinite(minimum) ||
            !double.IsFinite(maximum) || maximum < minimum) throw new ArgumentOutOfRangeException(nameof(width));
        Width = width; Size = checked((int)Math.Ceiling(50 * width / HorizontalCell) + 3);
        AnchorX = 50.0 * ((long)placement.CenterX - placement.WindTileX - width / 2.0);
        AnchorZ = 50.0 * ((long)placement.CenterZ - placement.WindTileZ - width / 2.0);
        FirstX = checked((long)Math.Floor(AnchorX / HorizontalCell) - 1);
        FirstZ = checked((long)Math.Floor(AnchorZ / HorizontalCell) - 1);
        Band = previous?.Band ?? 800;
        if (previous is not null && minimum >= previous.Bottom + previous.VerticalCell &&
            maximum <= previous.Bottom + previous.Band - previous.VerticalCell) { Bottom = previous.Bottom; return; }
        // Reserve an interior cell of vertical margin on each side, including snapping loss.
        while (Band <= 25600)
        {
            double cell = Band / 64;
            Bottom = Math.Floor(((minimum + maximum) / 2 - Band / 2) / cell) * cell;
            if (minimum >= Bottom + cell && maximum <= Bottom + Band - cell) return;
            Band *= 2;
        }
        throw new ArgumentOutOfRangeException(nameof(maximum), "Cloud height envelope exceeds the admitted band.");
    }

    /// <summary>Reports whether existing circular storage can retain this domain without reallocating.</summary>
    internal bool CanScroll(CloudVolumeDomain next) => Width == next.Width && Bottom == next.Bottom && Band == next.Band &&
        Math.Abs(FirstX - next.FirstX) < Size - 2 && Math.Abs(FirstZ - next.FirstZ) < Size - 2;

    /// <summary>Tests exact material identity; guard cells are never retained as occupied content.</summary>
    internal bool ContainsInterior(long x, int y, long z) => x > FirstX && x < FirstX + Size - 1 &&
        z > FirstZ && z < FirstZ + Size - 1 && y > 0 && y < Height - 1;

    /// <summary>Maps a bounded native-local position to circular hardware-filter coordinates.</summary>
    internal Vector3 TexturePosition(Vector3 native)
    {
        double localX = (AnchorX - FirstX * HorizontalCell + native.X) / HorizontalCell;
        double localZ = (AnchorZ - FirstZ * HorizontalCell + native.Z) / HorizontalCell;
        return new((float)((SlotX + localX) / Size), (float)((native.Y - Bottom) / VerticalCell + 1) / Height,
            (float)((SlotZ + localZ) / Size));
    }

    /// <summary>Returns a positive circular index for negative and positive material keys.</summary>
    internal static int Mod(long key, int size) => (int)((key % size + size) % size);
    #endregion
}
