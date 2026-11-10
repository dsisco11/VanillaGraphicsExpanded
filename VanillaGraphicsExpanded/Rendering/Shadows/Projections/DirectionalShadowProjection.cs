using System;
using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.Rendering.Shadows.Geometry;
using VanillaGraphicsExpanded.Rendering.Shadows.Lights;

namespace VanillaGraphicsExpanded.Rendering.Shadows.Projections;

/// <summary>Independent celestial clipmaps with absolute snapped XY addressing and domain-fitted depth.</summary>
internal sealed class DirectionalShadowProjection
{
    internal ShadowSourceKey Source { get; }
    internal long Generation { get; }
    internal Vector3d Right { get; }
    internal Vector3d Up { get; }
    internal Vector3d Forward { get; }
    internal double Near { get; }
    internal double Far { get; }
    internal double BaseTexelWidth { get; }
    internal int Levels { get; }
    internal int PageSize { get; }

    #region Public API
    #region Construction
    /// <summary>Fits every represented caster corner; basis or depth mapping changes require a new generation.</summary>
    internal DirectionalShadowProjection(ShadowSourceKey source, long generation, Vector3d towardLight,
        ShadowBounds representedCasters, double baseTexelWidth = 0.05, int levels = 8, int pageSize = 128,
        double depthPadding = 1)
    {
        if (!source.IsKnown || source.Kind is not (ShadowSourceKind.Sun or ShadowSourceKind.Moon) || generation <= 0
            || !representedCasters.IsValid || !double.IsFinite(baseTexelWidth) || baseTexelWidth <= 0
            || levels is < 1 or > 24 || !double.IsFinite(Math.ScaleB(baseTexelWidth, levels - 1))
            || pageSize < 1 || !double.IsFinite(depthPadding) || depthPadding <= 0)
            throw new ArgumentOutOfRangeException(nameof(source));
        Source = source; Generation = generation; BaseTexelWidth = baseTexelWidth; Levels = levels; PageSize = pageSize;
        Forward = -ShadowProjectionMath.Unit(towardLight);
        var axis = Math.Abs(Forward.Y) < 0.9 ? Vector3d.UnitY : Vector3d.UnitX;
        Right = ShadowProjectionMath.Unit(ShadowProjectionMath.Cross(Forward, axis)); Up = ShadowProjectionMath.Cross(Right, Forward);
        double lo = double.PositiveInfinity, hi = double.NegativeInfinity;
        for (int i = 0; i < 8; i++)
        {
            double depth = Vector3d.Dot(ShadowProjectionMath.Corner(representedCasters, i), Forward);
            lo = Math.Min(lo, depth); hi = Math.Max(hi, depth);
        }
        Near = lo - depthPadding; Far = hi + depthPadding;
        if (!double.IsFinite(Far - Near) || Far <= Near) throw new ArgumentOutOfRangeException(nameof(representedCasters));
    }
    #endregion
    #region Mapping and identity
    /// <summary>Doubles world texel width at each coarser level.</summary>
    internal double TexelWidth(int level) => level >= 0 && level < Levels
        ? Math.ScaleB(BaseTexelWidth, level) : throw new ArgumentOutOfRangeException(nameof(level));
    /// <summary>Projects world coordinates in double precision; camera translation does not alter matching page keys.</summary>
    internal Vector3d Project(Vector3d world, int level)
    {
        if (!ShadowProjectionMath.Finite(world)) throw new ArgumentOutOfRangeException(nameof(world));
        double width = TexelWidth(level);
        return new(Vector3d.Dot(world, Right) / width, Vector3d.Dot(world, Up) / width,
            (Vector3d.Dot(world, Forward) - Near) / (Far - Near));
    }
    /// <summary>Returns the page-aligned clipmap center without changing the underlying absolute mapping.</summary>
    internal (int X, int Y) SnapCenter(Vector3d camera, int level)
    {
        var p = Project(camera, level); return (ShadowProjectionMath.Page(p.X, PageSize), ShadowProjectionMath.Page(p.Y, PageSize));
    }
    /// <summary>Addresses one level explicitly; callers request every participating level rather than averaging depths.</summary>
    internal ShadowPageKey Key(Vector3d world, int level)
    {
        var p = Project(world, level);
        return new(Source, Generation, level, ShadowProjectionMath.Page(p.X, PageSize), ShadowProjectionMath.Page(p.Y, PageSize));
    }
    /// <summary>Retains matching mappings and advances depth identity whenever the light basis or fitted depth changes.</summary>
    internal DirectionalShadowProjection Update(Vector3d towardLight, ShadowBounds representedCasters, double depthPadding = 1)
    {
        var next = new DirectionalShadowProjection(Source, checked(Generation + 1), towardLight, representedCasters,
            BaseTexelWidth, Levels, PageSize, depthPadding);
        return next.Right == Right && next.Up == Up && next.Forward == Forward && next.Near == Near && next.Far == Far ? this : next;
    }
    /// <summary>Produces conventional page-local normalized-device coordinates without custom shader clipping.</summary>
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
