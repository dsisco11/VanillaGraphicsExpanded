using System.Numerics;
using VanillaGraphicsExpanded.LumOn.Scene;
using VanillaGraphicsExpanded.LumOn.WorldProbes.Gpu;
using VanillaGraphicsExpanded.LumOn.WorldProbes.Tracing;
using VanillaGraphicsExpanded.Tests.Fixtures.WorldProbes;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks resident GPU lighting transport through the production atlas publication owner.</summary>
[Collection("NearFieldMaterialCapture")]
[Trait("Category","GPU")]
public sealed class WorldProbeResidentPublicationTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Resident publication
    /// <summary>Ready lighting and valid black publish the same directional values as diagnostic full readback.</summary>
    [Theory]
    [InlineData(0)] [InlineData(32)]
    public void ResidentPublicationMatchesDiagnosticLighting(int light)
    {
        EnsureContextValid();
        using var platform=new EngineShaderPlatformScope();
        using var room=new SurfaceLightingEnclosureFixture(blockLight:light);room.Seed();
        using var assets=new BinaryShaderApiFixture();
        using var atlas=new SurfaceLightingWorldProbeFixture();
        using var batch=new WorldProbeTraceBatch(assets.Api);
        var item=Item();
        var directions=WorldProbeGpuIntegration.CreateDirections(item);
        batch.Submit(room.Geometry.Scene,room.Snapshot,
            [new(item.ProbePosWorld,16,256,8,0,directions.Length)],directions.AsSpan());
        GpuTestFence.WaitForGpuOrSkip("Diagnostic reference for resident publication");
        Assert.True(batch.TryRead(out var answers));
        var reference=WorldProbeGpuIntegration.Integrate(item,answers,0,answers.Length);
        atlas.Upload(reference);
        var expectedVisibility=atlas.Resources.ProbeVis0.ReadPixels();
        var expectedDistance=atlas.Resources.ProbeDist0.ReadPixels();
        var expectedMetadata=atlas.Resources.ProbeMeta0.ReadPixels();
        using var backend=new LumOnWorldProbeGpuTraceBackend(assets.Api,256,()=>room.Geometry.Scene,
            ()=>room.Snapshot,(_,_)=>true,new UnexpectedWorldProbeTraceScene(),_=>true);
        Assert.True(backend.TryEnqueue(item));Assert.False(backend.TryDequeueResult(out _));
        GpuTestFence.WaitForGpuOrSkip("Resident lighting completion");
        Assert.True(backend.TryDequeueResult(out var result));Assert.True(result.Success);
        Assert.NotNull(result.GpuLease);
        Assert.All(result.AtlasSamples,sample=>Assert.True(sample.GpuRayIndex>=0));
        var pixels=atlas.Upload(result);
        Assert.Equal(expectedVisibility,atlas.Resources.ProbeVis0.ReadPixels());
        Assert.Equal(expectedDistance,atlas.Resources.ProbeDist0.ReadPixels());
        Assert.Equal(expectedMetadata,atlas.Resources.ProbeMeta0.ReadPixels());
        foreach(var sample in reference.AtlasSamples)
        {
            int index=((sample.OctY<<3)+sample.OctX)<<2;
            Assert.InRange(Math.Abs(pixels[index]-sample.RadianceRgb.X),0,.003f);
            Assert.InRange(Math.Abs(pixels[index+1]-sample.RadianceRgb.Y),0,.003f);
            Assert.InRange(Math.Abs(pixels[index+2]-sample.RadianceRgb.Z),0,.003f);
            Assert.InRange(Math.Abs(pixels[index+3]-sample.AlphaEncodedDistSigned),0,.003f);
        }
        Assert.All(pixels.Where((_,index)=>(index&3)==3),value=>Assert.True(value>0));
    }

    /// <summary>Publication rechecks ownership after completion and leaves every atlas channel untouched on rejection.</summary>
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public void CompletedLeaseRejectsChangedOwnershipBeforeCommit(int change)
    {
        EnsureContextValid();
        using var platform=new EngineShaderPlatformScope();
        using var room=new SurfaceLightingEnclosureFixture();room.Seed();
        using var assets=new BinaryShaderApiFixture();
        using var atlas=new SurfaceLightingWorldProbeFixture();
        bool current=true, providerThrows=false;
        using var backend=new LumOnWorldProbeGpuTraceBackend(assets.Api,256,()=>room.Geometry.Scene,
            ()=>providerThrows?throw new InvalidOperationException("Retired provider"):room.Snapshot,(_,_)=>true,new UnexpectedWorldProbeTraceScene(),_=>current);
        Assert.True(backend.TryEnqueue(Item()));Assert.False(backend.TryDequeueResult(out _));
        GpuTestFence.WaitForGpuOrSkip("Resident stale completion");
        Assert.True(backend.TryDequeueResult(out var result));Assert.True(result.Success);
        var radiance=atlas.Resources.ProbeRadianceAtlas.ReadPixels();
        var metadata=atlas.Resources.ProbeMeta0.ReadPixels();
        if(change==0)room.Geometry.Dirty();
        else if(change==1)room.DependencyRevision++;
        else if(change==2)current=false;
        else if(change==3)backend.Dispose();
        else providerThrows=true;
        Assert.Equal(0,atlas.TryUpload(result,65536));
        Assert.Equal(radiance,atlas.Resources.ProbeRadianceAtlas.ReadPixels());
        Assert.Equal(metadata,atlas.Resources.ProbeMeta0.ReadPixels());
    }

    /// <summary>A refused budget cannot publish metadata or consume the retained resident lighting.</summary>
    [Fact]
    public void InsufficientBudgetRetainsCompletePublicationForRetry()
    {
        EnsureContextValid();
        using var platform=new EngineShaderPlatformScope();
        using var room=new SurfaceLightingEnclosureFixture();room.Seed();
        using var assets=new BinaryShaderApiFixture();
        using var atlas=new SurfaceLightingWorldProbeFixture();
        using var backend=new LumOnWorldProbeGpuTraceBackend(assets.Api,256,()=>room.Geometry.Scene,
            ()=>room.Snapshot,(_,_)=>true,new UnexpectedWorldProbeTraceScene(),_=>true);
        Assert.True(backend.TryEnqueue(Item()));Assert.False(backend.TryDequeueResult(out _));
        GpuTestFence.WaitForGpuOrSkip("Resident budget completion");
        Assert.True(backend.TryDequeueResult(out var result));Assert.True(result.Success);
        var before=atlas.Resources.ProbeRadianceAtlas.ReadPixels();
        var metadata=atlas.Resources.ProbeMeta0.ReadPixels();
        Assert.Equal(0,atlas.TryUpload(result with {GpuLease=null},65536));
        Assert.Equal(before,atlas.Resources.ProbeRadianceAtlas.ReadPixels());
        Assert.Equal(metadata,atlas.Resources.ProbeMeta0.ReadPixels());
        Assert.Equal(0,atlas.TryUpload(result,1));
        Assert.Equal(before,atlas.Resources.ProbeRadianceAtlas.ReadPixels());
        Assert.Equal(metadata,atlas.Resources.ProbeMeta0.ReadPixels());
        Assert.True(atlas.TryUpload(result,65536)>0);
        Assert.Contains(atlas.Resources.ProbeRadianceAtlas.ReadPixels(),value=>value>.001f);
    }

    /// <summary>CPU-confirmed unsupported hits and untouched resident directions publish together in one complete tile.</summary>
    [Fact]
    public void MixedFallbackAndResidentDirectionsPublishTogether()
    {
        EnsureContextValid();using var platform=new EngineShaderPlatformScope();
        using var room=new SurfaceLightingEnclosureFixture();room.Seed();
        room.Geometry.Sample=(x,y,z)=>
        {
            var voxel=room.Sample(x,y,z);
            return x==0&&(voxel.Geometry&3)==2?voxel with {Geometry=(voxel.Geometry&~3u)|3u}:voxel;
        };
        room.Geometry.Dirty();room.Geometry.Publish();
        var world=new ControlledVoxelWorld {MapSizeY=256};world.AddRoom((0,32,0),(7,39,7),materialId:room.BlockId);
        using var assets=new BinaryShaderApiFixture();using var atlas=new SurfaceLightingWorldProbeFixture();
        using var backend=new LumOnWorldProbeGpuTraceBackend(assets.Api,256,()=>room.Geometry.Scene,()=>room.Snapshot,
            (_,_)=>true,new BlockAccessorWorldProbeTraceScene(ControlledBlockAccessor.Create(world),false),_=>true);
        backend.BeginFrame(1);Assert.True(backend.TryEnqueue(Item()));
        LumOnWorldProbeTraceResult result=default;
        Assert.True(SpinWait.SpinUntil(()=>backend.TryDequeueResult(out result),TimeSpan.FromSeconds(5)));
        Assert.True(result.Success);Assert.NotNull(result.GpuLease);
        Assert.Contains(result.AtlasSamples,sample=>sample.GpuRayIndex>=0);
        var deferred=result.AtlasSamples.Where(sample=>sample.SurfaceHit.HasValue).ToArray();
        Assert.NotEmpty(deferred);Assert.All(deferred,sample=>Assert.Equal(-1,sample.GpuRayIndex));
        using var queries=new SurfaceLightingQueryBatch(assets.Api);
        queries.Submit(room.Geometry.Scene,room.Snapshot,deferred.Select(sample=>sample.SurfaceHit!.Value).ToArray());
        GpuTestFence.WaitForGpuOrSkip("Mixed fallback lighting queries");Assert.True(queries.TryRead(out var resolved));
        var samples=result.AtlasSamples.ToBuilder();int next=0;
        for(int index=0;index<samples.Count;index++)
        {
            if(!samples[index].SurfaceHit.HasValue)continue;
            var light=resolved[next++].Result;Assert.Equal(1,light.W);
            samples[index]=samples[index] with {SurfaceHit=null,RadianceRgb=new Vector3(light.X,light.Y,light.Z)};
        }
        var pixels=atlas.Upload(result with {AtlasSamples=samples.ToImmutable()});
        Assert.All(pixels.Where((_,index)=>(index&3)==3),value=>Assert.True(value>0));
        Assert.All(pixels.Where((_,index)=>(index&3)!=3),value=>Assert.True(value>.001f));
        Assert.Empty(world.LightQueries);
    }

    /// <summary>CPU-only lighting retries retain the original trace identity after their GPU storage is released.</summary>
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void ReleasedResidentStorageDoesNotReleaseRetryIdentity(int change)
    {
        EnsureContextValid();using var platform=new EngineShaderPlatformScope();
        using var room=new SurfaceLightingEnclosureFixture();
        using var assets=new BinaryShaderApiFixture();using var atlas=new SurfaceLightingWorldProbeFixture();
        bool current=true;
        using var backend=new LumOnWorldProbeGpuTraceBackend(assets.Api,256,()=>room.Geometry.Scene,()=>room.Snapshot,
            (_,_)=>true,new UnexpectedWorldProbeTraceScene(),_=>current);
        Assert.True(backend.TryEnqueue(Item()));Assert.False(backend.TryDequeueResult(out _));
        GpuTestFence.WaitForGpuOrSkip("Unresolved resident admission");
        Assert.True(backend.TryDequeueResult(out var source));Assert.True(source.Success);
        Assert.All(source.AtlasSamples,sample=>Assert.NotNull(sample.SurfaceHit));
        Assert.NotNull(source.GpuIdentity);
        room.Seed();
        using var queries=new SurfaceLightingQueryBatch(assets.Api);
        queries.Submit(room.Geometry.Scene,room.Snapshot,source.AtlasSamples.Select(sample=>sample.SurfaceHit!.Value).ToArray());
        GpuTestFence.WaitForGpuOrSkip("Lighting retry after publication becomes ready");
        Assert.True(queries.TryRead(out var lighting));
        var samples=source.AtlasSamples.ToBuilder();
        for(int index=0;index<samples.Count;index++)
        {
            var light=lighting[index].Result;Assert.Equal(1,light.W);
            samples[index]=samples[index] with {SurfaceHit=null,RadianceRgb=new(light.X,light.Y,light.Z)};
        }
        source.GpuLease!.Dispose();
        Assert.False(source.GpuLease.Answers.IsValid);Assert.True(source.GpuIdentity.IsCurrent);
        var retry=source with {GpuLease=null,AtlasSamples=samples.ToImmutable(),SurfaceRetryCount=1};
        Assert.True(atlas.TryUpload(retry,65536)>0);
        var pixels=atlas.Resources.ProbeRadianceAtlas.ReadPixels();
        var metadata=atlas.Resources.ProbeMeta0.ReadPixels();
        if(change==0)room.Geometry.Dirty();else if(change==1)room.DependencyRevision++;else current=false;
        Assert.False(retry.GpuIdentity!.IsCurrent);
        Assert.Equal(0,atlas.TryUpload(retry,65536));
        Assert.Equal(pixels,atlas.Resources.ProbeRadianceAtlas.ReadPixels());
        Assert.Equal(metadata,atlas.Resources.ProbeMeta0.ReadPixels());
    }
    #endregion

    #region Compact completion ownership
    /// <summary>Completed but unpublished leases hold the resident bound until every range of an allocation retires.</summary>
    [Fact]
    public void ResidentBackpressureRetainsCompletedUndrainedAllocations()
    {
        EnsureContextValid();using var room=new SurfaceLightingEnclosureFixture();room.Seed();
        using var assets=new BinaryShaderApiFixture();int claims=0;
        using var backend=new LumOnWorldProbeGpuTraceBackend(assets.Api,256,()=>room.Geometry.Scene,()=>room.Snapshot,
            (_,_)=>{claims++;return true;},new UnexpectedWorldProbeTraceScene(),_=>true);
        var leases=new List<WorldProbeGpuLease>();
        for(int batch=0;batch<2;batch++)
        {
            for(int probe=0;probe<2;probe++)
            {
                var item=Item() with {WorldProbeOctahedralTileSize=64,WorldProbeAtlasTexelsPerUpdate=4096};
                item=item with {Request=item.Request with {Ticket=1+(batch<<1)+probe}};
                Assert.True(backend.TryEnqueue(item));
            }
            Assert.False(backend.TryDequeueResult(out _));
            GpuTestFence.WaitForGpuOrSkip("Resident storage bound");
            for(int probe=0;probe<2;probe++)
            {
                Assert.True(backend.TryDequeueResult(out var result));Assert.True(result.Success);
                leases.Add(Assert.IsType<WorldProbeGpuLease>(result.GpuLease));
            }
        }
        Assert.Equal(16384,leases.Select(lease=>lease.Answers).Distinct().Sum(answers=>answers.Count));
        Assert.Equal(4,claims);Assert.True(backend.TryEnqueue(Item()));
        Assert.False(backend.TryDequeueResult(out _));Assert.Equal(4,claims);
        leases[0].Dispose();Assert.False(backend.TryDequeueResult(out _));Assert.Equal(4,claims);
        leases[1].Dispose();Assert.False(backend.TryDequeueResult(out _));Assert.Equal(5,claims);
        GpuTestFence.WaitForGpuOrSkip("Resident storage released");
        Assert.True(backend.TryDequeueResult(out var final));Assert.True(final.Success);
        final.GpuLease?.Dispose();foreach(var lease in leases)lease.Dispose();
    }

    /// <summary>Compact completion excludes ready RGB and retains the prior allocation when the batch traces again.</summary>
    [Fact]
    public void CompactCompletionRetainsBrightBufferAcrossDarkBatchReuse()
    {
        EnsureContextValid();
        using var platform=new EngineShaderPlatformScope();
        using var room=new SurfaceLightingEnclosureFixture();room.Seed();
        using var assets=new BinaryShaderApiFixture();
        using var atlas=new SurfaceLightingWorldProbeFixture();
        using var batch=new WorldProbeTraceBatch(assets.Api);
        var item=Item();var selectors=WorldProbeGpuIntegration.CreateDirections(item);
        WorldProbeTraceProbeGpu[] probes=[new(item.ProbePosWorld,16,256,8,0,selectors.Length)];
        batch.Submit(room.Geometry.Scene,room.Snapshot,probes,selectors.AsSpan());
        GpuTestFence.WaitForGpuOrSkip("Compact bright completion");
        Assert.True(batch.TryReadResident(out var bright,out var brightOwner));
        using var brightStorage=Assert.IsType<WorldProbeResidentAnswers>(brightOwner);
        Assert.Equal(4+(selectors.Length<<4),batch.LastReadbackBytes);
        Assert.All(bright,answer=>{Assert.Equal(1,answer.Hit.Result.W);Assert.Equal(0,answer.Hit.Result.X);});
        using var brightLease=new WorldProbeGpuLease(brightStorage,0,selectors.Length,()=>true);
        var brightResult=WorldProbeGpuIntegration.Integrate(item,bright,0,bright.Length,brightLease);
        room.BlockLight=0;room.Geometry.Dirty();room.Geometry.Publish();room.Seed();
        batch.Submit(room.Geometry.Scene,room.Snapshot,probes,selectors.AsSpan());
        GpuTestFence.WaitForGpuOrSkip("Compact dark completion");
        Assert.True(batch.TryReadResident(out var dark,out var darkOwner));
        using var darkStorage=Assert.IsType<WorldProbeResidentAnswers>(darkOwner);
        Assert.NotSame(brightStorage,darkStorage);
        using var darkLease=new WorldProbeGpuLease(darkStorage,0,selectors.Length,()=>true);
        var darkResult=WorldProbeGpuIntegration.Integrate(item,dark,0,dark.Length,darkLease);
        var brightPixels=atlas.Upload(brightResult);
        Assert.Contains(brightPixels.Where((_,index)=>(index&3)!=3),value=>value>.001f);
        var darkPixels=atlas.Upload(darkResult);
        Assert.All(darkPixels.Where((_,index)=>(index&3)!=3),value=>Assert.Equal(0,value));
        Assert.All(darkPixels.Where((_,index)=>(index&3)==3),value=>Assert.True(value>0));
    }

    /// <summary>Missing lighting reads only required hit descriptors and leaves those directions explicitly unresolved.</summary>
    [Fact]
    public void CompactCompletionReadsMissingLightingDescriptorsWithoutReadyPayloads()
    {
        EnsureContextValid();
        using var room=new SurfaceLightingEnclosureFixture();
        using var assets=new BinaryShaderApiFixture();using var batch=new WorldProbeTraceBatch(assets.Api);
        var item=Item();var selectors=WorldProbeGpuIntegration.CreateDirections(item);
        batch.Submit(room.Geometry.Scene,null,[new(item.ProbePosWorld,16,256,8,0,selectors.Length)],selectors.AsSpan());
        GpuTestFence.WaitForGpuOrSkip("Compact unresolved completion");
        Assert.True(batch.TryReadResident(out var answers,out var owner));
        using var storage=Assert.IsType<WorldProbeResidentAnswers>(owner);
        Assert.Equal(4+(selectors.Length<<4)+selectors.Length*80,batch.LastReadbackBytes);
        Assert.All(answers,answer=>{Assert.Equal(WorldProbeTraceOutcome.Hit,answer.TraceOutcome);Assert.Equal(0,answer.Hit.Result.W);Assert.Equal(room.BlockId,answer.Hit.BlockId);});
        using var lease=new WorldProbeGpuLease(storage,0,selectors.Length,()=>true);
        var result=WorldProbeGpuIntegration.Integrate(item,answers,0,answers.Length,lease);
        Assert.All(result.AtlasSamples,sample=>{Assert.NotNull(sample.SurfaceHit);Assert.Equal(-1,sample.GpuRayIndex);});
        Assert.Equal(16,System.Runtime.InteropServices.Marshal.SizeOf<WorldProbeCompletionGpu>());
    }
    #endregion

    #region Work construction
    /// <summary>Uses every octahedral direction of a single atlas tile inside the authored enclosure.</summary>
    private static LumOnWorldProbeTraceWorkItem Item()
        => new(1,new(0,new(),new(),0,Ticket:11),new(3.5,35.5,3.5),16,8,64,false,.25f,-1,1e-6f,0,true);
    #endregion
}
