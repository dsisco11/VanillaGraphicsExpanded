using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.PBR.Materials;
using VanillaGraphicsExpanded.Rendering.Shadows.Candidates;
using VanillaGraphicsExpanded.Rendering.Shadows.Configuration;
using VanillaGraphicsExpanded.Rendering.Shadows.Demand;
using VanillaGraphicsExpanded.Rendering.Shadows.Geometry;
using VanillaGraphicsExpanded.Rendering.Shadows.Lights;
using VanillaGraphicsExpanded.Rendering.Shadows.Projections;
using VanillaGraphicsExpanded.Rendering.Shadows.Scheduling;
using VanillaGraphicsExpanded.Rendering.Shadows.World;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.Rendering.Integration.Shadows;

/// <summary>Composes bounded native scene capture, conservative surface demand, projection history and GPU chunk associations.</summary>
internal sealed class NativeShadowFrameSource:IShadowFrameSource,IDisposable
{
    private readonly ICoreClientAPI api;
    private readonly ChunkRenderer renderer;
    private readonly FrustumCulling culler;
    private readonly NativeShadowWorldAdapter world;
    private readonly NativeShadowEntityPreparation preparation;
    private readonly MaterialAtlasSystem materials;
    private readonly DefaultShaderUniforms uniforms;
    private readonly ShadowSceneChanges changes;
    private readonly IShadowSurfaceReceiverSource receivers;
    private readonly ShadowSettings settings;
    private readonly ShadowScheduler scheduler;
    private readonly ShadowWorldGpuBuffers uploaded=new();
    private readonly ShadowCandidatePool candidates;
    private readonly ShadowCandidateSchedule associations;
    private readonly ShadowProjectionHistory projections;
    private readonly Func<bool,bool,ShadowNativeMaterialShader> terrainShader;
    private readonly ShadowEntityShader entityShader;
    private readonly ShadowStaticShader itemShader;
    private readonly Func<double> clock;
    private readonly bool waving;
    private long sceneSequence,sceneEpoch=1;
    private ShadowWorldPublication? previousWorld;
    private double previousPadding;
    private bool disposed;

    #region Public API
    /// <summary>Captures configuration and existing native owners; no mesh-pool registry or alternate scene ownership is created.</summary>
    internal NativeShadowFrameSource(ICoreClientAPI api,ChunkRenderer renderer,FrustumCulling culler,NativeShadowWorldAdapter world,
        NativeShadowEntityPreparation preparation,MaterialAtlasSystem materials,DefaultShaderUniforms uniforms,ShadowSceneChanges changes,
        IShadowSurfaceReceiverSource receivers,ShadowSettings settings,bool waving,Func<double> clock,
        Func<bool,bool,ShadowNativeMaterialShader> terrainShader,ShadowEntityShader entityShader,ShadowStaticShader itemShader,ShadowScheduler scheduler)
    {
        this.api=api;this.renderer=renderer;this.culler=culler;this.world=world;this.preparation=preparation;
        this.materials=materials;this.uniforms=uniforms;this.changes=changes;this.receivers=receivers;
        if(!ReferenceEquals(preparation.Changes,changes))throw new ArgumentException("Native preparation and shadow admission require the same change stream.",nameof(changes));
        this.waving=waving;this.clock=clock;this.terrainShader=terrainShader;this.entityShader=entityShader;this.itemShader=itemShader;
        this.scheduler=scheduler;
        this.settings=settings.Freeze();
        _=ShadowResourceLayout.Resolve(this.settings);
        candidates=ShadowCandidatePool.Create(api,this.settings.Candidates);
        associations=new(candidates,settings.Scheduling,settings.Candidates);
        projections=new(settings.Scheduling.ProjectionHistoryCapacity,settings.Temporal.MaxRenderedAgeSeconds);
    }

    /// <inheritdoc />
    public long LiveAndRetiringBytes=>checked(uploaded.AllocatedBytes+candidates.AllocatedBytes+projections.AllocatedBytes);

