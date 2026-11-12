using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.Rendering.Shadows.Configuration;
using VanillaGraphicsExpanded.Rendering.Shadows.Geometry;
using VanillaGraphicsExpanded.Rendering.Shadows.Lights;
using VanillaGraphicsExpanded.Rendering.Shadows.Projections;

namespace VanillaGraphicsExpanded.Rendering.Shadows.Candidates;

/// <summary>Amortizes source/receiver associations across light movement without tying them to native mesh allocations.</summary>
internal sealed class ShadowCandidateSchedule:IDisposable
{
    private readonly ShadowCandidatePool pool;
    private readonly ShadowSchedulingSettings policy;
    private readonly double guard;
    private readonly int capacity;
    private readonly Dictionary<ShadowSourceKey,Entry> entries=new();
    private readonly HashSet<ShadowSourceKey> visited=new();
    private long frame;

    /// <summary>Owns only bounded coordinate associations, anchored coverage and last observed source identity.</summary>
    private sealed record Entry(ShadowCandidateRequest Request,ShadowCandidateHandle Handle,long BuiltFrame,ShadowCandidateCurrent Current,
        ShadowCandidateRequest? PendingRequest=null,ShadowCandidateHandle? PendingHandle=null,long PendingFrame=0);

    #region Public API
    /// <summary>Shares the existing candidate pool's fixed allocation and configured motion guard.</summary>
    internal ShadowCandidateSchedule(ShadowCandidatePool pool,ShadowSchedulingSettings policy,ShadowCandidateSettings candidates)
    { this.pool=pool;this.policy=policy.Freeze();var settings=candidates.Freeze();guard=settings.RefreshGuardFraction;capacity=settings.HeaderCapacity; }

    /// <summary>Starts one prioritized source interval; previously unused associations may be reclaimed without evicting earlier current work.</summary>
    internal void BeginBatch(long frame)
    {
        if(frame<=this.frame)throw new ArgumentOutOfRangeException(nameof(frame));
        this.frame=frame;visited.Clear();
    }

    /// <summary>Reuses covered motion independently of projection changes and queues one bounded GPU dependency/traversal slice.</summary>
    internal bool Advance(ShadowProjectionSnapshot current,ShadowChunkDomain domain,ShadowBounds receivingChunks,
        long worldOwner,long domainGeneration,long influenceGeneration,long epoch,long sequence,out ShadowViewCandidates association,
        double terrainPaddingInChunks = 0)
    {
        association=default;
        if(!double.IsFinite(terrainPaddingInChunks) || terrainPaddingInChunks<0)throw new ArgumentOutOfRangeException(nameof(terrainPaddingInChunks));
        double yOffset=current.Source.Dimension==1?0:current.Source.Dimension*32768.0;
        var position=current.IsPoint?new Vector3d(current.PositionOrForward.X/32,(current.PositionOrForward.Y-yOffset)/32,current.PositionOrForward.Z/32):-current.PositionOrForward;
        var state=new ShadowCandidateCurrent(current.Source,worldOwner,domainGeneration,influenceGeneration,receivingChunks,position,epoch,sequence,current.Generation);
        entries.TryGetValue(current.Source,out var entry);
        visited.Add(current.Source);
        bool padded=entry is null || !current.IsPoint || entry.Request.SourceBox.Contains(new(position.X-terrainPaddingInChunks,
            position.Y-terrainPaddingInChunks,position.Z-terrainPaddingInChunks,position.X+terrainPaddingInChunks,
            position.Y+terrainPaddingInChunks,position.Z+terrainPaddingInChunks));
        if(entry is not null && (!entry.Request.Covers(state) || !padded))
        {
            pool.Retire(entry.Handle);
            if(entry.PendingRequest is { } pending && entry.PendingHandle is { } replacement && pending.Covers(state))
                entry=new(pending,replacement,entry.PendingFrame,state);
            else {if(entry.PendingHandle is { } unusedPending)pool.Retire(unusedPending);entry=null;}
            entries.Remove(current.Source);
            if(entry is not null)entries.Add(current.Source,entry);
        }
        if(entry is null)
        {
            if(entries.Count>=capacity)
            {
                ShadowSourceKey? victim=null;
                foreach(var key in entries.Keys)if(!visited.Contains(key)){victim=key;break;}
                if(victim is not { } unused)return false;
                pool.Retire(entries[unused].Handle);
                if(entries[unused].PendingHandle is { } pending)pool.Retire(pending);
                entries.Remove(unused);
            }
            ShadowCandidateRequest request;
            if(current.IsPoint)
            {
                var extent=policy.PointCoverageHalfExtentMetres;
                double x=extent.X/32.0+terrainPaddingInChunks,y=extent.Y/32.0+terrainPaddingInChunks,z=extent.Z/32.0+terrainPaddingInChunks;
                request=ShadowCandidateRequest.Point(current.Source,worldOwner,domainGeneration,influenceGeneration,domain,receivingChunks,
                    new(position.X-x,position.Y-y,position.Z-z,position.X+x,position.Y+y,position.Z+z),epoch,sequence);
            }
            else request=ShadowCandidateRequest.Directional(current.Source,worldOwner,domainGeneration,influenceGeneration,domain,receivingChunks,
                position,policy.DirectionalCoverageDegrees,epoch,sequence);
            if(!pool.TryAllocate(request,out var handle))return false;
            entry=new(request,handle,frame,state);entries.Add(current.Source,entry);
        }
        else{entry=entry with{Current=state};entries[current.Source]=entry;}
        NeedsRefresh=entry.Request.NeedsRefresh(state,guard);
        if(!pool.Advance(entry.Handle,state))return false;
        // Begin a replacement inside the guard while retaining the covered association.
        // Only the GPU certificate can choose it; a pending traversal never displaces
        // complete old candidates, and neither association extends the other's bounds.
        if(entry.PendingRequest is { } oldPending && !oldPending.Covers(state))
        {pool.Retire(entry.PendingHandle!.Value);entry=entry with{PendingRequest=null,PendingHandle=null};}
        if(NeedsRefresh && entry.PendingRequest is null)
        {
            ShadowCandidateRequest replacement;
            if(current.IsPoint)
            {
                var e=policy.PointCoverageHalfExtentMetres;
                double x=e.X/32.0+terrainPaddingInChunks,y=e.Y/32.0+terrainPaddingInChunks,z=e.Z/32.0+terrainPaddingInChunks;
                replacement=ShadowCandidateRequest.Point(current.Source,worldOwner,domainGeneration,influenceGeneration,domain,receivingChunks,
                    new(position.X-x,position.Y-y,position.Z-z,position.X+x,position.Y+y,position.Z+z),epoch,sequence);
            }
            else replacement=ShadowCandidateRequest.Directional(current.Source,worldOwner,domainGeneration,influenceGeneration,domain,receivingChunks,
                position,policy.DirectionalCoverageDegrees,epoch,sequence);
            if(pool.TryAllocate(replacement,out var replacementHandle))
                entry=entry with{PendingRequest=replacement,PendingHandle=replacementHandle,PendingFrame=frame};
        }
        ShadowCandidateHandle? readyReplacement=null;
        if(entry.PendingHandle is { } pendingHandle && pool.Advance(pendingHandle,state))readyReplacement=pendingHandle;
        entries[current.Source]=entry;
        association=new(0,entry.Handle,state,readyReplacement);return true;
    }

