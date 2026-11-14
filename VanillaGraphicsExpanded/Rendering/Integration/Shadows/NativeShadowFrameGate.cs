using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using VanillaGraphicsExpanded.Rendering.Shaders;
using VanillaGraphicsExpanded.Rendering.Shadows.Configuration;
using VanillaGraphicsExpanded.Rendering.Shadows.Candidates;
using VanillaGraphicsExpanded.Rendering.Shadows.Demand;
using VanillaGraphicsExpanded.Rendering.Shadows.Geometry;
using VanillaGraphicsExpanded.Rendering.Shadows.Lights;
using VanillaGraphicsExpanded.Rendering.Shadows.Pages;
using VanillaGraphicsExpanded.Rendering.Shadows.Readiness;
using VanillaGraphicsExpanded.Rendering.Shadows.Scheduling;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.Rendering.Integration.Shadows;

/// <summary>Orders one bounded native update and GPU readiness publication, retaining all submitted inputs until nonblocking retirement.</summary>
internal sealed class NativeShadowFrameGate:IDisposable
{
    private readonly ICoreAPI api;
    private readonly ShadowSettings settings;
    private readonly ShadowScheduler scheduler;
    private readonly ShadowPagePool pages;
    private readonly ShadowReadinessPool readiness;
    private readonly ShadowGpuTimings timings;
    private readonly ShadowPriorityFeedback priorityFeedback;
    private readonly IDisposable? reloadRegistration;
    private ImmutableArray<Vintagestory.Client.NoObf.ShaderProgramBase> surfacePrograms=[];
    private readonly (nint Handle,long Generation) context;
    private readonly List<Flight> flights=new();
    private bool disposed;
    private ShadowFramePublication? publication;
    private ShadowCandidatePool? currentCandidates;
    internal ShadowFrameDiagnostics? Diagnostics { get; private set; }
    /// <summary>Shares one stable priority history with bounded source preparation rather than maintaining another selection policy.</summary>
    internal ShadowScheduler Scheduler=>scheduler;
    internal GpuShaderStorageBuffer Readiness => !disposed&&pages.IsAvailable?readiness.Results:
        throw new InvalidOperationException("This shadow frame publication is unavailable.");
    /// <summary>Offers one same-snapshot provider while all participating surface consumers may still submit.</summary>
    internal ShadowFramePublication? Publication=>publication is {IsCurrent:true}?publication:null;

    /// <summary>Owns one submitted scene and all private draw inputs through the final GPU user.</summary>
    private sealed class Flight
    {
        internal required NativeShadowScene Scene;
        internal required ShadowViewBuffer Views;
        internal ShadowGeometrySelection? Selection;
        internal required ImmutableArray<ShadowPreparedDraw> Draws;
        internal long Bytes;
    }

    #region Public API
    #region Frame construction and admission
    /// <summary>Uses existing resource owners; construction does not enable or migrate a production consumer.</summary>
    internal NativeShadowFrameGate(ICoreAPI api,ShadowSettings settings,ShadowPagePool pages)
    {
        this.api=api;
        // Configuration arrives at an explicit owner boundary, never polled from rendering.
        this.settings=settings.Freeze();
        this.pages=pages;scheduler=new(this.settings.Scheduling);
        if(pages.AllocatedBytes>=this.settings.Scheduling.MemoryMiB*1048576L)
            throw new InvalidOperationException("The physical page allocation leaves no space for bounded frame admission.");
        readiness=new(api,this.settings.Scheduling,this.settings.Temporal);
        try{timings=new();}catch{readiness.Dispose();throw;}
        priorityFeedback=new(this.settings.Scheduling.LightCapacity,this.settings.Temporal);
        context=RenderContextRegistry.Current();
        ShaderCapabilities.Changed+=OnSurfaceChanged;RenderContextRegistry.Retiring+=OnContextRetiring;
        if(api is Vintagestory.API.Client.ICoreClientAPI client)reloadRegistration=ClientRenderingLifetime.Find(client)?.RegisterPublication(Withdraw);
        ShadowDiagnostics.Snapshot=()=>Diagnostics;ShadowDiagnostics.Readiness=()=>disposed||!pages.IsAvailable?null:readiness.Results;
        ShadowDiagnostics.LightCapacity=this.settings.Scheduling.LightCapacity;
        ShadowDiagnostics.CandidateHeaders=()=>disposed?null:currentCandidates?.Headers;
        ShadowDiagnostics.PageEntries=()=>disposed||!pages.IsAvailable?null:pages.Entries;
        ShadowDiagnostics.SampleFailures=()=>disposed||!pages.IsAvailable?null:readiness.SampleFailures;
    }

