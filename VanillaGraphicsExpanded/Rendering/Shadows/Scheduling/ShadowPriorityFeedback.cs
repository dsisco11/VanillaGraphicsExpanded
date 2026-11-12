using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.Shadows.Configuration;
using VanillaGraphicsExpanded.Rendering.Shadows.Lights;

namespace VanillaGraphicsExpanded.Rendering.Shadows.Scheduling;

/// <summary>Bounded delayed whole-light status hints; only GPU readiness can authorize current sampling.</summary>
internal sealed class ShadowPriorityFeedback:IDisposable
{
    private readonly int capacity;
    private readonly ShadowTemporalSettings temporal;
    private readonly Queue<Observation> pending=new();
    private readonly Dictionary<ShadowSourceKey,(string Signature,double Until)> eligible=new();
    private long epoch;
    internal long MissingSamples { get; private set; }
    /// <summary>Retains compact source fingerprints and staging until a nonblocking fence completes.</summary>
    private sealed record Observation(RHIReadbackTicket Ticket,ImmutableArray<(ShadowSourceKey Source,string Signature)> Sources,double Time,long Epoch);

    #region Public API
    #region Construction and observation
    /// <summary>Freezes finite feedback capacity and conservative priority eligibility deadlines.</summary>
    internal ShadowPriorityFeedback(int capacity,ShadowTemporalSettings temporal)
    {this.capacity=capacity;this.temporal=temporal.Freeze();}
    /// <summary>Includes three staging generations, fingerprints and bounded dictionary/queue metadata.</summary>
    internal long AllocatedBytes=>capacity*1152L;

    /// <summary>Consumes only ready compact statuses; delayed or unknown results never hold an incumbent.</summary>
    internal void Poll(double now)
    {
        foreach(var source in new List<ShadowSourceKey>(eligible.Keys))if(eligible[source].Until<now)eligible.Remove(source);
        while(pending.TryPeek(out var observation))
        {
            var state=observation.Ticket.Poll();if(state==CompletionStatus.Pending)break;
            pending.Dequeue();
            try
            {
                if(state!=CompletionStatus.Ready){MissingSamples++;continue;}
                if(observation.Epoch!=epoch)continue;
                using var mapped=observation.Ticket.MapReadyReadback();
                var words=MemoryMarshal.Cast<byte,uint>(mapped.Bytes);
                for(int i=0;i<observation.Sources.Length;i++)
                {
                    var source=observation.Sources[i];int at=i*4;
                    eligible.Remove(source.Source);
                    if(words[at] is not (1u or 2u))continue;
                    double age=MathF.BitIncrement(BitConverter.UInt32BitsToSingle(words[at+1]));
                    double stale=MathF.BitIncrement(BitConverter.UInt32BitsToSingle(words[at+2]));
                    double remaining=temporal.MaxRenderedAgeSeconds-age;
                    if(words[at]==2u)remaining=Math.Min(remaining,temporal.MaxStaleAgeSeconds-stale);
                    double until=observation.Time+remaining;
                    if(double.IsFinite(until) && until>=now)
                    {
                        if(eligible.Count>=capacity)
                        {ShadowSourceKey victim=default;foreach(var key in eligible.Keys){victim=key;break;}eligible.Remove(victim);}
                        eligible[source.Source]=(source.Signature,until);
                    }
                }
            }
            catch(Exception){MissingSamples++;eligible.Clear();}
            finally{observation.Ticket.Dispose();}
        }
    }

    /// <summary>Requires an observed complete result and unchanged demand/projection/dependencies within its original deadline.</summary>
    internal bool IsEligible(ShadowSourceKey source,string signature,double now)=>eligible.TryGetValue(source,out var state) &&
        state.Signature==signature && now<=state.Until;

    /// <summary>Copies only sixteen status bytes per light; no page table, geometry or depth image reaches the CPU.</summary>
    internal void Capture(GpuShaderStorageBuffer results,ImmutableArray<(ShadowSourceKey Source,string Signature)> sources,double now)
    {
        if(sources.IsEmpty)return;
        if(pending.Count>=3){MissingSamples++;return;}
        if(sources.Length>capacity)throw new ArgumentOutOfRangeException(nameof(sources));
        try
        {
            var ticket=RHICommandContext.Current.EnqueueReadback(new(new RHIBufferRange(new GpuBufferRef(results),0,sources.Length*16)));
            pending.Enqueue(new(ticket,sources,now,epoch));
        }
        catch(Exception){MissingSamples++;eligible.Clear();}
    }
    #endregion
    #region Eligibility and lifetime

    /// <summary>Fingerprints bounded current coverage inputs; feedback cannot protect a source after known demand or dependency changes.</summary>
    internal static string Fingerprint(ShadowFrameLight source,ShadowFrameBudget budget)
    {
        long count=0;
        foreach(var alternative in source.Alternatives)
            count=checked(count+alternative.Pages.Length+(alternative.RetainedPages.IsDefault?0:alternative.RetainedPages.Length));
        if(!budget.TryCharge(new(Inputs:checked(1+count*160))))return string.Empty;
        using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach(var alternative in source.Alternatives)
        {
            hash.AppendData(BitConverter.GetBytes(alternative.Detail));
            hash.AppendData(new byte[]{alternative.Complete?(byte)1:(byte)0});
            Append(alternative.Pages);
            if(!alternative.RetainedPages.IsDefault)Append(alternative.RetainedPages);
        }
        return Convert.ToHexString(hash.GetHashAndReset());

        /// <summary>Includes exact receiver enclosures, scene identity and current/rendered mappings without a moving observation timestamp.</summary>
        void Append(ImmutableArray<Demand.ShadowDemandPage> pages)
        {
            hash.AppendData(BitConverter.GetBytes(pages.Length));
            foreach(var page in pages)
            {
                var words=page.Request.Pack();words[46]=words[47]=0;
                hash.AppendData(MemoryMarshal.AsBytes(words.AsSpan()));
                page.Current.WriteTo(words);hash.AppendData(MemoryMarshal.AsBytes(words.AsSpan(64,34)));
                hash.AppendData(BitConverter.GetBytes(page.Current.Generation));
            }
        }
    }

    /// <summary>Withdraws hints across input/capability loss without releasing or replacing pending staging generations.</summary>
    internal void Invalidate(){epoch=checked(epoch+1);eligible.Clear();}

    /// <summary>Retires pending staging at the owning terminal lifetime without a completion wait.</summary>
    public void Dispose()
    {while(pending.TryDequeue(out var observation))observation.Ticket.Dispose();eligible.Clear();}
    #endregion
    #endregion
}
