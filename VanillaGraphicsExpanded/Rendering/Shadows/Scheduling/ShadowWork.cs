using System;
using VanillaGraphicsExpanded.Rendering.Shadows.Configuration;

namespace VanillaGraphicsExpanded.Rendering.Shadows.Scheduling;

/// <summary>Aggregate conservative work and temporary-storage costs, including rejected and repeated attempts.</summary>
internal readonly record struct ShadowWork(long Inputs = 0, long Slabs = 0, long Cells = 0, long Probes = 0,
    long Changes = 0, long Requests = 0, long Coverage = 0, long Expansion = 0, long Groups = 0,
    long Slots = 0, long Instances = 0, long Triangles = 0, long RasterPixels = 0, long RasterSamples = 0,
    long Draws = 0, long Clears = 0, long Retries = 0, long Bytes = 0, long Views = 0, long PageOperations = 0, long Tiles = 0)
{
    #region Public API
    /// <summary>Builds finite whole-batch ceilings from the already sanitized owning settings.</summary>
    internal static ShadowWork Limits(ShadowSettings settings)
    {
        settings.Sanitize();
        var c = settings.Candidates; var d = settings.Drawing; var s = settings.Scheduling;
        return new(s.InputVisits, c.SlabsPerBatch, c.CellsPerBatch, c.ProbesPerBatch, c.ChangesPerBatch,
            s.DemandRequests, s.CoverageChecks, d.ExpansionVisits, d.GroupVisits, d.CommandSlots,
            d.ViewInstances, d.EstimatedTriangles, d.RasterPixels, d.EstimatedRasterSamples, s.DrawCalls,
            s.Clears, s.Retries, s.MemoryMiB * 1048576L,d.ViewCapacity,settings.Pages.OperationsPerBatch,
            checked(settings.Pages.TileColumns*(long)settings.Pages.TileRows*settings.Pages.ArrayLayers));
    }

    /// <summary>Provides a stable field order for checked accounting and diagnostic views.</summary>
    internal long[] Counters() => [Inputs, Slabs, Cells, Probes, Changes, Requests, Coverage, Expansion,
        Groups, Slots, Instances, Triangles, RasterPixels, RasterSamples, Draws, Clears, Retries, Bytes,Views,PageOperations,Tiles];

    /// <summary>Adds all fields without accepting a negative cost or integer overflow.</summary>
    internal ShadowWork Add(ShadowWork other)
    {
        var a = Counters(); var b = other.Counters();
        for (int i = 0; i < a.Length; i++)
        { if (a[i] < 0 || b[i] < 0) throw new ArgumentOutOfRangeException(nameof(other)); a[i] = checked(a[i] + b[i]); }
        return From(a);
    }

    /// <summary>Reserves only increased alternative costs while retaining every already charged field.</summary>
    internal ShadowWork IncreaseOver(ShadowWork prior)
    {
        var a=Counters();var b=prior.Counters();
        for(int i=0;i<a.Length;i++)
        {if(a[i]<0 || b[i]<0)throw new ArgumentOutOfRangeException(nameof(prior));a[i]=Math.Max(0,a[i]-b[i]);}
        return From(a);
    }

    /// <summary>Checks every independent ceiling; scalar timing estimates cannot substitute for this test.</summary>
    internal bool Fits(ShadowWork limit)
    {
        var a = Counters(); var b = limit.Counters();
        for (int i = 0; i < a.Length; i++) if (a[i] < 0 || b[i] < 0 || a[i] > b[i]) return false;
        return true;
    }
    #endregion

    #region Private
    /// <summary>Reconstructs the record after checked fieldwise arithmetic.</summary>
    private static ShadowWork From(long[] a) => new(a[0], a[1], a[2], a[3], a[4], a[5], a[6], a[7], a[8],
        a[9], a[10], a[11], a[12], a[13], a[14], a[15], a[16], a[17],a[18],a[19],a[20]);
    #endregion
}
