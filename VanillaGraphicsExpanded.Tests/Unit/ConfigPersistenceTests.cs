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
    [Fact]
    public void DisposeAllowsReloadOfStoredSettings()
    {
        using var lifetime = new ConfigLifetime();
        var system = lifetime.System;
        system.Dispose();
        var api = new Mock<ICoreAPI>();
        api.SetupGet(x => x.Logger).Returns(Mock.Of<ILogger>());
        var document = JObject.FromObject(new VgeConfig());
        document["Atmosphere"]!["SkyLutQuality"] = "High";
        document["MaterialAtlas"]!["TerrainSubdivision"]!["MaximumLevel"] = 4;
        api.Setup(x => x.LoadModConfig<JObject>(It.IsAny<string>())).Returns(() => (JObject)document.DeepClone());
        ConfigModSystem.EnsureConfigLoaded(api.Object);
        Assert.Equal(AtmosphereQuality.High, ConfigModSystem.Config.Atmosphere.SkyLutQuality);
        Assert.Equal(TerrainSubdivisionLevel.Level4, ConfigModSystem.Config.MaterialAtlas.TerrainSubdivision.MaximumLevel);
        document["Atmosphere"]!["SkyLutQuality"] = "Ultra";
        system.Dispose();
        ConfigModSystem.EnsureConfigLoaded(api.Object);
        Assert.Equal(AtmosphereQuality.Ultra, ConfigModSystem.Config.Atmosphere.SkyLutQuality);
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
        api.Setup(x => x.LoadModConfig<JObject>(It.IsAny<string>())).Returns(JObject.Parse("{\"LumOn\":{\"Enabled\":false}}"));
        JObject? stored = null;
        api.Setup(x => x.StoreModConfig(It.IsAny<JObject>(), It.IsAny<string>()))
            .Callback<JObject, string>((document, _) => stored = document);
        ConfigModSystem.EnsureConfigLoaded(api.Object);
        Assert.NotNull(stored);
        Assert.NotNull(stored["Atmosphere"]!["SkyLutQuality"]);
        Assert.NotNull(stored["MaterialAtlas"]!["TerrainSubdivision"]!["MaximumLevel"]);
        Assert.False(stored["LumOn"]!["Enabled"]!.Value<bool>());
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

    #region Quality serialization
    /// <summary>ConfigLib mapping names and numeric documents retain the same typed quality.</summary>
    [Theory]
    [InlineData("\"Low\"", 0)]
    [InlineData("\"Medium\"", 1)]
    [InlineData("\"High\"", 2)]
    [InlineData("\"Ultra\"", 3)]
    [InlineData("0", 0)]
    [InlineData("1", 1)]
    [InlineData("2", 2)]
    [InlineData("3", 3)]
    public void QualityRoundTripsThroughNumericRepresentation(string json, int expected)
    {
        var settings = JsonConvert.DeserializeObject<AtmosphereSettings>("{\"SkyLutQuality\":" + json + "}")!;
        Assert.Equal((AtmosphereQuality)expected, settings.SkyLutQuality);
        var saved = JObject.FromObject(settings);
        Assert.Equal(JTokenType.Integer, saved["SkyLutQuality"]!.Type);
        Assert.Equal(expected, saved["SkyLutQuality"]!.Value<int>());
    }

    /// <summary>Every supported tessellation name round trips through the numeric saved representation.</summary>
    [Theory]
    [InlineData(TerrainSubdivisionLevel.Level1)]
    [InlineData(TerrainSubdivisionLevel.Level2)]
    [InlineData(TerrainSubdivisionLevel.Level3)]
    [InlineData(TerrainSubdivisionLevel.Level4)]
    [InlineData(TerrainSubdivisionLevel.Level5)]
    [InlineData(TerrainSubdivisionLevel.Level6)]
    [InlineData(TerrainSubdivisionLevel.Level7)]
    [InlineData(TerrainSubdivisionLevel.Level8)]
    public void SubdivisionNameRoundTrips(TerrainSubdivisionLevel level)
    {
        var settings = JsonConvert.DeserializeObject<TerrainSubdivisionSettings>("{\"MaximumLevel\":\"" + level + "\"}")!;
        Assert.Equal(level, settings.MaximumLevel);
        var saved = JObject.FromObject(settings);
        Assert.Equal(JTokenType.Integer, saved["MaximumLevel"]!.Type);
        Assert.Equal((int)level, saved["MaximumLevel"]!.Value<int>());
        Assert.Equal(level, saved.ToObject<TerrainSubdivisionSettings>()!.MaximumLevel);
    }

    /// <summary>Out-of-range serialized enum values retain the existing bounded subdivision contract.</summary>
    [Theory]
    [InlineData(-1, TerrainSubdivisionLevel.Level1)]
    [InlineData(0, TerrainSubdivisionLevel.Level1)]
    [InlineData(99, TerrainSubdivisionLevel.Level8)]
    public void SubdivisionEnumSanitizesToSupportedBounds(int value, TerrainSubdivisionLevel expected)
    {
        var settings = new TerrainSubdivisionSettings { MaximumLevel = (TerrainSubdivisionLevel)value };
        settings.Sanitize();
        Assert.Equal(expected, settings.MaximumLevel);
    }

    /// <summary>The shipped controls select precisely the typed supported values and named defaults.</summary>
    [Theory]
    [InlineData("ATMOSPHERE_SKY_LUT_QUALITY", typeof(AtmosphereQuality), "Low")]
    [InlineData("TESSELLATION_MAXIMUM_LEVEL", typeof(TerrainSubdivisionLevel), "Level5")]
    public void QualityMappingsMatchEnums(string key, Type enumType, string defaultName)
    {
        var definition = JObject.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "assets/config/configlib-patches.json")));
        var setting = definition["settings"]!["integer"]![key]!;
        Assert.Equal(defaultName, setting["default"]!.Value<string>());
        var mapping = (JObject)setting["mapping"]!;
        Assert.Equal(Enum.GetNames(enumType), mapping.Properties().Select(p => p.Name));
        foreach (var property in mapping.Properties())
            Assert.Equal(Convert.ToInt32(Enum.Parse(enumType, property.Name)), property.Value.Value<int>());
    }
    #endregion
}


