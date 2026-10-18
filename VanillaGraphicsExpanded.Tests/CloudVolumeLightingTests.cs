using System.Collections.Immutable;
using System.Numerics;
using System.Runtime.InteropServices;
using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.PBR.Atmosphere;
using VanillaGraphicsExpanded.PBR.Clouds;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.Tests;

/// <summary>Checks finite work controls, material-coordinate anchoring and pre-map physical source admission.</summary>
public sealed class CloudVolumeLightingTests
{
    #region Public API
    /// <summary>The frozen envelope bounds both directional phase peaks, one extra octave, environment and medium response.</summary>
    [Fact]
    public void BoundsBothSourcesAndOneOctaveBeforeMapGeneration()
    {
        var lighting=Lighting(); var settings=CloudVolumeSettings.Default;
        float bound=CloudVolumeLighting.Maximum(lighting,7,settings);
        double phase=1.6/(4*Math.PI*.16)+.5/(4*Math.PI);
        double expected=123*phase+20+5+7;
        Assert.InRange(bound,expected,expected*1.003);
        Assert.True(bound>CloudVolumeLighting.Maximum(lighting,7,settings with { OctaveContribution=0 }));
        Assert.Throws<ArgumentException>(()=>CloudVolumeLighting.Maximum(lighting with { Lunar=new(float.NaN) },7,settings));
        Assert.Throws<ArgumentException>(()=>CloudVolumeLighting.Maximum(lighting with { LookupStorageScale=1 },7,settings));
        Assert.Throws<ArgumentException>(()=>CloudVolumeLighting.Maximum(lighting,float.PositiveInfinity,settings));
    }

    /// <summary>Unsupported counts, amplitudes and coefficients are rejected rather than admitted with an invalid bound.</summary>
    [Fact]
    public void ControlsHaveFiniteExplicitLimits()
    {
        foreach(var settings in new[]{CloudVolumeSettings.Default with { LightRaySampleMax=1 },
            CloudVolumeSettings.Default with { LightRaySampleMax=65 },CloudVolumeSettings.Default with { ShadowDistance=49 },
            CloudVolumeSettings.Default with { ShadowDistance=10001 },CloudVolumeSettings.Default with { ShadowDistance=float.NaN },
            CloudVolumeSettings.Default with { ShadowDistance=float.PositiveInfinity },CloudVolumeSettings.Default with { DetailAmplitude=1 },
            CloudVolumeSettings.Default with { ViewRaySampleMax=3 },CloudVolumeSettings.Default with { ViewRaySampleMax=385 },
            CloudVolumeSettings.Default with { ViewRaySampleMin=0 },CloudVolumeSettings.Default with { ViewRaySampleMin=17 },
            CloudVolumeSettings.Default with { ViewBudgetDistance=float.NaN },CloudVolumeSettings.Default with { ViewBudgetDistance=49 },
            CloudVolumeSettings.Default with { EdgeWidth=float.NaN },CloudVolumeSettings.Default with { NoiseScale=0 },
            CloudVolumeSettings.Default with { OctaveContribution=-1 },CloudVolumeSettings.Default with { OctaveOcclusion=float.PositiveInfinity }})
            Assert.Throws<ArgumentOutOfRangeException>(()=>settings.Validate());
    }

    /// <summary>Placement copies include metres and integer wind anchoring but no extra fractional-wind translation.</summary>
    [Fact]
    public void PacksPhysicalUnitsAndStableMaterialAnchor()
    {
        using var parameters=new CloudVolumeParameters();
        var placement=new CloudPlacement(100,200,0,0,3,4,.25,.5,new(1,2,3),0);
        parameters.CaptureMap(20,placement,2,CloudVolumeSettings.Default);
        parameters.CaptureLighting(Lighting(),0,Vector4.Zero,Vector3.Zero);
        float[] before=MemoryMarshal.Cast<byte,float>(parameters.Bytes).ToArray();
        Assert.Equal(208,parameters.SizeBytes); Assert.Equal(2,before[6]); Assert.Equal(120,before[16]);
        Assert.Equal(3,before[20]); Assert.Equal(16,before[39]);
        Assert.Equal(10,before[13]); Assert.Equal(1500,before[50]);
        Assert.Equal(384,before[12]); Assert.Equal(2,before[48]); Assert.Equal(50,before[49]);
        parameters.CaptureMap(20,placement with { WindX=123,WindZ=456 },2,CloudVolumeSettings.Default);
        Assert.Equal(before[4],MemoryMarshal.Cast<byte,float>(parameters.Bytes)[4]);
        parameters.CaptureMap(20,placement with { CenterX=101 },2,CloudVolumeSettings.Default);
        Assert.Equal(50,MemoryMarshal.Cast<byte,float>(parameters.Bytes)[4]-before[4]);
        Assert.Throws<ArgumentException>(()=>parameters.CaptureMap(20,placement,0,CloudVolumeSettings.Default));
    }
    #endregion

    #region Private
    /// <summary>Supplies a completed immutable publication independent of native map colors.</summary>
    private static AtmosphereLighting Lighting() => new(Vector3.UnitY,new(120,90,60),new(20,15,10),Vector3.Zero,
        Vector3.Zero,ImmutableArray<float>.Empty)
    { Lunar=new(3,2,1),Moon=-Vector3.UnitY,CalibrationGeneration=1,LookupStorageScale=LightingStorage.PersistentScale,ReconstructionMaximumNits=5 };
    #endregion
}
