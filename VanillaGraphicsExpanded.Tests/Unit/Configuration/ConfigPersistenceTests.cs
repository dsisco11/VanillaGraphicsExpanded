
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.ModSystems;
using VanillaGraphicsExpanded.PBR.Tessellation;
using VanillaGraphicsExpanded.PBR.Atmosphere;


namespace VanillaGraphicsExpanded.Tests.Unit.Configuration;

/// <summary>Checks configuration document defaults, numeric persistence, and client ownership rules.</summary>
public sealed class ConfigPersistenceTests
{
    #region Public API
    #region Persistence policy

    /// <summary>All current settings acquire concrete nested leaves while saved and unrelated values survive.</summary>
    [Fact]
    public void SparseDocumentGainsCurrentPathsWithoutOverwritingValues()
    {
        // Arrange
        var document = JObject.Parse("""
            { "LumOn": { "Enabled": false }, "Atmosphere": {}, "MaterialAtlas": {},
              "unrelated": 7, "MaterialAtlas/TerrainSubdivision/MaximumLevel": 32 }
            """);
        var defaults = JObject.FromObject(new VgeConfig());
        // Act
        Assert.True(ConfigDocumentDefaults.FillMissing(document, defaults));
        // Assert existing values remain unchanged while current paths are added.
        Assert.False(document["LumOn"]!["Enabled"]!.Value<bool>());
        Assert.Equal(7, document["unrelated"]!.Value<int>());
        Assert.Equal(32, document["MaterialAtlas/TerrainSubdivision/MaximumLevel"]!.Value<int>());
        Assert.Equal(defaults["MaterialAtlas"]!["TerrainSubdivision"]!["MaximumLevel"], document["MaterialAtlas"]!["TerrainSubdivision"]!["MaximumLevel"]);
        Assert.NotNull(document["Atmosphere"]!["SkyLutQuality"]);
        var definition = JObject.Parse(TestAssetSnapshot.ReadBuiltText(Path.Combine(AppContext.BaseDirectory, "assets/config/configlib-patches.json")));
        foreach (var category in ((JObject)definition["settings"]!).Properties())
        foreach (var setting in ((JObject)category.Value).Properties())
        {
            JToken? value = document;
            foreach (string part in setting.Value["name"]!.Value<string>()!.Split('/')) value = value?[part];
            Assert.NotNull(value);
        }
        // Act: a second pass should be idempotent.
        bool changed = ConfigDocumentDefaults.FillMissing(document, defaults);
        // Assert
        Assert.False(changed);
    }

    #endregion

    #region Integer configuration
    /// <summary>Whole-document writes retain the numeric controls used by ConfigLib during initial load and saves.</summary>
    [Fact]
    public void WholeConfigurationWritesIntegerControls()
    {
        // Arrange
        var config = new VgeConfig();
        config.Atmosphere.SkyLutQuality = 3;
        config.MaterialAtlas.TerrainSubdivision.MaximumLevel = 8;
        config.MaterialAtlas.TerrainSurfaceDetailMode = 2;
        // Act
        var saved = JObject.FromObject(config);
        // Assert serialization uses integer tokens and restores the same choices.
        Assert.Equal(JTokenType.Integer, saved["Atmosphere"]!["SkyLutQuality"]!.Type);
        Assert.Equal(JTokenType.Integer, saved["MaterialAtlas"]!["TerrainSubdivision"]!["MaximumLevel"]!.Type);
        Assert.Equal(JTokenType.Integer, saved["MaterialAtlas"]!["TerrainSurfaceDetailMode"]!.Type);
        // Act: deserialize and sanitize the saved document.
        var restored = saved.ToObject<VgeConfig>()!;
        restored.Sanitize();
        // Assert the numeric choices retain their values.
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
        // Arrange and act
        var settings = JsonConvert.DeserializeObject<AtmosphereSettings>("{\"SkyLutQuality\":" + quality + "}")!;
        var saved = JObject.FromObject(settings);
        // Assert
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
        // Arrange and act
        var settings = JsonConvert.DeserializeObject<TerrainSubdivisionSettings>("{\"MaximumLevel\":" + level + "}")!;
        var saved = JObject.FromObject(settings);
        // Assert
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
        // Arrange
        var settings = new TerrainSubdivisionSettings { MaximumLevel = value };
        // Act
        settings.Sanitize();
        // Assert
        Assert.Equal(expected, settings.MaximumLevel);
    }

    /// <summary>ConfigLib uses bounded integer inputs without named mapping serialization.</summary>
    [Theory]
    [InlineData("ATMOSPHERE_SKY_LUT_QUALITY", 0, 3, 2)]
    [InlineData("TESSELLATION_MAXIMUM_LEVEL", 1, 8, 5)]
    [InlineData("TERRAIN_SURFACE_DETAIL_MODE", 0, 2, 1)]
    public void ControlsDeclareNumericBounds(string key, int minimum, int maximum, int defaultValue)
    {
        // Arrange
        var definition = JObject.Parse(TestAssetSnapshot.ReadBuiltText(Path.Combine(AppContext.BaseDirectory, "assets/config/configlib-patches.json")));
        // Act and assert the selected shipped integer control's contract.
        var setting = definition["settings"]!["integer"]![key]!;
        Assert.Null(setting["mapping"]);
        Assert.Equal(defaultValue, setting["default"]!.Value<int>());
        Assert.Equal(minimum, setting["range"]!["min"]!.Value<int>());
        Assert.Equal(maximum, setting["range"]!["max"]!.Value<int>());
        Assert.Equal(1, setting["range"]!["step"]!.Value<int>());
    }
    #endregion

    #region Client ownership
    /// <summary>Client-owned rendering settings must also persist when ConfigLib restricts writes to client settings.</summary>
    [Fact]
    public void AllRenderingSettingsAreClientOwned()
    {
        // Arrange
        var definition = JObject.Parse(TestAssetSnapshot.ReadBuiltText(Path.Combine(AppContext.BaseDirectory, "assets/config/configlib-patches.json")));
        // Act and assert each setting's ownership declaration.
        foreach (var category in ((JObject)definition["settings"]!).Properties())
        foreach (var setting in ((JObject)category.Value).Properties())
            Assert.True(setting.Value["clientSide"]?.Value<bool>() == true, setting.Name);
    }
    #endregion
    #endregion
}


