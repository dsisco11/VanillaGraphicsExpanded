using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System.Numerics;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.RHI;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Exercises spatial correspondence and resident lighting under real camera and streaming transitions.</summary>
[Collection("NearFieldMaterialCapture")]
[Trait("Category", "GpuIntegration")]
[Trait("Scenario", "HeadlessRuntime")]
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
        using var runtime=new SurfaceLightingConsumerRuntimeFixture(ImageAssertions, sh9,scene);
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
            AssertPixels(runtime.FinalImage,true);
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
        using var runtime=new SurfaceLightingConsumerRuntimeFixture(ImageAssertions, sh9,scene);
        Settle(runtime,true);
        var resources=runtime.WorldBuffers.Resources!;
        Assert.True(runtime.WorldBuffers.TryGetRuntimeParams(out var playerOrigin,out _,out float spacing,
            out _,out int resolution,out var origins,out var rings));
        Assert.Equal(resources.Resolution,resolution);
        var origin=origins[0]+new Vector3((float)playerOrigin.X,(float)playerOrigin.Y,(float)playerOrigin.Z);
        var ring=rings[0];
        var overlap=new List<(int X,int Y,int Z)>();
        // Only this room has requested Surface Cache lighting. Select supported centers
        // independently of confidence so missing lighting cannot silently reduce coverage.
        for(int y=0;y<resolution;y++) for(int z=0;z<resolution;z++) for(int x=1;x<resolution;x++)
        {
            var center=origin+(new Vector3(x,y,z)+new Vector3(.5f))*spacing;
            if(center.X>=scene.RoomOrigin+1 && center.X<scene.RoomOrigin+7 &&
                !scene.Solid((int)MathF.Floor(center.X),(int)MathF.Floor(center.Y),(int)MathF.Floor(center.Z)))
                overlap.Add((x,y,z));
        }
        Assert.Equal(27,overlap.Count);
        runtime.RunUntil(()=>
        {
            return overlap.All(p => ImageAssertions.Statistics.Inspect(resources.ProbeMeta0,
                selection: new(new(0), (p.X + (int)ring.X) % resolution + (p.Z + (int)ring.Z) % resolution * resolution,
                    (p.Y + (int)ring.Y) % resolution, 0, 1, 1), channelMask: 1).Minimum >= .25f);
        });
        // Stop further producer work through its budget, preserving a coherent published generation.
        runtime.RunUntil(runtime.Cache.AllRequestedLightingReady);
        runtime.Cache.Config.LumOn.LumonScene.RelightSeedPagesPerFrame = runtime.Cache.Config.LumOn.LumonScene.RelightDirectPagesPerFrame = runtime.Cache.Config.LumOn.LumonScene.RelightIndirectPagesPerFrame = 0;
        runtime.Frame(); runtime.Frame();
        // Isolate ring retention from legitimate asynchronous refreshes of surviving tiles.
        runtime.Cache.Config.WorldProbeClipmap.UploadBudgetBytesPerFrame=1;
        using var before = new Assertions.GpuImageSnapshot(runtime.WorldImage);
        using var reference = new Assertions.GpuImageSnapshot(runtime.FinalImage);
        using var metaBefore = new Assertions.GpuImageSnapshot(resources.ProbeMeta0);
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
        AssertAnchors(runtime,scene); AssertPixels(runtime.FinalImage,true);

        Assert.Same(resources,runtime.WorldBuffers.Resources);

        // Every supported old coordinate becomes x-1; wrapping must retain its physical tile.
        int tileSize=resources.WorldProbeTileSize;
        foreach(var p in overlap)
        {
            int sx=(p.X+shift.PrevRingOffset.X)%resolution;
            int sy=(p.Y+shift.PrevRingOffset.Y)%resolution;
            int sz=(p.Z+shift.PrevRingOffset.Z)%resolution;
            Assert.Equal(sx,(p.X-shift.DeltaProbes.X+shift.NewRingOffset.X)%resolution);
            Assert.Equal(sy,(p.Y-shift.DeltaProbes.Y+shift.NewRingOffset.Y)%resolution);
            Assert.Equal(sz,(p.Z-shift.DeltaProbes.Z+shift.NewRingOffset.Z)%resolution);
            var metaRegion = new RHITextureRegion(new(0), sx + sz * resolution, sy, 0, 1, 1);
            using var oldMeta = new Assertions.GpuImageSnapshot(metaBefore.Texture, metaRegion);
            ImageAssertions.Comparisons.CompareHistory(resources.ProbeMeta0, oldMeta, metaRegion, channelMask: 1).AssertEqual();
            ImageAssertions.Comparisons.CompareHistory(resources.ProbeMeta0, oldMeta, metaRegion, channelMask: 2, bitwise: true).AssertEqual();
            var tileRegion = new RHITextureRegion(new(0), (sx + sz * resolution) * tileSize, sy * tileSize, 0, tileSize, tileSize);
            using var oldTile = new Assertions.GpuImageSnapshot(before.Texture, tileRegion);
            ImageAssertions.Comparisons.CompareHistory(runtime.WorldImage, oldTile, tileRegion).AssertEqual();
            Assert.True(ImageAssertions.Statistics.Inspect(oldTile.Texture, channelMask: 7).Maximum > .001f,
                "Each surviving world tile must contain actual produced lighting.");
        }
        // Newly assigned edge slots remain unavailable while uploads are paused. The
        // prefilled-tile clear tests separately prove that this clear removes old light.
        for(int y=0;y<resolution;y++) for(int z=0;z<resolution;z++)
        {
            int sx=(resolution-1+shift.NewRingOffset.X)%resolution;
            int sy=(y+shift.NewRingOffset.Y)%resolution;
            int sz=(z+shift.NewRingOffset.Z)%resolution;
            var metaRegion = new RHITextureRegion(new(0), sx + sz * resolution, sy, 0, 1, 1);
            ImageAssertions.Comparisons.CompareConstant(resources.ProbeMeta0, 0, metaRegion).AssertEqual();
            using var clearedMeta = new Assertions.GpuImageSnapshot(resources.ProbeMeta0, metaRegion);
            Assert.Equal(0u, ImageAssertions.Statistics.CountFlags(clearedMeta.Texture, uint.MaxValue, 2).FlagMatches);
            ImageAssertions.Comparisons.CompareConstant(runtime.WorldImage, 0,
                new(new(0), (sx + sz * resolution) * tileSize, sy * tileSize, 0, tileSize, tileSize)).AssertEqual();
        }
        scene.Position=new(0,36,5); scene.EyeOffsetX=0;
        runtime.Frame(); runtime.Frame();
        AssertAnchors(runtime,scene);
        ImageAssertions.Comparisons.CompareHistory(runtime.FinalImage, reference, tolerance: .03f, channelMask: 7).AssertEqual();
    }
    /// <summary>Signed chunk crossings preserve allocations; reused unavailable slots cannot expose old light before reload.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void SignedStreamingRejectsReusedUnavailableSlots(bool sh9)
    {
        EnsureContextValid();
        var scene=new SpatialLightingScene();
        using var runtime=new SurfaceLightingConsumerRuntimeFixture(ImageAssertions, sh9,scene);
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
            AssertPixels(runtime.FinalImage,false);
            Assert.InRange(runtime.Energy(runtime.WorldImage),0,.0001f);
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
        using var runtime=new SurfaceLightingConsumerRuntimeFixture(ImageAssertions, sh9,scene);
        Settle(runtime,true);
        foreach(float x in new[]{8f,8.25f,40f,-24f,0f})
        {
            bool lit=x==0;
            scene.Position=new(x,36,5); scene.Bob=.1f; scene.Yaw=.15f;
            runtime.Frame();
            if(!lit) AssertPixels(runtime.FinalImage,false);
            Settle(runtime,lit); AssertAnchors(runtime,scene);
            for(int i=0;i<8;i++) { runtime.Frame(); AssertPixels(runtime.FinalImage,lit); }
            if(x==8f)
            {
                runtime.Cache.GeometryAvailable=false;
                for(int i=0;i<4;i++) { runtime.Frame(); AssertPixels(runtime.FinalImage,false); }
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
        using var runtime=new SurfaceLightingConsumerRuntimeFixture(ImageAssertions, sh9,scene);
        runtime.Cache.Config.LumOn.DebugMode=VanillaGraphicsExpanded.LumOn.LumOnDebugMode.WorldProbeLightingEffect;
        runtime.Cache.Config.WorldProbeClipmap.ClipmapResolution=4;
        Settle(runtime,true);
        runtime.RunUntil(()=>runtime.Energy(runtime.WorldImage)>.001f && runtime.Screen.WorldProbeSuppressedLighting!=null);
        using var baseline = new Assertions.GpuImageSnapshot(runtime.FinalImage);
        ImageAssertions.Comparisons.CompareHistory(runtime.Screen.WorldProbeSuppressedLighting!, baseline,
            tolerance: MathF.BitDecrement(.02f), channelMask: 7).AssertEqual();
        runtime.RunUntil(runtime.Cache.AllRequestedLightingReady);
        runtime.Cache.Config.LumOn.LumonScene.RelightSeedPagesPerFrame = runtime.Cache.Config.LumOn.LumonScene.RelightDirectPagesPerFrame = runtime.Cache.Config.LumOn.LumonScene.RelightIndirectPagesPerFrame = 0;
        runtime.Cache.Config.LumOn.ProbeSpacingPx=4;
        bool worldSelected=false, gatherSelected=false, tracedWorldSelected=false;
        var observations=new List<string>();
        foreach(float yaw in new[]{0f,.25f,.5f,.75f,1f,1.25f})
        {
            scene.Yaw=yaw; runtime.Frame();
            for(int i=0;i<24;i++) runtime.Frame();
            var final = ImageAssertions.Statistics.Inspect(runtime.FinalImage, channelMask: 7);
            AssertPixels(runtime.FinalImage, true);
            using var without = new Assertions.GpuImageSnapshot(runtime.Screen.WorldProbeSuppressedLighting!);
            bool different = ImageAssertions.Comparisons.CompareHistory(runtime.FinalImage, without,
                tolerance: .001f, channelMask: 7).Mismatches != 0;
            int world = (int)ImageAssertions.Statistics.CountFlags(runtime.Screen.ScreenProbeAtlasMetaTraceTex!, 32, 2).FlagMatches;
            observations.Add($"yaw={yaw},deltaAboveThreshold={different},worldRays={world},minRGB={final.Minimum}");
            worldSelected |= different;
            gatherSelected |= world == 0 && different;
            tracedWorldSelected |= world > 0 && different;
        }
        TestContext.Current.TestOutputHelper?.WriteLine(string.Join(";",observations));
        Assert.True(worldSelected,string.Join(";",observations));
        Assert.True(gatherSelected,"No isolated gather replacement: "+string.Join(";",observations));
        Assert.True(tracedWorldSelected,"No directional world contribution in screen tracing: "+string.Join(";",observations));
        runtime.Cache.Config.LumOn.LumonScene.RelightSeedPagesPerFrame = runtime.Cache.Config.LumOn.LumonScene.RelightDirectPagesPerFrame = runtime.Cache.Config.LumOn.LumonScene.RelightIndirectPagesPerFrame = 4;
        runtime.Cache.Config.LumOn.ProbeSpacingPx=1; scene.Yaw=0;
        runtime.Cache.GeometryAvailable=false;
        for(int i=0;i<4;i++) runtime.Frame();
        AssertPixels(runtime.FinalImage,false);
        ImageAssertions.Comparisons.CompareConstant(runtime.Screen.ScreenProbeAtlasMetaTraceTex!, 0, channelMask: 1).AssertEqual();
        runtime.Cache.GeometryAvailable=true; Settle(runtime,true);
    }
    #endregion
    #region Observations
    /// <summary>Waits for actual lighting and checks every final pixel, never just a maximum-energy sample.</summary>
    private void Settle(SurfaceLightingConsumerRuntimeFixture runtime,bool lit)
    {
        runtime.RunUntil(()=>runtime.Cache.TryGetLighting(out _) && runtime.Screen.IndirectFullTex!=null &&
            runtime.WorldBuffers.Resources!=null && ImageAssertions.Statistics.Inspect(runtime.WorldBuffers.Resources.ProbeMeta0, channelMask: 1).Maximum >= .25f &&
            ImageAssertions.Statistics.Inspect(runtime.Screen.ScreenProbeAtlasMetaTraceTex!, .99f, double.PositiveInfinity, channelMask: 1).InsideRange>=32 &&
            PixelsMatch(runtime.FinalImage, lit));
    }

    /// <summary>Compares shader-generated anchors against independent ray/box intersections in absolute world coordinates.</summary>
    private void AssertAnchors(SurfaceLightingConsumerRuntimeFixture runtime, SpatialLightingScene scene)
    {
        var image = runtime.Screen.ProbeAnchors!;
        Assert.Equal(image.Width * image.Height, scene.VisiblePoints.Length);
        var positions = scene.VisiblePoints.SelectMany(p => new[] { p.X - scene.Position.X, p.Y - scene.Position.Y, p.Z - scene.Position.Z, 0f }).ToArray();
        var normals = scene.VisibleNormals.SelectMany(n => new[] { (n.X + 1) / 2, (n.Y + 1) / 2, (n.Z + 1) / 2, 0f }).ToArray();
        RHITextureRegion positionsRegion = new(new(0, 0), 0, 0, 0, image.Width, image.Height);
        RHITextureRegion normalsRegion = positionsRegion with { Subresource = new(0, 1) };
        Assert.True(ImageAssertions.Statistics.Inspect(image, selection: positionsRegion, channelMask: 8).Minimum >= .5f);
        ImageAssertions.Comparisons.Compare(image, positions, .02f, positionsRegion, channelMask: 7, vectorDistance: true).AssertEqual();
        ImageAssertions.Comparisons.Compare(image, normals, .001f / 2, normalsRegion, channelMask: 7, vectorDistance: true).AssertEqual();
    }

    /// <summary>Reads the real ring publication bit using independently wrapped signed voxel coordinates.</summary>
    private void AssertCellReady(SurfaceLightingConsumerRuntimeFixture runtime, int x, int y, int z, bool ready)
    {
        var scene = runtime.Cache.Geometry.Resources!;
        int n = scene.Resolution / 16;
        int sx = ((x >> 4) % n + n) % n, sy = ((y >> 4) % n + n) % n, sz = ((z >> 4) % n + n) % n;
        ImageAssertions.Comparisons.CompareBits(scene.Readiness, [ready ? 1u : 0u],
            selection: new(new(0), sx, sy, sz, 1, 1, 1)).AssertEqual();
    }
    /// <summary>Checks all RGB channels for leakage, lost illumination and nonfinite output.</summary>
    private void AssertPixels(GpuTexture image, bool lit)
    {
        ImageAssertions.Statistics.Inspect(image).AssertFiniteRange();
        Assert.True(PixelsMatch(image, lit), $"Lighting domain {image.Width}x{image.Height} does not match lit={lit}.");
    }

    /// <summary>Checks every RGB component with compact finite and range predicates.</summary>
    private bool PixelsMatch(GpuTexture image, bool lit)
    {
        var rgb = ImageAssertions.Statistics.Inspect(image, -.0001f, .0001f, channelMask: 7);
        return rgb.NonFinite == 0 && (lit ? rgb.Minimum > .001f : rgb.OutsideRange == 0);
    }
    #endregion
    #region Private
    /// <summary>Prepares this suite's declared observation programs before subject execution.</summary>
    private protected override void PrepareObservations(VanillaGraphicsExpanded.Tests.GPU.Assertions.GpuObservationPrograms programs)
    {
        base.PrepareObservations(programs);
        programs.Prepare("compare", 2, 0);
        programs.Prepare("compare", 0, 0);
        programs.Prepare("inspection", 0, 0);
        programs.Prepare("predicate-observation", 0, 0);
    }
    #endregion
}
