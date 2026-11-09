using VanillaGraphicsExpanded.LumOn.Scene;
using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.Tests.Support.Lighting;

namespace VanillaGraphicsExpanded.Tests.Component.LumOn;

/// <summary>Exercises bounded lighting refresh without replacing captured surface identities or displayed storage.</summary>
[Collection("NearFieldMaterialCapture")]
[Trait("Category", "GpuIntegration")]
public sealed class SurfaceLightingRefreshTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{

    #region Public API
    #region Resident refresh
    /// <summary>Disabling indirect work leaves initial direct seeding and subsequent light refresh active.</summary>
    [Fact]
    [Trait("Scenario", "HeadlessRuntime")]
    public void ZeroIndirectBudgetPreservesDirectProgressAndLiveBudgetIdentity()
    {
        // Arrange a live producer with indirect work disabled and retain the mutable budget object identity.
        EnsureContextValid();
        using var runtime = new SurfaceCacheRuntimeFixture(ImageAssertions, exposedWall:true);
        var config = runtime.Config.LumOn.LumonScene;
        config.RelightIndirectPagesPerFrame = 0;
        runtime.PrimeGeometry(); runtime.RunUntil(runtime.AllRequestedLightingReady);
        Assert.True(runtime.TryGetLighting(out var initial));
        Assert.True(runtime.Feedback.TryGetNearDispatchState(out _,out _,out var mapping,out _));
        uint page = mapping.Keys.Single(); long capture = runtime.Feedback.GetCaptureRevision(page);
        Assert.True(HasDirect(runtime,page,32)); Assert.Equal(0,IndirectWeight(runtime,page));
        // Act through source removal and restoration using the same direct-only work budget.
        foreach (int light in new[] { 0, 32 })
        {
            config.RelightSeedPagesPerFrame = 0;
            config.RelightDirectPagesPerFrame = light == 0 ? 2 : 1;
            runtime.ChangeBlockLight(light);
            runtime.RunUntil(() => HasDirect(runtime,page,light));
            // Assert direct refresh stays live while indirect history and storage identities remain unchanged.
            Assert.True(runtime.TryGetLighting(out var current));
            Assert.Same(initial.DirectIrradiance,current.DirectIrradiance);
            Assert.True(initial.OutgoingRadiance.IsValid);
            Assert.Equal(initial.DependencyRevision,current.DependencyRevision);
            Assert.Equal(capture,runtime.Feedback.GetCaptureRevision(page));
            Assert.Equal(0,IndirectWeight(runtime,page));
        }
    }

    /// <summary>Live history-limit edits reach the producer without replacing storage, captures, or published lighting.</summary>
    [Fact]
    [Trait("Scenario", "HeadlessRuntime")]
    public void HistoryLimitChangesPreservePublishedSurfaceIdentity()
    {
        // Arrange populated surface and lighting pages under a live cache producer.
        EnsureContextValid();
        using var runtime=new SurfaceCacheRuntimeFixture(ImageAssertions, spatial:new SpatialLightingScene { Reflectance=.25f });
        runtime.Config.LumOn.LumonScene.RelightSeedPagesPerFrame = runtime.Config.LumOn.LumonScene.RelightDirectPagesPerFrame = runtime.Config.LumOn.LumonScene.RelightIndirectPagesPerFrame = 4;
        runtime.PrimeGeometry(); runtime.RunUntil(runtime.AllRequestedLightingReady);
        Assert.True(runtime.Feedback.TryGetNearDispatchState(out _,out _,out var mapping,out _));
        uint page=mapping.Keys.First();
        runtime.RunUntil(()=>IndirectWeight(runtime,page)>=4,maximumFrames:256);
        Assert.True(runtime.TryGetLighting(out var original));
        long capture=runtime.Feedback.GetCaptureRevision(page);
        // Act through live history-limit changes while normal producer frames continue.
        foreach(int cap in new[]{8,1})
        {
            runtime.Config.LumOn.LumonScene.RelightMaxFramesAccumulated=cap;
            bool reached=false;
            for(int frame=0;frame<256 && !reached;frame++)
            {
                runtime.Frame();
                // Assert the updated limit is observed without replacing cache, capture, or published storage.
                Assert.True(runtime.TryGetLighting(out var current));
                Assert.Same(original.IndirectIrradiance,current.IndirectIrradiance);
                Assert.Equal(original.DependencyRevision,current.DependencyRevision);
                Assert.Equal(capture,runtime.Feedback.GetCaptureRevision(page));
                using var outgoing=SurfaceLightingPageSelection.Capture(current.OutgoingRadiance,current,page);
                ImageAssertions.Comparisons.CompareConstant(outgoing.Texture, 1, channelMask: 8).AssertEqual();
                reached=IndirectWeight(runtime,page)==cap;
            }
            Assert.True(reached,"Runtime history configuration did not reach the retained producer history.");
        }
    }

