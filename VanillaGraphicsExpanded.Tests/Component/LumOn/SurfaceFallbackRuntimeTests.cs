using System.Text.RegularExpressions;
using VanillaGraphicsExpanded.LumOn.Scene;
using VanillaGraphicsExpanded.LumOn.Scene.Fallback;
using VanillaGraphicsExpanded.Tests.Support.LumOn.WorldProbes.Fixtures;
using VanillaGraphicsExpanded.Tests.Support.Lighting;

namespace VanillaGraphicsExpanded.Tests.Component.LumOn;

/// <summary>Checks fallback completion through actual renderer scheduling, collision traversal and publication.</summary>
[Collection("NearFieldMaterialCapture")]
[Trait("Category", "GpuIntegration")]
[Trait("Scenario", "HeadlessRuntime")]
public sealed class SurfaceFallbackRuntimeTests(HeadlessGLFixture fixture):RenderTestBase(fixture)
{


    #region Public API
    #region Runtime completion
    /// <summary>Completed CPU estimates retained by page credit must still reject withdrawn chunk dependencies before publication.</summary>
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public void BackloggedCompletedFallbackRejectsWithdrawnDependencies(int invalidation)
    {
        // Arrange: Complete CPU fallback queries, then retain their commits behind a reduced page-publication budget.
        EnsureContextValid();
        var world = new ControlledVoxelWorld { MapSizeY = 256 };
        using var runtime = new SurfaceCacheRuntimeFixture(ImageAssertions, requestedPages:24,enclosure:true,fallbackWorld:world);
        world.AddRoom((0,32,0),(7,39,7),materialId:runtime.SourceBlock.Id);
        runtime.PrimeGeometry(); runtime.RunUntil(runtime.AllRequestedLightingReady,256);
        runtime.TransformVoxel=(x,y,z,voxel)=>(voxel.Geometry&3u)==1?voxel with{Geometry=3u}:voxel;
        runtime.Config.LumOn.LumonScene.RelightIndirectPagesPerFrame=4;
        runtime.InvalidateGeometry();
        var renderer=(LumonSceneRelightUpdateRenderer)runtime.LightingProvider;
        // The GPU captures several source pages before reduced publication credit retains excess completed texels.
        runtime.RunUntil(()=>Counter(renderer,"fallbackAdmitted")>0,256);
        runtime.Config.LumOn.LumonScene.RelightIndirectPagesPerFrame=1;
        runtime.RunUntil(()=>Counter(renderer,"pendingCommits")>0,512);
        long committed=Counter(renderer,"fallbackCommitted"), rejected=Counter(renderer,"fallbackRejected");
        // Streaming may withdraw CPU-only geometry without changing the GPU scene revision.
        // Act: Withdraw one dependency through the selected terrain, page, chunk-object, or loaded-state failure.
        if (invalidation == 3)
            world.IsLoaded=_=>throw new InvalidOperationException("Terrain accessor unavailable during publication.");
        else if (invalidation == 2)
        {
            // Remove a captured dependency outside the remaining source pages: origin-only checks cannot catch this.
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
            var dependencies = (SurfaceFallbackCommitDependencies)typeof(LumonSceneRelightUpdateRenderer)
                .GetField("pendingCommitDependencies",flags)!.GetValue(renderer)!;
            var pending = (List<(SurfaceFallbackCommit Commit, SurfaceFallbackPage Origin, SurfaceFallbackLifetime Lifetime, bool Cpu)>)
                typeof(LumonSceneRelightUpdateRenderer).GetField("pendingCommits",flags)!.GetValue(renderer)!;
            uint dependency = dependencies.Pages.First(page=>pending.All(item=>item.Origin.Page!=page.Page)).Page;
            var captures = (System.Collections.IDictionary)typeof(LumonSceneFeedbackUpdateRenderer)
                .GetField("captureIdentities",flags)!.GetValue(runtime.Feedback)!;
            captures.Remove(dependency);
        }
        else if (invalidation == 1)
        {
            // Replace only the observed chunk object, without an edit event or GPU revision change.
            var accessor = runtime.Api.World.BlockAccessor;
            var field = typeof(ControlledBlockAccessor).GetField("loadedChunk",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!;
            field.SetValue(accessor,System.Reflection.DispatchProxy.Create<Vintagestory.API.Common.IWorldChunk,LoadedChunkSentinel>());
        }
        else world.IsLoaded=_=>false;
        runtime.Frame(); runtime.Frame();
        // Assert: A commit with withdrawn dependencies is rejected while the previously displayed lighting remains valid.
        Assert.Equal(committed,Counter(renderer,"fallbackCommitted"));
        Assert.True(Counter(renderer,"fallbackRejected")>rejected);
        Assert.True(runtime.TryGetLighting(out _));
    }

    /// <summary>Completed estimates retain their dependencies through a zero-credit pause and resume normally.</summary>
    [Fact]
    public void BackloggedCompletedFallbackResumesWithValidDependencies()
    {
        // Arrange: Queue completed fallback estimates behind a one-page commit budget.
        EnsureContextValid();
        var world = new ControlledVoxelWorld { MapSizeY = 256 };
        using var runtime = new SurfaceCacheRuntimeFixture(ImageAssertions, requestedPages:24,enclosure:true,fallbackWorld:world);
        world.AddRoom((0,32,0),(7,39,7),materialId:runtime.SourceBlock.Id);
        runtime.PrimeGeometry(); runtime.RunUntil(runtime.AllRequestedLightingReady,256);
        runtime.TransformVoxel=(x,y,z,voxel)=>(voxel.Geometry&3u)==1?voxel with{Geometry=3u}:voxel;
        runtime.Config.LumOn.LumonScene.RelightIndirectPagesPerFrame=4;
        runtime.InvalidateGeometry();
        var renderer=(LumonSceneRelightUpdateRenderer)runtime.LightingProvider;
        runtime.RunUntil(()=>Counter(renderer,"fallbackAdmitted")>0,256);
        runtime.Config.LumOn.LumonScene.RelightIndirectPagesPerFrame=1;
        runtime.RunUntil(()=>Counter(renderer,"pendingCommits")>0,512);
        long committed=Counter(renderer,"fallbackCommitted"), queued=Counter(renderer,"pendingCommits");
        Assert.True(runtime.TryGetLighting(out var before));
        // Zero publication credit must retain otherwise valid completed work.
        // Act: Pause publication at zero credit, then restore one-page credit and let the queue resume.
        runtime.Config.LumOn.LumonScene.RelightIndirectPagesPerFrame=0;
        for (int frame=0;frame<4;frame++) runtime.Frame();
        // Assert: Valid work remains pending during the pause and commits after credit returns.
        Assert.Equal(committed,Counter(renderer,"fallbackCommitted"));
        Assert.Equal(queued,Counter(renderer,"pendingCommits"));
        Assert.True(runtime.TryGetLighting(out var held));
        Assert.Same(before.DirectIrradiance,held.DirectIrradiance);
        runtime.Config.LumOn.LumonScene.RelightIndirectPagesPerFrame=1;
        runtime.RunUntil(()=>Counter(renderer,"fallbackCommitted")>committed,64);
    }

    /// <summary>Disabling indirect admissions retains outstanding CPU results until their budget resumes.</summary>
    [Fact]
    public void DisabledIndirectBudgetRetainsBlockedFallback()
    {
        // Arrange: Block a real worker query after it enters the controlled terrain accessor.
        EnsureContextValid(); using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        var world = new ControlledVoxelWorld { MapSizeY=36, IsLoaded=_=>
        { entered.Set(); release.Wait(TestContext.Current.CancellationToken); return true; } };
        using var runtime = new SurfaceCacheRuntimeFixture(ImageAssertions, exposedWall:true,fallbackWorld:world);
        runtime.PrimeGeometry(); runtime.RunUntil(runtime.AllRequestedLightingReady);
        Assert.True(runtime.TryGetLighting(out var initial));
        runtime.TransformVoxel=(x,y,z,voxel)=>x>=1?voxel with{Geometry=3u}:voxel;
        runtime.InvalidateGeometry(); var renderer=(LumonSceneRelightUpdateRenderer)runtime.LightingProvider;
        try
        {
            // Act: Disable indirect publication while the query is blocked, then release and resume the budget.
            runtime.RunUntil(()=>entered.IsSet,256);
            long committed=Counter(renderer,"fallbackCommitted");
            runtime.Config.LumOn.LumonScene.RelightIndirectPagesPerFrame=0;
            release.Set();
            for(int frame=0;frame<8;frame++) runtime.Frame();
            // Assert: The blocked result does not publish at zero budget, retains displayed light, then commits later.
            Assert.Equal(committed,Counter(renderer,"fallbackCommitted"));
            Assert.True(runtime.TryGetLighting(out var held)); Assert.Same(initial.DirectIrradiance,held.DirectIrradiance);
            // Act: Restore publication credit for the valid work retained during the pause.
            runtime.Config.LumOn.LumonScene.RelightIndirectPagesPerFrame=1;
            // Assert: The retained completion commits after credit is restored.
            runtime.RunUntil(()=>Counter(renderer,"fallbackCommitted")>committed,256);
        }
        finally { release.Set(); }
    }

    /// <summary>Unsupported GPU geometry can resolve confirmed sky through CPU collision without clearing displayed lighting.</summary>
    [Fact]
    public void UnsupportedGeometryCompletesThroughNormalPublication()
    {
        // Arrange: Capture visible lighting, then turn the traced wall into unsupported GPU geometry.
        EnsureContextValid();var world=new ControlledVoxelWorld{MapSizeY=36};
        using var runtime=new SurfaceCacheRuntimeFixture(ImageAssertions, exposedWall:true,fallbackWorld:world);
        runtime.PrimeGeometry();runtime.RunUntil(runtime.AllRequestedLightingReady);
        Assert.True(runtime.TryGetLighting(out var initial));
        Assert.True(runtime.Feedback.TryGetNearDispatchState(out _,out _,out var pages,out _));uint page=pages.Single().Key;
        using var before=SurfaceLightingPageSelection.Capture(initial.OutgoingRadiance,initial,page);
        runtime.TransformVoxel=(x,y,z,voxel)=>x>=1?voxel with{Geometry=3u}:voxel;
        runtime.InvalidateGeometry();
        var renderer=(LumonSceneRelightUpdateRenderer)runtime.LightingProvider;
        // Act: Let CPU collision confirm sky and pass the completion through normal renderer publication.
        runtime.RunUntil(()=>Counter(renderer,"fallbackCommitted")>0,256);
        // Assert: The output keeps its prior display while history records the valid zero-light completion.
        Assert.True(Counter(renderer,"fallbackAdmitted")>0);Assert.Empty(world.LightQueries);
        Assert.True(runtime.TryGetLighting(out var current));
        using var after=SurfaceLightingPageSelection.Capture(current.OutgoingRadiance,current,page);
        ImageAssertions.Comparisons.CompareConstant(after.Texture, 1, channelMask: 8).AssertEqual();
        ImageAssertions.Comparisons.CompareHistory(after.Texture, before).AssertEqual();
    }

    /// <summary>CPU collision hits resolve actual cached radiance through the asynchronous query and normal commit path.</summary>
    [Fact]
    public void UnsupportedRoomAirResolvesCapturedWallRadiance()
    {
        // Arrange: Publish a captured room and convert only interior air to unsupported GPU geometry.
        EnsureContextValid();var world=new ControlledVoxelWorld{MapSizeY=256};
        using var runtime=new SurfaceCacheRuntimeFixture(ImageAssertions, requestedPages:24,enclosure:true,fallbackWorld:world);
        world.AddRoom((0,32,0),(7,39,7),materialId:runtime.SourceBlock.Id);
        runtime.Config.LumOn.LumonScene.RelightSeedPagesPerFrame = runtime.Config.LumOn.LumonScene.RelightDirectPagesPerFrame = runtime.Config.LumOn.LumonScene.RelightIndirectPagesPerFrame = 4;
        runtime.PrimeGeometry();runtime.RunUntil(runtime.AllRequestedLightingReady,256);
        runtime.TransformVoxel=(x,y,z,voxel)=>(voxel.Geometry&3u)==1?voxel with{Geometry=3u}:voxel;
        runtime.InvalidateGeometry();var renderer=(LumonSceneRelightUpdateRenderer)runtime.LightingProvider;
        // Act: Resolve fallback rays against the real controlled collision world and commit their captured radiance.
        runtime.RunUntil(()=>Counter(renderer,"fallbackCommitted")>0,512);
        // Assert: No unrelated engine light query is used and at least one requested page receives nonzero light.
        Assert.Empty(world.LightQueries);Assert.True(runtime.TryGetLighting(out var lighting));
        Assert.True(runtime.Feedback.TryGetNearDispatchState(out _,out _,out var pages,out _));
        bool nonzero=false;
        foreach(uint page in pages.Keys)
        {
            using var pixels=SurfaceLightingPageSelection.Capture(lighting.IndirectIrradiance,lighting,page);
            nonzero |= ImageAssertions.Statistics.CountMatching(pixels.Texture,
                new(MathF.BitIncrement(.01f),float.NegativeInfinity,float.NegativeInfinity,float.Epsilon),
                new(float.PositiveInfinity), channelMask: 9).MatchingTexels > 0;
        }
        Assert.True(nonzero);
    }

    /// <summary>A geometry invalidation rejects delayed terrain work before it can overwrite retained displayed lighting.</summary>
    [Fact]
    public void GeometryEditRejectsBlockedFallbackCompletion()
    {
        // Arrange: Hold CPU collision in flight after the renderer has scheduled a fallback result.
        EnsureContextValid();using var entered=new ManualResetEventSlim();using var release=new ManualResetEventSlim();
        var world=new ControlledVoxelWorld{MapSizeY=36,IsLoaded=_=>
        { entered.Set();release.Wait(TestContext.Current.CancellationToken);return true; }};
        using var runtime=new SurfaceCacheRuntimeFixture(ImageAssertions, exposedWall:true,fallbackWorld:world);
        runtime.PrimeGeometry();runtime.RunUntil(runtime.AllRequestedLightingReady);
        runtime.TransformVoxel=(x,y,z,voxel)=>x>=1?voxel with{Geometry=3u}:voxel;
        runtime.InvalidateGeometry();var renderer=(LumonSceneRelightUpdateRenderer)runtime.LightingProvider;
        try
        {
            // Act: Invalidate geometry before releasing the blocked result.
            runtime.RunUntil(()=>entered.IsSet,256);long committed=Counter(renderer,"fallbackCommitted");
            long rejected=Counter(renderer,"fallbackRejected");runtime.InvalidateGeometry();
            runtime.RunUntil(()=>Counter(renderer,"fallbackRejected")>rejected,32);
            // Assert: The stale worker result cannot overwrite the retained display or increment commits.
            Assert.Equal(committed,Counter(renderer,"fallbackCommitted"));Assert.True(runtime.TryGetLighting(out _));
            // Act: Release the stale worker only after the new geometry revision has rejected its result.
            release.Set();runtime.Frame();Assert.Equal(committed,Counter(renderer,"fallbackCommitted"));
        }
        finally { release.Set(); }
    }

    /// <summary>Atlas replacement and world teardown cannot publish an old worker result into a different resource lifetime.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ResourceResetRetiresBlockedFallback(bool leaveWorld)
    {
        // Arrange: Hold a terrain query across a resource reset or full world leave.
        EnsureContextValid();using var entered=new ManualResetEventSlim();using var release=new ManualResetEventSlim();
        var world=new ControlledVoxelWorld{MapSizeY=36,IsLoaded=_=>
        { entered.Set();release.Wait(TestContext.Current.CancellationToken);return true; }};
        using var runtime=new SurfaceCacheRuntimeFixture(ImageAssertions, exposedWall:true,fallbackWorld:world);
        runtime.PrimeGeometry();runtime.RunUntil(runtime.AllRequestedLightingReady);
        runtime.TransformVoxel=(x,y,z,voxel)=>x>=1?voxel with{Geometry=3u}:voxel;
        runtime.InvalidateGeometry();var renderer=(LumonSceneRelightUpdateRenderer)runtime.LightingProvider;
        try
        {
            // Act: Retire the resource lifetime before allowing the worker query to return.
            runtime.RunUntil(()=>entered.IsSet,256);long committed=Counter(renderer,"fallbackCommitted");
            if(leaveWorld)runtime.LeaveWorld();
            else { runtime.RequestAtlasRecreation();runtime.Frame(); }
            // Assert: Work from the old lifetime never commits into replacement resources or after world leave.
            Assert.Equal(committed,Counter(renderer,"fallbackCommitted"));
            // Act: Allow the worker from the retired lifetime to return.
            release.Set();
            // Assert: The late result stays retired after the worker completes.
            if(leaveWorld)Assert.False(runtime.TryGetLighting(out _));
            else { runtime.Frame();Assert.Equal(committed,Counter(renderer,"fallbackCommitted")); }
        }
        finally { release.Set(); }
    }
    #endregion
    #endregion

    #region Protected
    /// <summary>Prepares this suite's declared observation programs before subject execution.</summary>
    private protected override void PrepareObservations(VanillaGraphicsExpanded.Tests.Support.GPU.Observations.GpuObservationPrograms programs)
    {
        base.PrepareObservations(programs);
        programs.Prepare("compare", 0, 0);
        programs.Prepare("inspection", 0, 0);
    }
    #endregion

    #region Private
    /// <summary>Reads bounded production counters without inspecting private pipeline ownership.</summary>
    private static long Counter(LumonSceneRelightUpdateRenderer renderer,string name)
    {
        Assert.True(renderer.TryGetSelfCheckLine(out string line));
        var match=Regex.Match(line,$@"\b{name}:(\d+)");Assert.True(match.Success,line);
        return long.Parse(match.Groups[1].Value);
    }
    #endregion
}