    /// <summary>Starts accounting before scene preparation or demand, including all retained native/private allocations.</summary>
    internal ShadowFrameBudget BeginFrame(long externalLiveAndRetiringBytes,long frame=0)
    {
        ObjectDisposedException.ThrowIf(disposed,this);EndFrame();Poll();
        if(externalLiveAndRetiringBytes<0 || frame<0)throw new ArgumentOutOfRangeException(nameof(externalLiveAndRetiringBytes));
        timings.Poll(scheduler);timings.Begin(settings.Scheduling.PreparationEstimateMilliseconds);
        if(frame>0)timings.Describe(frame,settings.Scheduling.PreparationEstimateMilliseconds);
        long resident=checked(pages.AllocatedBytes+readiness.AllocatedBytes+timings.AllocatedBytes+priorityFeedback.AllocatedBytes+externalLiveAndRetiringBytes+flights.Sum(f=>f.Bytes));
        return new(ShadowWork.Limits(settings),resident);
    }

    /// <summary>Takes ownership of the frozen scene, runs complete native drawing and publishes eligibility before consumers.</summary>
    internal void Update(NativeShadowFrameInput input,double now,ShadowFrameBudget budget)
    {
        ObjectDisposedException.ThrowIf(disposed,this);Poll();
        long resident=checked(pages.AllocatedBytes+readiness.AllocatedBytes+timings.AllocatedBytes+priorityFeedback.AllocatedBytes+input.WorldBuffers.AllocatedBytes+
            input.Candidates.AllocatedBytes+input.OtherLiveAndRetiringBytes+flights.Sum(f=>f.Bytes));
        bool transferred=false;string? failure=null;
        var decisions=ImmutableArray<ShadowLightDecision>.Empty;
        bool published=false;
        int requested=0,writable=0;
        try
        {
            if(input.Scene is { } captured && !ReferenceEquals(captured.Changes,input.Changes))
                throw new InvalidOperationException("The frame does not carry its native preparation change stream.");
            currentCandidates=input.Candidates;ShadowDiagnostics.Candidates=input.CandidateDiagnostics;
            input.Scene?.UseBudget(budget);
            bool sceneCurrent=input.CurrentInputsComplete&&input.Scene is {IsCurrent:true};
            surfacePrograms=input.SurfacePrograms;
            if(input.OtherLiveAndRetiringBytes<0 || !budget.Limit.Equals(ShadowWork.Limits(settings)) || budget.Used.Bytes<resident ||
                !budget.TryCharge(input.Scene?.PreparationCost??default) ||
                !input.WorldBuffers.Matches(input.World) || !input.Candidates.UsesBudget(budget) ||
                input.SurfacePrograms.IsDefaultOrEmpty || !input.SurfacePrograms.All(p=>ShaderCapabilities.Has(p,ShaderCapability.SharedVertexDeformation)))
                throw new InvalidOperationException("Complete current native preparation or shared surface deformation is unavailable.");
            if(input.Sources.IsDefault || input.Sources.Length>settings.Scheduling.LightCapacity)
                throw new InvalidOperationException("Current source inputs exceed frame capacity.");
            var offers=ImmutableArray.CreateBuilder<ShadowLightOffer>();
            priorityFeedback.Poll(now);
            var prioritySources=ImmutableArray.CreateBuilder<(ShadowSourceKey Source,string Signature)>();
            foreach(var source in input.Sources)
            {
                if(source.Alternatives.IsDefaultOrEmpty || source.Alternatives.Length>settings.Scheduling.MaximumDetailReductions+1)
                    throw new InvalidOperationException("Finite complete-detail alternatives are required.");
                var alternatives=ImmutableArray.CreateBuilder<ShadowDetailOffer>();
                foreach(var alternative in source.Alternatives)
                {
                    if(alternative.Pages.IsDefault || alternative.Views.IsDefault || alternative.Candidates.IsDefault)
                        throw new InvalidOperationException("Unknown demand is not an empty complete set.");
                    var fallback=Fallback(source,alternative);
                    int count=checked(alternative.Pages.Length+fallback.Length);
                    requested=checked(requested+count);
                    if(!ReferenceEquals(input.DemandBudget,budget) && !budget.TryCharge(new(Requests:count,Coverage:count,Bytes:count*2048L)))
                        throw new InvalidOperationException("Current demand production exceeded the shared frame allowance.");
                    bool canRefresh=alternative.CanRefresh&&sceneCurrent&&input.Candidates.BatchValid;
                    var coverage=new ShadowWork(Coverage:checked(count*(long)pages.ProbeLimit),
                        Tiles:checked((canRefresh?2L:1L)*alternative.Pages.Length+fallback.Length),
                        PageOperations:checked(fallback.Length+(canRefresh?0:alternative.Pages.Length)));
                    ShadowWork cost;
                    try
                    {
                        var caps=GpuSupport.Graphics;
                        if(canRefresh && alternative.Views.Any(view=>view.Region.Width>caps.MaxViewportWidth || view.Region.Height>caps.MaxViewportHeight ||
                            view.Region.Width>caps.MaxTextureSize || view.Region.Height>caps.MaxTextureSize))
                            throw new OverflowException("Virtual raster alternative exceeds the effective region limits.");
                        cost=canRefresh?input.Scene!.EstimateWork(alternative.Views,alternative.Pages.Length,alternative.Candidates.Length,input.Candidates.EntryCapacity).Add(coverage):coverage;
                    }
                    catch(OverflowException){cost=budget.Limit.Add(new(Bytes:1));}
                    alternatives.Add(new(alternative.Detail,cost,alternative.EstimatedMilliseconds));
                }
                int retained=source.Alternatives[0].RetainedPages.IsDefault?0:source.Alternatives[0].RetainedPages.Length;
                ShadowWork? retainedCost=retained>0?new(Coverage:checked((source.Alternatives[0].Pages.Length+retained)*(long)pages.ProbeLimit),
                    Tiles:source.Alternatives[0].Pages.Length+retained,
                    PageOperations:source.Alternatives[0].Pages.Length+retained):null;
                bool hasCoverage=source.Alternatives.Any(alternative=>alternative.Complete &&
                    (!alternative.Pages.IsEmpty || !alternative.RetainedPages.IsDefaultOrEmpty));
                string signature=ShadowPriorityFeedback.Fingerprint(source,budget);
                prioritySources.Add((source.Source,signature));
                bool held=source.RetentionEligible && sceneCurrent && input.CurrentInputsComplete && input.SceneChanges.Complete &&
                    signature.Length>0 && priorityFeedback.IsEligible(source.Source,signature,now);
                offers.Add(new(source.Source,hasCoverage?source.Contribution:0,source.Carried,source.RetentionEligible,alternatives.ToImmutable(),retainedCost,held));
            }
            if(!budget.TryCharge(new(Coverage:checked(2L*input.Sources.Length*pages.Capacity),PageOperations:2L*input.Sources.Length)))
                throw new InvalidOperationException("Whole-source dependency checks exceed the frame allowance.");
            decisions=scheduler.Select(input.Lights.Frame,now,offers.ToImmutable(),budget);
            double estimate=settings.Scheduling.PreparationEstimateMilliseconds;
            foreach(var decision in decisions)
                if(decision.Selected)estimate+=input.Sources.First(source=>source.Source==decision.Source).Alternatives.First(alternative=>alternative.Detail==decision.Detail).EstimatedMilliseconds;
            timings.Describe(input.Lights.Frame,estimate);
            if(!pages.BeginBatch()) throw new InvalidOperationException("Page storage is unavailable or still draining.");
            ValidateSources(input,input.CurrentInputsComplete&&(input.Scene is null||sceneCurrent)&&input.SceneChanges.Complete,now);
            var views=ImmutableArray.CreateBuilder<ShadowDrawView>();var candidates=ImmutableArray.CreateBuilder<ShadowViewCandidates>();
            var replacements=ImmutableArray.CreateBuilder<ShadowPageReplacement>();var demand=ImmutableArray.CreateBuilder<ShadowLightDemand>();
            for(int index=0;index<input.Sources.Length;index++)
            {
                var source=input.Sources[index];var decision=decisions[index];
                var alternative=source.Alternatives.FirstOrDefault(a=>a.Detail==decision.Detail)??source.Alternatives[0];
                var fallback=Fallback(source,alternative);
                bool fresh=decision.Selected&&decision.RefreshSelected&&alternative.Complete&&alternative.CanRefresh&&sceneCurrent&&input.Candidates.BatchValid;
                int viewBase=views.Count;
                if(decision.Selected)foreach(var page in alternative.Pages.Concat(fallback))
                    if(!pages.Execute(page.Request with{Operation=ShadowPageOperation.Require}))
                        throw new InvalidOperationException("Retained coverage protection exceeds page command capacity.");
                if(fresh)
                {
                    views.AddRange(alternative.Views);
                    foreach(var association in alternative.Candidates) candidates.Add(association with{View=checked(association.View+viewBase)});
                    foreach(var page in alternative.Pages)
                    {
                        if(page.Request.Key.Source!=source.Source || page.Current.Source!=source.Source) throw new InvalidOperationException("Source demand mismatch.");
                        // A retained projection remains read-only; only current mappings receive replacement writes.
                        if(page.Request.Projection is not { } projection || !projection.Matches(page.Current)) continue;
                        int view=-1;
                        for(int v=0;v<alternative.Views.Length;v++) if(alternative.Views[v].Projection.Matches(projection))
                        { view=v;break; }
                        if(view<0) throw new InvalidOperationException("Demand lacks a compatible full virtual view.");
                        replacements.Add(new(viewBase+view,page.Request with{Operation=ShadowPageOperation.Allocate,Timestamp=now,ReuseCurrent=true}));
                    }
                }
                bool contributing=source.Contribution>0&&(!alternative.Complete || !alternative.Pages.IsEmpty || !fallback.IsEmpty);
                demand.Add(new(source.Source,alternative.Detail,decision.Selected,alternative.Complete,alternative.Pages,fallback,contributing));
            }
            if(replacements.Count>0)
            {
                var immutableViews=views.ToImmutable();
                var buffer=new ShadowViewBuffer(immutableViews,pages,settings.Drawing);
                ShadowGeometrySelection? selection=null;ImmutableArray<ShadowPreparedDraw> draws=[];
                try
                {
                    selection=input.Scene!.Select(api,immutableViews,candidates.ToImmutable(),input.Candidates,input.WorldBuffers,buffer,pages,settings.Drawing);
                    draws=input.Scene.Prepare(input.NativeWorld,immutableViews,buffer,selection,pages,input.TerrainShader,input.EntityShader,input.ItemShader,
                        input.DeltaTime,input.PaddingX,input.PaddingY);
                    input.Scene.Submit(pages,selection,replacements.ToImmutable(),draws,input.Changes,input.SceneChanges,now);
                    writable=replacements.Count;selection.Seal();buffer.Seal();
                    flights.Add(new(){Scene=input.Scene,Views=buffer,Selection=selection,Draws=draws,
                        Bytes=checked(selection.AllocatedBytes+buffer.AllocatedBytes+input.Scene.PreparationCost.Bytes+draws.Length*8192L)});
                    transferred=true;
                }
                catch(Exception error)
                {
                    if(disposed || !pages.CanSubmit)
                    {foreach(var draw in draws)draw.Dispose();selection?.Dispose();buffer.Dispose();throw;}
                    if(pages.ReplacementsPending)pages.FinishReplacements(false);
                    // Selection and partial raster users still own their charged captures.
                    // Their failure cannot erase independently eligible complete depth.
                    selection?.Seal();buffer.Seal();
                    flights.Add(new(){Scene=input.Scene!,Views=buffer,Selection=selection,Draws=draws,
                        Bytes=checked((selection?.AllocatedBytes??0)+buffer.AllocatedBytes+input.Scene!.PreparationCost.Bytes+draws.Length*8192L)});
                    transferred=true;failure="Refresh deferred: "+error.Message;
                    ValidateSources(input,false,now);
                }
            }
            bool current=input.SurfacePrograms.All(p=>ShaderCapabilities.Has(p,ShaderCapability.SharedVertexDeformation));
            readiness.Publish(pages,input.Lights.Frame,now,demand.ToImmutable(),current);
            priorityFeedback.Capture(readiness.Priority,prioritySources.ToImmutable(),now);
            publication=new(input.Lights,pages,readiness.Requests,readiness.Results,readiness.SampleFailures,readiness.SourceIndices);published=true;
        }
        catch(Exception error)
        {
            failure=error.Message;readiness.Withdraw(false);priorityFeedback.Invalidate();
            if(pages.ReplacementsPending)pages.FinishReplacements(false);
            throw;
        }
        finally
        {
            if(!published && pages.CanSubmit)pages.SealBatch();
            timings.End();
            if(!transferred)input.Scene?.Dispose();
            Diagnostics=new(input.Lights.Frame,budget.Used,budget.Limit,budget.Rejections,resident,pages.DrainingBytes,requested,writable,decisions,failure,
                timings.LastMilliseconds,timings.LastFrame,timings.MissingSamples,budget.LastRejection,priorityFeedback.MissingSamples);
        }
    }

