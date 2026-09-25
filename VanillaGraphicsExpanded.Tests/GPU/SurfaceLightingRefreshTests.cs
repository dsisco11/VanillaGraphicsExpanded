using VanillaGraphicsExpanded.LumOn.Scene;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Exercises bounded lighting refresh without replacing captured surface identities or displayed storage.</summary>
[Collection("NearFieldMaterialCapture")]
[Trait("Category","GPU")]
public sealed class SurfaceLightingRefreshTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Resident refresh
    /// <summary>A light edit in one source chunk cannot prevent successful indirect work in an unchanged neighboring chunk.</summary>
    [Fact]
    public void RemoteResidentContinuesIndirectRefreshAfterAnotherChunkChanges()
    {
        EnsureContextValid();
        var spatial=new SpatialLightingScene();
        using var runtime=new SurfaceCacheRuntimeFixture(spatial:spatial);
        runtime.Config.LumOn.LumonScene.RelightMaxPagesPerFrame=4;
        runtime.PrimeGeometry();runtime.RunUntil(runtime.AllRequestedLightingReady);
        Assert.True(runtime.Feedback.TryGetNearDispatchState(out _,out _,out var mapping,out _));
        Assert.True(runtime.Feedback.TryGetNearChunkSlotAndGeneration(new(-1,1,0),out uint remoteSlot,out _));
        uint[] remote=mapping.Where(pair=>LumonSceneVirtualPageKeyUtil.UnpackChunkSlot(pair.Value)==remoteSlot).Select(pair=>pair.Key).ToArray();
        Assert.NotEmpty(remote);
        runtime.RunUntil(()=>remote.Any(page=>IndirectWeight(runtime,page)>1),maximumFrames:160);
        uint page=remote.OrderByDescending(id=>IndirectWeight(runtime,id)).First();
        float before=IndirectWeight(runtime,page);
        long capture=runtime.Feedback.GetCaptureRevision(page);
        Assert.True(runtime.TryGetLighting(out var original));
        runtime.TransformVoxel=(x,y,z,voxel)=>x>=0 ? voxel with {LegacyLight=0} : voxel;
        var edited=VanillaGraphicsExpanded.Voxels.ChunkProcessing.ChunkKey.FromChunkCoords(0,1,0);
        foreach(var source in runtime.Sources.Where(source=>!source.Disposed)) source.MarkDirty(edited);
        runtime.RunUntil(()=>IndirectWeight(runtime,page)>before,maximumFrames:160);
        Assert.Equal(capture,runtime.Feedback.GetCaptureRevision(page));
        Assert.True(runtime.TryGetLighting(out var current));
        Assert.Equal(original.DependencyRevision,current.DependencyRevision);
    }

    /// <summary>Refreshing exterior occupancy revisits hidden faces without recapturing their unchanged owning patch.</summary>
    [Fact]
    public void ExteriorOcclusionCanHideAndRevealTheSameCapture()
    {
        EnsureContextValid();
        using var runtime=new SurfaceCacheRuntimeFixture(exposedWall:true);
        runtime.PrimeGeometry();runtime.RunUntil(runtime.AllRequestedLightingReady);
        Assert.True(runtime.Feedback.TryGetNearDispatchState(out _,out _,out var mapping,out _));
        uint page=mapping.Keys.Single();
        long revision=runtime.Feedback.GetCaptureRevision(page);
        foreach(bool hidden in new[]{true,false,true})
        {
            runtime.TransformVoxel=hidden ? (x,y,z,voxel)=>x==1 ? voxel with {Geometry=2u|((uint)runtime.BlockId<<2)} : voxel : null;
            runtime.InvalidateGeometry();
            runtime.RunUntil(()=>HasDirect(runtime,page,hidden?0:32,hidden?2:1),maximumFrames:64);
            Assert.Equal(revision,runtime.Feedback.GetCaptureRevision(page));
        }
    }

    /// <summary>Repeated unavailable rounds cannot restart page selection and starve later resident pages.</summary>
    [Fact]
    public void RepeatedDirtyAvailabilityCyclesRefreshEveryPage()
    {
        EnsureContextValid();
        using var runtime=new SurfaceCacheRuntimeFixture(requestedPages:4,exposedWall:true);
        runtime.Config.LumOn.LumonScene.RelightTexelsPerPagePerFrame=4;
        runtime.PrimeGeometry();runtime.RunUntil(runtime.AllRequestedLightingReady);
        Assert.True(runtime.Feedback.TryGetNearDispatchState(out _,out _,out var mapping,out _));
        uint[] pages=mapping.Keys.ToArray();
        runtime.ChangeBlockLight(0);
        bool complete=false;
        for(int cycle=0;cycle<48 && !complete;cycle++)
        {
            runtime.GeometryAvailable=false;runtime.InvalidateGeometry();
            for(int frame=0;frame<3;frame++) runtime.Frame();
            Assert.All(pages,page=>Assert.False(runtime.Feedback.IsCaptureAvailable(page)));
            runtime.GeometryAvailable=true;runtime.InvalidateGeometry();
            runtime.RunUntil(()=>pages.All(runtime.Feedback.IsCaptureAvailable));
            ((LumonSceneRelightUpdateRenderer)runtime.LightingProvider).TryGetSelfCheckLine(out string state);
            var match=System.Text.RegularExpressions.Regex.Match(state,@"pages:(\d+)");
            Assert.True(match.Success,state);
            Assert.InRange(int.Parse(match.Groups[1].Value),0,1);
            complete=pages.All(page=>HasDirect(runtime,page,0));
        }
        Assert.True(complete,"Repeated readiness changes starved at least one resident page.");
    }

    /// <summary>Small page and texel budgets eventually refresh every resident page through repeated light changes.</summary>
    [Theory]
    [InlineData(4)] [InlineData(16)]
    public void LightChangesRefreshAllResidentsWithoutRecapture(int texels)
    {
        EnsureContextValid();
        using var runtime=new SurfaceCacheRuntimeFixture(requestedPages:2,exposedWall:true);
        runtime.Config.LumOn.LumonScene.RelightTexelsPerPagePerFrame=texels;
        runtime.PrimeGeometry();runtime.RunUntil(runtime.AllRequestedLightingReady);
        Assert.True(runtime.TryGetLighting(out var original));
        var captureAtlas=runtime.IrradianceAtlas();
        Assert.True(runtime.Feedback.TryGetNearDispatchState(out _,out _,out var mapping,out _));
        var revisions=mapping.Keys.ToDictionary(page=>page,page=>runtime.Feedback.GetCaptureRevision(page));
        foreach(int light in new[]{0,32,0})
        {
            runtime.ChangeBlockLight(light);
            bool completed=false;
            for(int frame=0;frame<96 && !completed;frame++)
            {
                runtime.Frame();
                Assert.True(runtime.TryGetLighting(out var current));
                Assert.Same(captureAtlas,runtime.IrradianceAtlas());
                Assert.Equal(original.DependencyRevision,current.DependencyRevision);
                completed=true;
                foreach(var pair in revisions)
                {
                    Assert.Equal(pair.Value,runtime.Feedback.GetCaptureRevision(pair.Key));
                    using var outgoing=SurfaceLightingPageReadback.Read(current.OutgoingRadiance,current,pair.Key);
                    for(int index=3;index<outgoing.Length;index+=4) Assert.Equal(1,outgoing.Span[index]);
                    completed &= HasDirect(runtime,pair.Key,light);
                }
            }
            Assert.True(completed,"Resident direct refresh exceeded the bounded frame allowance.");
        }
    }

    /// <summary>Unavailable neighboring lighting retains valid texels and recovers without recapturing unchanged surfaces.</summary>
    [Fact]
    public void UnresolvedRefreshRetainsPreviousDirectAndOutgoing()
    {
        EnsureContextValid();
        using var runtime=new SurfaceCacheRuntimeFixture(exposedWall:true);
        runtime.PrimeGeometry();runtime.RunUntil(runtime.AllRequestedLightingReady);
        Assert.True(runtime.TryGetLighting(out var initial));
        Assert.True(runtime.Feedback.TryGetNearDispatchState(out _,out _,out var mapping,out _));
        uint page=mapping.Keys.Single();
        long revision=runtime.Feedback.GetCaptureRevision(page);
        using var before=SurfaceLightingPageReadback.Read(initial.DirectIrradiance,initial,page);
        runtime.TransformVoxel=(x,y,z,voxel)=>x==1 ? voxel with {Geometry=3} : voxel;
        runtime.ChangeBlockLight(0);
        for(int frame=0;frame<32;frame++)
        {
            runtime.Frame();
            Assert.True(runtime.TryGetLighting(out var current));
            using var direct=SurfaceLightingPageReadback.Read(current.DirectIrradiance,current,page);
            Assert.True(before.Span.SequenceEqual(direct.Span));
            using var outgoing=SurfaceLightingPageReadback.Read(current.OutgoingRadiance,current,page);
            for(int index=0;index<outgoing.Length;index+=4)
            {
                Assert.Equal(1,outgoing.Span[index+3]);
                Assert.True(outgoing.Span[index]>.001f);
            }
        }
        runtime.TransformVoxel=null;runtime.InvalidateGeometry();
        runtime.RunUntil(()=>HasDirect(runtime,page,0),maximumFrames:96);
        Assert.Equal(revision,runtime.Feedback.GetCaptureRevision(page));
    }
    #endregion

    #region Observations
    /// <summary>Observes completed indirect samples on the selected physical page.</summary>
    private static float IndirectWeight(SurfaceCacheRuntimeFixture runtime,uint page)
    {
        if(!runtime.TryGetLighting(out var current)) return 0;
        using var indirect=SurfaceLightingPageReadback.Read(current.IndirectIrradiance,current,page);
        float maximum=0;
        for(int index=3;index<indirect.Length;index+=4) maximum=Math.Max(maximum,indirect.Span[index]);
        return maximum;
    }

    /// <summary>Checks exact initialized direct values independently of retained indirect history.</summary>
    private static bool HasDirect(SurfaceCacheRuntimeFixture runtime,uint page,float expected,float validity=1)
    {
        if(!runtime.TryGetLighting(out var current)) return false;
        using var direct=SurfaceLightingPageReadback.Read(current.DirectIrradiance,current,page);
        for(int index=0;index<direct.Length;index+=4)
        {
            if(direct.Span[index+3]!=validity) return false;
            for(int channel=0;channel<3;channel++)
                if(Math.Abs(direct.Span[index+channel]-expected)>.01f) return false;
        }
        return true;
    }
    #endregion
}