    /// <summary>A light edit in one source chunk cannot prevent successful indirect work in an unchanged neighboring chunk.</summary>
    [Fact]
    [Trait("Scenario", "HeadlessRuntime")]
    public void RemoteResidentContinuesIndirectRefreshAfterAnotherChunkChanges()
    {
        // Arrange two resident chunks and retain the unchanged neighbor's page and work counter.
        EnsureContextValid();
        var spatial=new SpatialLightingScene { Reflectance=.25f };
        using var runtime=new SurfaceCacheRuntimeFixture(ImageAssertions, spatial:spatial);
        runtime.Config.LumOn.LumonScene.RelightSeedPagesPerFrame = runtime.Config.LumOn.LumonScene.RelightDirectPagesPerFrame = runtime.Config.LumOn.LumonScene.RelightIndirectPagesPerFrame = 4;
        runtime.PrimeGeometry();runtime.RunUntil(runtime.AllRequestedLightingReady);
        Assert.True(runtime.Feedback.TryGetNearDispatchState(out _,out _,out var mapping,out _));
        Assert.True(runtime.Feedback.TryGetNearChunkSlotAndGeneration(new(-1,1,0),out uint remoteSlot,out _));
        uint[] remote=mapping.Where(pair=>LumonSceneVirtualPageKeyUtil.UnpackChunkSlot(pair.Value)==remoteSlot).Select(pair=>pair.Key).ToArray();
        Assert.NotEmpty(remote);
        runtime.RunUntil(()=>remote.Any(page=>IndirectWeight(runtime,page)>=runtime.Config.LumOn.LumonScene.RelightMaxFramesAccumulated),maximumFrames:256);
        for(int frame=0;frame<160;frame++) runtime.Frame();
        uint page=remote.OrderByDescending(id=>IndirectBrightness(runtime,id)).First();
        float before=IndirectBrightness(runtime,page);
        Assert.True(before>.1f);
        long capture=runtime.Feedback.GetCaptureRevision(page);
        Assert.True(runtime.TryGetLighting(out var original));
        // Remove the authored light from both published encodings, not only its legacy level word.
        // Act on the changed source and let the remote page continue through its normal refresh schedule.
        runtime.TransformVoxel=(x,y,z,voxel)=>x>=0 ? voxel with
        {
            LegacyLight=LumonSceneOccupancyPacking.Pack(0,0,0,LumonSceneOccupancyPacking.UnpackMaterialPaletteIndex(voxel.LegacyLight)),
            NormalizedLight=0
        } : voxel;
        var edited=VanillaGraphicsExpanded.Voxels.ChunkProcessing.ChunkKey.FromChunkCoords(0,1,0);
        foreach(var source in runtime.Sources.Where(source=>!source.Disposed)) source.MarkDirty(edited);
        runtime.RunUntil(()=>IndirectBrightness(runtime,page)<before*.9f,maximumFrames:256);
        // Assert source refresh changes remote indirect energy but does not revise its captured surface identity.
        Assert.Equal(capture,runtime.Feedback.GetCaptureRevision(page));
        Assert.True(runtime.TryGetLighting(out var current));
        Assert.Equal(original.DependencyRevision,current.DependencyRevision);
    }

