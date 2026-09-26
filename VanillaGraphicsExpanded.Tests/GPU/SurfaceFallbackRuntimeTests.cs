using System.Text.RegularExpressions;
using VanillaGraphicsExpanded.LumOn.Scene;
using VanillaGraphicsExpanded.LumOn.Scene.Fallback;
using VanillaGraphicsExpanded.Tests.Fixtures.WorldProbes;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks fallback completion through actual renderer scheduling, collision traversal and publication.</summary>
[Collection("NearFieldMaterialCapture")]
[Trait("Category","GPU")]
public sealed class SurfaceFallbackRuntimeTests(HeadlessGLFixture fixture):RenderTestBase(fixture)
{
    #region Runtime completion
    /// <summary>Completed CPU estimates retained by page credit must still reject withdrawn chunk dependencies before publication.</summary>
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public void BackloggedCompletedFallbackRejectsWithdrawnDependencies(int invalidation)
    {
        EnsureContextValid();
        var world = new ControlledVoxelWorld { MapSizeY = 256 };
        using var runtime = new SurfaceCacheRuntimeFixture(requestedPages:24,enclosure:true,fallbackWorld:world);
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
        Assert.Equal(committed,Counter(renderer,"fallbackCommitted"));
        Assert.True(Counter(renderer,"fallbackRejected")>rejected);
        Assert.True(runtime.TryGetLighting(out _));
    }

    /// <summary>Completed estimates retain their dependencies through a zero-credit pause and resume normally.</summary>
    [Fact]
    public void BackloggedCompletedFallbackResumesWithValidDependencies()
    {
        EnsureContextValid();
        var world = new ControlledVoxelWorld { MapSizeY = 256 };
        using var runtime = new SurfaceCacheRuntimeFixture(requestedPages:24,enclosure:true,fallbackWorld:world);
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
        runtime.Config.LumOn.LumonScene.RelightIndirectPagesPerFrame=0;
        for (int frame=0;frame<4;frame++) runtime.Frame();
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
        EnsureContextValid(); using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        var world = new ControlledVoxelWorld { MapSizeY=36, IsLoaded=_=>
        { entered.Set(); release.Wait(TestContext.Current.CancellationToken); return true; } };
        using var runtime = new SurfaceCacheRuntimeFixture(exposedWall:true,fallbackWorld:world);
        runtime.PrimeGeometry(); runtime.RunUntil(runtime.AllRequestedLightingReady);
        Assert.True(runtime.TryGetLighting(out var initial));
        runtime.TransformVoxel=(x,y,z,voxel)=>x>=1?voxel with{Geometry=3u}:voxel;
        runtime.InvalidateGeometry(); var renderer=(LumonSceneRelightUpdateRenderer)runtime.LightingProvider;
        try
        {
            runtime.RunUntil(()=>entered.IsSet,256);
            long committed=Counter(renderer,"fallbackCommitted");
            runtime.Config.LumOn.LumonScene.RelightIndirectPagesPerFrame=0;
            release.Set();
            for(int frame=0;frame<8;frame++) runtime.Frame();
            Assert.Equal(committed,Counter(renderer,"fallbackCommitted"));
            Assert.True(runtime.TryGetLighting(out var held)); Assert.Same(initial.DirectIrradiance,held.DirectIrradiance);
            runtime.Config.LumOn.LumonScene.RelightIndirectPagesPerFrame=1;
            runtime.RunUntil(()=>Counter(renderer,"fallbackCommitted")>committed,256);
        }
        finally { release.Set(); }
    }

    /// <summary>Unsupported GPU geometry can resolve confirmed sky through CPU collision without clearing displayed lighting.</summary>
    [Fact]
    public void UnsupportedGeometryCompletesThroughNormalPublication()
    {
        EnsureContextValid();var world=new ControlledVoxelWorld{MapSizeY=36};
        using var runtime=new SurfaceCacheRuntimeFixture(exposedWall:true,fallbackWorld:world);
        runtime.PrimeGeometry();runtime.RunUntil(runtime.AllRequestedLightingReady);
        Assert.True(runtime.TryGetLighting(out var initial));
        Assert.True(runtime.Feedback.TryGetNearDispatchState(out _,out _,out var pages,out _));uint page=pages.Single().Key;
        using var before=SurfaceLightingPageReadback.Read(initial.OutgoingRadiance,initial,page);
        runtime.TransformVoxel=(x,y,z,voxel)=>x>=1?voxel with{Geometry=3u}:voxel;
        runtime.InvalidateGeometry();
        var renderer=(LumonSceneRelightUpdateRenderer)runtime.LightingProvider;
        runtime.RunUntil(()=>Counter(renderer,"fallbackCommitted")>0,256);
        Assert.True(Counter(renderer,"fallbackAdmitted")>0);Assert.Empty(world.LightQueries);
        Assert.True(runtime.TryGetLighting(out var current));
        using var after=SurfaceLightingPageReadback.Read(current.OutgoingRadiance,current,page);
        for(int index=3;index<after.Length;index+=4)Assert.Equal(1,after.Span[index]);
        Assert.Equal(before.Span.ToArray(),after.Span.ToArray());
    }

