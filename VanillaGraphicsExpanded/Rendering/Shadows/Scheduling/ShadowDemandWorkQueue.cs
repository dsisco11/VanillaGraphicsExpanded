using System;
using System.Collections.Generic;
using System.Linq;
using VanillaGraphicsExpanded.Rendering.Shadows.Lights;

namespace VanillaGraphicsExpanded.Rendering.Shadows.Scheduling;

/// <summary>Gives every prioritized source a minimum-coverage attempt before any source spends work on refinement.</summary>
internal sealed class ShadowDemandWorkQueue
{
    private readonly int capacity;
    private readonly Dictionary<ShadowSourceKey,(ShadowSourcePriority Priority,Action Minimum,Action Refine)> work=new();

    #region Public API
    /// <summary>Bounds source callbacks by the persisted frame light capacity.</summary>
    internal ShadowDemandWorkQueue(int capacity)
    {if(capacity<=0)throw new ArgumentOutOfRangeException(nameof(capacity));this.capacity=capacity;}

    /// <summary>Registers one source's minimum and optional refinement production without executing either.</summary>
    internal void Add(ShadowSourcePriority priority,Action minimum,Action refine)
    {
        if(work.Count>=capacity || !priority.Source.IsKnown || !double.IsFinite(priority.Contribution) || priority.Contribution<0)
            throw new ArgumentOutOfRangeException(nameof(priority));
        work.Add(priority.Source,(priority,minimum,refine));
    }

    /// <summary>Uses the admission scheduler's same stable history and postpones all refinement until minimum production finishes.</summary>
    internal void Execute(ShadowScheduler scheduler,double now)
    {
        var order=scheduler.PrioritizeInputs(now,work.Values.Select(item=>item.Priority).ToArray());
        foreach(var key in order)work[key].Minimum();
        foreach(var key in order)work[key].Refine();
    }
    #endregion
}