    /// <summary>Refreshing exterior occupancy revisits hidden faces without recapturing their unchanged owning patch.</summary>
    [Fact]
    [Trait("Scenario", "HeadlessRuntime")]
    public void ExteriorOcclusionCanHideAndRevealTheSameCapture()
    {
        // Arrange a captured interior surface whose exterior occupancy controls visibility.
        EnsureContextValid();
        using var runtime=new SurfaceCacheRuntimeFixture(ImageAssertions, exposedWall:true);
        runtime.PrimeGeometry();runtime.RunUntil(runtime.AllRequestedLightingReady);
        Assert.True(runtime.Feedback.TryGetNearDispatchState(out _,out _,out var mapping,out _));
        uint page=mapping.Keys.Single();
        long revision=runtime.Feedback.GetCaptureRevision(page);
        // Act through repeated exterior hide/reveal transitions on the same captured page.
        foreach(bool hidden in new[]{true,false,true})
        {
            runtime.TransformVoxel=hidden ? (x,y,z,voxel)=>x==1 ? voxel with {Geometry=2u|((uint)runtime.BlockId<<2)} : voxel : null;
            runtime.InvalidateGeometry();
            runtime.RunUntil(()=>HasDirect(runtime,page,hidden?0:32,hidden?2:1),maximumFrames:64);
            // Assert the same page refreshes visibility without recapture.
            Assert.Equal(revision,runtime.Feedback.GetCaptureRevision(page));
        }
    }

