using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.LumOn.Scene;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Exercises partial cache publication using production capture, seed, scheduling and atlas ownership.</summary>
[Collection("NearFieldMaterialCapture")]
[Trait("Category","GPU")]
public sealed class SurfaceLightingPartialPageTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Partial seed publication
    /// <summary>Partial lighting support never relaxes the requirement for a coherent complete material capture.</summary>
    [Fact]
    public void UnresolvedCapturedSourceKeepsEntirePageUnavailable()
    {
        EnsureContextValid();
        using var runtime=new SurfaceCacheRuntimeFixture(exposedWall:true);
        runtime.TransformVoxel=(x,y,z,voxel)=>x==0 && y>=34 ? voxel with { Geometry=0 } : voxel;
        runtime.PrimeGeometry();
        for(int frame=0;frame<16;frame++) runtime.Frame();
        Assert.False(runtime.AllRequestedCaptured());
        Assert.False(runtime.TryGetLighting(out _));
        runtime.Feedback.TryGetSelfCheckLine(out string diagnostics);
        Assert.Contains("captureFail:0/0",diagnostics);
        Assert.Matches(@"captureDeferredChecks:[1-9]\d*",diagnostics);
    }

    /// <summary>Unsupported neighboring geometry cannot suppress valid texels on the same fully captured page.</summary>
    [Theory]
    [InlineData(1)] [InlineData(4)]
    public void ReadySeedTexelsPublishWhileUnresolvedNeighborsRetry(int density)
    {
        EnsureContextValid();
        using var runtime=new SurfaceCacheRuntimeFixture(exposedWall:true);
        runtime.Config.LumOn.LumonScene.NearTexelsPerVoxelFaceEdge=density;
        runtime.Config.LumOn.LumonScene.RelightTexelsPerPagePerFrame=density*density<<2;
        runtime.TransformVoxel=(x,y,z,voxel)=>x==1 && y>=34 ? voxel with { Geometry=3 } : voxel;
        runtime.PrimeGeometry();
        runtime.RunUntil(()=>runtime.TryGetLighting(out _));
        Assert.True(runtime.AllRequestedCaptured());
        Assert.DoesNotContain(runtime.Events.Registrations,r=>r.Name=="vge_worldprobe_update");
        for(int frame=0;frame<16;frame++) runtime.Frame();
        Assert.True(runtime.TryGetLighting(out var snapshot));
        using var pixelsOwner=ReadFirstPage(runtime,snapshot.OutgoingRadiance,snapshot);
        var pixels=pixelsOwner.Span;
        Assert.Equal((density*density)<<3,CountValidity(pixels,1));
        Assert.Equal((density*density)<<3,CountValidity(pixels,0));
        for(int index=0;index<pixels.Length;index+=4)
        {
            foreach(float value in pixels.Slice(index,4)) Assert.True(float.IsFinite(value));
            if(pixels[index+3]==0) { foreach(float value in pixels.Slice(index,4)) Assert.Equal(0,value); }
            else Assert.True(pixels[index]>.001f);
        }
        using var queries=new SurfaceLightingQueryBatch(runtime.Api);
        queries.Submit(runtime.Geometry.Resources!,snapshot,
            [new(new(0,33,1),new(1,0,0),new(1,.5f,.5f),runtime.BlockId),
             new(new(0,35,1),new(1,0,0),new(1,.5f,.5f),runtime.BlockId)]);
        GpuTestFence.WaitForGpuOrSkip("Partial surface page hit queries");
        Assert.True(queries.TryRead(out var answers));
        Assert.Equal(1,answers[0].Result.W);Assert.True(answers[0].Result.X>.001f);
        Assert.Equal(0,answers[1].Result.W);
    }

    /// <summary>A real source invalidation resets prior lighting before publishing a different partial valid region.</summary>
    [Fact]
    public void SourceChangeCannotExposePriorValidTexelsInNewPartialPage()
    {
        EnsureContextValid();
        using var runtime=new SurfaceCacheRuntimeFixture(exposedWall:true);
        runtime.PrimeGeometry();runtime.RunUntil(()=>runtime.TryGetLighting(out _));
        Assert.True(runtime.TryGetLighting(out var before));
        Assert.True(runtime.Feedback.TryGetNearDispatchState(out _,out _,out var pages,out _));
        uint page=pages.Single().Key;
        long capture=runtime.Feedback.GetCaptureRevision(page);
        runtime.TransformVoxel=(x,y,z,voxel)=>x==0 && y>=34 ? voxel with { Geometry=1 } : x==1 && y<34 ? voxel with { Geometry=3 } : voxel;
        runtime.ChangeBlockLight(0);
        for(int frame=0;frame<24;frame++) runtime.Frame();
        Assert.True(runtime.TryGetLighting(out var snapshot));
        Assert.True(runtime.Feedback.GetCaptureRevision(page)>capture);
        Assert.Equal(before.DependencyRevision,snapshot.DependencyRevision);
        using var pixelsOwner=ReadFirstPage(runtime,snapshot.OutgoingRadiance,snapshot);
        var pixels=pixelsOwner.Span;
        Assert.Equal(8,CountValidity(pixels,1));
        Assert.Equal(8,CountValidity(pixels,0));
        for(int index=0;index<pixels.Length;index++) if((index&3)!=3) Assert.Equal(0,pixels[index]);
    }
    #endregion

    #region Partial indirect publication
    /// <summary>Short ray budgets leave some directions unresolved while successful nearby bounces still reach published outgoing light.</summary>
    [Fact]
    public void MixedIndirectTraceOutcomesPublishSuccessfulTexels()
    {
        EnsureContextValid();
        using var runtime=new SurfaceCacheRuntimeFixture(requestedPages:24,enclosure:true);
        runtime.Config.LumOn.LumonScene.RelightSeedPagesPerFrame = runtime.Config.LumOn.LumonScene.RelightDirectPagesPerFrame = runtime.Config.LumOn.LumonScene.RelightIndirectPagesPerFrame = 4;
        runtime.Config.LumOn.LumonScene.RelightMaxDdaSteps=3;
        runtime.PrimeGeometry();runtime.RunUntil(runtime.AllRequestedLightingReady);
        for(int frame=0;frame<48;frame++) runtime.Frame();
        Assert.True(runtime.TryGetLighting(out var snapshot));
        Assert.True(runtime.Feedback.TryGetNearDispatchState(out _,out _,out var mapping,out _));
        bool publishedBounce=false;
        foreach(uint physical in mapping.Keys)
        {
            using var outgoingOwner=SurfaceLightingPageReadback.Read(snapshot.OutgoingRadiance,snapshot,physical);
            var outgoing=outgoingOwner.Span;
            using var directOwner=SurfaceLightingPageReadback.Read(snapshot.DirectIrradiance,snapshot,physical);
            var direct=directOwner.Span;
            using var indirectOwner=SurfaceLightingPageReadback.Read(snapshot.IndirectIrradiance,snapshot,physical);
            var indirect=indirectOwner.Span;
            for(int index=0;index<outgoing.Length;index+=4)
            {
                Assert.Equal(1,outgoing[index+3]);
                Assert.True(outgoing[index]>=direct[index]/MathF.PI-.01f,"An unresolved bounce erased initialized direct lighting.");
                if(indirect[index]>.001f && outgoing[index]>direct[index]/MathF.PI+.001f) publishedBounce=true;
            }
        }
        ((LumonSceneRelightUpdateRenderer)runtime.LightingProvider).TryGetSelfCheckLine(out string diagnostics);
        Assert.Matches(@"indirectFail:[1-9]\d*/",diagnostics);
        Assert.True(publishedBounce,"Successful texels remained trapped in unpublished indirect scratch history: "+diagnostics);
    }
    #endregion

    #region Atlas observations
    /// <summary>Counts initialized texels directly from the borrowed readback span.</summary>
    private static int CountValidity(ReadOnlySpan<float> pixels,float value)
    {
        int count=0;
        for(int i=3;i<pixels.Length;i+=4) if(pixels[i]==value) count++;
        return count;
    }
    /// <summary>Reads one real resident tile without copying the full physical atlas.</summary>
    private static VanillaGraphicsExpanded.Collections.PooledArray<float> ReadFirstPage(SurfaceCacheRuntimeFixture runtime,Texture3D texture,in SurfaceLightingSnapshot snapshot)
    {
        Assert.True(runtime.Feedback.TryGetNearDispatchState(out _,out _,out var mapping,out _));
        return SurfaceLightingPageReadback.Read(texture,snapshot,mapping.Keys.Min());
    }

    #endregion
}
