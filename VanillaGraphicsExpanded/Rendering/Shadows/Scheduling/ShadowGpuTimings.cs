using System;
using VanillaGraphicsExpanded.Rendering.RHI;

namespace VanillaGraphicsExpanded.Rendering.Shadows.Scheduling;

/// <summary>Fixed delayed timestamp slots; unavailable results skip feedback rather than block the frame.</summary>
internal sealed class ShadowGpuTimings:IDisposable
{
    private readonly Slot[] slots=new Slot[8];
    private int next;
    private Slot? active;
    internal long MissingSamples { get; private set; }
    internal double? LastMilliseconds { get; private set; }
    internal long LastFrame { get; private set; }

    /// <summary>Retains one query pair and its original estimate until both nonblocking tickets are available.</summary>
    private sealed class Slot:IDisposable
    {
        internal readonly GpuQuery Start,End;
        internal RHIQueryTicket? StartResult,EndResult;
        internal double Estimate;
        internal long Frame;
        /// <summary>Creates RHI-owned timestamps without a nested elapsed-query target.</summary>
        internal Slot()
        {
            var rhi=RHICommandContext.Current;Start=rhi.CreateQuery(new(QueryKind.Timestamp,"Shadows.Start")).RequireResource();
            try{End=rhi.CreateQuery(new(QueryKind.Timestamp,"Shadows.End")).RequireResource();}catch{Start.Dispose();throw;}
        }
        /// <summary>Cancels observation before retiring owned queries.</summary>
        public void Dispose(){StartResult?.Cancel();EndResult?.Cancel();Start.Dispose();End.Dispose();}
    }

    #region Public API
    /// <summary>Creates a fixed query ring whose metadata is charged to the scheduling owner.</summary>
    internal ShadowGpuTimings()
    {
        try{for(int i=0;i<slots.Length;i++)slots[i]=new();}catch{foreach(var slot in slots)slot?.Dispose();throw;}
    }
    /// <summary>Reports conservative bookkeeping bytes; driver-private query overhead is untracked.</summary>
    internal long AllocatedBytes=>slots.Length*512L;

    /// <summary>Polls only earlier issued tickets and feeds completed measurements into later estimates.</summary>
    internal void Poll(ShadowScheduler scheduler)
    {
        foreach(var slot in slots)
        {
            if(slot.StartResult is not { } start || slot.EndResult is not { } end)continue;
            var a=start.Poll();var b=end.Poll();
            if(a==CompletionStatus.Pending || b==CompletionStatus.Pending)continue;
            if(a==CompletionStatus.Ready && b==CompletionStatus.Ready && end.Result>=start.Result)
            {LastMilliseconds=(end.Result-start.Result)/1000000.0;LastFrame=slot.Frame;scheduler.ObserveTiming(slot.Estimate,LastMilliseconds.Value);}
            else MissingSamples++;
            slot.StartResult=slot.EndResult=null;
        }
    }

    /// <summary>Starts an available pair or records a missing sample; never reissues a still-pending query.</summary>
    internal void Begin(double estimate)
    {
        if(active is not null)throw new InvalidOperationException("A shadow timing interval is already open.");
        var slot=slots[next];next=(next+1)%slots.Length;
        if(slot.StartResult is not null){MissingSamples++;return;}
        slot.Estimate=Math.Max(0.001,estimate);slot.Frame=0;active=slot;
        var rhi=RHICommandContext.Current;rhi.WriteTimestamp(slot.Start);slot.StartResult=rhi.RequestQueryResult(slot.Start);
    }

    /// <summary>Tags the issued interval with its original frame and unscaled whole-work estimate once admission is known.</summary>
    internal void Describe(long frame,double estimate)
    {
        if(frame<=0 || !double.IsFinite(estimate) || estimate<=0)throw new ArgumentOutOfRangeException(nameof(frame));
        if(active is { } slot){slot.Frame=frame;slot.Estimate=estimate;}
    }

    /// <summary>Orders the final timestamp without requesting completion.</summary>
    internal void End()
    {
        if(active is not { } slot)return;active=null;
        var rhi=RHICommandContext.Current;rhi.WriteTimestamp(slot.End);slot.EndResult=rhi.RequestQueryResult(slot.End);
    }

    /// <summary>Retires query owners through RHI without waiting for pending observations.</summary>
    public void Dispose(){active=null;foreach(var slot in slots)slot.Dispose();}
    #endregion
}
