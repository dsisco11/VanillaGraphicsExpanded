using System.Numerics;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.LumOn.Scene;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Exercises the production cache lookup and asynchronous transport against real captured and lit surfaces.</summary>
[Collection("NearFieldMaterialCapture")]
[Trait("Category","GPU")]
public sealed class SurfaceLightingConsumerTests : NearFieldShaderTestBase
{
    /// <summary>Uses the isolated material registry and headless GPU context.</summary>
    public SurfaceLightingConsumerTests(HeadlessGLFixture fixture):base(fixture) { }

    #region Hit lookup
    /// <summary>All six faces resolve across positive and negative chunk boundaries without another albedo factor.</summary>
    [Theory]
    [InlineData(0)] [InlineData(28)] [InlineData(-4)] [InlineData(-32)]
    public void FacesAndChunkBoundariesConsumeOutgoingRadiance(int offset)
    {
        EnsureShaderTestAvailable();
        using var room=new SurfaceLightingEnclosureFixture(.25f,xOffset:offset); room.Seed();
        var hits=new SurfaceLightingQuery[]
        {
            new(new(offset,35,3),new(1,0,0),new(1,.5f,.5f)),
            new(new(offset+7,35,3),new(-1,0,0),new(0,.5f,.5f)),
            new(new(offset+3,32,3),new(0,1,0),new(.5f,1,.5f)),
            new(new(offset+4,39,3),new(0,-1,0),new(0,0,.5f)),
            new(new(offset+3,35,0),new(0,0,1),new(.5f,.5f,1)),
            new(new(offset+4,35,7),new(0,0,-1),new(0,.5f,0))
        };
        foreach(var hit in Query(room,hits))
        {
            Assert.Equal(1,hit.Result.W);
            Assert.InRange(hit.Result.X,32*(64f/255)/MathF.PI-.01f,32*(64f/255)/MathF.PI+.01f);
        }
        Assert.Equal(ErrorCode.NoError,GL.GetError());
    }

    /// <summary>Black is ready; absent publication, stale slots and replaced block identities are not.</summary>
    [Fact]
    public void ValidBlackIsDistinctFromUnavailableOrStale()
    {
        EnsureShaderTestAvailable();
        using var room=new SurfaceLightingEnclosureFixture(0); room.Seed();
        SurfaceLightingQuery hit=new(new(0,35,3),new(1,0,0),new(1,.5f,.5f));
        Assert.Equal(new Vector4(0,0,0,1),Query(room,[hit])[0].Result);
        var wrong=hit; wrong.BlockId=int.MaxValue;
        Assert.Equal(Vector4.Zero,Query(room,[wrong])[0].Result);
        room.StaleSlots();
        Assert.Equal(Vector4.Zero,Query(room,[hit])[0].Result);
    }

    /// <summary>Unready pages cannot be mistaken for valid zero radiance or returned from old atlas contents.</summary>
    [Fact]
    public void WithheldPagesRejectPreviouslyBrightLighting()
    {
        EnsureShaderTestAvailable();
        using var room=new SurfaceLightingEnclosureFixture(); room.Seed();
        SurfaceLightingQuery hit=new(new(0,35,3),new(1,0,0),new(1,.5f,.5f));
        Assert.True(Query(room,[hit])[0].Result.X>1);
        room.WithholdPages();
        Assert.Equal(Vector4.Zero,Query(room,[hit])[0].Result);
    }
    #endregion

    #region Mapping and transport lifetime
    /// <summary>A ready physical tile belonging to another patch must not satisfy the requested surface.</summary>
    [Fact]
    public void AliasedPageMappingIsUnavailable()
    {
        EnsureShaderTestAvailable();
        using var room=new SurfaceLightingEnclosureFixture();room.Seed();
        SurfaceLightingQuery hit=new(new(0,35,3),new(1,0,0),new(1,.5f,.5f));
        Assert.Equal(1,Query(room,[hit])[0].Result.W);
        room.AliasFirstPage();
        Assert.Equal(Vector4.Zero,Query(room,[hit])[0].Result);
    }

