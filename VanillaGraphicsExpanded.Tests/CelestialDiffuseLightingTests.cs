using System.Collections.Immutable;
using System.Numerics;
using VanillaGraphicsExpanded.PBR.Atmosphere;
using VanillaGraphicsExpanded.PBR.Atmosphere.Celestial;

namespace VanillaGraphicsExpanded.Tests;

/// <summary>Checks complete-source incident power independently of display quality and atmospheric storage.</summary>
public sealed class CelestialDiffuseLightingTests
{
    #region Public API
    /// <summary>Exact cosine and solid-angle weights recover constant radiance and catalog flux, once per source.</summary>
    [Fact]
    public void ConstantRadianceAndPointFluxHaveKnownHorizontalIntegral()
    {
        var samples = ImmutableArray.CreateBuilder<CelestialLightSample>();
        const int size = 64;
        for (int face = 0; face < 6; face++) for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
            samples.Add(new(CelestialCubeGeometry.Direction(face, 2f*(x+.5f)/size-1, 2f*(y+.5f)/size-1),
                new(2,3,4), CelestialCubeGeometry.SolidAngle(x,y,size)));
        var star = new CelestialStar(0, Vector3.UnitY, 0, 0, new(1,2,3), .4f);
        var source = Source(samples.ToImmutable(), ImmutableArray.Create(star));
        AssertClose(new Vector3(2,3,4)+star.Color*(star.FluxLux/MathF.PI),
            CelestialDiffuseLighting.Integrate(source, Matrix4x4.Identity, Transmission(Vector3.One), 0, 0), .0003f);
        var starOnly = source with { Quadrature = ImmutableArray<CelestialLightSample>.Empty };
        AssertClose(Vector3.Zero, CelestialDiffuseLighting.Integrate(starOnly,
            Matrix4x4.CreateRotationX(MathF.PI), Transmission(Vector3.One), 0, 0));
    }

    /// <summary>RGB attenuation, orientation and cloud coverage are applied once to all catalog entries irrespective of display prefix.</summary>
    [Fact]
    public void FullSourcePowerIsQualityInvariantAndCloudFactorIsAppliedOnce()
    {
        var low = CelestialSource.Create(CelestialQuality.FromLevel(0));
        var high = CelestialSource.Create(CelestialQuality.FromLevel(3));
        var rotation = Matrix4x4.CreateRotationZ(.6f)*Matrix4x4.CreateRotationY(.7f);
        var transmission = Transmission(new(.2f,.4f,.8f));
        var clear = CelestialDiffuseLighting.Integrate(low, rotation, transmission, 0, 0);
        var highPower = CelestialDiffuseLighting.Integrate(high, rotation, transmission, 0, 0);
        Assert.Equal(clear, highPower);
        AssertClose(clear*.55f, CelestialDiffuseLighting.Integrate(low, rotation, transmission, 0, .5f));
        Vector3 expected = Vector3.Zero;
        foreach (var sample in low.Quadrature)
        {
            float cosine = Math.Max(Vector3.TransformNormal(sample.Direction, rotation).Y,0);
            expected += sample.Radiance * new Vector3(.2f,.4f,.8f) * (sample.SolidAngle*cosine/MathF.PI);
        }
        foreach (var star in low.Stars)
        {
            float cosine = Math.Max(Vector3.TransformNormal(star.Direction, rotation).Y,0);
            expected += star.Color * new Vector3(.2f,.4f,.8f) * (star.FluxLux*cosine/MathF.PI);
        }
        AssertClose(expected, clear, .00002f);
    }

