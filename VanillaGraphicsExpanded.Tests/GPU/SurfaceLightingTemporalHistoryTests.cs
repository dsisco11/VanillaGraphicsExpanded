using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Guards cache-to-pixel retention and rejection using persistent, partially updated probe atlases.</summary>
[Collection("NearFieldMaterialCapture")]
[Trait("Category","GPU")]
public sealed class SurfaceLightingTemporalHistoryTests : SurfaceLightingTemporalTestBase
{
    /// <summary>Uses the material-isolated GPU context.</summary>
    public SurfaceLightingTemporalHistoryTests(HeadlessGLFixture fixture) : base(fixture) { }

    #region Lighting transitions
    /// <summary>Unchanged inputs retain directions, and source removal/restoration settles within one eight-frame sweep.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void RetainedHistoryFollowsSourceLighting(bool sh9)
    {
        EnsureShaderTestAvailable();
        using var room=new SurfaceLightingEnclosureFixture(); room.Seed();
        using var history=new SurfaceLightingHistoryFixture();
        var bright=Sweep(room,history,sh9,true);
        long revision=history.Buffers.HistoryRevision;
        var retained=RenderHistory(room,history,sh9);
        Assert.False(retained.Reset); Assert.Equal(revision,history.Buffers.HistoryRevision);
        AssertRetainedDirections(bright.Pixels.Trace,retained,history.FrameIndex-1);
        AssertPipeline(retained.Pixels,true);
        foreach(int light in new[]{0,32})
        {
            room.BlockLight=light; room.Geometry.Dirty(); room.Geometry.Publish(); room.Seed();
            var first=RenderHistory(room,history,sh9); Assert.True(first.Reset);
            if(light==0) AssertPipeline(first.Pixels,false);
            var settled=Sweep(room,history,sh9,light!=0,SurfaceLightingHistoryFixture.SweepFrames-1);
            if(light!=0) AssertClose(bright.Pixels.Final,settled.Pixels.Final,.01f);
        }
    }

    /// <summary>Progressive bounce publication keeps old directions and blends freshly traced energy instead of resetting.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void BounceGenerationPreservesAndAccumulatesHistory(bool sh9)
    {
        EnsureShaderTestAvailable();
        using var room=new SurfaceLightingEnclosureFixture(); room.Seed();
        using var history=new SurfaceLightingHistoryFixture();
        var seed=Sweep(room,history,sh9,true);
        long revision=history.Buffers.HistoryRevision;
        room.Bounce();
        var bounce=RenderHistory(room,history,sh9);
        Assert.False(bounce.Reset); Assert.Equal(revision,history.Buffers.HistoryRevision);
        AssertRetainedDirections(seed.Pixels.Trace,bounce,history.FrameIndex-1);
        // At least one freshly traced direction must actually blend old energy, not merely copy current.
        Assert.Contains(Enumerable.Range(0,256),i => bounce.Raw[i*4] > seed.Pixels.Trace[i*4]+.01f
            && bounce.Pixels.Trace[i*4] < bounce.Raw[i*4]-.001f
            && bounce.Pixels.Trace[i*4] > seed.Pixels.Trace[i*4]+.0001f);
        AssertPipeline(bounce.Pixels,true);
    }
    #endregion

    #region Dependency lifetime
    /// <summary>Provider loss and dependency-only changes reject populated history, independently of geometry revision.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void UnavailabilityAndDependencyRevisionRejectHistory(bool sh9)
    {
        EnsureShaderTestAvailable();
        using var room=new SurfaceLightingEnclosureFixture(); room.Seed();
        using var history=new SurfaceLightingHistoryFixture();
        Sweep(room,history,sh9,true);
        var absent=RenderHistory(room,history,sh9,available:false);
        Assert.True(absent.Reset); AssertPipeline(absent.Pixels,false);
        Assert.All(Enumerable.Range(0,256),i=>Assert.Equal(0,absent.Pixels.Meta[i*2]));
        Sweep(room,history,sh9,true);
        long geometry=room.Geometry.Scene.Revision;
        room.DependencyRevision++;
        var changed=RenderHistory(room,history,sh9);
        Assert.True(changed.Reset); Assert.Equal(geometry,room.Geometry.Scene.Revision);
        AssertColdUntracedDirections(changed,history.FrameIndex-1);
        Sweep(room,history,sh9,true);
    }

