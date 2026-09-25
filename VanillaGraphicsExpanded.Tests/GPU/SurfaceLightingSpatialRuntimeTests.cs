using System.Numerics;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Exercises spatial correspondence and resident lighting under real camera and streaming transitions.</summary>
[Collection("NearFieldMaterialCapture")]
[Trait("Category","GPU")]
public sealed class SurfaceLightingSpatialRuntimeTests : RenderTestBase
{
    /// <summary>Uses the material-isolated graphics context.</summary>
    public SurfaceLightingSpatialRuntimeTests(HeadlessGLFixture fixture) : base(fixture) { }

    #region Camera movement
    /// <summary>Rotation, translation and view bob retain alignment with fixed voxel faces and recover the reference lighting.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void MovingCameraKeepsAnchorsOnWorldSurfaces(bool sh9)
    {
        EnsureContextValid();
        var scene=new SpatialLightingScene();
        using var runtime=new SurfaceLightingConsumerRuntimeFixture(sh9,scene);
        runtime.Cache.Config.LumOn.TemporalAlpha=.9f;
        runtime.Cache.Config.LumOn.EnableReprojectionVelocity=true;
        runtime.Cache.Config.LumOn.ProbeAtlasTexelsPerFrame=8;
        Settle(runtime,true);
        foreach(var pose in new[]{(-.25f,.12f,.12f),(.25f,-.08f,-.15f),(0f,0f,MathF.PI/2),(0f,0f,0f)})
        {
            scene.Position=new(pose.Item1,36,5); scene.Bob=pose.Item2; scene.Yaw=pose.Item3;
            runtime.Frame();
            AssertAnchors(runtime,scene);
            Settle(runtime,true);
            AssertPixels(runtime.FinalPixels(),true);
        }
        Assert.Equal(ErrorCode.NoError,GL.GetError());
    }
    #endregion