    /// <summary>Actual galaxy quadrature agrees within one percent RGB with independently refined media and orientation integrals.</summary>
    [Theory]
    [InlineData(0f,0f,0f,1f)] [InlineData(.7f,.4f,.001f,1f)] [InlineData(-.8f,1.2f,1f,6.25f)]
    public void GalaxyDirectionalQuadratureConverges(float xRotation, float zRotation, float altitude, float aerosol)
    {
        var source = CelestialSource.Create(CelestialQuality.FromLevel(3));
        var rotation = Matrix4x4.CreateRotationX(xRotation)*Matrix4x4.CreateRotationZ(zRotation);
        var table = AtmosphereExternalTransmission.Build(altitude,aerosol);
        float horizon = AtmosphereSkyMapping.Horizon(altitude);
        var galaxy = source with { Stars = ImmutableArray<CelestialStar>.Empty };
        Vector3 actual = CelestialDiffuseLighting.Integrate(galaxy, rotation, table, horizon, 0);
        double r=0,g=0,b=0;
        const int size=128;
        for (int face=0;face<6;face++) for(int y=0;y<size;y++) for(int x=0;x<size;x++)
        {
            Vector3 direction = CelestialCubeGeometry.Direction(face,2f*(x+.5f)/size-1,2f*(y+.5f)/size-1);
            Vector3 world = Vector3.TransformNormal(direction,rotation);
            if(world.Y<=0) continue;
            Vector3 radiance = CelestialProceduralField.Evaluate(direction,7,2f/size)*source.GalaxyAmplitude;
            Vector3 t = AtmosphereExternalTransmission.Sample(table,MathF.Asin(Math.Clamp(world.Y,-1,1)),horizon);
            double weight = CelestialCubeGeometry.SolidAngle(x,y,size)*(double)world.Y/Math.PI;
            r+=radiance.X*t.X*weight; g+=radiance.Y*t.Y*weight; b+=radiance.Z*t.Z*weight;
        }
        AssertClose(new((float)r,(float)g,(float)b),actual,.01f);
    }

    /// <summary>Source incident power enters the existing diffuse and aerial fields without changing direct light or extinction.</summary>
    [Fact]
    public void ApplyAddsAerialOnceAndPreservesDirectAndDimensionlessFields()
    {
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
        var actual = CelestialDiffuseLighting.Apply(lighting,source,Matrix4x4.Identity);
        var environment = new Vector3(1,2,3)*(.4f/MathF.PI);
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

    /// <summary>Unsupported transforms, incomplete attenuation and cancelled work cannot yield a valid source publication.</summary>
    [Fact]
    public void InvalidInputsAndCancellationRejectWork()
    {
        var source = Source(ImmutableArray<CelestialLightSample>.Empty,
            ImmutableArray.Create(new CelestialStar(0,Vector3.UnitY,0,0,Vector3.One,1)));
        var table = Transmission(Vector3.One);
        Assert.Throws<ArgumentException>(()=>CelestialDiffuseLighting.Integrate(source,Matrix4x4.CreateScale(2),table,0,0));
        Assert.Throws<ArgumentException>(()=>CelestialDiffuseLighting.Integrate(source,Matrix4x4.CreateTranslation(1,0,0),table,0,0));
        Assert.Throws<ArgumentException>(()=>CelestialDiffuseLighting.Integrate(source,Matrix4x4.Identity,table,0,float.NaN));
        Assert.Throws<ArgumentException>(()=>CelestialDiffuseLighting.Integrate(source,Matrix4x4.Identity,ImmutableArray<float>.Empty,0,0));
        foreach(float invalid in new[]{float.NaN,float.PositiveInfinity,-.1f,1.01f})
        {
            float[] data=table.ToArray(); data[3]=invalid;
            Assert.Throws<ArgumentException>(()=>CelestialDiffuseLighting.Integrate(source,Matrix4x4.Identity,ImmutableArray.Create(data),0,0));
        }
        foreach(float invalid in new[]{float.NaN,float.PositiveInfinity,.1f,-2f})
            Assert.Throws<ArgumentException>(()=>CelestialDiffuseLighting.Integrate(source,Matrix4x4.Identity,table,invalid,0));
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(()=>CelestialDiffuseLighting.Integrate(source,Matrix4x4.Identity,table,0,0,cancelled.Token));
    }
    #endregion

    #region Private
    /// <summary>Constructs a minimal immutable physical source without display generation.</summary>
    private static CelestialSource Source(ImmutableArray<CelestialLightSample> samples, ImmutableArray<CelestialStar> stars)
        => new(CelestialQuality.FromLevel(0),stars,samples,1,1,1,1);
    /// <summary>Creates a constant spectral transmission table to isolate angular and photometric integration.</summary>
    private static ImmutableArray<float> Transmission(Vector3 value)
        => ImmutableArray.CreateRange(Enumerable.Range(0,512).SelectMany(_=>new[]{value.X,value.Y,value.Z,1f}));
    /// <summary>Compares channels using a relative bound with only a tiny numerical zero allowance.</summary>
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

