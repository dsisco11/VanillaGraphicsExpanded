using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.Rendering.Shadows.Configuration;
using VanillaGraphicsExpanded.Rendering.Shadows.Geometry;
using VanillaGraphicsExpanded.Rendering.Shadows.Pages;
using VanillaGraphicsExpanded.Rendering.Shadows.Projections;
using VanillaGraphicsExpanded.Rendering.Shadows.Scheduling;

namespace VanillaGraphicsExpanded.Rendering.Shadows.Demand;

/// <summary>Builds charged conservative page enclosures; transparency never writes depth or removes later layers.</summary>
internal sealed class ShadowSurfaceDemand
{
    private readonly ShadowFrameBudget budget;
    private readonly ShadowPageSettings pages;
    private readonly int capacity;
    private readonly Dictionary<ShadowPageKey, ShadowDemandPage> requests = new();
    private ShadowSurfaceCategory completed;
    private bool failed;
    internal bool Complete => !failed && completed == ShadowSurfaceCategory.All;
    internal string? Failure { get; private set; }
    internal ImmutableArray<ShadowDemandPage> Pages => ImmutableArray.CreateRange(requests.Values);

    #region Public API
    /// <summary>Uses the same global ledger as preparation, candidate work and every light.</summary>
    internal ShadowSurfaceDemand(ShadowFrameBudget budget, ShadowPageSettings settings, int capacity)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        this.budget = budget; pages = settings.Freeze(); this.capacity = capacity;
    }

    /// <summary>Declares a category complete only after its current early provider has supplied every contributing region.</summary>
    internal void CompleteCategory(ShadowSurfaceCategory category)
    {
        if (category == ShadowSurfaceCategory.None || (category & ~ShadowSurfaceCategory.All) != 0)
            throw new ArgumentOutOfRangeException(nameof(category));
        completed |= category;
    }

    /// <summary>Records unresolved or overflowed production; existing partial requests never become a complete empty set.</summary>
    internal void Reject(string reason) { failed = true; Failure ??= reason; }

    /// <summary>Projects all corners of a depth-inclusive bound, expanded for all PCF taps and bounded normal bias.</summary>
    internal void AddDirectional(ShadowReceiverRegion receiver, DirectionalShadowProjection rendered,
        DirectionalShadowProjection current, int level, long world, long epoch, long sequence)
    {
        if (!Begin(receiver) || rendered.Source != current.Source || pages.PageSize != rendered.PageSize) { Reject("Projection"); return; }
        double padding = (3 + pages.NormalBiasTexels) * rendered.TexelWidth(level);
        var bounds = Expand(receiver.Bounds, padding);
        double loX = double.PositiveInfinity, loY = loX, hiX = double.NegativeInfinity, hiY = hiX;
        for (int i = 0; i < 8; i++)
        {
            var p = rendered.Project(ShadowProjectionMath.Corner(bounds, i), level);
            if (p.Z < 0 || p.Z > 1) { Reject("ReceiverDepth"); return; }
            loX = Math.Min(loX, p.X); loY = Math.Min(loY, p.Y); hiX = Math.Max(hiX, p.X); hiY = Math.Max(hiY, p.Y);
        }
        Rectangle(rendered.Source, rendered.Generation, level, loX, loY, hiX, hiY, bounds,
            ShadowProjectionSnapshot.Directional(rendered, level), ShadowProjectionSnapshot.Directional(current, level), world, epoch, sequence);
    }

    /// <summary>Bounds each cube face independently; source-plane crossings conservatively request the full affected face.</summary>
    internal void AddPoint(ShadowReceiverRegion receiver, PointShadowProjection rendered, PointShadowProjection current,
        double influenceRadius, long world, long epoch, long sequence)
    {
        if (!Begin(receiver) || rendered.Source != current.Source || pages.PageSize != rendered.PageSize ||
            !double.IsFinite(influenceRadius) || influenceRadius <= 0) { Reject("Projection"); return; }
        var b = receiver.Bounds;
        // Intersect illumination's axis-aligned influence enclosure, retaining all points inside the sphere.
        var p = current.Position;
        b = new(Math.Max(b.MinX, p.X-influenceRadius), Math.Max(b.MinY, p.Y-influenceRadius), Math.Max(b.MinZ, p.Z-influenceRadius),
            Math.Min(b.MaxX, p.X+influenceRadius), Math.Min(b.MaxY, p.Y+influenceRadius), Math.Min(b.MaxZ, p.Z+influenceRadius));
        if (!b.IsValid) return;
        double farthest = 0;
        for (int i = 0; i < 8; i++)
        { var d = ShadowProjectionMath.Corner(b, i) - rendered.Position; farthest = Math.Max(farthest, Math.Sqrt(Vector3d.Dot(d,d))); }
        b = Expand(b, (3 * (1 + 8.0/rendered.FaceResolution) + pages.NormalBiasTexels) * 2 * farthest/rendered.FaceResolution);
        for (int face = 0; face < 6; face++)
        {
            double loU = double.PositiveInfinity, loV = loU, hiU = double.NegativeInfinity, hiV = hiU;
            double minZ = double.PositiveInfinity, maxZ = double.NegativeInfinity;
            for (int i = 0; i < 8; i++)
            {
                var d = ShadowProjectionMath.Corner(b,i) - rendered.Position;
                var c = FaceCoordinates(d,face);
                minZ = Math.Min(minZ,c.Z); maxZ = Math.Max(maxZ,c.Z);
                if (c.Z <= 0) continue;
                loU = Math.Min(loU,c.X/c.Z); loV = Math.Min(loV,c.Y/c.Z);
                hiU = Math.Max(hiU,c.X/c.Z); hiV = Math.Max(hiV,c.Y/c.Z);
            }
            if (maxZ <= 0) continue;
            if (minZ <= 0) { loU = loV = -1; hiU = hiV = 1; }
            if (loU > 1 || hiU < -1 || loV > 1 || hiV < -1) continue;
            double size = rendered.FaceResolution;
            Rectangle(rendered.Source, rendered.Generation, face,
                Math.Max(0,(loU+1)*0.5*size-2), Math.Max(0,(loV+1)*0.5*size-2),
                Math.Min(size-1,(hiU+1)*0.5*size+2), Math.Min(size-1,(hiV+1)*0.5*size+2), b,
                ShadowProjectionSnapshot.Point(rendered,face), ShadowProjectionSnapshot.Point(current,face), world,epoch,sequence);
            if (failed) return;
        }
    }
    #endregion

    #region Private
    /// <summary>Charges every region including conservative late-category bounds and duplicate/hidden layers.</summary>
    private bool Begin(ShadowReceiverRegion receiver)
    {
        if (failed) return false;
        if (!receiver.Bounds.IsValid || receiver.Category == ShadowSurfaceCategory.None ||
            (receiver.Category & ~ShadowSurfaceCategory.All) != 0 || (completed & receiver.Category) != 0 ||
            !budget.TryCharge(new(Inputs:1))) { Reject("ReceiverInput"); return false; }
        return true;
    }

    /// <summary>Bounds enumeration before iterating and unions current required depths sharing a page.</summary>
    private void Rectangle(Lights.ShadowSourceKey source, long generation, int face, double loX, double loY, double hiX, double hiY,
        ShadowBounds bounds, ShadowProjectionSnapshot rendered, ShadowProjectionSnapshot current, long world, long epoch, long sequence)
    {
        if (!double.IsFinite(loX) || !double.IsFinite(loY) || !double.IsFinite(hiX) || !double.IsFinite(hiY)) { Reject("Coordinate"); return; }
        double x0 = Math.Floor(loX/pages.PageSize), y0 = Math.Floor(loY/pages.PageSize);
        double x1 = Math.Floor(hiX/pages.PageSize), y1 = Math.Floor(hiY/pages.PageSize);
        double count = (x1-x0+1)*(y1-y0+1);
        if (x0 < int.MinValue || y0 < int.MinValue || x1 > int.MaxValue || y1 > int.MaxValue || count < 0 || count > capacity ||
            !budget.TryCharge(new(Requests:(long)count, Coverage:(long)count))) { Reject("RequestOverflow"); return; }
        for (long y=(long)y0; y<=(long)y1; y++) for(long x=(long)x0; x<=(long)x1; x++)
        {
            var key = new ShadowPageKey(source,generation,face,(int)x,(int)y);
            var region = bounds;
            if (requests.TryGetValue(key,out var previous)) region = Union(previous.Request.Region,bounds);
            else if (requests.Count >= capacity || !budget.TryCharge(new(Bytes:2048))) { Reject("RequestStorage"); return; }
            requests[key] = new(new(ShadowPageOperation.Require,key,region,world,epoch,sequence,Projection:rendered),current);
        }
    }
    /// <summary>Expands all axes without depending on unknown receiver normals.</summary>
    private static ShadowBounds Expand(ShadowBounds b,double r) => new(b.MinX-r,b.MinY-r,b.MinZ-r,b.MaxX+r,b.MaxY+r,b.MaxZ+r);
    /// <summary>Encloses every currently required receiver; the draw producer must cover this entire region.</summary>
    private static ShadowBounds Union(ShadowBounds a,ShadowBounds b) => new(Math.Min(a.MinX,b.MinX),Math.Min(a.MinY,b.MinY),Math.Min(a.MinZ,b.MinZ),
        Math.Max(a.MaxX,b.MaxX),Math.Max(a.MaxY,b.MaxY),Math.Max(a.MaxZ,b.MaxZ));
    /// <summary>Uses the same fixed cube axes as sampling with unnormalized signed depth.</summary>
    private static Vector3d FaceCoordinates(Vector3d d,int f) => f switch
    { 0=>new(-d.Z,-d.Y,d.X),1=>new(d.Z,-d.Y,-d.X),2=>new(d.X,d.Z,d.Y),3=>new(d.X,-d.Z,-d.Y),4=>new(d.X,-d.Y,d.Z),_=>new(-d.X,-d.Y,-d.Z) };
    #endregion
}