    #endregion
    #region Publication and retirement
    /// <summary>Invalidates borrowed packets synchronously without submitting GPU work or retiring their pool.</summary>
    internal void WithdrawPublication(){publication?.Withdraw();publication=null;}

    /// <summary>Withdraws all consumer eligibility immediately at shader, world, policy or capability loss.</summary>
    internal void Withdraw() { WithdrawPublication();timings.End();readiness.Withdraw();priorityFeedback.Invalidate();pages.Withdraw(); }

    /// <summary>Fails the current frame closed while preserving bounded cached pages for later overload recovery.</summary>
    internal void RejectFrame(string reason,ShadowLightSnapshot? lights=null,double now=0)
    {
        WithdrawPublication();timings.End();readiness.Withdraw(false);priorityFeedback.Invalidate();
        if(lights is not null && lights.Frame>readiness.Frame && pages.IsAvailable)
        {
            // Current source capture is still known even if receiver/native capture
            // failed. Count its unavailable interval without erasing duty history.
            var unavailable=ImmutableArray.CreateBuilder<ShadowLightDemand>();
            foreach(var light in new[]{lights.Sun,lights.Moon})
                if(light.Source.IsKnown && light.Irradiance.LengthSquared()>0 && unavailable.Count<settings.Scheduling.LightCapacity)
                    unavailable.Add(new(light.Source,0,false,false,[]));
            foreach(var light in lights.Points)
                if(light.Source.IsKnown && unavailable.Count<settings.Scheduling.LightCapacity)
                    unavailable.Add(new(light.Source,0,false,false,[]));
            readiness.Publish(pages,lights.Frame,now,unavailable.ToImmutable(),true,unavailableOnly:true);
        }
        if(Diagnostics is { } previous)Diagnostics=previous with{Failure=reason};
    }

