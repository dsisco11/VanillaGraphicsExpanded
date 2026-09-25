using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.LumOn.Scene.Geometry;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Exercises environment completion through the packaged Surface Cache lighting producer.</summary>
[Collection("NearFieldMaterialCapture")]
[Trait("Category", "GPU")]
public sealed class SurfaceLightingEnvironmentTests : RenderTestBase
{
    /// <summary>Uses the isolated material and real graphics context.</summary>
    public SurfaceLightingEnvironmentTests(HeadlessGLFixture fixture) : base(fixture) { }

    #region Environment completion
    /// <summary>Proven sky finishes an indirect batch without counting the seeded vanilla sunlight twice.</summary>
    [Theory]
    [InlineData(false, 0f)]
    [InlineData(true, 12f)]
    public void OpenSkyCompletesWithoutDuplicatingDirectSources(bool emissionPolicy, float emission)
    {
        EnsureContextValid();
        using var floor=new SurfaceLightingEnclosureFixture(.25f,32,emission,skyFloor:true,worldHeight:34);
        floor.EmissionPolicy=emissionPolicy; floor.SunLight=16;
        floor.Geometry.Dirty(); floor.Geometry.Publish(); floor.Seed();
        var direct=floor.Read(floor.Snapshot.DirectIrradiance);
        var outgoing=floor.Read(floor.Snapshot.OutgoingRadiance);
        Assert.InRange(direct[0],emissionPolicy?15.99f:47.99f,emissionPolicy?16.01f:48.01f);
        Assert.True(floor.BounceSample());
        Assert.Equal(new float[]{0,0,0,1},floor.Read(floor.Snapshot.IndirectIrradiance));
        Assert.Equal(direct,floor.Read(floor.Snapshot.DirectIrradiance));
        Assert.Equal(outgoing,floor.Read(floor.Snapshot.OutgoingRadiance));
        Assert.True(floor.BounceSample(frame:2));
        Assert.Equal(2,floor.Read(floor.Snapshot.IndirectIrradiance)[3]);
        Assert.Equal(outgoing,floor.Read(floor.Snapshot.OutgoingRadiance));
        Assert.Equal(ErrorCode.NoError,GL.GetError());
    }

    /// <summary>Every texel of all captured floor pages completes a bounded sky bounce before publication.</summary>
    [Fact]
    public void EntireSkyFloorCompletesAndPublishes()
    {
        EnsureContextValid();
        using var floor=new SurfaceLightingEnclosureFixture(skyFloor:true,worldHeight:34,surfaceResolution:64);
        floor.RaysPerTexel=1; floor.Seed();
        var outgoing=floor.Read(floor.Snapshot.OutgoingRadiance);
        floor.Bounce();
        for(int page=0;page<4;page++) for(int linear=0;linear<64;linear++)
        {
            Assert.Equal(new float[]{0,0,0,1},floor.Read(floor.Snapshot.IndirectIrradiance,page,linear));
            Assert.Equal(outgoing,floor.Read(floor.Snapshot.OutgoingRadiance,page,linear));
        }
    }

    /// <summary>Incomplete upward paths retain previously completed weight and outgoing illumination.</summary>
    [Theory]
    [InlineData("coverage")]
    [InlineData("unpublished")]
    [InlineData("unsupported")]
    [InlineData("budget")]
    [InlineData("one-step-budget")]
    [InlineData("missing-hit-lighting")]
    public void UnresolvedPathsCannotBecomeSkyOrAlterCompletedHistory(string scenario)
    {
        EnsureContextValid();
        using var floor=new SurfaceLightingEnclosureFixture(skyFloor:true,worldHeight:35);
        floor.Seed(); Assert.True(floor.BounceSample());
        var indirect=floor.Read(floor.Snapshot.IndirectIrradiance);
        var outgoing=floor.Read(floor.Snapshot.OutgoingRadiance);
        long generation=floor.Snapshot.Generation;
        if(scenario=="coverage")
        {
            var domain=floor.Geometry.Plan.Surface!.Value;
            floor.Geometry.Move(floor.Geometry.Plan with
            {
                Surface=new(domain.Min,new(domain.Max.X,34,domain.Max.Z))
            });
        }
        if(scenario=="unpublished") floor.Geometry.Dirty();
        if(scenario is "unsupported" or "missing-hit-lighting")
        {
            var sample=floor.Geometry.Sample;
            floor.Geometry.Sample=(x,y,z)=> y>=34
                ? new(scenario=="unsupported"?3u:sample(x,32,z).Geometry,0,0) : sample(x,y,z);
            floor.Geometry.Dirty(); floor.Geometry.Publish();
        }
        Assert.False(floor.BounceSample(steps:scenario=="budget"?0u:scenario=="one-step-budget"?1u:256u));
        Assert.Equal(generation,floor.Snapshot.Generation);
        Assert.Equal(indirect,floor.Read(floor.Snapshot.IndirectIrradiance));
        Assert.Equal(outgoing,floor.Read(floor.Snapshot.OutgoingRadiance));
    }