    /// <summary>Repeated unavailable rounds cannot restart page selection and starve later resident pages.</summary>
    [Fact]
    [Trait("Scenario", "HeadlessRuntime")]
    public void RepeatedDirtyAvailabilityCyclesRefreshEveryPage()
    {
        // Arrange multiple captured pages while dirty and unavailable states can interrupt page selection.
        EnsureContextValid();
        using var runtime=new SurfaceCacheRuntimeFixture(ImageAssertions, requestedPages:4,exposedWall:true);
        runtime.Config.LumOn.LumonScene.RelightTexelsPerPagePerFrame=4;
        runtime.PrimeGeometry();runtime.RunUntil(runtime.AllRequestedLightingReady);
        Assert.True(runtime.Feedback.TryGetNearDispatchState(out _,out _,out var mapping,out _));
        uint[] pages=mapping.Keys.ToArray();
        runtime.ChangeBlockLight(0);
        bool complete=false;
        // Act through bounded dirty and unavailable rounds until every resident page receives refreshed direct data.
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
            Assert.InRange(int.Parse(match.Groups[1].Value),0,3);
            complete=pages.All(page=>HasDirect(runtime,page,0));
        }
        // Assert intermittent readiness never starves a later page.
        Assert.True(complete,"Repeated readiness changes starved at least one resident page.");
    }

    /// <summary>Small page and texel budgets eventually refresh every resident page through repeated light changes.</summary>
    [Theory]
    [InlineData(4)] [InlineData(16)]
    [Trait("Scenario", "HeadlessRuntime")]
    public void LightChangesRefreshAllResidentsWithoutRecapture(int texels)
    {
        // Arrange resident captures with the selected small per-frame texel budget.
        EnsureContextValid();
        using var runtime=new SurfaceCacheRuntimeFixture(ImageAssertions, requestedPages:2,exposedWall:true);
        runtime.Config.LumOn.LumonScene.RelightTexelsPerPagePerFrame=texels;
        runtime.PrimeGeometry();runtime.RunUntil(runtime.AllRequestedLightingReady);
        Assert.True(runtime.TryGetLighting(out var original));
        var captureAtlas=runtime.IrradianceAtlas();
        Assert.True(runtime.Feedback.TryGetNearDispatchState(out _,out _,out var mapping,out _));
        var revisions=mapping.Keys.ToDictionary(page=>page,page=>runtime.Feedback.GetCaptureRevision(page));
        // Act through removal/restoration/removal with the selected small texel budget.
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
                    // Assert page identities and atlas storage stay fixed while direct values refresh.
                    Assert.Equal(pair.Value,runtime.Feedback.GetCaptureRevision(pair.Key));
                    using var outgoing=SurfaceLightingPageSelection.Capture(current.OutgoingRadiance,current,pair.Key);
                    ImageAssertions.Comparisons.CompareConstant(outgoing.Texture, 1, channelMask: 8).AssertEqual();
                    completed &= HasDirect(runtime,pair.Key,light);
                }
            }
            Assert.True(completed,"Resident direct refresh exceeded the bounded frame allowance.");
        }
    }

    /// <summary>Unavailable neighboring lighting retains valid texels and recovers without recapturing unchanged surfaces.</summary>
    [Fact]
    [Trait("Scenario", "HeadlessRuntime")]
    public void UnresolvedRefreshRetainsPreviousDirectAndOutgoing()
    {
        // Arrange a lit surface, then make its neighbor unavailable so a refresh cannot resolve every sample.
        EnsureContextValid();
        using var runtime=new SurfaceCacheRuntimeFixture(ImageAssertions, exposedWall:true);
        runtime.PrimeGeometry();runtime.RunUntil(runtime.AllRequestedLightingReady);
        Assert.True(runtime.TryGetLighting(out var initial));
        Assert.True(runtime.Feedback.TryGetNearDispatchState(out _,out _,out var mapping,out _));
        uint page=mapping.Keys.Single();
        long revision=runtime.Feedback.GetCaptureRevision(page);
        using var before=SurfaceLightingPageSelection.Capture(initial.DirectIrradiance,initial,page);
        runtime.TransformVoxel=(x,y,z,voxel)=>x==1 ? voxel with {Geometry=3} : voxel;
        // Act through an unresolved refresh while neighboring geometry remains unavailable.
        runtime.ChangeBlockLight(0);
        for(int frame=0;frame<32;frame++)
        {
            runtime.Frame();
            // Assert unresolved samples preserve the previously published direct page and outgoing validity.
            Assert.True(runtime.TryGetLighting(out var current));
            using var direct=SurfaceLightingPageSelection.Capture(current.DirectIrradiance,current,page);
            ImageAssertions.Comparisons.CompareHistory(direct.Texture, before).AssertEqual();
            using var outgoing=SurfaceLightingPageSelection.Capture(current.OutgoingRadiance,current,page);
            ImageAssertions.Comparisons.CompareConstant(outgoing.Texture, 1, channelMask: 8).AssertEqual();
            Assert.True(ImageAssertions.Statistics.Inspect(outgoing.Texture, channelMask: 1).Minimum > .001f);
        }
        // Act by restoring neighbor geometry and waiting for its direct refresh to complete.
        runtime.TransformVoxel=null;runtime.InvalidateGeometry();
        runtime.RunUntil(()=>HasDirect(runtime,page,0),maximumFrames:96);
        // Assert recovery retains the originally captured surface.
        Assert.Equal(revision,runtime.Feedback.GetCaptureRevision(page));
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
        programs.Prepare("statistics", 0, 0);
    }
    #endregion

    #region Private
    #region Observations
    /// <summary>Observes indirect energy independently of capped effective temporal weight.</summary>
    private static float IndirectBrightness(SurfaceCacheRuntimeFixture runtime,uint page)
    {
        if(!runtime.TryGetLighting(out var current)) return 0;
        using var indirect=SurfaceLightingPageSelection.Capture(current.IndirectIrradiance,current,page);
        var result = runtime.ImageAssertions.Statistics.Statistics(indirect.Texture, channelMask: 1);
        result.AssertFiniteRange();
        return (float)(result.Sum(0) / (indirect.Texture.Width * indirect.Texture.Height));
    }

    /// <summary>Observes completed indirect samples on the selected physical page.</summary>
    private static float IndirectWeight(SurfaceCacheRuntimeFixture runtime,uint page)
    {
        if(!runtime.TryGetLighting(out var current)) return 0;
        using var indirect=SurfaceLightingPageSelection.Capture(current.IndirectIrradiance,current,page);
        var result = runtime.ImageAssertions.Statistics.Inspect(indirect.Texture, channelMask: 8);
        result.AssertFiniteRange(); return (float)Math.Max(0, result.Maximum);
    }

    /// <summary>Checks physical direct irradiance after decoding persistent RGB, retaining unscaled validity.</summary>
    private static bool HasDirect(SurfaceCacheRuntimeFixture runtime,uint page,float expected,float validity=1)
    {
        if(!runtime.TryGetLighting(out var current)) return false;
        using var direct=SurfaceLightingPageSelection.Capture(current.DirectIrradiance,current,page);
        return runtime.ImageAssertions.Comparisons.CompareConstant(direct.Texture, validity, channelMask: 8).Mismatches == 0 &&
            runtime.ImageAssertions.Comparisons.CompareConstant(direct.Texture, expected * LightingStorage.PersistentScale,
                channelMask: 7, tolerance: .01f * LightingStorage.PersistentScale).Mismatches == 0;

    }
    #endregion
    #endregion
}
