using System.Collections.Immutable;
using System.Numerics;
using VanillaGraphicsExpanded.PBR.Atmosphere;
using VanillaGraphicsExpanded.PBR.Atmosphere.Celestial;

namespace VanillaGraphicsExpanded.Tests.Unit.Celestial;

/// <summary>Checks how celestial incident power is composed into atmospheric lighting fields.</summary>
public sealed class CelestialLightingCompositionRulesTests
{
    #region Public API
    /// <summary>Source incident power enters the existing diffuse and aerial fields without changing direct light or extinction.</summary>
    [Fact]
    public void ApplyAddsAerialOnceAndPreservesDirectAndDimensionlessFields()
    {
        // Arrange: provide one calibrated star and distinctive direct, diffuse, aerial, and dimensionless fields.
        var source = Source(ImmutableArray<CelestialLightSample>.Empty,
            ImmutableArray.Create(new CelestialStar(0,Vector3.UnitY,0,0,new(1,2,3),.4f)));
        var sky = ImmutableArray.Create(1f,2f,3f,1f);
        var attenuation = ImmutableArray.Create(.2f,.4f,.8f,1f);
        var lighting = new AtmosphereLighting(Vector3.UnitY,new(12),new(2),new(3),new(.9f),sky)
        {
            Lunar=new(4), SkyMie=sky, SkyMoonMie=sky, AerialMie=sky, AerialMoonMie=sky,
            AerialRadiance=sky, AerialAttenuation=attenuation, ExternalTransmission=Transmission(Vector3.One),
            BackgroundRadiance=.001f
        };
        // Act: compose the source into the completed atmosphere lighting snapshot.
        var actual = CelestialDiffuseLighting.Apply(lighting,source,Matrix4x4.Identity);
        var environment = new Vector3(1,2,3)*(.4f/MathF.PI);
        // Assert: celestial power is added once while direct lighting and attenuation remain intact.
        AssertClose(environment,actual.CelestialEnvironment);
        Assert.Equal(lighting.Solar,actual.Solar); Assert.Equal(lighting.Lunar,actual.Lunar);
        Assert.Equal(lighting.Extinction,actual.Extinction); Assert.Equal(lighting.Sky,actual.Sky);
        Assert.Equal(lighting.SkyMie,actual.SkyMie); Assert.Equal(lighting.SkyMoonMie,actual.SkyMoonMie);
        Assert.Equal(lighting.AerialMie,actual.AerialMie); Assert.Equal(lighting.AerialMoonMie,actual.AerialMoonMie);
        Assert.Equal(lighting.AerialAttenuation,actual.AerialAttenuation);
        Assert.Equal(lighting.ExternalTransmission,actual.ExternalTransmission);
        Assert.Equal(lighting.BackgroundRadiance,actual.BackgroundRadiance);
        AssertClose(new Vector3(2)+environment,actual.Environment); AssertClose(new Vector3(3)+environment,actual.Horizon);
        AssertClose(new Vector3(1,2,3)+environment*new Vector3(.2f,.4f,.8f),new(actual.AerialRadiance[0],actual.AerialRadiance[1],actual.AerialRadiance[2]));
        Assert.Equal(1,actual.AerialRadiance[3]); Assert.Same(source,actual.CelestialSource);
    }
    #endregion
    #region Private
    /// <summary>Constructs a minimal immutable source used to isolate diffuse photometry.</summary>
    private static CelestialSource Source(ImmutableArray<CelestialLightSample> samples, ImmutableArray<CelestialStar> stars)
        => new(CelestialQuality.FromLevel(0),stars,samples,1,1,1,1);
    /// <summary>Creates a constant spectral transmission table for controlled lighting composition.</summary>
    private static ImmutableArray<float> Transmission(Vector3 value)
        => ImmutableArray.CreateRange(Enumerable.Range(0,512).SelectMany(_=>new[]{value.X,value.Y,value.Z,1f}));
    /// <summary>Compares RGB channels against a relative bound with a small absolute floor.</summary>
    private static void AssertClose(Vector3 expected, Vector3 actual, float relative=.00001f)
    {
        for(int i=0;i<3;i++)
        {
            float e=i==0?expected.X:i==1?expected.Y:expected.Z;
            float a=i==0?actual.X:i==1?actual.Y:actual.Z;
            Assert.True(Math.Abs(e-a)<=Math.Abs(e)*relative+1e-10f,$"Channel {i}: actual {a:R}, expected {e:R}, relative error {Math.Abs((a-e)/e):R}");
        }
    }
    #endregion
}
