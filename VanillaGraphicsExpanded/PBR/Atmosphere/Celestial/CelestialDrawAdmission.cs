using System;
using System.Numerics;
using System.Runtime.InteropServices;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.PBR.Atmosphere.Celestial;

/// <summary>Bounds all overlapping billboard flux using the exact admitted perspective camera.</summary>
internal static class CelestialDrawAdmission
{
    #region Public API
    /// <summary>Rejects unsupported projections and includes every star, even ones currently outside the view.</summary>
    internal static float MaximumNits(CelestialSource source, VgeFrameUniformBuffer frame)
    {
        var p = frame.Projection;
        Vector2 size = MemoryMarshal.Read<Vector2>(frame.Bytes.Slice(384, 8));
        if (size.X < 32 || size.Y < 32 || p[0] <= 0 || p[5] <= 0 || p[11] != -1 || p[15] != 0
            || p[1] != 0 || p[4] != 0 || p[3] != 0 || p[7] != 0 || p[12] != 0 || p[13] != 0)
            throw new ArgumentException("Celestial billboards require a finite conventional perspective viewport.");
        var view = frame.View;
        Vector3 a = new(view[0],view[1],view[2]), b = new(view[4],view[5],view[6]), c = new(view[8],view[9],view[10]);
        if (MathF.Abs(a.LengthSquared()-1) > .001f || MathF.Abs(b.LengthSquared()-1) > .001f
            || MathF.Abs(c.LengthSquared()-1) > .001f || MathF.Abs(Vector3.Dot(a,b)) > .001f
            || MathF.Abs(Vector3.Dot(a,c)) > .001f || MathF.Abs(Vector3.Dot(b,c)) > .001f)
            throw new ArgumentException("Celestial angular-area admission requires a rigid camera view.");
        double x = (1 + Math.Abs(p[8]) + 4 / size.X) / p[0];
        double y = (1 + Math.Abs(p[9]) + 4 / size.Y) / p[5];
        double minimumArea = 4 / ((double)size.X * size.Y * p[0] * p[5] * Math.Pow(1 + x*x + y*y, 1.5));
        // Full-footprint weights are <=1. The edge padding bounds every fragment, and
        // all-source overlap plus arithmetic headroom also bounds additive blending.
        return LightingStorage.UpperBound((source.MaximumNits + source.ResolvedStarMaximumFluxLux / minimumArea) * 1.01);
    }
    #endregion
}