    #region Streaming and neighboring rooms
    /// <summary>Entity-relative camera motion wraps probe storage while unchanged world coverage preserves overlapping lighting.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ProbeRingPreservesOverlapWithinStableGeometryCoverage(bool sh9)
    {
        EnsureContextValid();
        var scene=new SpatialLightingScene();
        using var runtime=new SurfaceLightingConsumerRuntimeFixture(sh9,scene);
        Settle(runtime,true);
        runtime.RunUntil(()=>runtime.WorldBuffers.Resources!.ProbeMeta0.ReadPixels().Where((_,i)=>i%2==0).All(c=>c>=.25f));
        // Stop further producer work through its budget, preserving a coherent published generation.
        runtime.RunUntil(runtime.Cache.AllRequestedLightingReady);
        runtime.Cache.Config.LumOn.LumonScene.RelightSeedPagesPerFrame = runtime.Cache.Config.LumOn.LumonScene.RelightDirectPagesPerFrame = runtime.Cache.Config.LumOn.LumonScene.RelightIndirectPagesPerFrame = 0;
        runtime.Frame(); runtime.Frame();
        // Isolate ring retention from legitimate asynchronous refreshes of surviving tiles.
        runtime.Cache.Config.WorldProbeClipmap.UploadBudgetBytesPerFrame=1;
        var before=runtime.WorldPixels(); var reference=runtime.FinalPixels();
        Assert.True(runtime.Cache.TryGetLighting(out var initial));
        var shifts=new List<VanillaGraphicsExpanded.LumOn.WorldProbes.LumOnWorldProbeScheduler.WorldProbeAnchorShiftEvent>();
        runtime.WorldBuffers.AnchorShifted+=shifts.Add;
        // Keep the physical eye fixed while the entity-relative render origin changes.
        scene.Position=new(2.25f,36,5); scene.EyeOffsetX=-2.25f;
        runtime.Frame();
        Assert.True(runtime.Cache.TryGetLighting(out var current));
        Assert.Equal(initial.DependencyRevision,current.DependencyRevision);
        var shift=Assert.Single(shifts);
        Assert.Equal(1,shift.DeltaProbes.X);
        Assert.Equal(0,shift.DeltaProbes.Y); Assert.Equal(0,shift.DeltaProbes.Z);
        Assert.NotEqual(shift.PrevRingOffset.X,shift.NewRingOffset.X);
        AssertAnchors(runtime,scene); AssertPixels(runtime.FinalPixels(),true);
        var after=runtime.WorldPixels();
        // The old local x=1 plane becomes local x=0; its physical tiles must survive exactly.
        for(int y=0;y<2;y++) for(int z=0;z<2;z++)
        {
            int sx=(1+shift.PrevRingOffset.X)%2,sy=(y+shift.PrevRingOffset.Y)%2,sz=(z+shift.PrevRingOffset.Z)%2;
            Assert.Equal(sx,shift.NewRingOffset.X%2);
            bool populated=false;
            for(int oy=0;oy<8;oy++) for(int ox=0;ox<8;ox++) for(int c=0;c<4;c++)
            {
                int index=((sy*8+oy)*32+(sx+sz*2)*8+ox)*4+c;
                Assert.Equal(before[index],after[index]);
                populated|=c!=3 && before[index]>.001f;
            }
            Assert.True(populated,"Each surviving world tile must contain actual produced lighting.");
        }
        scene.Position=new(0,36,5); scene.EyeOffsetX=0;
        runtime.Frame(); runtime.Frame();
        AssertAnchors(runtime,scene);
        foreach(var pair in reference.Zip(runtime.FinalPixels()).Where((_,i)=>i%4!=3))
            Assert.InRange(Math.Abs(pair.First-pair.Second),0,.03f);
    }
    /// <summary>Signed chunk crossings preserve allocations; reused unavailable slots cannot expose old light before reload.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void SignedStreamingRejectsReusedUnavailableSlots(bool sh9)
    {
        EnsureContextValid();
        var scene=new SpatialLightingScene();
        using var runtime=new SurfaceLightingConsumerRuntimeFixture(sh9,scene);
        Settle(runtime,true);
        var geometry=runtime.Cache.Geometry.Resources;
        var probes=runtime.WorldBuffers.Resources;
        foreach(float x in new[]{-.25f,.25f,31.75f,32.25f,-32.25f,-31.75f})
        {
            scene.Position=new(x,36,5); runtime.Frame(); Settle(runtime,true);
            Assert.Same(geometry,runtime.Cache.Geometry.Resources);
            Assert.Same(probes,runtime.WorldBuffers.Resources);
            AssertAnchors(runtime,scene);
        }
        Assert.True(runtime.Cache.Feedback.TryGetNearChunkSlotAndGeneration(new(-1,1,0),out uint oldSlot,out ushort oldGeneration));
        AssertCellReady(runtime,-32,36,5,true);
        // A 480-block jump wraps both the 48-voxel geometry ring and the five-chunk cache ring.
        var left=VanillaGraphicsExpanded.Voxels.ChunkProcessing.ChunkKey.FromChunkCoords(13,1,0);
        var right=VanillaGraphicsExpanded.Voxels.ChunkProcessing.ChunkKey.FromChunkCoords(14,1,0);
        scene.Unloaded.TryAdd(left,0); scene.Unloaded.TryAdd(right,0); scene.Position=new(448.25f,36,5);
        for(int frame=0;frame<8;frame++)
        {
            runtime.Frame();
            AssertPixels(runtime.FinalPixels(),false);
            Assert.InRange(SurfaceLightingConsumerRuntimeFixture.Energy(runtime.WorldPixels()),0,.0001f);
        }
        Assert.Same(geometry,runtime.Cache.Geometry.Resources);
        AssertCellReady(runtime,448,36,5,false);
        Assert.True(runtime.Cache.Feedback.TryGetNearChunkSlotAndGeneration(new(14,1,0),out uint newSlot,out ushort newGeneration));
        Assert.Equal(oldSlot,newSlot); Assert.NotEqual(oldGeneration,newGeneration);
        scene.Unloaded.Clear();
        Settle(runtime,true); AssertAnchors(runtime,scene);
        AssertCellReady(runtime,448,36,5,true);
        Assert.Same(probes,runtime.WorldBuffers.Resources);
    }

