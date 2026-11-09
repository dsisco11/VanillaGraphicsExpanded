using System.Collections.Immutable;
using System.Numerics;
using VanillaGraphicsExpanded.PBR.Atmosphere.Celestial;

namespace VanillaGraphicsExpanded.Tests.Unit.Celestial;

/// <summary>Checks the cosine-weighted diffuse integral against closed-form constant radiance and point-source flux.</summary>
public sealed class CelestialDiffuseQuadratureRulesTests
{
    #region Public API
    /// <summary>Exact cosine and solid-angle weights recover constant radiance and catalog flux, once per source.</summary>
    [Fact]
    public void ConstantRadianceAndPointFluxHaveKnownHorizontalIntegral()
    {
        // Arrange: build constant radiance over the cube and add one upward point source.
        var samples = ImmutableArray.CreateBuilder<CelestialLightSample>();
        const int size = 64;
        for (int face = 0; face < 6; face++) for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
            samples.Add(new(CelestialCubeGeometry.Direction(face, 2f*(x+.5f)/size-1, 2f*(y+.5f)/size-1),
                new(2,3,4), CelestialCubeGeometry.SolidAngle(x,y,size)));
        var star = new CelestialStar(0, Vector3.UnitY, 0, 0, new(1,2,3), .4f);
        var source = Source(samples.ToImmutable(), ImmutableArray.Create(star));
        // Act: integrate both the full source and the point-source-only case.
        Vector3 actual = CelestialDiffuseLighting.Integrate(source, Matrix4x4.Identity, Transmission(Vector3.One), 0, 0);
        var starOnly = source with { Quadrature = ImmutableArray<CelestialLightSample>.Empty };
        Vector3 belowHorizon = CelestialDiffuseLighting.Integrate(starOnly,
            Matrix4x4.CreateRotationX(MathF.PI), Transmission(Vector3.One), 0, 0);
        // Assert: the closed-form integral is recovered and a downward point source contributes nothing.
        AssertClose(new Vector3(2,3,4)+star.Color*(star.FluxLux/MathF.PI), actual, .0003f);
        AssertClose(Vector3.Zero, belowHorizon);
    }
    #endregion
    #region Private
    /// <summary>Constructs a minimal immutable source used to isolate diffuse photometry.</summary>
    private static CelestialSource Source(ImmutableArray<CelestialLightSample> samples, ImmutableArray<CelestialStar> stars)
        => new(CelestialQuality.FromLevel(0),stars,samples,1,1,1,1);
    /// <summary>Creates a constant spectral transmission table for controlled source integration.</summary>
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
