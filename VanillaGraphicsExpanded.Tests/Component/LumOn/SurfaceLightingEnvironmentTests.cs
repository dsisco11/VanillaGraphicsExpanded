using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.LumOn.Scene.Geometry;
using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.Tests.Support.Lighting;

namespace VanillaGraphicsExpanded.Tests.Component.LumOn;

/// <summary>Exercises environment completion through the packaged Surface Cache lighting producer.</summary>
[Collection("NearFieldMaterialCapture")]
[Trait("Category", "GpuIntegration")]
public sealed class SurfaceLightingEnvironmentTests : RenderTestBase
{
    #region Public API
    /// <summary>Uses the isolated material and real graphics context.</summary>
    public SurfaceLightingEnvironmentTests(HeadlessGLFixture fixture) : base(fixture) { }

    #region Environment completion
    /// <summary>A neutral completed atmosphere cannot invent a legacy intensity when sky availability is positive.</summary>
    [Fact]
    public void NeutralAtmospherePublishesValidBlackWithoutLegacySkyFallback()
    {
        // Arrange
        EnsureContextValid();
        using var atmosphere = new ScopedAtmosphereLighting(System.Numerics.Vector3.Zero);
        using var floor = new SurfaceLightingEnclosureFixture(ImageAssertions, blockLight: 0, skyFloor: true, worldHeight: 34);
        floor.SunLight = 32;
        floor.Geometry.Dirty(); floor.Geometry.Publish();
        // Act
        floor.Seed();
        // Assert
        Assert.Equal(new float[] { 0, 0, 0, 1 }, floor.ReadStored(floor.Snapshot.DirectIrradiance));
        Assert.Equal(new float[] { 0, 0, 0, 1 }, floor.ReadStored(floor.Snapshot.OutgoingRadiance));
    }

    /// <summary>The installed cache decodes calibrated block RGB once and removes byte-stored accessor sky zero.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CalibratedFieldReachesInstalledCacheProducer(bool emissionPolicy)
    {
        // Arrange
        EnsureContextValid();
        using var room = new SurfaceLightingEnclosureFixture(ImageAssertions, blockLight: 0, emission: 32, skyFloor: true, worldHeight: 34);
        room.EmissionPolicy = emissionPolicy;
        room.SetCalibration(LightingCalibration.Default);
        // The synthetic normalized source publishes exactly white RGB and the accessor's rounded zero byte.
        var original = room.Geometry.Sample;
        room.Geometry.Sample = (x, y, z) => original(x, y, z) with
        { NormalizedLight = TraceGeometryVoxel.PackLight(new(1, 1, 1, .03f)) };
        room.Geometry.Dirty(); room.Geometry.Publish();
        // Act
        room.Seed();
        float amplitude = 1 - LightingCalibration.Default.BlockNeutralFloor;
        float expectedLux = emissionPolicy ? 0 : LightingCalibration.Default.BlockReferenceLux
            * MathF.Pow(amplitude, 4) / (MathF.Pow(amplitude, 3) + .064f);
        float[] direct = room.Read(room.Snapshot.DirectIrradiance);
        // Assert
        for (int channel = 0; channel < 3; channel++)
            Assert.InRange(direct[channel], expectedLux - .01f, expectedLux + .01f);
        Assert.Equal(1, direct[3]);
        if (emissionPolicy)
            Assert.InRange(room.Read(room.Snapshot.OutgoingRadiance)[0], 31.99f, 32.01f);
    }

