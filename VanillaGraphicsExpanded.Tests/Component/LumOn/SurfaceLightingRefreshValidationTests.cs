using VanillaGraphicsExpanded.LumOn.Scene;
using VanillaGraphicsExpanded.Voxels.ChunkProcessing;
using Assertions = VanillaGraphicsExpanded.Tests.Support.GPU.Observations;
using VanillaGraphicsExpanded.Tests.Support.Lighting;

namespace VanillaGraphicsExpanded.Tests.Component.LumOn;

/// <summary>Checks settled lighting transport through retained pages after localized source edits.</summary>
[Collection("NearFieldMaterialCapture")]
[Trait("Category", "GpuIntegration")]
public sealed class SurfaceLightingRefreshValidationTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{

    #region Public API
    #region Settled refresh
    /// <summary>A recycled physical page rejects its bright old contents until a different dark surface is captured and initialized.</summary>
    [Fact]
    [Trait("Scenario", "HeadlessRuntime")]
    public void ReusedPhysicalPageCannotPublishOldLightingBeforeCapture()
    {
        // Arrange an illuminated page, then set up an uncaptured replacement surface for its recycled slot.
        EnsureContextValid();
        using var runtime=new SurfaceCacheRuntimeFixture(ImageAssertions, exposedWall:true);
        bool missing=true;
        runtime.TransformVoxel=(x,y,z,voxel)=>voxel with
        {
            Geometry=x==32 && missing ? 0u : (x&31)==0 ? 2u|(runtime.MaterialId<<2) : 1u,
            LegacyLight=x>=32
                ? LumonSceneOccupancyPacking.Pack(0,0,0,LumonSceneOccupancyPacking.UnpackMaterialPaletteIndex(voxel.LegacyLight))
                : voxel.LegacyLight,
            NormalizedLight=x>=32 ? 0u : voxel.NormalizedLight
        };
        runtime.PrimeGeometry(); runtime.RunUntil(runtime.AllRequestedLightingReady);
        Assert.True(runtime.Feedback.TryGetNearDispatchState(out _,out _,out var initialPages,out _));
        uint physical=initialPages.Single().Key;
        Assert.True(runtime.Feedback.TryGetNearChunkSlotAndGeneration(new(0,1,0),out uint oldSlot,out ushort oldGeneration));
        long oldCapture=runtime.Feedback.GetCaptureRevision(physical);
        Assert.True(runtime.TryGetLighting(out var initial));
        using(var pixels=SurfaceLightingPageSelection.Capture(initial.OutgoingRadiance,initial,physical))
            Assert.True(Assertions.GpuLocalRead.Scalar(pixels.Texture, 0)>.1f);
        runtime.CameraX=32; runtime.FeedbackChunk=new(1,1,0);
        for(int frame=0;frame<24;frame++)
        {
            runtime.Frame();
            Assert.False(runtime.AllRequestedCaptured());
            Assert.False(runtime.TryGetLighting(out _));
        }
        Assert.True(runtime.Feedback.TryGetNearDispatchState(out _,out _,out var recycled,out _));
        Assert.Equal(physical,recycled.Single().Key);
        Assert.True(runtime.Feedback.TryGetNearChunkSlotAndGeneration(new(1,1,0),out uint newSlot,out ushort newGeneration));
        Assert.Equal(oldSlot,newSlot); Assert.NotEqual(oldGeneration,newGeneration);
        // Act by publishing the replacement geometry and allowing the recycled page to capture and relight.
        missing=false; runtime.InvalidateGeometry();
        runtime.RunUntil(runtime.AllRequestedLightingReady);
        Assert.True(runtime.TryGetLighting(out var current));
        // Assert old data was rejected before capture and the newly captured dark surface owns the reused page.
        Assert.NotEqual(oldCapture,runtime.Feedback.GetCaptureRevision(physical));
        Assert.Same(initial.IndirectIrradiance,current.IndirectIrradiance);
        using var dark=SurfaceLightingPageSelection.Capture(current.OutgoingRadiance,current,physical);
        ImageAssertions.Comparisons.CompareConstant(dark.Texture, 0, channelMask: 7).AssertEqual();
        ImageAssertions.Comparisons.CompareConstant(dark.Texture, 1, channelMask: 8).AssertEqual();
    }

