using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Runtime.InteropServices;
using VanillaGraphicsExpanded.PBR.Clouds;

namespace VanillaGraphicsExpanded.Tests;

/// <summary>Checks finite primary termination controls, persistence and unchanged parameter layout.</summary>
public sealed class CloudTerminationSettingsTests
{
    #region Public API
    /// <summary>Missing fields use the current default without a compatibility converter.</summary>
    [Fact]
    public void MissingThresholdUsesAuthoredDefault()
    {
        var value=JsonConvert.DeserializeObject<CloudRenderingSettings>("{}")!;
        Assert.Equal(.005f,value.Capture().StopTransmittance);
        Assert.Equal(.005f,(float)JObject.Parse(JsonConvert.SerializeObject(value))["StopTransmittance"]!);
    }

    /// <summary>Finite selections round-trip and occupy the final spare float in the existing uniform block.</summary>
    [Theory]
    [InlineData(0f)][InlineData(.00390625f)][InlineData(.005f)]
    public void ThresholdRoundTripsAndPacksWithoutChangingOtherBudgets(float threshold)
    {
        var value=new CloudRenderingSettings {StopTransmittance=threshold};
        var captured=JsonConvert.DeserializeObject<CloudRenderingSettings>(JsonConvert.SerializeObject(value))!.Capture();
        captured.Validate();Assert.Equal(threshold,captured.StopTransmittance);
        using var parameters=new CloudVolumeParameters();parameters.CaptureMap(20,default,1,captured);
        Assert.Equal(208,parameters.Bytes.Length);
        var values=MemoryMarshal.Cast<byte,float>(parameters.Bytes);
        Assert.Equal(threshold,values[51]);Assert.Equal(384,values[12]);Assert.Equal(10,values[13]);
        Assert.Equal(2,values[48]);Assert.Equal(50,values[49]);Assert.Equal(1500,values[50]);
    }

    /// <summary>Persisted invalid inputs clamp or restore defaults while internal invalid selections reject.</summary>
    [Theory]
    [InlineData(-1f,0f)][InlineData(1f,.005f)]
    [InlineData(float.NaN,.005f)][InlineData(float.PositiveInfinity,.005f)][InlineData(float.NegativeInfinity,.005f)]
    public void InvalidThresholdsCannotReachShaderArithmetic(float input,float expected)
    {
        var value=new CloudRenderingSettings {StopTransmittance=input}.Capture();
        Assert.Equal(expected,value.StopTransmittance);value.Validate();
        Assert.Throws<ArgumentOutOfRangeException>(()=>(CloudVolumeSettings.Default with {StopTransmittance=input}).Validate());
    }

    /// <summary>Ordinary partial updates preserve controls that the user did not edit.</summary>
    [Fact]
    public void PartialUpdatesRetainThresholdAndOtherBudgets()
    {
        var value=new CloudRenderingSettings {StopTransmittance=0,ShadowDistance=3000};
        JsonConvert.PopulateObject("{\"ViewRaySampleMax\":96}",value);
        var captured=value.Capture();Assert.Equal(0,captured.StopTransmittance);
        Assert.Equal(96,captured.ViewRaySampleMax);Assert.Equal(3000,captured.ShadowDistance);
    }
    #endregion
}
