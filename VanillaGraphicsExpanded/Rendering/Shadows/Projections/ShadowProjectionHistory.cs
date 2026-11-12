using System;
using System.Collections.Generic;
using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.Rendering.Shadows.Geometry;
using VanillaGraphicsExpanded.Rendering.Shadows.Lights;

namespace VanillaGraphicsExpanded.Rendering.Shadows.Projections;

/// <summary>Bounded attempted mappings retained until their finite rendered-age limit, independent of native geometry.</summary>
internal sealed class ShadowProjectionHistory
{
    private readonly int capacity;
    private readonly double maxAge;
    private readonly List<Entry> entries=new();
    private long generation;
    /// <summary>Retains the actual typed mapping required to regenerate current demand in an older rendered projection.</summary>
    internal sealed record Entry(ShadowSourceKey Source,int Detail,double Time,DirectionalShadowProjection? Directional,PointShadowProjection? Point);

    #region Public API
    /// <summary>Sets a charged finite mapping-history bound; capacity exhaustion withdraws stale fallback rather than losing identity.</summary>
    internal ShadowProjectionHistory(int capacity,double maxAge)
    {
        if(capacity<=0 || !double.IsFinite(maxAge) || maxAge<0)throw new ArgumentOutOfRangeException(nameof(capacity));
        this.capacity=capacity;this.maxAge=maxAge;
    }

    /// <summary>Returns matching mappings without changing identity merely because another frame was captured.</summary>
    internal Entry Directional(ShadowSourceKey source,int detail,double now,Vector3d direction,ShadowBounds domain,double width,int levels,int pageSize,
        double depthPadding=1)
    {
        var candidate=new DirectionalShadowProjection(source,checked(generation+1),direction,domain,width,levels,pageSize,depthPadding);
        for(int i=entries.Count-1;i>=0;i--)
            if(entries[i] is {Directional:{ } previous} entry && entry.Source==source && entry.Detail==detail &&
                previous.Right==candidate.Right && previous.Up==candidate.Up && previous.Forward==candidate.Forward && previous.Near==candidate.Near && previous.Far==candidate.Far &&
                previous.BaseTexelWidth==width && previous.Levels==levels && previous.PageSize==pageSize)
            {var updated=entry with{Time=now};entries[i]=updated;return updated;}
        generation++;var result=new Entry(source,detail,now,candidate,null);Add(result,now);return result;
    }

    /// <summary>Creates a point mapping only when its position or resolution changes.</summary>
    internal Entry Point(ShadowSourceKey source,int detail,double now,Vector3d position,double near,double far,int resolution,int pageSize)
    {
        for(int i=entries.Count-1;i>=0;i--)
            if(entries[i] is {Point:{ } previous} entry && entry.Source==source && entry.Detail==detail &&
                previous.Position==position && previous.Near==near && previous.Far==far && previous.FaceResolution==resolution && previous.PageSize==pageSize)
            {var updated=entry with{Time=now};entries[i]=updated;return updated;}
        var mapping=new PointShadowProjection(source,checked(++generation),position,near,far,resolution,pageSize);
        var result=new Entry(source,detail,now,null,mapping);Add(result,now);return result;
    }

    /// <summary>Enumerates bounded fallback mappings newest-first; GPU completion, age, motion and coverage still decide eligibility.</summary>
    internal IEnumerable<Entry> Retained(Entry current,double now)
    {
        for(int i=entries.Count-1;i>=0;i--)
        {
            var entry=entries[i];
            if(entry.Source==current.Source && entry.Detail==current.Detail && !ReferenceEquals(entry,current) && now-entry.Time<=maxAge)yield return entry;
        }
    }

    /// <summary>Reports conservative charged CPU metadata, including capacity reserved for attempted-but-unpublished mappings.</summary>
    internal long AllocatedBytes=>capacity*1024L;
    #endregion

    #region Private
    /// <summary>Removes expired mappings first; finite capacity never expands in response to sustained motion.</summary>
    private void Add(Entry entry,double now)
    {
        entries.RemoveAll(e=>now-e.Time>maxAge);
        if(entries.Count==capacity)entries.RemoveAt(0);
        entries.Add(entry);
    }
    #endregion
}
