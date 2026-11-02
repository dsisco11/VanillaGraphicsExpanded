using VanillaGraphicsExpanded.Tests.GPU.Assertions;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks exact temporal shader retention, confidence and blending with a deterministic component schedule.</summary>
[Collection("NearFieldMaterialCapture")]
[Trait("Category", "GpuIntegration")]
public sealed class SurfaceLightingTemporalComponentTests : SurfaceLightingTemporalTestBase
{


    /// <summary>Uses the material-isolated GPU context.</summary>
    public SurfaceLightingTemporalComponentTests(HeadlessGLFixture fixture) : base(fixture) { }

    #region Lighting transitions
    /// <summary>Unchanged inputs retain directions, and source removal/restoration settles within one eight-frame sweep.</summary>
    [Fact]
    public void RetainedHistoryFollowsSourceLighting()
    {
        EnsureShaderTestAvailable();
        using var room=new SurfaceLightingEnclosureFixture(ImageAssertions); room.Seed();
        using var history=new SurfaceLightingHistoryFixture();
        var bright=Sweep(room,history,true);
        long revision=history.Buffers.HistoryRevision;
        var retained=RenderHistory(room,history);
        Assert.False(retained.Reset); Assert.Equal(revision,history.Buffers.HistoryRevision);
        AssertRetainedDirections(bright.Pixels.Radiance,retained,history.FrameIndex-1);
        AssertHits(retained.Pixels,true);
        foreach(int light in new[]{0,32})
        {
            room.BlockLight=light; room.Geometry.Dirty(); room.Geometry.Publish(); room.Seed();
            var first=RenderHistory(room,history); Assert.True(first.Reset);
            if(light==0) AssertHits(first.Pixels,false);
            var settled=Sweep(room,history,light!=0,SurfaceLightingHistoryFixture.SweepFrames-1);
            if(light!=0) AssertClose(bright.Pixels.Radiance,settled.Pixels.Radiance,.01f);
        }
    }

    /// <summary>Progressive bounce publication keeps old directions and blends freshly traced energy instead of resetting.</summary>
    [Fact]
    public void BounceGenerationPreservesAndAccumulatesHistory()
    {
        EnsureShaderTestAvailable();
        using var room=new SurfaceLightingEnclosureFixture(ImageAssertions); room.Seed();
        using var history=new SurfaceLightingHistoryFixture();
        var seed=Sweep(room,history,true);
        long revision=history.Buffers.HistoryRevision;
        room.Bounce();
        var bounce=RenderHistory(room,history);
        Assert.False(bounce.Reset); Assert.Equal(revision,history.Buffers.HistoryRevision);
        AssertRetainedDirections(seed.Pixels.Radiance,bounce,history.FrameIndex-1);
        // At least one freshly traced direction must actually blend old energy, not merely copy current.
        Assert.True(ImageAssertions.Predicates.Predicate(bounce.Raw, SurfacePredicates.SurfaceLightingTemporalComponentTests_BounceGenerationPreservesAndAccumulatesHistory,seed.Pixels.Radiance,bounce.Pixels.Radiance).MatchingTexels>0);
        AssertHits(bounce.Pixels,true);
    }
    #endregion

    #region Dependency lifetime
    /// <summary>Provider loss and dependency-only changes reject populated history, independently of geometry revision.</summary>
    [Fact]
    public void UnavailabilityAndDependencyRevisionRejectHistory()
    {
        EnsureShaderTestAvailable();
        using var room=new SurfaceLightingEnclosureFixture(ImageAssertions); room.Seed();
        using var history=new SurfaceLightingHistoryFixture();
        Sweep(room,history,true);
        var absent=RenderHistory(room,history,available:false);
        Assert.True(absent.Reset); AssertHits(absent.Pixels,false);
        ImageAssertions.Comparisons.CompareConstant(absent.Pixels.Meta,0,channelMask:1).AssertEqual();
        Sweep(room,history,true);
        long geometry=room.Geometry.Scene.Revision;
        room.DependencyRevision++;
        var changed=RenderHistory(room,history);
        Assert.True(changed.Reset); Assert.Equal(geometry,room.Geometry.Scene.Revision);
        AssertColdUntracedDirections(changed,history.FrameIndex-1);
        Sweep(room,history,true);
    }

    /// <summary>Cache recreation invalidates existing probe history without rebuilding the downstream history fixture.</summary>
    [Fact]
    public void CacheResourceRecreationRejectsPopulatedHistory()
    {
        EnsureShaderTestAvailable();
        using var history=new SurfaceLightingHistoryFixture();
        using(var bright=new SurfaceLightingEnclosureFixture(ImageAssertions)) { bright.Seed(); Sweep(bright,history,true); }
        using(var dark=new SurfaceLightingEnclosureFixture(ImageAssertions, blockLight:0))
        {
            dark.Seed(); var frame=RenderHistory(dark,history);
            Assert.True(frame.Reset); AssertHits(frame.Pixels,false); Sweep(dark,history,false);
        }
        using(var restored=new SurfaceLightingEnclosureFixture(ImageAssertions)) { restored.Seed(); Sweep(restored,history,true); }
    }
    #endregion

