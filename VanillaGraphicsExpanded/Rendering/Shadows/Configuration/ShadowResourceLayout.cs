using System;

namespace VanillaGraphicsExpanded.Rendering.Shadows.Configuration;

/// <summary>Validates aggregate configurable storage before constructing individual RHI owners.</summary>
internal static class ShadowResourceLayout
{
    #region Public API
    /// <summary>Uses actual record strides and full physical capacity, not an all-depth interpretation of the memory budget.</summary>
    internal static ShadowEffectiveCapacity Resolve(ShadowSettings settings)
    {
        settings.Sanitize();
        var p=settings.Pages;var c=settings.Candidates;var s=settings.Scheduling;
        long tiles=checked(p.TileColumns*(long)p.TileRows*p.ArrayLayers);
        long pages=checked(tiles*(528L+p.PageSize*(long)p.PageSize*4)+608);
        long candidates=checked(16+c.HeaderCapacity*(1024L+128L+c.SegmentsPerAssociation*64L*16+c.DedupCapacity*4L)+c.ChangeCapacity*16L);
        int lookup=2;
        while(lookup<settings.Inputs.ChunkCapacity*2L)lookup=checked(lookup*2);
        long world=checked(lookup*64L+Math.Max(32,settings.Inputs.RangeCapacity*32L));
        long readiness=checked(128+s.LightCapacity*(800L+1152L)+s.DemandRequests*1536L);
        long history=checked(s.ProjectionHistoryCapacity*1024L);
        long fixedBytes=checked(pages+candidates+world+readiness+history+4096);
        long drawing=settings.Drawing.MemoryMiB*1048576L,requested=s.MemoryMiB*1048576L;
        var capacity=new ShadowEffectiveCapacity(requested,fixedBytes,drawing,tiles,c.HeaderCapacity,lookup,s.DemandRequests,settings.Drawing.CommandSlots,settings.Drawing.ViewCapacity);
        if(fixedBytes>requested || drawing>requested-fixedBytes)
            throw new InvalidOperationException($"Shadow layout requires {fixedBytes} fixed bytes and {drawing} drawing reserve bytes; requested total is {requested}.");
        return capacity;
    }
    #endregion
}