    /// <summary>Releases completed private captures without a CPU completion wait.</summary>
    internal void Poll()
    {
        pages.Poll();
        for(int i=flights.Count-1;i>=0;i--)
        {
            var flight=flights[i];
            if((flight.Selection is { } selection && !selection.Poll()) || !flight.Views.Poll())continue;
            foreach(var draw in flight.Draws)draw.Dispose();flight.Scene.Dispose();flights.RemoveAt(i);
        }
    }

    /// <summary>Fences physical-page users only after all participating surface consumers have submitted.</summary>
    internal void EndFrame()
    {
        WithdrawPublication();
        if(!disposed && pages.CanSubmit)
        {
            // Surface fragments append diagnostic atomics after readiness.
            // Publish them before overlays and the next frame's buffer reset.
            RHI.RHICommandContext.Current.Barrier(new(RHI.ResourceAccess.ShaderStorage|RHI.ResourceAccess.BufferUpdate));
            pages.SealBatch();
        }
    }

    /// <summary>Terminates ownership through existing RHI retirement; native storage remains borrowed.</summary>
    public void Dispose()
    {
        if(disposed)return;disposed=true;WithdrawPublication();readiness.Dispose();
        timings.Dispose();priorityFeedback.Dispose();reloadRegistration?.Dispose();ShaderCapabilities.Changed-=OnSurfaceChanged;RenderContextRegistry.Retiring-=OnContextRetiring;
        ShadowDiagnostics.Snapshot=null;ShadowDiagnostics.Readiness=null;ShadowDiagnostics.LightCapacity=0;
        ShadowDiagnostics.SampleFailures=null;
        ShadowDiagnostics.CandidateHeaders=null;ShadowDiagnostics.PageEntries=null;ShadowDiagnostics.Candidates=[];
        foreach(var flight in flights){foreach(var draw in flight.Draws)draw.Dispose();flight.Selection?.Dispose();flight.Views.Dispose();flight.Scene.Dispose();}
        flights.Clear();
    }
    #endregion
    #endregion