    /// <inheritdoc />
    public NativeShadowFrameInput Capture(ShadowLightSnapshot lights,float deltaTime,ShadowFrameBudget budget)
    {
        ObjectDisposedException.ThrowIf(disposed,this);
        bool currentInputs=budget.TryCharge(new(Inputs:settings.Inputs.ChunkCapacity+settings.Inputs.RangeCapacity+settings.Drawing.EntityVisits));
        var observed=currentInputs?world.Capture():previousWorld;
        currentInputs&=observed is {Complete:true};
        var publication=currentInputs?observed!:previousWorld;
        if(publication is null || !publication.Envelopes.TryGetValue(lights.Dimension,out var envelope))
            throw new InvalidOperationException("Complete loaded world envelope is unavailable.");
        long newBytes=uploaded.AdditionalBytesFor(publication);
        if(!budget.TryCharge(new(Bytes:newBytes)))
            throw new InvalidOperationException("World publication storage exceeds the total allowance.");
        if(!uploaded.Upload(publication))throw new InvalidOperationException("Current world upload failed.");
        if(currentInputs)previousWorld=publication;
        var surface=receivers.Capture(lights,budget);
        if(surface.Frame!=lights.Frame || surface.World!=lights.World || surface.Dimension!=lights.Dimension ||
            surface.Regions.IsDefault || surface.Regions.Length>settings.Scheduling.ReceiverVisits ||
            surface.Programs.IsDefaultOrEmpty || surface.CompleteCategories!=ShadowSurfaceCategory.All)
            throw new InvalidOperationException("Every participating surface family requires complete current early receiver bounds.");
        var scene=changes.Capture(sceneSequence,sceneEpoch);sceneSequence=scene.Sequence;sceneEpoch=scene.Epoch;
        var candidateChanges=new ShadowCandidateChanges(publication.Generation,0,0,true,[]);
        if(!candidates.BeginBatch(candidateChanges,publication,uploaded,budget))throw new InvalidOperationException("Candidate dependencies are unavailable.");
        associations.BeginBatch(lights.Frame);
        var origin=new Vector3d(lights.OriginX*32.0+lights.OriginRemainder.X,lights.OriginY*32.0+lights.OriginRemainder.Y,lights.OriginZ*32.0+lights.OriginRemainder.Z);
        var detail=NativeShadowTerrainDetail.Capture(culler,origin.X,origin.Z);
        NativeShadowTerrainScene? terrain=null;
        NativeShadowScene? native=null;
        try
        {
            if(currentInputs)
            {
                try
                {
                    terrain=new NativeShadowTerrainScene(renderer,world,publication,detail,materials,uniforms,waving,settings.Drawing);
                    previousPadding=terrain.Padding;
                    var entities=new NativeShadowEntityScene(preparation,settings.Drawing,terrain.Drawing.CommandCount,settings.Drawing.ViewCapacity);
                    try{native=new(terrain,entities);}catch{entities.Dispose();throw;}
                }
                catch(InvalidOperationException){terrain?.Dispose();terrain=null;currentInputs=false;}
                catch(NotSupportedException){terrain?.Dispose();terrain=null;currentInputs=false;}
            }
            var sources=ImmutableArray.CreateBuilder<ShadowFrameLight>();
            var work=new ShadowDemandWorkQueue(settings.Scheduling.LightCapacity);
            double now=clock();
            AddDirectional(lights.Sun);AddDirectional(lights.Moon);
            if(lights.WorldToView.Length!=16)throw new InvalidOperationException("Current light coordinate convention is missing.");
            var inverse=Mat4d.Invert(new double[16],lights.WorldToView.ToArray());
            if(inverse is null)throw new InvalidOperationException("Current light transform is singular.");
            foreach(var point in lights.Points)
            {
                if(!point.Source.IsKnown || point.Source.Dimension!=lights.Dimension || point.Radius<=settings.Pages.PointNear)continue;
                var v=point.ViewPosition;
                double x=inverse[0]*v.X+inverse[4]*v.Y+inverse[8]*v.Z+inverse[12];
                double y=inverse[1]*v.X+inverse[5]*v.Y+inverse[9]*v.Z+inverse[13];
                double z=inverse[2]*v.X+inverse[6]*v.Y+inverse[10]*v.Z+inverse[14];
                var position=origin+new Vector3d(x,y,z);
                double contribution=Contribution(surface.Regions,origin,position,point.Radius,
                    Math.Max(0,point.Payload.X)+Math.Max(0,point.Payload.Y)+Math.Max(0,point.Payload.Z),budget);
                Add(point.Source,contribution,point.Source.Kind==ShadowSourceKind.Hand,
                    (quality)=>projections.Point(point.Source,quality,now,position,settings.Pages.PointNear,point.Radius,
                        Math.Max(settings.Pages.PageSize,settings.Scheduling.PointFaceResolution>>Math.Max(0,settings.Scheduling.MaximumDetailReductions-quality)),settings.Pages.PageSize),point.Radius);
            }
            work.Execute(scheduler,now);
            associations.Retain(sources.Select(s=>s.Source).ToHashSet());
            return new(lights,native,publication,uploaded,candidates,changes,scene,sources.ToImmutable(),surface.Programs,api.World,
                terrainShader,entityShader,itemShader,deltaTime,0,0,projections.AllocatedBytes,currentInputs,budget,associations.Diagnostics);

            /// <summary>Preserves independent celestial opportunities and fits depth to the complete represented domain.</summary>
            void AddDirectional(ShadowDirectionalLight light)
            {
                var d=light.Direction;double contribution=Contribution(surface.Regions,origin,null,0,
                    Math.Max(0,light.Irradiance.X)+Math.Max(0,light.Irradiance.Y)+Math.Max(0,light.Irradiance.Z),budget);
                if(contribution<=0)return;
                double offset=lights.Dimension==1?0:lights.Dimension*32768.0;
                var represented=new ShadowBounds(envelope.MinX,envelope.MinY+offset,envelope.MinZ,envelope.MaxX,envelope.MaxY+offset,envelope.MaxZ);
                foreach(var receiver in surface.Regions)represented=Union(represented,receiver.Bounds);
                double depthPadding=1+Math.Sqrt(3)*((terrain?.Padding??previousPadding)+(3+settings.Pages.NormalBiasTexels)*
                    Math.ScaleB(settings.Scheduling.DirectionalTexelMetres,settings.Scheduling.MaximumDetailReductions));
                Add(light.Source,contribution,false,(quality)=>projections.Directional(light.Source,quality,now,new(d.X,d.Y,d.Z),represented,
                    settings.Scheduling.DirectionalTexelMetres,settings.Scheduling.MaximumDetailReductions+1,settings.Pages.PageSize,depthPadding),0);
            }

            /// <summary>Builds bounded quality alternatives and regenerates current receiving coverage in every retained mapping.</summary>
            void Add(ShadowSourceKey source,double contribution,bool carried,Func<int,ShadowProjectionHistory.Entry> mapping,double radius)
            {
                var alternatives=ImmutableArray.CreateBuilder<ShadowFrameAlternative>();
                ShadowBounds? union=null;ShadowProjectionSnapshot? currentSnapshot=null;
                bool associationReady=false;ShadowViewCandidates candidate=default;
                int sourceIndex=-1;
                work.Add(new(source,contribution,carried),Minimum,Refine);

                /// <summary>Completes this source's minimum attempt before any source refines its demand.</summary>
                void Minimum()
                {
                    var minimum=Build(0);
                    foreach(var page in minimum.Pages)union=union is { } b?Union(b,page.Request.Region):page.Request.Region;
                    var domain=Domain(lights.Dimension,envelope);
                    if(union is { } receiving && currentSnapshot is not null)
                    {
                        double offset=lights.Dimension==1?0:lights.Dimension*32768.0,padding=terrain?.Padding??previousPadding;
                        var r=new ShadowBounds(Math.Floor((receiving.MinX-padding)/32),Math.Floor((receiving.MinY-offset-padding)/32),Math.Floor((receiving.MinZ-padding)/32),
                            Math.Ceiling((receiving.MaxX+padding)/32),Math.Ceiling((receiving.MaxY-offset+padding)/32),Math.Ceiling((receiving.MaxZ+padding)/32));
                        associationReady=associations.Advance(currentSnapshot,domain,r,publication.Owner,publication.Generation,1,publication.Generation,0,out candidate,padding/32);
                    }
                    alternatives.Add(Associate(minimum));sourceIndex=sources.Count;
                    sources.Add(new(source,contribution,carried,true,alternatives.ToImmutable(),currentSnapshot));
                }

                /// <summary>Spends remaining demand work only after every source had a minimum-coverage opportunity.</summary>
                void Refine()
                {
                    if(!alternatives[0].Complete)return;
                    for(int quality=1;quality<=settings.Scheduling.MaximumDetailReductions;quality++)
                    {
                        var alternative=Build(quality);
                        if(!alternative.Complete)break;
                        alternatives.Add(Associate(alternative));
                    }
                    sources[sourceIndex]=sources[sourceIndex] with{Alternatives=alternatives.ToImmutable()};
                }

                /// <summary>Builds one complete mapping attempt and its bounded retained alternatives.</summary>
                ShadowFrameAlternative Build(int quality)
                {
                    var current=mapping(quality);var demand=Demand(current,current,quality);
                    var retained=ImmutableArray.CreateBuilder<ShadowDemandPage>();
                    foreach(var old in projections.Retained(current,now))
                    {
                        var oldDemand=Demand(old,current,quality);
                        if(oldDemand.Complete)retained.AddRange(oldDemand.Pages.Select(page=>page with{Detail=quality}));
                        else break;
                    }
                    var viewList=Views(current,demand.Pages,origin);
                    currentSnapshot??=current.Point is { } p?ShadowProjectionSnapshot.Point(p,0):ShadowProjectionSnapshot.Directional(current.Directional!,0);
                    return new(quality,demand.Complete,ImmutableArray.CreateRange(demand.Pages.Select(page=>page with{Detail=quality})),
                        viewList,[],settings.Scheduling.LightEstimateMilliseconds+viewList.Length*settings.Scheduling.ViewEstimateMilliseconds,retained.ToImmutable());
                }

                /// <summary>Reuses the coarsest enclosure only when it conservatively contains every finer receiving footprint.</summary>
                ShadowFrameAlternative Associate(ShadowFrameAlternative alternative)
                {
                    bool covered=associationReady && union is { } box && alternative.Pages.All(page=>box.Contains(page.Request.Region));
                    var handles=ImmutableArray.CreateBuilder<ShadowViewCandidates>();
                    for(int v=0;v<alternative.Views.Length;v++)handles.Add(candidate with{View=v,Current=candidate.Current with{ProjectionGeneration=alternative.Views[v].Projection.Generation}});
                    return alternative with{Candidates=covered?handles.ToImmutable():[],CanRefresh=covered};
                }

                /// <summary>Generates every layer with the selected rendered mapping and the unchanged current illumination.</summary>
                ShadowSurfaceDemand Demand(ShadowProjectionHistory.Entry rendered,ShadowProjectionHistory.Entry current,int quality)
                {
                    var demand=new ShadowSurfaceDemand(budget,settings.Pages,settings.Scheduling.DemandRequests);
                    foreach(var region in surface.Regions)
                    {
                        if(rendered.Point is { } point)demand.AddPoint(region,point,current.Point!,radius,publication.Generation,scene.Epoch,scene.Sequence);
                        else demand.AddDirectional(region,rendered.Directional!,current.Directional!,settings.Scheduling.MaximumDetailReductions-quality,
                            publication.Generation,scene.Epoch,scene.Sequence);
                        if(demand.Failure is not null)break;
                    }
                    demand.CompleteCategory(surface.CompleteCategories);return demand;
                }
            }
        }
        catch{if(native is not null)native.Dispose();else terrain?.Dispose();throw;}
    }

