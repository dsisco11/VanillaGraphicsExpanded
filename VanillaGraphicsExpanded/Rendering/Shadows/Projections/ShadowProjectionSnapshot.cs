using System;
using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.Rendering.Shadows.Lights;
using VanillaGraphicsExpanded.Rendering.Shadows.Pages;

namespace VanillaGraphicsExpanded.Rendering.Shadows.Projections;

/// <summary>Freezes the rendered mapping independently of current illumination and native mesh allocations.</summary>
internal sealed class ShadowProjectionSnapshot
{
    private readonly uint[] words = new uint[34];
    internal ShadowSourceKey Source { get; }
    internal long Generation { get; }
    internal int FaceOrLevel { get; }
    internal bool IsPoint { get; }
    internal Vector3d PositionOrForward { get; }
    internal int PageSize => checked((int)words[1]);

    #region Public API
    /// <summary>Retains one directional level's exact basis, depth range and world texel width.</summary>
    internal static ShadowProjectionSnapshot Directional(DirectionalShadowProjection projection, int level)
    {
        var result = new ShadowProjectionSnapshot(projection.Source, projection.Generation, level, false, projection.Forward);
        result.words[1] = (uint)projection.PageSize;
        result.Vector(4, projection.Right); result.Vector(10, projection.Up); result.Vector(16, projection.Forward);
        result.Number(22, projection.Near); result.Number(24, projection.Far); result.Number(26, projection.TexelWidth(level));
        return result;
    }

    /// <summary>Retains a point light's fixed cube mapping; receiver-dependent texel widths are not projection identity.</summary>
    internal static ShadowProjectionSnapshot Point(PointShadowProjection projection, int face)
    {
        if (face is < 0 or > 5) throw new ArgumentOutOfRangeException(nameof(face));
        var result = new ShadowProjectionSnapshot(projection.Source, projection.Generation, face, true, projection.Position);
        result.words[0] = 1; result.words[1] = (uint)projection.PageSize; result.words[2] = (uint)projection.FaceResolution;
        result.Number(22, projection.Near); result.Number(24, projection.Far); result.Vector(28, projection.Position);
        return result;
    }

    /// <summary>Writes the same canonical projection fields into page storage and sampling requests.</summary>
    internal void WriteTo(Span<uint> destination)
    {
        if (destination.Length < 98) throw new ArgumentException("Projection storage requires 98 words.", nameof(destination));
        words.AsSpan().CopyTo(destination[64..98]);
    }
    /// <summary>Requires both identity and the exact frozen mapping before sharing raster or page coverage.</summary>
    internal bool Matches(ShadowProjectionSnapshot other) => other is not null && Source == other.Source &&
        Generation == other.Generation && FaceOrLevel == other.FaceOrLevel && words.AsSpan().SequenceEqual(other.words);
    #endregion

    #region Private
    /// <summary>Freezes source identity while factories construct the only supported projection forms.</summary>
    private ShadowProjectionSnapshot(ShadowSourceKey source, long generation, int faceOrLevel, bool point, Vector3d positionOrForward)
    { Source = source; Generation = generation; FaceOrLevel = faceOrLevel; IsPoint = point; PositionOrForward = positionOrForward; }

    /// <summary>Packs projection scalars without narrowing their world-space precision.</summary>
    private void Number(int offset, double value) => ShadowPageRequest.Put(words, offset, BitConverter.DoubleToInt64Bits(value));

    /// <summary>Keeps vector components adjacent in the shared double-word layout.</summary>
    private void Vector(int offset, Vector3d value)
    { Number(offset, value.X); Number(offset + 2, value.Y); Number(offset + 4, value.Z); }
    #endregion
}
