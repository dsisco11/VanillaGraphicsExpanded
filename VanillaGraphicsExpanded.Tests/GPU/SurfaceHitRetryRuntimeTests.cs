using System.Text.RegularExpressions;
using VanillaGraphicsExpanded.LumOn.Scene;
using VanillaGraphicsExpanded.Tests.Fixtures.WorldProbes;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks retained hit retries across production feedback, capture, initial seeding and publication.</summary>
[Collection("NearFieldMaterialCapture")]
[Trait("Category","GPU")]
public sealed class SurfaceHitRetryRuntimeTests(HeadlessGLFixture fixture):RenderTestBase(fixture)
{
    #region Missing page progress
    /// <summary>Missing hit residency can later seed and wake retained geometry from either trace backend.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void NewlyResidentHitPagesWakeRetainedGeometry(bool cpuFallback)
    {
        EnsureContextValid();var world=cpuFallback?new ControlledVoxelWorld{MapSizeY=256}:null;
        using var runtime=new SurfaceCacheRuntimeFixture(requestedPages:24,enclosure:true,fallbackWorld:world);
        world?.AddRoom((0,32,0),(7,39,7),materialId:runtime.SourceBlock.Id);
        runtime.Config.LumOn.LumonScene.RelightMaxPagesPerFrame=4;
        runtime.VisibleFeedbackPages=1;
        // Keep the adjacent air used by direct seeding supported; only interior ray segments require CPU geometry.
        if(cpuFallback)runtime.TransformVoxel=(x,y,z,voxel)=>x>=2&&x<=5&&y>=34&&y<=37&&z>=2&&z<=5&&(voxel.Geometry&3u)==1?voxel with{Geometry=3u}:voxel;
        runtime.PrimeGeometry();var renderer=(LumonSceneRelightUpdateRenderer)runtime.LightingProvider;
        runtime.RunUntil(()=>Counter(renderer,cpuFallback?"hitCpuRetained":"hitPending")>0,256);
        Assert.True(runtime.TryGetLighting(out _));
        string completion=cpuFallback?"hitCpuCommitted":"hitCommitted";
        long committed=Counter(renderer,completion);
        runtime.VisibleFeedbackPages=24;
        runtime.RunUntil(()=>runtime.AllRequestedLightingReady()&&Counter(renderer,completion)>committed,512);
        Assert.True(runtime.TryGetLighting(out var lighting));
        Assert.True(runtime.Feedback.TryGetNearDispatchState(out _,out _,out var pages,out _));
        bool nonzero=false;
        foreach(uint page in pages.Keys)
        {
            using var pixels=SurfaceLightingPageReadback.Read(lighting.IndirectIrradiance,lighting,page);
            for(int index=0;index<pixels.Length;index+=4)nonzero|=pixels.Span[index]>.01f&&pixels.Span[index+3]>0;
        }
        Assert.True(nonzero);if(world!=null){Assert.Empty(world.LightQueries);Assert.True(Counter(renderer,"fallbackAdmitted")>0);}
    }

    /// <summary>A resource replacement cannot commit retained descriptors from the previous atlas lifetime.</summary>
    [Theory]
    [InlineData("atlas")] [InlineData("world")] [InlineData("geometry")]
    public void ResourceResetRetiresMissingHitSamples(string invalidation)
    {
        EnsureContextValid();using var runtime=new SurfaceCacheRuntimeFixture(requestedPages:24,enclosure:true);
        runtime.VisibleFeedbackPages=1;runtime.PrimeGeometry();var renderer=(LumonSceneRelightUpdateRenderer)runtime.LightingProvider;
        runtime.RunUntil(()=>Counter(renderer,"hitPending")>0,256);
        long committed=Counter(renderer,"hitCommitted"),rejected=Counter(renderer,"hitRejected");
        if(invalidation=="world")runtime.LeaveWorld();
        else if(invalidation=="atlas"){runtime.RequestAtlasRecreation();runtime.Frame();}
        else
        {
            runtime.InvalidateGeometry();
            // Source invalidation becomes a GPU scene revision through bounded upload callbacks, not immediately.
            runtime.RunUntil(()=>Counter(renderer,"hitRejected")>rejected,64);
        }
        if(invalidation!="geometry")Assert.Equal(0,Counter(renderer,"hitPending"));
        Assert.Equal(committed,Counter(renderer,"hitCommitted"));
        if(invalidation=="world")Assert.False(runtime.TryGetLighting(out _));
    }
    #endregion

    #region Observations
    /// <summary>Reads public production counters without observing private retained queue ownership.</summary>
    private static long Counter(LumonSceneRelightUpdateRenderer renderer,string name)
    {
        Assert.True(renderer.TryGetSelfCheckLine(out string line));
        var match=Regex.Match(line,$@"\b{name}:(\d+)");Assert.True(match.Success,line);
        return long.Parse(match.Groups[1].Value);
    }
    #endregion
}
