using System;

namespace VanillaGraphicsExpanded.Rendering.Shadows.Scheduling;

/// <summary>One monotonic ledger shared by preparation, demand, every light, group, layer and retry.</summary>
internal sealed class ShadowFrameBudget
{
    internal ShadowWork Limit { get; }
    internal ShadowWork Used { get; private set; }
    internal int Rejections { get; private set; }
    internal string? LastRejection { get; private set; }

    #region Public API
    /// <summary>Charges live and retired storage before any new preparation or allocation.</summary>
    internal ShadowFrameBudget(ShadowWork limit, long residentBytes)
    {
        if (residentBytes < 0 || !default(ShadowWork).Fits(limit)) throw new ArgumentOutOfRangeException(nameof(limit));
        Limit = limit; Used = new(Bytes: residentBytes);
    }

    /// <summary>Reserves the complete cost before effects; unsuccessful work may never refund already performed work.</summary>
    internal bool TryCharge(ShadowWork cost)
    {
        try
        {
            var next = Used.Add(cost);
            if (next.Fits(Limit)) { Used = next; return true; }
            var actual=next.Counters();var limits=Limit.Counters();
            string[] names=["input visits","slabs","cells","probes","changes","requests","coverage checks","expansion visits",
                "groups","command slots","instances","triangles","raster pixels","atomic sample proxy","draw calls","clears",
                "retries","tracked bytes","views","page operations","tile reservations"];
            for(int i=0;i<actual.Length;i++)if(actual[i]>limits[i]){LastRejection=names[i];break;}
        }
        catch (OverflowException) { LastRejection="counter arithmetic overflow"; }
        Rejections++; return false;
    }
    #endregion
}