    #region Private
    /// <summary>Checks every cached source mapping once; repeated failures preserve its first invalidation deadline.</summary>
    private void ValidateSources(NativeShadowFrameInput input,bool complete,double now)
    {
        foreach(var source in input.Sources)
        {
            var projection=source.CurrentProjection??source.Alternatives.SelectMany(alternative=>alternative.Pages).FirstOrDefault()?.Current;
            if(projection is null)continue;
            if(!pages.Execute(new(ShadowPageOperation.ValidateSource,new(source.Source,projection.Generation,projection.FaceOrLevel,0,0),
                default,input.World.Generation,input.SceneChanges.Epoch,input.SceneChanges.Sequence,
                Complete:complete,Projection:projection,Timestamp:now)))
                throw new InvalidOperationException("Source dependency validation cannot fit the frame allowance.");
        }
    }
    /// <summary>Offers coherent lower-detail generations only after the selected generation fails complete coverage.</summary>
    private static ImmutableArray<ShadowDemandPage> Fallback(ShadowFrameLight source,ShadowFrameAlternative selected)
    {
        var result=ImmutableArray.CreateBuilder<ShadowDemandPage>();
        if(!selected.RetainedPages.IsDefault)result.AddRange(selected.RetainedPages.Select(page=>page with{Detail=selected.Detail}));
        foreach(var lower in source.Alternatives.Where(alternative=>alternative.Detail<selected.Detail).OrderByDescending(alternative=>alternative.Detail))
        {
            if(!lower.Complete)continue;
            result.AddRange(lower.Pages.Select(page=>page with{Detail=lower.Detail}));
            if(!lower.RetainedPages.IsDefault)result.AddRange(lower.RetainedPages.Select(page=>page with{Detail=lower.Detail}));
        }
        return result.ToImmutable();
    }
    /// <summary>Withdraws readiness immediately when a participating surface shader recovers, loses capability or changes executable.</summary>
    private void OnSurfaceChanged(Vintagestory.Client.NoObf.ShaderProgramBase program)
    {if(surfacePrograms.Contains(program)){WithdrawPublication();readiness.Withdraw();priorityFeedback.Invalidate();}}
    /// <summary>Ends publications at their originating context lifetime.</summary>
    private void OnContextRetiring((nint Handle,long Generation) retiring){if(retiring==context)Dispose();}
    #endregion
}
