using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Runtime.InteropServices;
using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.PBR.Clouds;

namespace VanillaGraphicsExpanded.Tests;

/// <summary>Checks clean-break secondary controls and their finite shader-publication contract.</summary>
public sealed class CloudSecondarySettingsTests
{
    #region Public API
    /// <summary>Missing and obsolete fields use current defaults without converting per-column counts.</summary>
    [Theory]
    [InlineData("{}")]
    [InlineData("{\"Clouds\":{}}")]
    [InlineData("{\"Clouds\":{\"LightSamples\":1}}")]
    [InlineData("{\"Clouds\":{\"LightSamples\":4}}")]
    public void MissingAndObsoleteControlsUseCurrentDefaults(string json)
    {
        var config=JsonConvert.DeserializeObject<VgeConfig>(json)!;
        config.Sanitize();
        Assert.Equal(CloudVolumeSettings.Default,config.Clouds.Capture());
        JObject saved=JObject.Parse(JsonConvert.SerializeObject(config.Clouds));
        Assert.Null(saved["LightSamples"]);
        Assert.Equal(10,(int)saved["LightRaySampleMax"]!);
        Assert.Equal(1500,(float)saved["ShadowDistance"]!);
    }

    /// <summary>Explicit bounded controls survive serialization and publish in distinct existing uniform slots.</summary>
    [Theory]
    [InlineData(2,50)]
    [InlineData(10,1500)]
    [InlineData(64,10000)]
    public void CurrentControlsRoundTripAndPack(int count,float distance)
    {
        var settings=new CloudRenderingSettings { LightRaySampleMax=count,ShadowDistance=distance };
        var quality=JsonConvert.DeserializeObject<CloudRenderingSettings>(JsonConvert.SerializeObject(settings))!.Capture();
        quality.Validate();
        Assert.Equal(count,quality.LightRaySampleMax);Assert.Equal(distance,quality.ShadowDistance);
        using var parameters=new CloudVolumeParameters();
        parameters.CaptureMap(20,default,1,quality);
        var values=MemoryMarshal.Cast<byte,float>(parameters.Bytes);
        Assert.Equal(count,values[13]);Assert.Equal(distance,values[50]);
        Assert.Equal(384,values[12]);Assert.Equal(2,values[48]);Assert.Equal(50,values[49]);
    }

    /// <summary>Integral extremes and unsupported finite distances clamp before immutable capture.</summary>
    [Theory]
    [InlineData(int.MinValue,-1,2,50)]
    [InlineData(int.MaxValue,10001,64,10000)]
    public void InvalidFiniteControlsClamp(int count,float distance,int expectedCount,float expectedDistance)
    {
        var value=new CloudRenderingSettings { LightRaySampleMax=count,ShadowDistance=distance }.Capture();
        value.Validate();
        Assert.Equal(expectedCount,value.LightRaySampleMax);Assert.Equal(expectedDistance,value.ShadowDistance);
    }

    /// <summary>Nonfinite distances restore the authored physical span rather than reaching shader arithmetic.</summary>
    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    public void NonfiniteDistanceRestoresDefault(float distance)
    {
        var value=new CloudRenderingSettings { ShadowDistance=distance }.Capture();
        value.Validate();Assert.Equal(1500,value.ShadowDistance);
    }

    /// <summary>Ordinary partial configuration updates retain all unedited current controls.</summary>
    [Fact]
    public void PartialCurrentUpdatesRetainOtherControls()
    {
        var config=new VgeConfig();
        config.Clouds.LightRaySampleMax=32;config.Clouds.ShadowDistance=3000;config.Clouds.ViewRaySampleMax=192;
        JsonConvert.PopulateObject("{\"Clouds\":{\"LightRaySampleMax\":12}}",config);
        var quality=config.Clouds.Capture();
        Assert.Equal(12,quality.LightRaySampleMax);Assert.Equal(3000,quality.ShadowDistance);
        Assert.Equal(192,quality.ViewRaySampleMax);
    }
    #endregion
}