    /// <summary>Remote retained indirect lighting reaches the same energy as a fresh cache of the edited scene.</summary>
    [Fact]
    [Trait("Scenario", "HeadlessRuntime")]
    public void RemoteIndirectConvergesToFreshEditedSceneWithoutRecapture()
    {
        // Arrange an illuminated remote page and edit a separate source while preserving the remote capture.
        EnsureContextValid();
        ulong identity;
        float retained;
        using(var runtime=new SurfaceCacheRuntimeFixture(ImageAssertions, spatial:new SpatialLightingScene { Reflectance=.25f }))
        {
            runtime.Config.LumOn.LumonScene.RelightSeedPagesPerFrame = runtime.Config.LumOn.LumonScene.RelightDirectPagesPerFrame = runtime.Config.LumOn.LumonScene.RelightIndirectPagesPerFrame = 4;
            runtime.Config.LumOn.LumonScene.RelightRaysPerTexel=16;
            runtime.PrimeGeometry(); runtime.RunUntil(runtime.AllRequestedLightingReady);
            for(int frame=0;frame<256;frame++) runtime.Frame();
            Assert.True(runtime.Feedback.TryGetNearDispatchState(out _,out _,out var pages,out _));
            Assert.True(runtime.Feedback.TryGetNearChunkSlotAndGeneration(new(-1,1,0),out uint slot,out _));
            uint page=pages.Where(pair=>LumonSceneVirtualPageKeyUtil.UnpackChunkSlot(pair.Value)==slot)
                .OrderByDescending(pair=>Energy(runtime,pair.Key)).First().Key;
            identity=pages[page];
            float bright=Energy(runtime,page);
            Assert.True(bright>.1f);
            long capture=runtime.Feedback.GetCaptureRevision(page);
            Assert.True(runtime.TryGetLighting(out var original));
            // The edited source withdraws the same light from both GPU publication encodings.
            // Act through repeated retained-cache refreshes after the source is removed.
            runtime.TransformVoxel=(x,y,z,voxel)=>x>=0 ? voxel with
            {
                LegacyLight=LumonSceneOccupancyPacking.Pack(0,0,0,LumonSceneOccupancyPacking.UnpackMaterialPaletteIndex(voxel.LegacyLight)),
                NormalizedLight=0
            } : voxel;
            foreach(var source in runtime.Sources.Where(source=>!source.Disposed))
                source.MarkDirty(ChunkKey.FromChunkCoords(0,1,0));
            for(int frame=0;frame<512;frame++)
            {
                runtime.Frame();
                Assert.True(runtime.TryGetLighting(out var current));
                Assert.Equal(original.DependencyRevision,current.DependencyRevision);
                Assert.Equal(capture,runtime.Feedback.GetCaptureRevision(page));
                using var pixels=SurfaceLightingPageSelection.Capture(current.OutgoingRadiance,current,page);
                ImageAssertions.Comparisons.CompareConstant(pixels.Texture, 1, channelMask: 8).AssertEqual();
            }
            retained=Energy(runtime,page);
            Assert.True(retained<bright*.9f);
            for(int frame=0;frame<128;frame++) runtime.Frame();
            Assert.InRange(Math.Abs(Energy(runtime,page)-retained),0,Math.Max(.03f,retained*.05f));
        }
        // Assert retained remote energy against a separately initialized cache for the edited scene.
        // A separately initialized cache supplies the target; it never inherits pre-edit illumination.
        using var reference=new SurfaceCacheRuntimeFixture(ImageAssertions, spatial:new SpatialLightingScene { Reflectance=.25f });
        reference.Config.LumOn.LumonScene.RelightSeedPagesPerFrame = reference.Config.LumOn.LumonScene.RelightDirectPagesPerFrame = reference.Config.LumOn.LumonScene.RelightIndirectPagesPerFrame = 4;
        reference.Config.LumOn.LumonScene.RelightRaysPerTexel=16;
        reference.TransformVoxel=(x,y,z,voxel)=>x>=0 ? voxel with
        {
            LegacyLight=LumonSceneOccupancyPacking.Pack(0,0,0,LumonSceneOccupancyPacking.UnpackMaterialPaletteIndex(voxel.LegacyLight)),
            NormalizedLight=0
        } : voxel;
        reference.PrimeGeometry(); reference.RunUntil(reference.AllRequestedLightingReady);
        for(int frame=0;frame<512;frame++) reference.Frame();
        Assert.True(reference.Feedback.TryGetNearDispatchState(out _,out _,out var mapping,out _));
        Assert.True(reference.Feedback.TryGetNearChunkSlotAndGeneration(new(-1,1,0),out uint referenceSlot,out _));
        ulong referenceIdentity=LumonSceneVirtualPageKeyUtil.Pack(referenceSlot,LumonSceneVirtualPageKeyUtil.UnpackVirtualPageIndex(identity));
        uint referencePage=mapping.Single(pair=>pair.Value==referenceIdentity).Key;
        float expected=Energy(reference,referencePage);
        Assert.True(reference.TryGetLighting(out var referenceLighting));
        using(var pixels=SurfaceLightingPageSelection.Capture(referenceLighting.IndirectIrradiance,referenceLighting,referencePage))
        {
            Assert.True(ImageAssertions.Statistics.Inspect(pixels.Texture, channelMask: 8).Maximum >= 4);
        }
        for(int frame=0;frame<128;frame++) reference.Frame();
        Assert.InRange(Math.Abs(Energy(reference,referencePage)-expected),0,Math.Max(.03f,expected*.05f));
        Assert.InRange(Math.Abs(retained-expected),0,Math.Max(.05f,expected*.1f));
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
    /// <summary>Averages red indirect irradiance over the page independently of the capped history count.</summary>
    private static float Energy(SurfaceCacheRuntimeFixture runtime,uint page)
    {
        Assert.True(runtime.TryGetLighting(out var snapshot));
        using var pixels=SurfaceLightingPageSelection.Capture(snapshot.IndirectIrradiance,snapshot,page);
        var result = runtime.ImageAssertions.Statistics.Statistics(pixels.Texture, channelMask: 1);
        result.AssertFiniteRange();
        return (float)(result.Sum(0) / (pixels.Texture.Width * pixels.Texture.Height));
    }
    #endregion
    #endregion
}