    /// <summary>Reusing an oversized allocation binds only the current batch and refuses overwrite while pending.</summary>
    [Fact]
    public void QueryBatchReuseRetainsExactAdmissionBounds()
    {
        EnsureShaderTestAvailable();
        using var room=new SurfaceLightingEnclosureFixture();room.Seed();
        using var assets=new BinaryShaderApiFixture();
        using var batch=new SurfaceLightingQueryBatch(assets.Api);
        SurfaceLightingQuery hit=new(new(0,35,3),new(1,0,0),new(1,.5f,.5f));
        batch.Submit(room.Geometry.Scene,room.Snapshot,new[]{hit,hit});
        Assert.Throws<InvalidOperationException>(()=>batch.Submit(room.Geometry.Scene,room.Snapshot,new[]{hit}));
        GpuTestFence.WaitForGpuOrSkip("First hit batch");
        Assert.True(batch.TryRead(out var first));Assert.Equal(2,first.Length);
        batch.Submit(room.Geometry.Scene,room.Snapshot,new[]{hit});
        GpuTestFence.WaitForGpuOrSkip("Reused hit batch");
        Assert.True(batch.TryRead(out var second));Assert.Single(second);Assert.Equal(1,second[0].Result.W);
        Assert.False(batch.TryRead(out _));
        Assert.Equal(ErrorCode.NoError,GL.GetError());
    }
    #endregion

    #region Screen probe consumption
    /// <summary>Real offscreen traces read produced outgoing radiance, while missing pages retain opaque distances at zero confidence.</summary>
    [Theory]
    [InlineData(0f,32)] [InlineData(.25f,32)] [InlineData(.25f,192)] [InlineData(.25f,256)]
    public void ScreenProbesConsumePublishedCache(float albedo,int surfaceResolution)
    {
        EnsureShaderTestAvailable();
        using var room=new SurfaceLightingEnclosureFixture(albedo,surfaceResolution:surfaceResolution); room.Seed();
        using var placeholder=new NearFieldVoxelFixture();
        var lit=Trace(placeholder,shared:room.Geometry.Scene,surfaceLighting:room.Snapshot,anchorPosition:new(4,36,4));
        int hits=0;
        for(int i=0;i<lit.Meta.Length;i+=2)
        {
            if((Flags(lit.Meta[i+1])&1u)==0)continue;
            hits++;
            Assert.Equal(1,lit.Meta[i]);
            Assert.InRange(lit.Radiance[i*2],32*MathF.Round(albedo*255)/255/MathF.PI-.02f,32*MathF.Round(albedo*255)/255/MathF.PI+.02f);
        }
        Assert.True(hits>32);
        room.WithholdPages();
        var missing=Trace(placeholder,shared:room.Geometry.Scene,surfaceLighting:room.Snapshot,anchorPosition:new(4,36,4));
        for(int i=0;i<lit.Meta.Length;i+=2)
        {
            Assert.Equal(lit.Radiance[i*2+3],missing.Radiance[i*2+3]);
            Assert.Equal(Flags(lit.Meta[i+1]) & 63u,Flags(missing.Meta[i+1]) & 63u);
            Assert.Equal(4u,(Flags(missing.Meta[i+1]) >> 16) & 7u);
            Assert.Equal(0,missing.Meta[i]);
            Assert.Equal(0,missing.Radiance[i*2]);
        }
    }
    #endregion

