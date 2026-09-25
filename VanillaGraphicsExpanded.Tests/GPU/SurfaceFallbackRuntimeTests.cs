using System.Text.RegularExpressions;
using VanillaGraphicsExpanded.LumOn.Scene;
using VanillaGraphicsExpanded.Tests.Fixtures.WorldProbes;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks fallback completion through actual renderer scheduling, collision traversal and publication.</summary>
[Collection("NearFieldMaterialCapture")]
[Trait("Category","GPU")]
public sealed class SurfaceFallbackRuntimeTests(HeadlessGLFixture fixture):RenderTestBase(fixture)
{
    #region Runtime completion
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
        runtime.Config.LumOn.LumonScene.RelightMaxPagesPerFrame=4;
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