    /// <summary>Bright adjacent rooms must never illuminate any final pixel of a sealed dark room during movement or coverage reuse.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void SealedDarkNeighborsRemainDarkWhileMoving(bool sh9)
    {
        EnsureContextValid();
        var scene=new SpatialLightingScene { AlternateDarkRooms=true };
        using var runtime=new SurfaceLightingConsumerRuntimeFixture(sh9,scene);
        Settle(runtime,true);
        foreach(float x in new[]{8f,8.25f,40f,-24f,0f})
        {
            bool lit=x==0;
            scene.Position=new(x,36,5); scene.Bob=.1f; scene.Yaw=.15f;
            runtime.Frame();
            if(!lit) AssertPixels(runtime.FinalPixels(),false);
            Settle(runtime,lit); AssertAnchors(runtime,scene);
            for(int i=0;i<8;i++) { runtime.Frame(); AssertPixels(runtime.FinalPixels(),lit); }
            if(x==8f)
            {
                runtime.Cache.GeometryAvailable=false;
                for(int i=0;i<4;i++) { runtime.Frame(); AssertPixels(runtime.FinalPixels(),false); }
                runtime.Cache.GeometryAvailable=true; Settle(runtime,false);
            }
        }
    }
    #endregion
    #region Mixed screen and world consumers
    /// <summary>Real screen availability and surface orientation select world lighting without adding it twice to supported screen results.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void MixedConsumersFollowReplacementPolicy(bool sh9)
    {
        EnsureContextValid();
        var scene=new SpatialLightingScene();
        using var runtime=new SurfaceLightingConsumerRuntimeFixture(sh9,scene);
        runtime.Cache.Config.LumOn.DebugMode=VanillaGraphicsExpanded.LumOn.LumOnDebugMode.WorldProbeLightingEffect;
        runtime.Cache.Config.WorldProbeClipmap.ClipmapResolution=4;
        Settle(runtime,true);
        runtime.RunUntil(()=>SurfaceLightingConsumerRuntimeFixture.Energy(runtime.WorldPixels())>.001f && runtime.Screen.WorldProbeSuppressedLighting!=null);
        var baseline=runtime.FinalPixels();
        var suppressed=runtime.Screen.WorldProbeSuppressedLighting!.ReadPixels();
        Assert.True(baseline.Zip(suppressed).Where((_,i)=>i%4!=3).All(p=>Math.Abs(p.First-p.Second)<.02f),"Resolved screen hits must not receive an additional gather world term.");
        runtime.RunUntil(runtime.Cache.AllRequestedLightingReady);
        runtime.Cache.Config.LumOn.LumonScene.RelightSeedPagesPerFrame = runtime.Cache.Config.LumOn.LumonScene.RelightDirectPagesPerFrame = runtime.Cache.Config.LumOn.LumonScene.RelightIndirectPagesPerFrame = 0;
        runtime.Cache.Config.LumOn.ProbeSpacingPx=4;
        bool worldSelected=false, gatherSelected=false, tracedWorldSelected=false;
        var observations=new List<string>();
        foreach(float yaw in new[]{0f,.25f,.5f,.75f,1f,1.25f})
        {
            scene.Yaw=yaw; runtime.Frame();
            for(int i=0;i<24;i++) runtime.Frame();
            var final=runtime.FinalPixels();
            AssertPixels(final,true); var without=runtime.Screen.WorldProbeSuppressedLighting!.ReadPixels();
            float difference=final.Zip(without).Where((_,i)=>i%4!=3).Max(p=>Math.Abs(p.First-p.Second));
            var meta=runtime.Screen.ScreenProbeAtlasMetaTraceTex!.ReadPixels();
            int world=meta.Where((_,i)=>i%2==1).Count(value=>(BitConverter.SingleToUInt32Bits(value)&32)!=0);
            observations.Add($"yaw={yaw},delta={difference},worldRays={world},minRGB={final.Where((_,i)=>i%4!=3).Min()}");
            worldSelected|=difference>.001f;
            gatherSelected|=world==0 && difference>.001f;
            tracedWorldSelected|=world>0 && difference>.001f;
        }
        TestContext.Current.TestOutputHelper?.WriteLine(string.Join(";",observations));
        Assert.True(worldSelected,string.Join(";",observations));
        Assert.True(gatherSelected,"No isolated gather replacement: "+string.Join(";",observations));
        Assert.True(tracedWorldSelected,"No directional world contribution in screen tracing: "+string.Join(";",observations));
        runtime.Cache.Config.LumOn.LumonScene.RelightSeedPagesPerFrame = runtime.Cache.Config.LumOn.LumonScene.RelightDirectPagesPerFrame = runtime.Cache.Config.LumOn.LumonScene.RelightIndirectPagesPerFrame = 4;
        runtime.Cache.Config.LumOn.ProbeSpacingPx=1; scene.Yaw=0;
        runtime.Cache.GeometryAvailable=false;
        for(int i=0;i<4;i++) runtime.Frame();
        AssertPixels(runtime.FinalPixels(),false);
        Assert.All(runtime.Screen.ScreenProbeAtlasMetaTraceTex!.ReadPixels().Where((_,i)=>i%2==0),c=>Assert.Equal(0,c));
        runtime.Cache.GeometryAvailable=true; Settle(runtime,true);
    }
    #endregion
    #region Observations
    /// <summary>Waits for actual lighting and checks every final pixel, never just a maximum-energy sample.</summary>
    private static void Settle(SurfaceLightingConsumerRuntimeFixture runtime,bool lit)
    {
        runtime.RunUntil(()=>runtime.Cache.TryGetLighting(out _) && runtime.Screen.IndirectFullTex!=null &&
            runtime.WorldBuffers.Resources!=null && runtime.WorldBuffers.Resources.ProbeMeta0.ReadPixels().Where((_,i)=>i%2==0).Any(c=>c>=.25f) &&
            runtime.Screen.ScreenProbeAtlasMetaTraceTex!.ReadPixels().Where((_,i)=>i%2==0).Count(c=>c>=.99f)>=32 &&
            runtime.FinalPixels().Where((_,i)=>i%4!=3).All(value=>float.IsFinite(value) && (lit?value>.001f:Math.Abs(value)<=.0001f)));
    }