    /// <summary>Cache recreation invalidates existing probe history without rebuilding the downstream history fixture.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void CacheResourceRecreationRejectsPopulatedHistory(bool sh9)
    {
        EnsureShaderTestAvailable();
        using var history=new SurfaceLightingHistoryFixture();
        using(var bright=new SurfaceLightingEnclosureFixture()) { bright.Seed(); Sweep(bright,history,sh9,true); }
        using(var dark=new SurfaceLightingEnclosureFixture(blockLight:0))
        {
            dark.Seed(); var frame=RenderHistory(dark,history,sh9);
            Assert.True(frame.Reset); AssertPipeline(frame.Pixels,false); Sweep(dark,history,sh9,false);
        }
        using(var restored=new SurfaceLightingEnclosureFixture()) { restored.Seed(); Sweep(restored,history,sh9,true); }
    }
    #endregion

    #region Geometry transitions
    /// <summary>A fixed bright exterior lights an open doorway; closure occludes it and clears retained final illumination.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void DoorwayClosureAndReopeningReachRetainedFinalPixels(bool sh9)
    {
        EnsureShaderTestAvailable();
        using var room=new SurfaceLightingEnclosureFixture(dividedRoom:true); room.Seed();
        using var history=new SurfaceLightingHistoryFixture();
        var open=Sweep(room,history,sh9,true);
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
            var first=RenderHistory(room,history,sh9); Assert.True(first.Reset);
            var settled=Sweep(room,history,sh9,doorOpen,SurfaceLightingHistoryFixture.SweepFrames-1);
            if(doorOpen) AssertClose(open.Pixels.Final,settled.Pixels.Final,.01f);
            else
            {
                AssertPipeline(first.Pixels,false);
                Assert.Contains(Enumerable.Range(0,256),i=>settled.Pixels.Trace[i*4+3]<open.Pixels.Trace[i*4+3]-.01f);
                var unresolved=Enumerable.Range(0,256).Where(i=>settled.Pixels.Meta[i*2]!=1).ToArray();
                Assert.True(unresolved.Length==0,string.Join("; ",unresolved.Select(i=>$"pixel={i}, flags={Flags(settled.Pixels.Meta[i*2+1]):X}, distance={MathF.Exp(settled.Pixels.Trace[i*4+3])-1:F3}")));
            }
        }
    }
    #endregion

    #region Assertions and update budgets
    /// <summary>Runs one fixed eight-frame directional sweep; all observations retain the same GPU history.</summary>
    private HistoryFrame Sweep(SurfaceLightingEnclosureFixture room,SurfaceLightingHistoryFixture history,bool sh9,bool lit,int frames=SurfaceLightingHistoryFixture.SweepFrames)
    {
        HistoryFrame frame=null!;
        for(int i=0;i<frames;i++)
        {
            frame=RenderHistory(room,history,sh9);
            if(i>0) Assert.False(frame.Reset);
            foreach(var pixels in new[]{frame.Raw,frame.Pixels.Trace,frame.Pixels.Filtered,frame.Pixels.Gathered,frame.Pixels.Final})
                for(int channel=0;channel<pixels.Length;channel++)
                    Assert.True(float.IsFinite(pixels[channel]),"Every intermediate frame must remain finite.");
            if(!lit) AssertPipeline(frame.Pixels,false);
        }
        AssertPipeline(frame.Pixels,lit);
        return frame;
    }

    /// <summary>Checks every untraced RGBA direction is bit-identical to previous temporal output.</summary>
    private static void AssertRetainedDirections(float[] previous,HistoryFrame frame,int index)
    {
        int lit=0;
        for(int i=0;i<256;i++) if(!Traced(i,index))
        {
            for(int c=0;c<4;c++) Assert.Equal(previous[i*4+c],frame.Pixels.Trace[i*4+c]);
            if(previous[i*4]>.001f) lit++;
        }
        Assert.True(lit>32,"Untraced retained history must contain actual cache-originating illumination.");
    }

    /// <summary>Checks reset directions cannot copy lighting from the obsolete generation.</summary>
    private static void AssertColdUntracedDirections(HistoryFrame frame,int index)
    {
        for(int i=0;i<256;i++) if(!Traced(i,index))
        {
            for(int c=0;c<4;c++) Assert.Equal(0,frame.Pixels.Trace[i*4+c]);
            Assert.Equal(0,frame.Pixels.Meta[i*2]);
        }
    }

    /// <summary>Mirrors the documented batch schedule solely to select assertions, never to generate shader output.</summary>
    private static bool Traced(int pixel,int frame)
    {
        int x=pixel%16,y=pixel/16,probe=x/8+(y/8)*2,direction=(y%8)*8+x%8;
        return direction/8==(frame+probe)%8;
    }

    /// <summary>Compares every RGB output with an absolute half-float transport tolerance.</summary>
    private static void AssertClose(float[] expected,float[] actual,float tolerance)
    {
        Assert.Equal(expected.Length,actual.Length);
        for(int i=0;i<actual.Length;i++) if(i%4!=3) Assert.InRange(Math.Abs(actual[i]-expected[i]),0,tolerance);
    }
    #endregion
}