    /// <summary>Withdraws association ownership and releases owned input metadata; borrowed native geometry remains native-owned.</summary>
    public void Dispose(){if(disposed)return;disposed=true;associations.Dispose();candidates.Dispose();uploaded.Dispose();}
    #endregion

    #region Private
    /// <summary>Estimates visible direct contribution from conservative projected receiver extent and local attenuation.</summary>
    private static double Contribution(ImmutableArray<ShadowReceiverRegion> regions,Vector3d camera,Vector3d? light,
        double radius,double power,ShadowFrameBudget budget)
    {
        if(power<=0 || !budget.TryCharge(new(Inputs:regions.Length)))return 0;
        double sum=0;
        foreach(var receiver in regions)
        {
            var b=receiver.Bounds;
            double x=b.MaxX-b.MinX,y=b.MaxY-b.MinY,z=b.MaxZ-b.MinZ;
            double area=Math.Max(1e-12,x*y+x*z+y*z);
            var centre=new Vector3d((b.MinX+b.MaxX)*0.5,(b.MinY+b.MaxY)*0.5,(b.MinZ+b.MaxZ)*0.5);
            var delta=centre-camera;double projected=area/(area+Vector3d.Dot(delta,delta));
            double attenuation=1;
            if(light is { } point)
            {
                double dx=Math.Max(0,Math.Max(b.MinX-point.X,point.X-b.MaxX));
                double dy=Math.Max(0,Math.Max(b.MinY-point.Y,point.Y-b.MaxY));
                double dz=Math.Max(0,Math.Max(b.MinZ-point.Z,point.Z-b.MaxZ));
                double distance=Math.Sqrt(dx*dx+dy*dy+dz*dz);
                attenuation=Math.Max(0,1-distance/radius);attenuation*=attenuation;
            }
            sum+=projected*attenuation;
        }
        return power*sum;
    }
    /// <summary>Groups pages by projection face/level so one virtual region covers many physical tiles.</summary>
    private static ImmutableArray<ShadowDrawView> Views(ShadowProjectionHistory.Entry entry,ImmutableArray<ShadowDemandPage> pages,Vector3d origin)
    {
        var result=ImmutableArray.CreateBuilder<ShadowDrawView>();
        foreach(var face in pages.GroupBy(page=>page.Request.Key.FaceOrLevel))
        {
            int size=face.First().Request.Projection!.PageSize;
            long x=face.Min(p=>p.Request.Key.X)*(long)size,y=face.Min(p=>p.Request.Key.Y)*(long)size;
            long x1=(face.Max(p=>p.Request.Key.X)+1L)*size,y1=(face.Max(p=>p.Request.Key.Y)+1L)*size;
            var region=new ShadowRasterRegion(x,y,checked((int)(x1-x)),checked((int)(y1-y)));
            result.Add(entry.Point is { } point?ShadowDrawView.Point(point,face.Key,region,region,origin):
                ShadowDrawView.Directional(entry.Directional!,face.Key,region,region,origin));
        }
        return result.ToImmutable();
    }

    /// <summary>Converts the native dimension-local envelope without imposing a separate caster distance.</summary>
    private static ShadowChunkDomain Domain(int dimension,ShadowBounds b)=>new(dimension,checked((int)Math.Floor(b.MinX/32)),
        checked((int)Math.Floor(b.MinY/32)),checked((int)Math.Floor(b.MinZ/32)),checked((int)Math.Ceiling(b.MaxX/32)-1),
        checked((int)Math.Ceiling(b.MaxY/32)-1),checked((int)Math.Ceiling(b.MaxZ/32)-1));
    /// <summary>Encloses all current receiver footprints before constructing one reusable source association.</summary>
    private static ShadowBounds Union(ShadowBounds a,ShadowBounds b)=>new(Math.Min(a.MinX,b.MinX),Math.Min(a.MinY,b.MinY),Math.Min(a.MinZ,b.MinZ),
        Math.Max(a.MaxX,b.MaxX),Math.Max(a.MaxY,b.MaxY),Math.Max(a.MaxZ,b.MaxZ));
    #endregion
}
