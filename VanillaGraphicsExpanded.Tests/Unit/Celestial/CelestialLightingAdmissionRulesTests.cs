using System.Collections.Immutable;
using System.Numerics;
using VanillaGraphicsExpanded.PBR.Atmosphere.Celestial;

namespace VanillaGraphicsExpanded.Tests.Unit.Celestial;

/// <summary>Checks invalid transforms, incomplete transmission data, and cancellation at the diffuse-lighting admission boundary.</summary>
public sealed class CelestialLightingAdmissionRulesTests
{
    #region Public API
    /// <summary>Unsupported transforms, incomplete attenuation and cancelled work cannot yield a valid source publication.</summary>
    [Fact]
    public void InvalidInputsAndCancellationRejectWork()
    {
        // Arrange: start with a valid source and complete spectral transmission table.
        var source = Source(ImmutableArray<CelestialLightSample>.Empty,
            ImmutableArray.Create(new CelestialStar(0,Vector3.UnitY,0,0,Vector3.One,1)));
        var table = Transmission(Vector3.One);
        // Act: call the lighting boundary with each malformed input and a cancelled token.
        // Assert: each call throws its documented rejection exception.
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
    /// <summary>Constructs a minimal immutable source used to isolate diffuse photometry.</summary>
    private static CelestialSource Source(ImmutableArray<CelestialLightSample> samples, ImmutableArray<CelestialStar> stars)
        => new(CelestialQuality.FromLevel(0),stars,samples,1,1,1,1);
    /// <summary>Creates a constant spectral transmission table for controlled source integration.</summary>
    private static ImmutableArray<float> Transmission(Vector3 value)
        => ImmutableArray.CreateRange(Enumerable.Range(0,512).SelectMany(_=>new[]{value.X,value.Y,value.Z,1f}));
    #endregion
}
