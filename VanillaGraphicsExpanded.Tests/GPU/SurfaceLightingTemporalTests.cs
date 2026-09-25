using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Measures temporal response through the packaged lighting producer and real half-float history.</summary>
[Collection("NearFieldMaterialCapture")]
[Trait("Category","GPU")]
public sealed class SurfaceLightingTemporalTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Controlled completed samples
    /// <summary>Old high-confidence histories respond to either lighting step without erasing displayed values.</summary>
    [Theory]
    [InlineData(0f,8f)]
    [InlineData(8f,0f)]
    public void AgedHistoryConvergesWithinTwentyFourCompletedUpdates(float initial,float target)
    {
        EnsureContextValid();
        using var room=new SurfaceLightingEnclosureFixture();
        room.Seed(); room.SetIndirect(initial,1024);
        float prior=initial;
        for(int sample=0;sample<24;sample++)
        {
            // Reset only the incident boundary, preventing the test estimator from feeding itself.
            room.SetIncidentRadiance(target/MathF.PI);
            Assert.True(room.BounceSample());
            var actual=room.Read(room.Snapshot.IndirectIrradiance);
            Assert.Equal(8,actual[3]);
            Assert.InRange(actual[0],Math.Min(initial,target),Math.Max(initial,target)+.01f);
            Assert.True(Math.Abs(actual[0]-target)<=Math.Abs(prior-target)+.005f);
            if(sample==0) Assert.InRange(actual[0],initial+(target-initial)/8-.01f,initial+(target-initial)/8+.01f);
            prior=actual[0];
        }
        Assert.InRange(Math.Abs(prior-target),0,.4f);
    }

    /// <summary>Warmup averages completed estimates before adopting bounded history, with no stable-source energy growth.</summary>
    [Fact]
    public void WarmupUsesCompletedMeanAndStableSamplesRemainBounded()
    {
        EnsureContextValid();
        using var room=new SurfaceLightingEnclosureFixture();
        room.Seed();
        float sum=0;
        for(int sample=1;sample<=40;sample++)
        {
            float target=sample<=8?sample:4.5f;
            room.SetIncidentRadiance(target/MathF.PI);
            Assert.True(room.BounceSample());
            var actual=room.Read(room.Snapshot.IndirectIrradiance);
            sum+=target;
            Assert.Equal(Math.Min(sample,8),actual[3]);
            float expected=sample<=8?sum/sample:4.5f;
            Assert.InRange(actual[0],expected-.025f,expected+.025f);
        }
    }

    /// <summary>A failed trace cannot age, dilute, or clear an established history, while resolved black is a valid estimate.</summary>
    [Fact]
    public void UnresolvedSamplesRetainAgedHistoryAndResolvedBlackAccumulates()
    {
        EnsureContextValid();
        using var room=new SurfaceLightingEnclosureFixture();
        room.Seed(); room.SetIndirect(8,1024);
        var before=room.Read(room.Snapshot.IndirectIrradiance);
        var displayed=room.Read(room.Snapshot.OutgoingRadiance);
        for(int attempt=0;attempt<16;attempt++) Assert.False(room.BounceSample(steps:0));
        Assert.Equal(before,room.Read(room.Snapshot.IndirectIrradiance));
        Assert.Equal(displayed,room.Read(room.Snapshot.OutgoingRadiance));
        room.SetIndirect(0,0); room.SetIncidentRadiance(0);
        Assert.True(room.BounceSample());
        Assert.Equal(new float[]{0,0,0,1},room.Read(room.Snapshot.IndirectIrradiance));
    }
    #endregion

    #region Source refresh
    /// <summary>A real light-source edit propagates into retained indirect history without recreating the atlas or recapturing surfaces.</summary>
    [Fact]
    public void DirectRefreshChangesIndirectWithoutResettingHistory()
    {
        EnsureContextValid();
        using var room=new SurfaceLightingEnclosureFixture();
        room.Seed();
        for(int bounce=0;bounce<16;bounce++) room.Bounce();
        var original=room.Snapshot;
        float initial=room.Read(original.IndirectIrradiance)[0];
        Assert.True(initial>1);
        room.BlockLight=0; room.Geometry.Dirty(); room.Geometry.Publish(); room.RefreshDirect();
        Assert.Same(original.IndirectIrradiance,room.Snapshot.IndirectIrradiance);
        Assert.Equal(initial,room.Read(room.Snapshot.IndirectIrradiance)[0]);
        Assert.Equal(8,room.Read(room.Snapshot.IndirectIrradiance)[3]);
        for(int bounce=0;bounce<48;bounce++) room.Bounce();
        Assert.InRange(room.Read(room.Snapshot.IndirectIrradiance)[0],0,initial*.05f);
        Assert.Equal(8,room.Read(room.Snapshot.IndirectIrradiance)[3]);
    }
    #endregion
}