    /// <summary>CPU collision hits resolve actual cached radiance through the asynchronous query and normal commit path.</summary>
    [Fact]
    public void UnsupportedRoomAirResolvesCapturedWallRadiance()
    {
        EnsureContextValid();var world=new ControlledVoxelWorld{MapSizeY=256};
        using var runtime=new SurfaceCacheRuntimeFixture(requestedPages:24,enclosure:true,fallbackWorld:world);
        world.AddRoom((0,32,0),(7,39,7),materialId:runtime.SourceBlock.Id);
        runtime.Config.LumOn.LumonScene.RelightSeedPagesPerFrame = runtime.Config.LumOn.LumonScene.RelightDirectPagesPerFrame = runtime.Config.LumOn.LumonScene.RelightIndirectPagesPerFrame = 4;
        runtime.PrimeGeometry();runtime.RunUntil(runtime.AllRequestedLightingReady,256);
        runtime.TransformVoxel=(x,y,z,voxel)=>(voxel.Geometry&3u)==1?voxel with{Geometry=3u}:voxel;
        runtime.InvalidateGeometry();var renderer=(LumonSceneRelightUpdateRenderer)runtime.LightingProvider;
        runtime.RunUntil(()=>Counter(renderer,"fallbackCommitted")>0,512);
        Assert.Empty(world.LightQueries);Assert.True(runtime.TryGetLighting(out var lighting));
        Assert.True(runtime.Feedback.TryGetNearDispatchState(out _,out _,out var pages,out _));
        bool nonzero=false;
        foreach(uint page in pages.Keys)
        {
            using var pixels=SurfaceLightingPageReadback.Read(lighting.IndirectIrradiance,lighting,page);
            for(int index=0;index<pixels.Length;index+=4)nonzero|=pixels.Span[index]>.01f&&pixels.Span[index+3]>0;
        }
        Assert.True(nonzero);
    }

    /// <summary>A geometry invalidation rejects delayed terrain work before it can overwrite retained displayed lighting.</summary>
    [Fact]
    public void GeometryEditRejectsBlockedFallbackCompletion()
    {
        EnsureContextValid();using var entered=new ManualResetEventSlim();using var release=new ManualResetEventSlim();
        var world=new ControlledVoxelWorld{MapSizeY=36,IsLoaded=_=>
        { entered.Set();release.Wait(TestContext.Current.CancellationToken);return true; }};
        using var runtime=new SurfaceCacheRuntimeFixture(exposedWall:true,fallbackWorld:world);
        runtime.PrimeGeometry();runtime.RunUntil(runtime.AllRequestedLightingReady);
        runtime.TransformVoxel=(x,y,z,voxel)=>x>=1?voxel with{Geometry=3u}:voxel;
        runtime.InvalidateGeometry();var renderer=(LumonSceneRelightUpdateRenderer)runtime.LightingProvider;
        try
        {
            runtime.RunUntil(()=>entered.IsSet,256);long committed=Counter(renderer,"fallbackCommitted");
            long rejected=Counter(renderer,"fallbackRejected");runtime.InvalidateGeometry();
            runtime.RunUntil(()=>Counter(renderer,"fallbackRejected")>rejected,32);
            Assert.Equal(committed,Counter(renderer,"fallbackCommitted"));Assert.True(runtime.TryGetLighting(out _));
            release.Set();runtime.Frame();Assert.Equal(committed,Counter(renderer,"fallbackCommitted"));
        }
        finally { release.Set(); }
    }

    /// <summary>Atlas replacement and world teardown cannot publish an old worker result into a different resource lifetime.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ResourceResetRetiresBlockedFallback(bool leaveWorld)
    {
        EnsureContextValid();using var entered=new ManualResetEventSlim();using var release=new ManualResetEventSlim();
        var world=new ControlledVoxelWorld{MapSizeY=36,IsLoaded=_=>
        { entered.Set();release.Wait(TestContext.Current.CancellationToken);return true; }};
        using var runtime=new SurfaceCacheRuntimeFixture(exposedWall:true,fallbackWorld:world);
        runtime.PrimeGeometry();runtime.RunUntil(runtime.AllRequestedLightingReady);
        runtime.TransformVoxel=(x,y,z,voxel)=>x>=1?voxel with{Geometry=3u}:voxel;
        runtime.InvalidateGeometry();var renderer=(LumonSceneRelightUpdateRenderer)runtime.LightingProvider;
        try
        {
            runtime.RunUntil(()=>entered.IsSet,256);long committed=Counter(renderer,"fallbackCommitted");
            if(leaveWorld)runtime.LeaveWorld();
            else { runtime.RequestAtlasRecreation();runtime.Frame(); }
            Assert.Equal(committed,Counter(renderer,"fallbackCommitted"));
            release.Set();
            if(leaveWorld)Assert.False(runtime.TryGetLighting(out _));
            else { runtime.Frame();Assert.Equal(committed,Counter(renderer,"fallbackCommitted")); }
        }
        finally { release.Set(); }
    }
    #endregion

    #region Observations
    /// <summary>Reads bounded production counters without inspecting private pipeline ownership.</summary>
    private static long Counter(LumonSceneRelightUpdateRenderer renderer,string name)
    {
        Assert.True(renderer.TryGetSelfCheckLine(out string line));
        var match=Regex.Match(line,$@"\b{name}:(\d+)");Assert.True(match.Success,line);
        return long.Parse(match.Groups[1].Value);
    }
    #endregion
}
