using System.Collections.Immutable;
using System.Numerics;
using System.Runtime.InteropServices;
using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.PBR.Atmosphere;
using VanillaGraphicsExpanded.PBR.Clouds;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.Tests;

/// <summary>Checks geometry-independent physical admission and shader-local perception before cloud submission.</summary>
public sealed class CloudSimpleLightingTests
{
    #region Public API
    /// <summary>The envelope includes both celestial sources rather than selecting the receiver's single shadowed emitter.</summary>
    [Fact]
    public void IncludesSolarLunarEnvironmentAerialAndLocalFog()
    {
        var lighting = Lighting();
        float bound = CloudSimpleLighting.Maximum(lighting, 7);
        double expected = (120.0 + 3.0) / Math.PI + 20 + 5 + 7;
        Assert.InRange(bound, expected, expected * 1.0001);
        Assert.Throws<ArgumentException>(() => CloudSimpleLighting.Maximum(lighting with { Lunar = new(float.NaN) }, 0));
        Assert.Throws<ArgumentException>(() => CloudSimpleLighting.Maximum(lighting with { LookupStorageScale = 1 }, 0));
        Assert.Throws<ArgumentException>(() => CloudSimpleLighting.Maximum(lighting with { CalibrationGeneration = 0 }, 0));
        Assert.Throws<ArgumentException>(() => CloudSimpleLighting.Maximum(lighting with { Sun = Vector3.Zero }, 0));
        Assert.Throws<ArgumentException>(() => CloudSimpleLighting.Maximum(lighting, float.PositiveInfinity));
    }

    /// <summary>Simple parameters keep perception local, physical source units untouched and lookup decoding explicit.</summary>
    [Fact]
    public void PacksLocalPerceptionAndPhysicalUnits()
    {
        using var parameters = new CloudSimpleParameters();
        float[] identity = [1,0,0,0, 0,1,0,0, 0,0,1,0, 0,0,0,1];
        parameters.CaptureGeometry(identity, identity, new(1,2,3,1000), new(50,100,15,.9f),
            new(2,CloudSimpleParameters.LocalPerception(.25f),60,0),new(4,5,6,0));
        parameters.CaptureLighting(Lighting(), 0, Vector4.Zero, new(.5f), new(.25f));
        var values = MemoryMarshal.Cast<byte,float>(parameters.Bytes);
        Assert.Equal(448, parameters.SizeBytes);
        Assert.Equal(5, values[41]); Assert.Equal(120, values[48]); Assert.Equal(3,values[52]);
        Assert.Equal(20,values[56]); Assert.Equal(16,values[71]);
        Assert.Equal(.5f,values[76]); // Authored local fog is decoded only at its physical mix.
        Assert.Throws<ArgumentOutOfRangeException>(() => CloudSimpleParameters.LocalPerception(float.MaxValue));
    }
    #endregion

    #region Private
    /// <summary>Defines a completed two-source publication with an independent aerial reconstruction bound.</summary>
    private static AtmosphereLighting Lighting() => new(Vector3.UnitY,new(120,90,60),new(20,15,10),Vector3.Zero,
        Vector3.Zero,ImmutableArray<float>.Empty)
    { Lunar = new(3,2,1), Moon = Vector3.UnitZ, CalibrationGeneration=1, LookupStorageScale=LightingStorage.PersistentScale, ReconstructionMaximumNits=5 };
    #endregion
}
