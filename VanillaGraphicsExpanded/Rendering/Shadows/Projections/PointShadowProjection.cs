using System;
using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.Rendering.Shadows.Lights;
using VanillaGraphicsExpanded.Rendering.Shadows.Configuration;

namespace VanillaGraphicsExpanded.Rendering.Shadows.Projections;

/// <summary>Six fixed perspective faces using conventional depth and deterministic edge ownership.</summary>
internal sealed class PointShadowProjection
{
    internal ShadowSourceKey Source { get; }
    internal long Generation { get; }
    internal Vector3d Position { get; }
    internal double Near { get; }
    internal double Far { get; }
    internal int FaceResolution { get; }
    internal int PageSize { get; }

    #region Public API
    #region Construction and cube axes
    /// <summary>Freezes source position and explicit near/far mapping; any change requires a new projection generation.</summary>
    internal PointShadowProjection(ShadowSourceKey source, long generation, Vector3d position,
        double near = 0.05, double far = 25, int faceResolution = 2048, int pageSize = 128)
    {
        if (!source.IsKnown || generation <= 0 || !ShadowProjectionMath.Finite(position) || !double.IsFinite(near)
            || !double.IsFinite(far) || near <= 0 || far <= near || pageSize < 1 || faceResolution < pageSize
            || faceResolution % pageSize != 0) throw new ArgumentOutOfRangeException(nameof(source));
        Source = source; Generation = generation; Position = position; Near = near; Far = far;
        FaceResolution = faceResolution; PageSize = pageSize;
    }
    /// <summary>Selects +X,-X,+Y,-Y,+Z,-Z, resolving ties in X then Y then Z order.</summary>
    internal static int Face(Vector3d direction)
    {
        if (!ShadowProjectionMath.Finite(direction) || Vector3d.Dot(direction, direction) == 0)
            throw new ArgumentOutOfRangeException(nameof(direction));
        double x = Math.Abs(direction.X), y = Math.Abs(direction.Y), z = Math.Abs(direction.Z);
        return x >= y && x >= z ? (direction.X >= 0 ? 0 : 1) : y >= z ? (direction.Y >= 0 ? 2 : 3) : (direction.Z >= 0 ? 4 : 5);
    }
    /// <summary>Maps face-plane coordinates to a direction; also accepts taps outside the face.</summary>
    internal static Vector3d Direction(int face, double u, double v) => face switch
    {
        0 => new(1, -v, -u), 1 => new(-1, -v, u), 2 => new(u, 1, v),
        3 => new(u, -1, -v), 4 => new(u, -v, 1), 5 => new(-u, -v, -1),
        _ => throw new ArgumentOutOfRangeException(nameof(face))
    };
    #endregion
    #region Mapping and identity
    /// <summary>Projects onto the selected face and recomputes reference depth in that face.</summary>
    internal Vector3d Project(Vector3d world, int face)
    {
        var d = world - Position;
        (double u, double v, double z) = face switch
        {
            0 => (-d.Z, -d.Y, d.X), 1 => (d.Z, -d.Y, -d.X), 2 => (d.X, d.Z, d.Y),
            3 => (d.X, -d.Z, -d.Y), 4 => (d.X, -d.Y, d.Z), 5 => (-d.X, -d.Y, -d.Z),
            _ => throw new ArgumentOutOfRangeException(nameof(face))
        };
        if (!ShadowProjectionMath.Finite(d) || z <= 0) throw new ArgumentOutOfRangeException(nameof(world));
        return new((u / z + 1) * 0.5 * FaceResolution, (v / z + 1) * 0.5 * FaceResolution,
            Far / (Far - Near) - Far * Near / ((Far - Near) * z));
    }
    /// <summary>Maps a receiver to a face and virtual page without allowing the upper boundary to escape that face.</summary>
    internal ShadowPageKey Key(Vector3d world)
    {
        int face = Face(world - Position); var p = Project(world, face);
        return new(Source, Generation, face, Math.Clamp(ShadowProjectionMath.Page(p.X, PageSize), 0, FaceResolution / PageSize - 1),
            Math.Clamp(ShadowProjectionMath.Page(p.Y, PageSize), 0, FaceResolution / PageSize - 1));
    }
    /// <summary>Applies the configured near/page policy and invalidates depths on movement or projection changes.</summary>
    internal PointShadowProjection Update(Vector3d position, double far, int faceResolution, ShadowPageSettings settings)
    {
        var policy = settings.Freeze();
        if (position == Position && policy.PointNear == Near && far == Far && faceResolution == FaceResolution && policy.PageSize == PageSize) return this;
        return new(Source, checked(Generation + 1), position, policy.PointNear, far, faceResolution, policy.PageSize);
    }
    /// <summary>Produces page-local normalized-device coordinates after the selected face's perspective division.</summary>
    internal Vector3d ProjectPage(Vector3d world, ShadowPageKey page)
    {
        if (page.Source != Source || page.Projection != Generation) throw new ArgumentException("Page belongs to another projection.", nameof(page));
        var p = Project(world, page.FaceOrLevel);
        return new(2 * (p.X - (double)page.X * PageSize) / PageSize - 1,
            2 * (p.Y - (double)page.Y * PageSize) / PageSize - 1, p.Z * 2 - 1);
    }
    #endregion
    #endregion
}