    /// <summary>An upward grazing ray can leave horizontal coverage before the nearby world top and must remain unresolved.</summary>
    [Fact]
    public void GrazingSkyDirectionCannotBypassHorizontalCoverage()
    {
        EnsureContextValid();
        using var floor=new SurfaceLightingEnclosureFixture(skyFloor:true,worldHeight:34);
        floor.Seed(); Assert.True(floor.BounceSample());
        var before=floor.Read(floor.Snapshot.IndirectIrradiance);
        // This cosine sample has over 99 blocks of horizontal travel per vertical block,
        // so it necessarily leaves this 32-block domain before reaching y34.
        uint frame=(uint)Enumerable.Range(1,100000).First(value=>Hash(Hash(1,27,(uint)value),0,0)/4294967295f>.9999f);
        Assert.False(floor.BounceSample(rays:1,frame:frame));
        Assert.Equal(before,floor.Read(floor.Snapshot.IndirectIrradiance));
    }

    /// <summary>A mixed batch retains sky directions in the Monte Carlo denominator while transporting cached wall light.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MixedSkyAndSurfaceHitsAverageAcrossEveryRay(bool staleRoofBeyondWorldTop)
    {
        EnsureContextValid();
        using var room=new SurfaceLightingEnclosureFixture(worldHeight:40);
        room.Seed();
        var original=room.Geometry.Sample;
        // Open the roof below the fixed world top; stale source geometry above it must not obstruct sky.
        room.Geometry.Sample=(x,y,z)=>y==39 || (!staleRoofBeyondWorldTop && y>=40) ? new(1u,0,0) : original(x,y,z);
        room.Geometry.Dirty();
        room.Geometry.Publish();
        const uint rays=128;
        int sky=CountSkyDirections(9,27,1,rays);
        Assert.InRange(sky,1,(int)rays-1);
        Assert.True(room.BounceSample(page:8,rays:rays));
        var indirect=room.Read(room.Snapshot.IndirectIrradiance,page:8);
        float expected=32*(64f/255f)*(rays-sky)/rays;
        Assert.InRange(indirect[0],expected-.04f,expected+.04f);
        Assert.Equal(1,indirect[3]);
    }

    /// <summary>Computes only the analytic room-boundary classification for the documented deterministic sampling sequence.</summary>
    private static int CountSkyDirections(uint page,uint linear,uint frame,uint rays)
    {
        uint seed=Hash(page,linear,frame); int sky=0;
        for(uint ray=0;ray<rays;ray++)
        {
            float u=Math.Clamp(Hash(seed,ray,0)/4294967295f,.000001f,.999999f);
            float phi=2*MathF.PI*(Hash(seed,ray,1)/4294967295f);
            float dx=-MathF.Sqrt(u)*MathF.Cos(phi), dz=MathF.Sqrt(u)*MathF.Sin(phi);
            // Above the roof opening at y39, the remaining path to the fixed world top is empty.
            float skyDistance=(39-33.01f)/MathF.Sqrt(1-u);
            float wallX=(dx>0?7-1.75f:1-1.75f)/dx;
            float wallZ=(dz>0?7-1.75f:1-1.75f)/dz;
            if(skyDistance<MathF.Min(wallX,wallZ)) sky++;
        }
        return sky;
    }

    /// <summary>Reproduces the published Squirrel3 sequence without reproducing geometry traversal or lighting evaluation.</summary>
    private static uint Hash(uint x,uint y,uint z)
    {
        unchecked
        {
            uint value=x+y*198491317u+z*6542989u;
            value*=3039394381u; value^=value>>8; value+=1759714724u;
            value^=value<<8; value*=458671337u; return value^(value>>8);
        }
    }

    #endregion
}