    /// <summary>Published physical sky and solar inputs reach the real dispatch and remain gated by local sky availability.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(16)]
    public void PublishedAtmosphereSharesEnvironmentAndSolar(int skyLevel)
    {
        // Arrange
        EnsureContextValid();
        var property = typeof(VanillaGraphicsExpanded.ModSystems.AtmosphereModSystem).GetProperty("Lighting",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        object? previous = property.GetValue(null);
        try
        {
            property.SetValue(null, new VanillaGraphicsExpanded.PBR.Atmosphere.AtmosphereLighting(
                System.Numerics.Vector3.UnitY, new(.3f,.5f,.7f), new(.2f,.4f,.6f), new(0), new(0), []));
            using var floor = new SurfaceLightingEnclosureFixture(ImageAssertions, blockLight:0, skyFloor:true, worldHeight:34);
            floor.SunLight = skyLevel;
            floor.Geometry.Dirty(); floor.Geometry.Publish();
            // Act
            floor.Seed();
            float[] direct = floor.Read(floor.Snapshot.DirectIrradiance);
            // Assert
            for (int channel = 0; channel < 3; channel++)
            {
                float expected = MathF.Round(skyLevel / 32f * 255) / 255 * ((channel + 1) * .2f * MathF.PI + .3f + channel * .2f);
                Assert.InRange(direct[channel], expected - .002f, expected + .002f);
            }
            // Act
            Assert.True(floor.BounceSample());
            // Assert
            Assert.Equal(new float[] {0,0,0,1}, floor.Read(floor.Snapshot.IndirectIrradiance));
            Assert.Equal(direct, floor.Read(floor.Snapshot.DirectIrradiance));
        }
        finally { property.SetValue(null, previous); }
    }

    /// <summary>A fully published open route can exceed the default DDA budget without providing evidence of sky.</summary>
    [Fact]
    public void SixtyFourStepsCannotCompleteLongPublishedSkyRoute()
    {
        // Arrange
        EnsureContextValid();
        using var floor=new SurfaceLightingEnclosureFixture(ImageAssertions, skyFloor:true,worldHeight:100,surfaceResolution:128);
        floor.Seed();
        // Select a nearly vertical cosine ray so coverage contains its complete route to y100.
        uint frame=(uint)Enumerable.Range(1,100000).First(value=>Hash(Hash(1,27,(uint)value),0,0)/4294967295f<.001f);
        var before=floor.Read(floor.Snapshot.IndirectIrradiance);
        // Act
        Assert.False(floor.BounceSample(rays:1,steps:64,frame:frame));
        // Assert
        Assert.Equal(before,floor.Read(floor.Snapshot.IndirectIrradiance));
        // Act
        Assert.True(floor.BounceSample(rays:1,steps:128,frame:frame));
        // Assert
        Assert.Equal(1,floor.Read(floor.Snapshot.IndirectIrradiance)[3]);
    }

    /// <summary>Proven sky finishes an indirect batch without counting the seeded vanilla sunlight twice.</summary>
    [Theory]
    [InlineData(false, 0f)]
    [InlineData(true, 12f)]
    public void OpenSkyCompletesWithoutDuplicatingDirectSources(bool emissionPolicy, float emission)
    {
        // Arrange
        EnsureContextValid();
        using var atmosphere = new ScopedAtmosphereLighting(new System.Numerics.Vector3(32 / MathF.PI));
        using var floor=new SurfaceLightingEnclosureFixture(ImageAssertions, .25f,32,emission,skyFloor:true,worldHeight:34);
        floor.EmissionPolicy=emissionPolicy; floor.SunLight=16;
        floor.Geometry.Dirty(); floor.Geometry.Publish();
        // Act
        floor.Seed();
        var direct=floor.Read(floor.Snapshot.DirectIrradiance);
        var outgoing=floor.Read(floor.Snapshot.OutgoingRadiance);
        float expectedDirect = 32 * (128f / 255) + (emissionPolicy ? 0 : 32);
        // Assert
        Assert.InRange(direct[0], expectedDirect - .04f, expectedDirect + .04f);
        // Act
        Assert.True(floor.BounceSample());
        // Assert
        Assert.Equal(new float[]{0,0,0,1},floor.Read(floor.Snapshot.IndirectIrradiance));
        Assert.Equal(direct,floor.Read(floor.Snapshot.DirectIrradiance));
        Assert.Equal(outgoing,floor.Read(floor.Snapshot.OutgoingRadiance));
        // Act
        Assert.True(floor.BounceSample(frame:2));
        // Assert
        Assert.Equal(2,floor.Read(floor.Snapshot.IndirectIrradiance)[3]);
        Assert.Equal(outgoing,floor.Read(floor.Snapshot.OutgoingRadiance));
        Assert.Equal(ErrorCode.NoError,GL.GetError());
    }

    /// <summary>Every texel of all captured floor pages completes a bounded sky bounce before publication.</summary>
    [Fact]
    public void EntireSkyFloorCompletesAndPublishes()
    {
        // Arrange
        EnsureContextValid();
        using var floor=new SurfaceLightingEnclosureFixture(ImageAssertions, skyFloor:true,worldHeight:34,surfaceResolution:64);
        floor.RaysPerTexel=1; floor.Seed();
        var outgoing=floor.Read(floor.Snapshot.OutgoingRadiance);
        // Act
        floor.Bounce();
        // Assert each exact mapped page tile; the fixture decodes RGB with the same persistent scale as scalar reads.
        const int pages=4, texelsPerPage=64, componentsPerTexel=4;
        for(int page=0;page<pages;page++)
        {
            float[] indirect=floor.ReadPage(floor.Snapshot.IndirectIrradiance,page);
            float[] outgoingPage=floor.ReadPage(floor.Snapshot.OutgoingRadiance,page);
            Assert.Equal(texelsPerPage*componentsPerTexel,indirect.Length);
            Assert.Equal(texelsPerPage*componentsPerTexel,outgoingPage.Length);
            for(int linear=0;linear<texelsPerPage;linear++)
            {
                int offset=linear*componentsPerTexel;
                Assert.True(indirect[offset]==0f,$"page={page}, texel={linear}, channel=R expected=0 actual={indirect[offset]:R}");
                Assert.True(indirect[offset+1]==0f,$"page={page}, texel={linear}, channel=G expected=0 actual={indirect[offset+1]:R}");
                Assert.True(indirect[offset+2]==0f,$"page={page}, texel={linear}, channel=B expected=0 actual={indirect[offset+2]:R}");
                Assert.True(indirect[offset+3]==1f,$"page={page}, texel={linear}, channel=A expected=1 actual={indirect[offset+3]:R}");
                for(int channel=0;channel<componentsPerTexel;channel++)
                    Assert.True(outgoing[channel]==outgoingPage[offset+channel],$"page={page}, texel={linear}, channel={channel} expected={outgoing[channel]:R} actual={outgoingPage[offset+channel]:R}");
            }
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
        // Arrange
        EnsureContextValid();
        using var floor=new SurfaceLightingEnclosureFixture(ImageAssertions, skyFloor:true,worldHeight:35);
        floor.Seed();
        // Act
        Assert.True(floor.BounceSample());
        // Assert
        var indirect=floor.Read(floor.Snapshot.IndirectIrradiance);
        var outgoing=floor.Read(floor.Snapshot.OutgoingRadiance);
        long generation=floor.Snapshot.Generation;
        // Arrange the selected unresolved path after recording the completed baseline.
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
        // Act
        Assert.False(floor.BounceSample(steps:scenario=="budget"?0u:scenario=="one-step-budget"?1u:256u));
        // Assert
        Assert.Equal(generation,floor.Snapshot.Generation);
        Assert.Equal(indirect,floor.Read(floor.Snapshot.IndirectIrradiance));
        Assert.Equal(outgoing,floor.Read(floor.Snapshot.OutgoingRadiance));
    }

    /// <summary>An upward grazing ray can leave horizontal coverage before the nearby world top and must remain unresolved.</summary>
    [Fact]
    public void GrazingSkyDirectionCannotBypassHorizontalCoverage()
    {
        // Arrange
        EnsureContextValid();
        using var floor=new SurfaceLightingEnclosureFixture(ImageAssertions, skyFloor:true,worldHeight:34);
        floor.Seed();
        // Act
        Assert.True(floor.BounceSample());
        // Assert
        var before=floor.Read(floor.Snapshot.IndirectIrradiance);
        // This cosine sample has over 99 blocks of horizontal travel per vertical block,
        // so it necessarily leaves this 32-block domain before reaching y34.
        uint frame=(uint)Enumerable.Range(1,100000).First(value=>Hash(Hash(1,27,(uint)value),0,0)/4294967295f>.9999f);
        // Act
        Assert.False(floor.BounceSample(rays:1,frame:frame));
        // Assert
        Assert.Equal(before,floor.Read(floor.Snapshot.IndirectIrradiance));
    }

    /// <summary>A mixed batch retains sky directions in the Monte Carlo denominator while transporting cached wall light.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MixedSkyAndSurfaceHitsAverageAcrossEveryRay(bool staleRoofBeyondWorldTop)
    {
        // Arrange
        EnsureContextValid();
        using var room=new SurfaceLightingEnclosureFixture(ImageAssertions, worldHeight:40);
        room.Seed();
        var original=room.Geometry.Sample;
        // Open the roof below the fixed world top; stale source geometry above it must not obstruct sky.
        room.Geometry.Sample=(x,y,z)=>y==39 || (!staleRoofBeyondWorldTop && y>=40) ? new(1u,0,0) : original(x,y,z);
        room.Geometry.Dirty();
        room.Geometry.Publish();
        const uint rays=128;
        int sky=CountSkyDirections(9,27,1,rays);
        // Assert the deterministic sequence includes both sky and surface directions.
        Assert.InRange(sky,1,(int)rays-1);
        // Act
        Assert.True(room.BounceSample(page:8,rays:rays));
        var indirect=room.Read(room.Snapshot.IndirectIrradiance,page:8);
        // The cache encodes view-independent diffuse transport after reserving the projected
        // average specular budget, then quantizes that coefficient to its existing byte channel.
        float budget=VanillaGraphicsExpanded.PBR.Materials.Energy.MicrofacetEnergyTable.Shared.DiffuseAlbedo(
            new(.5f,0,0,new(.25f),new(.04f))).X;
        float expected=32*(MathF.Round(budget*255)/255)*(rays-sky)/rays;
        // Assert
        Assert.InRange(indirect[0],expected-.04f,expected+.04f);
        Assert.Equal(1,indirect[3]);
    }

    #endregion

    #endregion

    #region Private
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
