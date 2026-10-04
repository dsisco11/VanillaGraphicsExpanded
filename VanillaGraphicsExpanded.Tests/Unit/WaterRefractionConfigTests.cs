using System.Reflection;
using Newtonsoft.Json.Linq;
using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.ModSystems;
using Vintagestory.API.Datastructures;

namespace VanillaGraphicsExpanded.Tests.Unit;

/// <summary>Protects water setting persistence and ConfigLib's numeric event contract.</summary>
[Trait("Category", "Unit")]
public sealed class WaterRefractionConfigTests
{
    #region Public API
    /// <summary>Stable numeric identifiers round-trip independently of the existing enable flag.</summary>
    [Theory]
    [InlineData(false,0,1)]
    [InlineData(true,0,2)]
    [InlineData(true,1,1)]
    [InlineData(false,2,2)]
    [InlineData(true,3,1)]
    [InlineData(false,3,2)]
    public void NumericChoicesPreserveSavedEnable(bool enabled, int quality, int scale)
    {
        var config = new VgeConfig { WaterRefractionEnabled = enabled, WaterRefractionQuality = quality,
            WaterRefractionBackgroundScale = scale };
        var saved = JObject.FromObject(config);
        Assert.Equal(JTokenType.Integer,saved[nameof(config.WaterRefractionQuality)]!.Type);
        Assert.Equal(JTokenType.Integer,saved[nameof(config.WaterRefractionBackgroundScale)]!.Type);
        var restored = saved.ToObject<VgeConfig>()!;
        restored.Sanitize();
        Assert.Equal(enabled,restored.WaterRefractionEnabled);
        Assert.Equal(quality,restored.WaterRefractionQuality);
        Assert.Equal(scale,restored.WaterRefractionBackgroundScale);
    }

    /// <summary>Unknown identifiers reset to safe defaults without enabling or disabling saved water behavior.</summary>
    [Theory]
    [InlineData(-1,0)]
    [InlineData(4,3)]
    [InlineData(int.MaxValue,int.MinValue)]
    public void InvalidSelectionsResetToDefaults(int quality, int scale)
    {
        var config = new VgeConfig { WaterRefractionEnabled = true, WaterRefractionQuality = quality,
            WaterRefractionBackgroundScale = scale };
        config.Sanitize();
        Assert.True(config.WaterRefractionEnabled);
        Assert.Equal(3,config.WaterRefractionQuality);
        Assert.Equal(2,config.WaterRefractionBackgroundScale);
    }

    /// <summary>Shipped controls form a separate group and retain numeric choices rather than string mappings.</summary>
    [Fact]
    public void WaterControlsUseOwnGroupAndNumericChoices()
    {
        var definition = Definition();
        var settings = ((JObject)definition["settings"]!).Properties()
            .SelectMany(category => ((JObject)category.Value).Properties())
            .ToDictionary(setting => setting.Value["code"]!.Value<string>()!,setting => setting.Value);
        var separator = definition["formatting"]!.Single(item => item["title"]!.Value<string>() == "vanillagraphicsexpanded:config-section-water");
        Assert.Equal(.1,separator["weight"]!.Value<double>());
        string[] codes = [nameof(VgeConfig.WaterRefractionEnabled),nameof(VgeConfig.WaterRefractionQuality),nameof(VgeConfig.WaterRefractionBackgroundScale)];
        for (int index = 0; index < codes.Length; index++)
        {
            var setting = settings[codes[index]];
            Assert.Equal(codes[index],setting["name"]!.Value<string>());
            Assert.True(setting["clientSide"]!.Value<bool>());
            Assert.Equal((index + 2) / 10.0,setting["weight"]!.Value<double>());
            Assert.Null(setting["mapping"]); Assert.Null(setting["range"]);
        }
        Assert.Equal(new[] {0,3},settings[codes[1]]["values"]!.Values<int>());
        Assert.Equal(new[] {1,2},settings[codes[2]]["values"]!.Values<int>());
        Assert.Equal(3,settings[codes[1]]["default"]!.Value<int>());
        Assert.Equal(2,settings[codes[2]]["default"]!.Value<int>());
    }

    /// <summary>ConfigLib's actual shipped keys apply live values to the root config fields.</summary>
    [Theory]
    [InlineData(0,2)]
    [InlineData(3,1)]
    public void ShippedEventKeysApplyLiveSettings(int quality, int scale)
    {
        var definition = Definition();
        var controls = ((JObject)definition["settings"]!).Properties()
            .SelectMany(category => ((JObject)category.Value).Properties())
            .Where(setting => setting.Value["code"]!.Value<string>()!.StartsWith("WaterRefraction"))
            .ToArray();
        var mapping = controls.ToDictionary(setting => setting.Name,setting => setting.Value["code"]!.Value<string>()!);
        var config = new VgeConfig();
        var method = typeof(ConfigModSystem).GetMethod("ApplyConfigLibSettingsToModConfig",BindingFlags.NonPublic | BindingFlags.Static)!;
        foreach (var control in controls)
        {
            string code = mapping[control.Name];
            string value = code == nameof(VgeConfig.WaterRefractionEnabled) ? "true"
                : (code == nameof(VgeConfig.WaterRefractionQuality) ? quality : scale).ToString();
            var data = new TreeAttribute(); data.SetString("MappingKey",control.Name); data.SetString("Value",value);
            object?[] args = [config,data,mapping,null];
            Assert.Equal(1,method.Invoke(null,args));
        }
        Assert.True(config.WaterRefractionEnabled);
        Assert.Equal(quality,config.WaterRefractionQuality);
        Assert.Equal(scale,config.WaterRefractionBackgroundScale);
    }
    #endregion

    #region Private
    /// <summary>Reads the shipped ConfigLib definition copied by the normal test build.</summary>
    private static JObject Definition() => JObject.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"assets/config/configlib-patches.json")));
    #endregion
}
