using VanillaGraphicsExpanded.Tests.Support.Lighting;

namespace VanillaGraphicsExpanded.Tests.Component.LumOn;

/// <summary>Measures temporal response through the packaged lighting producer and real half-float history.</summary>
[Collection("NearFieldMaterialCapture")]
[Trait("Category", "GpuIntegration")]
public sealed class SurfaceLightingTemporalTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    #region Controlled completed samples
    /// <summary>Old high-confidence histories respond to either lighting step without erasing displayed values.</summary>
    [Theory]
    [InlineData(0f,8f)]
    [InlineData(8f,0f)]
    public void AgedHistoryConvergesWithinTwentyFourCompletedUpdates(float initial,float target)
    {
        // Arrange aged history with a distinct target incident radiance.
        EnsureContextValid();
        using var room=new SurfaceLightingEnclosureFixture(ImageAssertions);
        room.Seed(); room.SetIndirect(initial,1024);
        // Assert each completed estimate moves toward the target within the authored radiance range.
        float prior=initial;
        // Act only on completed samples; hold incident radiance fixed so estimates cannot feed themselves.
        for(int sample=0;sample<24;sample++)
        {
            // Reset only the incident boundary, preventing the test estimator from feeding itself.
            room.SetIncidentRadiance(target/MathF.PI);
            Assert.True(room.BounceSample());
            var actual=room.Read(room.Snapshot.IndirectIrradiance);
            Assert.Equal(4,actual[3]);
            Assert.InRange(actual[0],Math.Min(initial,target),Math.Max(initial,target)+.01f);
            Assert.True(Math.Abs(actual[0]-target)<=Math.Abs(prior-target)+.005f);
            if(sample==0) Assert.InRange(actual[0],initial+(target-initial)/1025-.01f,initial+(target-initial)/1025+.01f);
            prior=actual[0];
        }
        Assert.InRange(Math.Abs(prior-target),0,.4f);
    }

    /// <summary>Warmup averages completed estimates before adopting bounded history, with no stable-source energy growth.</summary>
    [Fact]
    public void WarmupUsesCompletedMeanAndStableSamplesRemainBounded()
    {
        // Arrange a fresh history and deterministic warmup values followed by a stable source.
        EnsureContextValid();
        using var room=new SurfaceLightingEnclosureFixture(ImageAssertions);
        room.Seed();
        // Assert each stored warmup sample matches the independent mean and remains stable afterward.
        float sum=0;
        // Act through completed samples and compare the stored estimate with the independent running mean.
        for(int sample=1;sample<=40;sample++)
        {
            float target=sample<=4?sample:2.5f;
            room.SetIncidentRadiance(target/MathF.PI);
            Assert.True(room.BounceSample());
            var actual=room.Read(room.Snapshot.IndirectIrradiance);
            sum+=target;
            Assert.Equal(Math.Min(sample,4),actual[3]);
            float expected=sample<=4?sum/sample:2.5f;
            Assert.InRange(actual[0],expected-.025f,expected+.025f);
        }
    }

    /// <summary>A failed trace cannot age, dilute, or clear an established history, while resolved black is a valid estimate.</summary>
    [Fact]
    public void UnresolvedSamplesRetainAgedHistoryAndResolvedBlackAccumulates()
    {
        // Arrange aged bright history and snapshot both indirect history and displayed outgoing radiance.
        EnsureContextValid();
        using var room=new SurfaceLightingEnclosureFixture(ImageAssertions);
        room.Seed(); room.SetIndirect(8,1024);
        // Assert unresolved work preserves both stored history and displayed outgoing values, unlike resolved black.
        var before=room.Read(room.Snapshot.IndirectIrradiance);
        var displayed=room.Read(room.Snapshot.OutgoingRadiance);
        // Act on unresolved attempts, then submit a resolved black sample as valid data.
        for(int attempt=0;attempt<16;attempt++) Assert.False(room.BounceSample(steps:0));
        Assert.Equal(before,room.Read(room.Snapshot.IndirectIrradiance));
        Assert.Equal(displayed,room.Read(room.Snapshot.OutgoingRadiance));
        room.SetIndirect(0,0); room.SetIncidentRadiance(0);
        Assert.True(room.BounceSample());
        Assert.Equal(new float[]{0,0,0,1},room.Read(room.Snapshot.IndirectIrradiance));
    }
    /// <summary>Saturated histories blend with the old count before applying the configured storage cap.</summary>
    [Theory]
    [InlineData(1)] [InlineData(4)] [InlineData(8)]
    public void ConfiguredCapControlsSaturatedWeight(int cap)
    {
        // Arrange history at the selected accumulation cap and a black incoming sample.
        EnsureContextValid();
        using var room=new SurfaceLightingEnclosureFixture(ImageAssertions) { MaxFramesAccumulated=cap };
        // Assert the completed sample blends with the previous count and stores the selected cap.
        room.Seed(); room.SetIndirect(8,cap); room.SetIncidentRadiance(0);
        // Act through one completed sample; weighting uses the previous count before storage is capped.
        Assert.True(room.BounceSample());
        var actual=room.Read(room.Snapshot.IndirectIrradiance);
        Assert.Equal(cap,actual[3]);
        float expected=8f*cap/(cap+1);
        Assert.InRange(actual[0],expected-.01f,expected+.01f);
    }

    /// <summary>Lowering a cap preserves history until completion and uses the previous count on that first update.</summary>
    [Fact]
    public void LoweringCapUsesPreviousCountForFirstCompletedSample()
    {
        // Arrange saturated history, then lower the configured cap without editing the stored estimate.
        EnsureContextValid();
        using var room=new SurfaceLightingEnclosureFixture(ImageAssertions) { MaxFramesAccumulated=8 };
        room.Seed(); room.SetIndirect(8,8);
        // Assert lowering the cap leaves existing values intact and the first update uses the old count.
        var before=room.Read(room.Snapshot.IndirectIrradiance);
        room.MaxFramesAccumulated=4;
        Assert.Equal(before,room.Read(room.Snapshot.IndirectIrradiance));
        Assert.False(room.BounceSample(steps:0));
        Assert.Equal(before,room.Read(room.Snapshot.IndirectIrradiance));
        // Act through the first completed update with the previous count and a second update at the new cap.
        room.SetIncidentRadiance(0); Assert.True(room.BounceSample());
        var first=room.Read(room.Snapshot.IndirectIrradiance);
        Assert.Equal(4,first[3]); Assert.InRange(first[0],8f*8/9-.01f,8f*8/9+.01f);
        room.SetIncidentRadiance(0); Assert.True(room.BounceSample());
        var second=room.Read(room.Snapshot.IndirectIrradiance);
        Assert.Equal(4,second[3]); Assert.InRange(second[0],first[0]*.8f-.01f,first[0]*.8f+.01f);
    }
    #endregion

    #region Source refresh
    /// <summary>A real light-source edit propagates into retained indirect history without recreating the atlas or recapturing surfaces.</summary>
    [Fact]
    public void DirectRefreshChangesIndirectWithoutResettingHistory()
    {
        // Arrange accumulated indirect light and retain its published texture identity.
        EnsureContextValid();
        using var room=new SurfaceLightingEnclosureFixture(ImageAssertions);
        // Assert direct refresh retains the indirect texture and completed bounces subsequently decay its old energy.
        room.Seed();
        for(int bounce=0;bounce<16;bounce++) room.Bounce();
        var original=room.Snapshot;
        float initial=room.Read(original.IndirectIrradiance)[0];
        Assert.True(initial>1);
        // Act by removing the source and refreshing direct light, then advance completed bounces for decay.
        room.BlockLight=0; room.Geometry.Dirty(); room.Geometry.Publish(); room.RefreshDirect();
        Assert.Same(original.IndirectIrradiance,room.Snapshot.IndirectIrradiance);
        Assert.Equal(initial,room.Read(room.Snapshot.IndirectIrradiance)[0]);
        Assert.Equal(4,room.Read(room.Snapshot.IndirectIrradiance)[3]);
        for(int bounce=0;bounce<48;bounce++) room.Bounce();
        Assert.InRange(room.Read(room.Snapshot.IndirectIrradiance)[0],0,initial*.05f);
        Assert.Equal(4,room.Read(room.Snapshot.IndirectIrradiance)[3]);
    }
    #endregion
    #endregion
}
