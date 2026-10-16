using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.PBR;

namespace VanillaGraphicsExpanded.Tests;

/// <summary>Constrains shared shading-quality defaults, persisted values and ConfigLib metadata.</summary>
public sealed class ShadingQualityTests
{
    #region Public API
    /// <summary>Saved supported levels survive serialization and sanitization; unsupported values recover the highest default.</summary>
    [Theory]
    [InlineData(-1,3,128)] [InlineData(0,0,16)] [InlineData(1,1,32)]
    [InlineData(2,2,64)] [InlineData(3,3,128)] [InlineData(4,3,128)]
    public void QualityRoundTripsAndSanitizes(int input,int expected,int size)
    {
        var config=JsonConvert.DeserializeObject<VgeConfig>($"{{\"ShadingQuality\":{input}}}")!;
        config.Sanitize();
        Assert.Equal(expected,config.ShadingQuality);
        Assert.Equal(size,ShadingQuality.EnergyLookupSize(config.ShadingQuality));
        var roundTrip=JsonConvert.DeserializeObject<VgeConfig>(JsonConvert.SerializeObject(config))!;
        Assert.Equal(expected,roundTrip.ShadingQuality);
    }

    /// <summary>Missing preferences retain the maximum tier and the public control advertises the same four levels.</summary>
    [Fact]
    public void DefaultAndPublicControlAgree()
    {
        Assert.Equal(3,new VgeConfig().ShadingQuality);
        Assert.Equal(3,JsonConvert.DeserializeObject<VgeConfig>("{}")!.ShadingQuality);
        var document=JObject.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"assets/config/configlib-patches.json")));
        var setting=document.Descendants().OfType<JObject>().Single(value=>value["name"]?.Value<string>()=="ShadingQuality");
        Assert.Equal(3,setting["default"]!.Value<int>());
        Assert.Equal(new[]{0,1,2,3},setting["values"]!.Values<int>());
        Assert.Equal("ShadingQuality",setting["code"]!.Value<string>());
    }
    #endregion
}