    /// <summary>Compares shader-generated anchors against independent ray/box intersections in absolute world coordinates.</summary>
    private static void AssertAnchors(SurfaceLightingConsumerRuntimeFixture runtime,SpatialLightingScene scene)
    {
        var anchors=runtime.Screen.ProbeAnchorPositionTex!.ReadPixels();
        var normals=runtime.Screen.ProbeAnchorNormalTex!.ReadPixels();
        for(int i=0;i<scene.VisiblePoints.Length;i++)
        {
            Assert.True(anchors[i*4+3]>=.5f);
            var actual=new Vector3(anchors[i*4],anchors[i*4+1],anchors[i*4+2])+scene.Position;
            Assert.InRange(Vector3.Distance(actual,scene.VisiblePoints[i]),0,.02f);
            var normal=new Vector3(normals[i*4],normals[i*4+1],normals[i*4+2])*2-Vector3.One;
            Assert.InRange(Vector3.Distance(normal,scene.VisibleNormals[i]),0,.001f);
        }
    }

    /// <summary>Reads the real ring publication bit using independently wrapped signed voxel coordinates.</summary>
    private static void AssertCellReady(SurfaceLightingConsumerRuntimeFixture runtime,int x,int y,int z,bool ready)
    {
        var scene=runtime.Cache.Geometry.Resources!;
        int n=scene.Resolution/16;
        var cells=new uint[n*n*n];
        using var binding=VanillaGraphicsExpanded.Rendering.GlStateCache.Current.BindTextureScope(TextureTarget.Texture3D,0,scene.Readiness.TextureId);
        GL.GetTexImage(TextureTarget.Texture3D,0,PixelFormat.RedInteger,PixelType.UnsignedInt,cells);
        int sx=((x>>4)%n+n)%n,sy=((y>>4)%n+n)%n,sz=((z>>4)%n+n)%n;
        Assert.Equal(ready?1u:0u,cells[(sz*n+sy)*n+sx]);
    }
    /// <summary>Checks all RGB channels for leakage, lost illumination and nonfinite output.</summary>
    private static void AssertPixels(float[] pixels,bool lit)
    {
        Assert.NotEmpty(pixels);
        for(int i=0;i<pixels.Length;i++)
        {
            Assert.True(float.IsFinite(pixels[i]));
            if(i%4!=3) { if(lit) Assert.True(pixels[i]>.001f,$"Dark channel {i}: {pixels[i]}"); else Assert.InRange(Math.Abs(pixels[i]),0,.0001f); }
        }
    }
    #endregion
}
