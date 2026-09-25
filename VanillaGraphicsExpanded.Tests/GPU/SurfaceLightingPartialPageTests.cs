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
        Assert.Matches(@"captureFail:[1-9]\d*/",diagnostics);
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
        var pixels=ReadFirstPage(runtime,snapshot.OutgoingRadiance,snapshot);
        Assert.Equal((density*density)<<3,pixels.Where((_,i)=>(i&3)==3).Count(value=>value==1));
        Assert.Equal((density*density)<<3,pixels.Where((_,i)=>(i&3)==3).Count(value=>value==0));
        for(int index=0;index<pixels.Length;index+=4)
        {
            Assert.All(pixels[index..(index+4)],value=>Assert.True(float.IsFinite(value)));
            if(pixels[index+3]==0) Assert.Equal(new float[4],pixels[index..(index+4)]);
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
        runtime.TransformVoxel=(x,y,z,voxel)=>x==1 && y>=34 ? voxel with { Geometry=3 } : voxel;
        runtime.ChangeBlockLight(0);
        runtime.RunUntil(()=>runtime.TryGetLighting(out var after) && after.DependencyRevision!=before.DependencyRevision);
        for(int frame=0;frame<8;frame++) runtime.Frame();
        Assert.True(runtime.TryGetLighting(out var snapshot));
        var pixels=ReadFirstPage(runtime,snapshot.OutgoingRadiance,snapshot);
        Assert.Equal(8,pixels.Where((_,i)=>(i&3)==3).Count(value=>value==1));
        Assert.Equal(8,pixels.Where((_,i)=>(i&3)==3).Count(value=>value==0));
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
        runtime.Config.LumOn.LumonScene.RelightMaxPagesPerFrame=4;
        runtime.Config.LumOn.LumonScene.RelightMaxDdaSteps=3;
        runtime.PrimeGeometry();runtime.RunUntil(runtime.AllRequestedLightingReady);
        for(int frame=0;frame<48;frame++) runtime.Frame();
        Assert.True(runtime.TryGetLighting(out var snapshot));
        Assert.True(runtime.Feedback.TryGetNearDispatchState(out _,out _,out var mapping,out _));
        bool publishedBounce=false;
        foreach(uint physical in mapping.Keys)
        {
            var outgoing=ReadPage(snapshot.OutgoingRadiance,snapshot,physical);
            var direct=ReadPage(snapshot.DirectIrradiance,snapshot,physical);
            var indirect=ReadPage(snapshot.IndirectIrradiance,snapshot,physical);
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
    /// <summary>Reads one real resident tile without copying the full physical atlas.</summary>
    private static float[] ReadFirstPage(SurfaceCacheRuntimeFixture runtime,Texture3D texture,in SurfaceLightingSnapshot snapshot)
    {
        Assert.True(runtime.Feedback.TryGetNearDispatchState(out _,out _,out var mapping,out _));
        return ReadPage(texture,snapshot,mapping.Keys.Min());
    }

    /// <summary>Reads a selected physical page through its actual array layer and tile coordinates.</summary>
    private static float[] ReadPage(Texture3D texture,in SurfaceLightingSnapshot snapshot,uint page)
    {
        int physical=checked((int)page)-1;
        int local=physical%snapshot.TilesPerAtlas;
        int x=(local%snapshot.TilesPerAxis)*snapshot.TileSize;
        int y=(local/snapshot.TilesPerAxis)*snapshot.TileSize;
        using var framebuffer=GpuFramebuffer.CreateEmpty("Tests.PartialSurfacePage.Readback");
        framebuffer.Bind();
        GL.FramebufferTextureLayer(FramebufferTarget.Framebuffer,FramebufferAttachment.ColorAttachment0,texture.TextureId,0,physical/snapshot.TilesPerAtlas);
        GL.ReadBuffer(ReadBufferMode.ColorAttachment0);
        var pixels=new float[(snapshot.TileSize*snapshot.TileSize)<<2];
        GL.ReadPixels(x,y,snapshot.TileSize,snapshot.TileSize,PixelFormat.Rgba,PixelType.Float,pixels);
        GpuFramebuffer.Unbind();
        Assert.Equal(ErrorCode.NoError,GL.GetError());
        return pixels;
    }
    #endregion
}
