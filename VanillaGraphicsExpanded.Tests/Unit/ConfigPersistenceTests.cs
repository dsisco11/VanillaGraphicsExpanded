using System.Reflection;
using VanillaGraphicsExpanded.PBR.Tessellation;
using Moq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.ModSystems;
using VanillaGraphicsExpanded.PBR.Atmosphere;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.Tests.Unit;

/// <summary>Protects current configuration paths and ConfigLib quality serialization across reloads.</summary>
public sealed class ConfigPersistenceTests
{
    #region Document preparation
    /// <summary>All current settings acquire concrete nested leaves while saved and unrelated values survive.</summary>
    [Fact]
    public void SparseDocumentGainsCurrentPathsWithoutOverwritingValues()
    {
        var document = JObject.Parse("""
            { "LumOn": { "Enabled": false }, "Atmosphere": {}, "MaterialAtlas": {},
              "unrelated": 7, "MaterialAtlas/TerrainSubdivision/MaximumLevel": 32 }
            """);
        var defaults = JObject.FromObject(new VgeConfig());
        Assert.True(ConfigDocumentDefaults.FillMissing(document, defaults));
        Assert.False(document["LumOn"]!["Enabled"]!.Value<bool>());
        Assert.Equal(7, document["unrelated"]!.Value<int>());
        Assert.Equal(32, document["MaterialAtlas/TerrainSubdivision/MaximumLevel"]!.Value<int>());
        Assert.Equal(defaults["MaterialAtlas"]!["TerrainSubdivision"]!["MaximumLevel"], document["MaterialAtlas"]!["TerrainSubdivision"]!["MaximumLevel"]);
        Assert.NotNull(document["Atmosphere"]!["SkyLutQuality"]);
        var definition = JObject.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "assets/config/configlib-patches.json")));
        foreach (var category in ((JObject)definition["settings"]!).Properties())
        foreach (var setting in ((JObject)category.Value).Properties())
        {
            JToken? value = document;
            foreach (string part in setting.Value["name"]!.Value<string>()!.Split('/')) value = value?[part];
            Assert.NotNull(value);
        }
        Assert.False(ConfigDocumentDefaults.FillMissing(document, defaults));
    }

    /// <summary>World shutdown permits the next startup to read persisted values again.</summary>
    [Theory]
    [InlineData(false, 0, 1)]
    [InlineData(true, 0, 2)]
    [InlineData(false, 1, 1)]
    [InlineData(true, 1, 2)]
    [InlineData(false, 2, 1)]
    [InlineData(true, 2, 2)]
    [InlineData(false, 3, 1)]
    [InlineData(true, 3, 2)]
    public void DisposeAllowsReloadOfStoredSettings(bool enabled, int quality, int scale)
    {
        using var lifetime = new ConfigLifetime();
        var system = lifetime.System;
        system.Dispose();
        var api = new Mock<ICoreAPI>();
        api.SetupGet(x => x.Logger).Returns(Mock.Of<ILogger>());
        var document = JObject.FromObject(new VgeConfig());
        document["Atmosphere"]!["SkyLutQuality"] = 2;
        document["WaterRefractionEnabled"] = enabled;
        document["WaterRefractionQuality"] = quality;
        document["WaterRefractionBackgroundScale"] = scale;
        document["MaterialAtlas"]!["TerrainSubdivision"]!["MaximumLevel"] = 4;
        api.Setup(x => x.LoadModConfig<JObject>(It.IsAny<string>())).Returns(() => (JObject)document.DeepClone());
        ConfigModSystem.EnsureConfigLoaded(api.Object);
        Assert.Equal(2, ConfigModSystem.Config.Atmosphere.SkyLutQuality);
        Assert.Equal(enabled, ConfigModSystem.Config.WaterRefractionEnabled);
        Assert.Equal(quality, ConfigModSystem.Config.WaterRefractionQuality);
        Assert.Equal(scale, ConfigModSystem.Config.WaterRefractionBackgroundScale);
        Assert.Equal(4, ConfigModSystem.Config.MaterialAtlas.TerrainSubdivision.MaximumLevel);
        document["Atmosphere"]!["SkyLutQuality"] = 3;
        document["WaterRefractionEnabled"] = !enabled;
        document["WaterRefractionQuality"] = 3 - quality;
        document["WaterRefractionBackgroundScale"] = 3 - scale;
        system.Dispose();
        ConfigModSystem.EnsureConfigLoaded(api.Object);
        Assert.Equal(3, ConfigModSystem.Config.Atmosphere.SkyLutQuality);
        Assert.Equal(!enabled, ConfigModSystem.Config.WaterRefractionEnabled);
        Assert.Equal(3 - quality, ConfigModSystem.Config.WaterRefractionQuality);
        Assert.Equal(3 - scale, ConfigModSystem.Config.WaterRefractionBackgroundScale);
        api.Verify(x => x.LoadModConfig<JObject>(It.IsAny<string>()), Times.Exactly(2));
    }
    #endregion

    /// <summary>Startup writes completed nested defaults before ConfigLib starts reading the document.</summary>
    [Fact]
    public void SparseStartupStoresCompletedDocument()
    {
        using var lifetime = new ConfigLifetime();
        lifetime.System.Dispose();
        var api = new Mock<ICoreAPI>();
        api.SetupGet(x => x.Logger).Returns(Mock.Of<ILogger>());
        api.Setup(x => x.LoadModConfig<JObject>(It.IsAny<string>())).Returns(JObject.Parse("{\"LumOn\":{\"Enabled\":false},\"WaterRefractionEnabled\":true}"));
        JObject? stored = null;
        api.Setup(x => x.StoreModConfig(It.IsAny<JObject>(), It.IsAny<string>()))
            .Callback<JObject, string>((document, _) => stored = document);
        ConfigModSystem.EnsureConfigLoaded(api.Object);
        Assert.NotNull(stored);
        Assert.NotNull(stored["Atmosphere"]!["SkyLutQuality"]);
        Assert.NotNull(stored["MaterialAtlas"]!["TerrainSubdivision"]!["MaximumLevel"]);
        Assert.False(stored["LumOn"]!["Enabled"]!.Value<bool>());
        Assert.True(stored["WaterRefractionEnabled"]!.Value<bool>());
        Assert.Equal(3,stored["WaterRefractionQuality"]!.Value<int>());
        Assert.Equal(2,stored["WaterRefractionBackgroundScale"]!.Value<int>());
        Assert.True(ConfigModSystem.Config.WaterRefractionEnabled);
        Assert.Equal(3,ConfigModSystem.Config.WaterRefractionQuality);
        Assert.Equal(2,ConfigModSystem.Config.WaterRefractionBackgroundScale);
        api.Verify(x => x.StoreModConfig(It.IsAny<JObject>(), It.IsAny<string>()), Times.Once);
    }

    #region Test isolation
    /// <summary>Restores the shared configuration and startup latch after exercising production lifecycle methods.</summary>
    private sealed class ConfigLifetime : IDisposable
    {
        private readonly VgeConfig previous = ConfigModSystem.Config;
        private readonly FieldInfo loadedField = typeof(ConfigModSystem).GetField("configLoaded", BindingFlags.Static | BindingFlags.NonPublic)!;
        private readonly bool loaded;
        internal ConfigModSystem System { get; } = new();

        /// <summary>Snapshots the process-wide startup latch before tests reset it.</summary>
        internal ConfigLifetime() => loaded = (bool)loadedField.GetValue(null)!;

        /// <summary>Restores both static values even when an assertion fails.</summary>
        public void Dispose()
        {
            System.Dispose();
            typeof(ConfigModSystem).GetProperty(nameof(ConfigModSystem.Config))!.SetValue(null, previous);
            loadedField.SetValue(null, loaded);
        }
    }
    #endregion

    #region Integer configuration
    /// <summary>Whole-document writes retain the numeric controls used by ConfigLib during initial load and saves.</summary>
    [Fact]
    public void WholeConfigurationWritesIntegerControls()
    {
        var config = new VgeConfig();
        config.Atmosphere.SkyLutQuality = 3;
        config.MaterialAtlas.TerrainSubdivision.MaximumLevel = 8;
        config.MaterialAtlas.TerrainSurfaceDetailMode = 2;
        var saved = JObject.FromObject(config);
        Assert.Equal(JTokenType.Integer, saved["Atmosphere"]!["SkyLutQuality"]!.Type);
        Assert.Equal(JTokenType.Integer, saved["MaterialAtlas"]!["TerrainSubdivision"]!["MaximumLevel"]!.Type);
        Assert.Equal(JTokenType.Integer, saved["MaterialAtlas"]!["TerrainSurfaceDetailMode"]!.Type);
        var restored = saved.ToObject<VgeConfig>()!;
        restored.Sanitize();
        Assert.Equal(3, restored.Atmosphere.SkyLutQuality);
        Assert.Equal(8, restored.MaterialAtlas.TerrainSubdivision.MaximumLevel);
        Assert.Equal(2, restored.MaterialAtlas.TerrainSurfaceDetailMode);
    }

    /// <summary>Every supported quality value retains its integer representation.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void QualityRoundTripsAsInteger(int quality)
    {
        var settings = JsonConvert.DeserializeObject<AtmosphereSettings>("{\"SkyLutQuality\":" + quality + "}")!;
        var saved = JObject.FromObject(settings);
        Assert.Equal(JTokenType.Integer, saved["SkyLutQuality"]!.Type);
        Assert.Equal(quality, saved["SkyLutQuality"]!.Value<int>());
    }

    /// <summary>All bounded subdivision selections survive numeric serialization.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    public void SubdivisionNumberRoundTrips(int level)
    {
        var settings = JsonConvert.DeserializeObject<TerrainSubdivisionSettings>("{\"MaximumLevel\":" + level + "}")!;
        var saved = JObject.FromObject(settings);
        Assert.Equal(JTokenType.Integer, saved["MaximumLevel"]!.Type);
        Assert.Equal(level, saved.ToObject<TerrainSubdivisionSettings>()!.MaximumLevel);
    }

    /// <summary>Out-of-range integer settings clamp before GPU resource publication.</summary>
    [Theory]
    [InlineData(-1, 1)]
    [InlineData(0, 1)]
    [InlineData(99, 8)]
    public void SubdivisionSanitizesToSupportedBounds(int value, int expected)
    {
        var settings = new TerrainSubdivisionSettings { MaximumLevel = value };
        settings.Sanitize();
        Assert.Equal(expected, settings.MaximumLevel);
    }

    /// <summary>ConfigLib uses bounded integer inputs without named mapping serialization.</summary>
    [Theory]
    [InlineData("ATMOSPHERE_SKY_LUT_QUALITY", 0, 3, 0)]
    [InlineData("TESSELLATION_MAXIMUM_LEVEL", 1, 8, 5)]
    [InlineData("TERRAIN_SURFACE_DETAIL_MODE", 0, 2, 1)]
    public void ControlsDeclareNumericBounds(string key, int minimum, int maximum, int defaultValue)
    {
        var definition = JObject.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "assets/config/configlib-patches.json")));
        var setting = definition["settings"]!["integer"]![key]!;
        Assert.Null(setting["mapping"]);
        Assert.Equal(defaultValue, setting["default"]!.Value<int>());
        Assert.Equal(minimum, setting["range"]!["min"]!.Value<int>());
        Assert.Equal(maximum, setting["range"]!["max"]!.Value<int>());
        Assert.Equal(1, setting["range"]!["step"]!.Value<int>());
    }
    #endregion

    /// <summary>Client-owned rendering settings must also persist when ConfigLib restricts writes to client settings.</summary>
    [Fact]
    public void AllRenderingSettingsAreClientOwned()
    {
        var definition = JObject.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "assets/config/configlib-patches.json")));
        foreach (var category in ((JObject)definition["settings"]!).Properties())
        foreach (var setting in ((JObject)category.Value).Properties())
            Assert.True(setting.Value["clientSide"]?.Value<bool>() == true, setting.Name);
    }
}


