using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.PBR.Clouds;

namespace VanillaGraphicsExpanded.Tests;

/// <summary>Checks current primary controls, direct serialization and finite bounds before GPU publication.</summary>
public sealed class CloudPrimarySettingsTests
{
    #region Public API
    /// <summary>Absent controls use current defaults and explicit current controls survive a save and load.</summary>
    [Theory]
    [InlineData("{}",384,2,50)]
    [InlineData("{\"ViewRaySampleMax\":120,\"ViewRaySampleMin\":7,\"ViewBudgetDistance\":500}",120,7,500)]
    public void CurrentDefaultsAndOverridesRoundTrip(string json,int maximum,int minimum,float distance)
    {
        var value=JsonConvert.DeserializeObject<CloudRenderingSettings>(json)!;
        var quality=value.Capture();quality.Validate();
        Assert.Equal(maximum,quality.ViewRaySampleMax);Assert.Equal(minimum,quality.ViewRaySampleMin);
        Assert.Equal(distance,quality.ViewBudgetDistance);
        Assert.Equal(quality,JsonConvert.DeserializeObject<CloudRenderingSettings>(JsonConvert.SerializeObject(value))!.Capture());
        Assert.Equal(384,JsonConvert.DeserializeObject<VgeConfig>("{}")!.Clouds.Capture().ViewRaySampleMax);
    }

    /// <summary>Unknown properties do not affect current controls or become retained configuration state.</summary>
    [Fact]
    public void UnknownPropertiesAreIgnored()
    {
        const string json="{\"Clouds\":{\"ViewSamples\":1,\"CloudQualityVersion\":99,\"LightSamplingVersion\":99}}";
        var config=JsonConvert.DeserializeObject<VgeConfig>(json)!;
        config.Sanitize();Assert.Equal(CloudVolumeSettings.Default,config.Clouds.Capture());
        JObject saved=JObject.Parse(JsonConvert.SerializeObject(config.Clouds));
        Assert.Null(saved["ViewSamples"]);Assert.Null(saved["CloudQualityVersion"]);Assert.Null(saved["LightSamplingVersion"]);
    }

    /// <summary>Partial current edits retain unedited controls through ordinary configuration population.</summary>
    [Fact]
    public void CurrentPartialUpdatesRetainUneditedControls()
    {
        var config=new VgeConfig();
        config.Clouds.ViewRaySampleMax=192;config.Clouds.ViewRaySampleMin=7;config.Clouds.ViewBudgetDistance=500;
        JsonConvert.PopulateObject("{\"Clouds\":{\"ViewRaySampleMax\":120}}",config);
        var quality=config.Clouds.Capture();
        Assert.Equal(120,quality.ViewRaySampleMax);Assert.Equal(7,quality.ViewRaySampleMin);Assert.Equal(500,quality.ViewBudgetDistance);
    }

    /// <summary>Nonfinite distance and integral extremes are sanitized before uniform-buffer publication.</summary>
    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    public void BoundsBeforeUpload(float distance)
    {
        var value=new CloudRenderingSettings {ViewRaySampleMax=int.MinValue,ViewRaySampleMin=int.MaxValue,ViewBudgetDistance=distance};
        var quality=value.Capture();quality.Validate();
        Assert.Equal(4,quality.ViewRaySampleMax);Assert.Equal(4,quality.ViewRaySampleMin);Assert.Equal(50,quality.ViewBudgetDistance);
        value.ViewRaySampleMax=int.MaxValue;value.ViewRaySampleMin=int.MinValue;value.ViewBudgetDistance=.001f;
        quality=value.Capture();quality.Validate();
        Assert.Equal(384,quality.ViewRaySampleMax);Assert.Equal(1,quality.ViewRaySampleMin);Assert.Equal(50,quality.ViewBudgetDistance);
    }
    #endregion
}