    #region Geometry transitions
    /// <summary>A fixed bright exterior lights an open doorway; closure occludes it and clears retained directional illumination.</summary>
    [Fact]
    public void DoorwayClosureAndReopeningReachRetainedProbeHistory()
    {
        EnsureShaderTestAvailable();
        using var room=new SurfaceLightingEnclosureFixture(ImageAssertions, dividedRoom:true); room.Seed();
        using var history=new SurfaceLightingHistoryFixture();
        var open=Sweep(room,history,true);
        uint exterior=room.Geometry.ReadGeometry(4,36,7);
        uint exteriorLight=room.Sample(4,36,6).LegacyLight;
        Assert.Equal(32u,VanillaGraphicsExpanded.LumOn.Scene.LumonSceneOccupancyPacking.UnpackBlockLevel(exteriorLight));
        foreach(bool doorOpen in new[]{false,true})
        {
            room.DoorOpen=doorOpen; room.Geometry.Dirty(); room.Geometry.Publish(); room.Capture(); room.Seed();
            Assert.Equal(doorOpen?1u:2u,room.Geometry.ReadGeometry(4,36,5)&3u);
            Assert.Equal(exterior,room.Geometry.ReadGeometry(4,36,7));
            Assert.Equal(exteriorLight,room.Sample(4,36,6).LegacyLight);
            Assert.Equal(0u,VanillaGraphicsExpanded.LumOn.Scene.LumonSceneOccupancyPacking.UnpackBlockLevel(room.Sample(4,36,4).LegacyLight));
            var first=RenderHistory(room,history); Assert.True(first.Reset);
            var settled=Sweep(room,history,doorOpen,SurfaceLightingHistoryFixture.SweepFrames-1);
            if(doorOpen) AssertClose(open.Pixels.Radiance,settled.Pixels.Radiance,.01f);
            else
            {
                AssertHits(first.Pixels,false);
                Assert.True(ImageAssertions.Predicates.Predicate(settled.Pixels.Radiance, SurfacePredicates.SurfaceLightingTemporalComponentTests_DoorwayClosureAndReopeningReachRetainedProbeHistory,open.Pixels.Radiance).MatchingTexels>0);
                ImageAssertions.Comparisons.CompareConstant(settled.Pixels.Meta,1,channelMask:1).AssertEqual();
            }
        }
    }
    #endregion

    #region Assertions and update budgets
    /// <summary>Runs one fixed eight-frame directional sweep; all observations retain the same GPU history.</summary>
    private HistoryFrame Sweep(SurfaceLightingEnclosureFixture room,SurfaceLightingHistoryFixture history,bool lit,int frames=SurfaceLightingHistoryFixture.SweepFrames)
    {
        HistoryFrame frame=null!;
        for(int i=0;i<frames;i++)
        {
            frame=RenderHistory(room,history);
            if(i>0) Assert.False(frame.Reset);
            foreach(var pixels in new[]{frame.Raw,frame.Pixels.Radiance}) ImageAssertions.Statistics.Inspect(pixels).AssertFiniteRange();
            if(!lit) AssertHits(frame.Pixels,false);
        }
        AssertHits(frame.Pixels,lit);
        return frame;
    }

    /// <summary>Checks every untraced RGBA direction equals previous temporal output, with actual retained illumination.</summary>
    private void AssertRetainedDirections(GpuTexture previous,HistoryFrame frame,int index)
    {
        var observation=ImageAssertions.Predicates.Predicate(frame.Pixels.Radiance, SurfacePredicates.SurfaceLightingTemporalComponentTests_AssertRetainedDirections,previous,parameters:new(index,0,0,0));
        observation.AssertEqual();
        Assert.True(observation.MatchingTexels>32,"Untraced retained history must contain actual cache-originating illumination.");
    }

    /// <summary>Checks reset directions cannot copy lighting or confidence from the obsolete generation.</summary>
    private void AssertColdUntracedDirections(HistoryFrame frame,int index)
    {
        ImageAssertions.Predicates.Predicate(frame.Pixels.Radiance, SurfacePredicates.SurfaceLightingTemporalComponentTests_AssertColdUntracedDirections,frame.Pixels.Meta,parameters:new(index,0,0,0)).AssertEqual();
    }

    // The documented eight-direction batch schedule selects assertions; it never predicts shader radiance.

    /// <summary>Compares every RGB output with the original absolute half-float transport tolerance.</summary>
    private void AssertClose(GpuTexture expected,GpuTexture actual,float tolerance)
    {
        ImageAssertions.Predicates.Predicate(actual, SurfacePredicates.SurfaceLightingTemporalComponentTests_AssertClose,
            expected,parameters:new(tolerance,0,0,0)).AssertEqual();
    }
    #endregion
    #region Private
    /// <summary>Prepares this suite's declared observation programs before subject execution.</summary>
    private protected override void PrepareObservations(VanillaGraphicsExpanded.Tests.GPU.Assertions.GpuObservationPrograms programs)
    {
        base.PrepareObservations(programs);
        programs.Prepare("compare", 0, 0);
        programs.Prepare("inspection", 0, 0);
        programs.Prepare("predicate-surface", 0, 0);
    }
    #endregion
}