    #region Screen hit continuity
    /// <summary>Accepted screen hits and offscreen hits consume identical cached emission without reapplying boost or albedo.</summary>
    [Fact]
    public void ScreenHitsPreserveCachedEmissionExactlyOnce()
    {
        EnsureShaderTestAvailable();
        using var room=new SurfaceLightingEnclosureFixture(0,32,12){EmissionPolicy=true};room.Seed();
        using var placeholder=new NearFieldVoxelFixture();
        var projection=LumOnTestInputFactory.CreateRealisticProjection();
        float z=-8, depth=(projection[10]*z+projection[14])/(projection[11]*z+projection[15])*.5f+.5f;
        var control=Trace(placeholder,screenDepth:depth,nearFieldTracing:false);
        var actual=Trace(placeholder,screenDepth:depth,screenEmission:8,emissionBoost:3,
            shared:room.Geometry.Scene,surfaceLighting:room.Snapshot,worldOffset:new(0,32,0),matrixRemainder:new(4,4,9));
        int hits=0;
        for(int i=0;i<control.Meta.Length;i+=2)
        {
            if((Flags(control.Meta[i+1])&1u)==0)continue;
            hits++;
            Assert.Equal(1,actual.Meta[i]);
            Assert.Equal(1u,Flags(actual.Meta[i+1])&1u);
            Assert.InRange(actual.Radiance[i*2],11.99f,12.01f);
        }
        Assert.True(hits>0);
    }
    #endregion

    #region CPU world-probe transport
    /// <summary>Worker-produced hits resolve through the real GPU cache while preserving signed distances and occlusion.</summary>
    [Fact]
    public void CpuWorldProbeHitsResolveThroughPublishedCache()
    {
        EnsureShaderTestAvailable();
        using var room=new SurfaceLightingEnclosureFixture();room.Seed();
        var world=new VanillaGraphicsExpanded.Tests.Fixtures.WorldProbes.ControlledVoxelWorld();
        world.AddRoom((0,32,0),(7,39,7),materialId:room.BlockId);
        var request=new VanillaGraphicsExpanded.LumOn.WorldProbes.LumOnWorldProbeUpdateRequest(0,new(),new(),0);
        var work=new VanillaGraphicsExpanded.LumOn.WorldProbes.Tracing.LumOnWorldProbeTraceWorkItem(
            0,request,new(4,36,4),64,8,64,false,.25f,-1,1e-6f,DeferSurfaceLighting:true);
        var traced=new VanillaGraphicsExpanded.LumOn.WorldProbes.Tracing.LumOnWorldProbeTraceIntegrator()
            .TraceProbe(world.CreateTraceScene(),work,CancellationToken.None);
        Assert.True(traced.Success);
        var descriptors=traced.AtlasSamples.Select(s=>s.SurfaceHit!.Value).ToArray();
        var answers=Query(room,descriptors);
        int index=0;
        var resolved=VanillaGraphicsExpanded.LumOn.WorldProbes.Tracing.WorldProbeSurfaceLighting.Resolve(traced,answers,ref index);
        Assert.True(resolved.Success);Assert.Equal(64,index);
        for(int i=0;i<64;i++)
        {
            Assert.InRange(resolved.AtlasSamples[i].RadianceRgb.X,2.54f,2.58f);
            Assert.Equal(traced.AtlasSamples[i].AlphaEncodedDistSigned,resolved.AtlasSamples[i].AlphaEncodedDistSigned);
        }
        room.WithholdPages();answers=Query(room,descriptors);index=0;
        Assert.False(VanillaGraphicsExpanded.LumOn.WorldProbes.Tracing.WorldProbeSurfaceLighting.Resolve(traced,answers,ref index).Success);
    }
    #endregion

    #region Query execution
    /// <summary>Waits only in the test harness; production polls the same query owner on subsequent frames.</summary>
    private static SurfaceLightingQuery[] Query(SurfaceLightingEnclosureFixture room, SurfaceLightingQuery[] hits)
    {
        using var assets=new BinaryShaderApiFixture();
        using var batch=new SurfaceLightingQueryBatch(assets.Api);
        batch.Submit(room.Geometry.Scene,room.Snapshot,hits);
        GpuTestFence.WaitForGpuOrSkip("Surface hit queries");
        Assert.True(batch.TryRead(out var answers));
        return answers;
    }
    #endregion
}