    /// <summary>Reports guard-band entry independently of rendered-depth currentness.</summary>
    internal bool NeedsRefresh { get; private set; }

    /// <summary>Returns bounded diagnostic metadata; GPU headers remain the authority for completion and unresolved cells.</summary>
    internal ImmutableArray<ShadowCandidateDiagnostic> Diagnostics
    {
        get
        {
            var result=ImmutableArray.CreateBuilder<ShadowCandidateDiagnostic>(entries.Count);
            foreach(var entry in entries.Values)
            {
                var request=entry.Request;var current=entry.Current.PositionOrDirection;double motion;
                if(request.IsPoint)
                {
                    var b=request.SourceBox;
                    motion=Math.Max(Math.Abs(2*current.X-b.MinX-b.MaxX)/(b.MaxX-b.MinX),Math.Max(
                        Math.Abs(2*current.Y-b.MinY-b.MaxY)/(b.MaxY-b.MinY),Math.Abs(2*current.Z-b.MinZ-b.MaxZ)/(b.MaxZ-b.MinZ)));
                }
                else motion=Math.Acos(Math.Clamp(Vector3d.Dot(request.Direction,current)/Math.Sqrt(Vector3d.Dot(current,current)),-1,1))*180/Math.PI/request.RadiusDegrees;
                result.Add(new(request.Source,entry.Handle.Slot,frame-entry.BuiltFrame,request.IsPoint,request.SourceBox,
                    request.Direction,request.RadiusDegrees,motion,request.NeedsRefresh(entry.Current,guard)));
            }
            return result.MoveToImmutable();
        }
    }

    /// <summary>Withdraws disappeared sources at the scheduling boundary without waiting for their GPU users.</summary>
    internal void Retain(IReadOnlySet<ShadowSourceKey> current)
    {
        var removed=new List<ShadowSourceKey>();
        foreach(var key in entries.Keys)if(!current.Contains(key))removed.Add(key);
        foreach(var key in removed)
        {pool.Retire(entries[key].Handle);if(entries[key].PendingHandle is { } pending)pool.Retire(pending);entries.Remove(key);}
    }

    /// <summary>Ends bookkeeping when the containing source also disposes the entire candidate pool, including context teardown.</summary>
    public void Dispose(){entries.Clear();visited.Clear();}
    #endregion
}
