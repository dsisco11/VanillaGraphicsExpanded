using System.Collections.Immutable;
using System.Numerics;
using VanillaGraphicsExpanded.PBR.Atmosphere;
using VanillaGraphicsExpanded.PBR.Atmosphere.Celestial;

namespace VanillaGraphicsExpanded.Tests.Component.Celestial;

/// <summary>Checks complete-source diffuse lighting and its independently refined directional integral.</summary>
public sealed class CelestialDiffuseLightingTests
{
    #region Public API
    /// <summary>RGB attenuation, orientation and cloud coverage are applied once to all catalog entries irrespective of display prefix.</summary>
    [Fact]
    public void FullSourcePowerIsQualityInvariantAndCloudFactorIsAppliedOnce()
    {
        // Arrange: select two display qualities, a rotated frame, and wavelength-dependent attenuation.
        var low = CelestialSource.Create(CelestialQuality.FromLevel(0));
        var high = CelestialSource.Create(CelestialQuality.FromLevel(3));
        var rotation = Matrix4x4.CreateRotationZ(.6f)*Matrix4x4.CreateRotationY(.7f);
        var transmission = Transmission(new(.2f,.4f,.8f));
        // Act: integrate the complete source at both qualities.
        var clear = CelestialDiffuseLighting.Integrate(low, rotation, transmission, 0, 0);
        var highPower = CelestialDiffuseLighting.Integrate(high, rotation, transmission, 0, 0);
        // Assert: display quality does not change source power.
        Assert.Equal(clear, highPower);
        // Act/Assert: applying partial cloud coverage scales the clear result exactly once.
        AssertClose(clear*.55f, CelestialDiffuseLighting.Integrate(low, rotation, transmission, 0, .5f));
        // Act: independently sum every quadrature sample and catalog star.
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
        // Assert: the independent complete-source sum matches the integrated result.
        AssertClose(expected, clear, .00002f);
    }

    /// <summary>Actual galaxy quadrature agrees within one percent RGB with independently refined media and orientation integrals.</summary>
    [Theory]
    [InlineData(0f,0f,0f,1f)] [InlineData(.7f,.4f,.001f,1f)] [InlineData(-.8f,1.2f,1f,6.25f)]
    public void GalaxyDirectionalQuadratureConverges(float xRotation, float zRotation, float altitude, float aerosol)
    {
        // Arrange: capture one complete source under the selected orientation and atmosphere profile.
        var source = CelestialSource.Create(CelestialQuality.FromLevel(3));
        var rotation = Matrix4x4.CreateRotationX(xRotation)*Matrix4x4.CreateRotationZ(zRotation);
        var table = AtmosphereExternalTransmission.Build(altitude,aerosol);
        float horizon = AtmosphereSkyMapping.Horizon(altitude);
        var galaxy = source with { Stars = ImmutableArray<CelestialStar>.Empty };
        // Act: integrate the production quadrature and an independently sampled higher-resolution grid.
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
        // Assert: the independently refined RGB integral agrees within one percent.
        AssertClose(new((float)r,(float)g,(float)b),actual,.01f);
    }
    #endregion
    #region Private
    /// <summary>Creates a constant spectral transmission table to isolate complete-source integration.</summary>
    private static ImmutableArray<float> Transmission(Vector3 value)
        => ImmutableArray.CreateRange(Enumerable.Range(0,512).SelectMany(_=>new[]{value.X,value.Y,value.Z,1f}));
    /// <summary>Compares RGB channels against the selected relative integration tolerance.</summary>
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
