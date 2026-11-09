using System.Reflection;
using VanillaGraphicsExpanded.PBR.Tessellation;
using Moq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.ModSystems;
using VanillaGraphicsExpanded.PBR.Atmosphere;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.Tests.Component.Configuration;

/// <summary>Checks configuration startup and shutdown against the real configuration owner with mocked API storage.</summary>
[Collection("CPU state")]
public sealed class ConfigPersistenceTests
{
    #region Public API
    #region Configuration owner lifecycle
    /// <summary>Unversioned saved exposure controls survive startup without a compatibility offset.</summary>
    [Theory]
    [InlineData(0, -4, 4)]
    [InlineData(-2, -9, 3)]
    public void UnversionedStartupPreservesSavedExposure(float manual, float minimum, float maximum)
    {
        // Arrange
        using var lifetime = new ConfigLifetime();
        lifetime.System.Dispose();
        var api = new Mock<ICoreAPI>();
        api.SetupGet(x => x.Logger).Returns(Mock.Of<ILogger>());
        var document = new JObject
        {
            ["CameraExposure"] = new JObject { ["ManualEV"] = manual, ["MinEV"] = minimum, ["MaxEV"] = maximum }
        };
        api.Setup(x => x.LoadModConfig<JObject>(It.IsAny<string>())).Returns(document);
        JObject? stored = null;
        api.Setup(x => x.StoreModConfig(It.IsAny<JObject>(), It.IsAny<string>()))
            .Callback<JObject, string>((value, _) => stored = value);

        // Act
        ConfigModSystem.EnsureConfigLoaded(api.Object);

        // Assert migrated exposure values and the stored current-format fields.
        Assert.Equal(-manual, ConfigModSystem.Config.CameraExposure.ManualEV100);
        Assert.Equal(-minimum, ConfigModSystem.Config.CameraExposure.MaxEV100);
        Assert.Equal(-maximum, ConfigModSystem.Config.CameraExposure.MinEV100);
        Assert.NotNull(stored);
        Assert.Null(stored["PhotometricVersion"]);
        Assert.Equal(-manual, stored["CameraExposure"]!["ManualEV100"]!.Value<float>());
        Assert.Null(stored["CameraExposure"]!["ManualEV"]);
        Assert.Equal(-minimum, stored["CameraExposure"]!["MaxEV100"]!.Value<float>());
        Assert.Null(stored["CameraExposure"]!["MinEV"]);
        Assert.Equal(-maximum, stored["CameraExposure"]!["MinEV100"]!.Value<float>());
        Assert.Null(stored["CameraExposure"]!["MaxEV"]);
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
        // Arrange
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
        // Act
        ConfigModSystem.EnsureConfigLoaded(api.Object);
        // Assert the initial document was loaded before disposal.
        Assert.Equal(2, ConfigModSystem.Config.Atmosphere.SkyLutQuality);
        Assert.Equal(enabled, ConfigModSystem.Config.WaterRefractionEnabled);
        Assert.Equal(quality, ConfigModSystem.Config.WaterRefractionQuality);
        Assert.Equal(scale, ConfigModSystem.Config.WaterRefractionBackgroundScale);
        Assert.Equal(4, ConfigModSystem.Config.MaterialAtlas.TerrainSubdivision.MaximumLevel);
        // Act: update persisted values and simulate the world shutdown.
        document["Atmosphere"]!["SkyLutQuality"] = 3;
        document["WaterRefractionEnabled"] = !enabled;
        document["WaterRefractionQuality"] = 3 - quality;
        document["WaterRefractionBackgroundScale"] = 3 - scale;
        // Act after shutdown: the next startup must reload the changed document.
        system.Dispose();
        // Act: start a new configuration lifetime from the changed document.
        ConfigModSystem.EnsureConfigLoaded(api.Object);
        // Assert the second startup sees the new persisted values.
        Assert.Equal(3, ConfigModSystem.Config.Atmosphere.SkyLutQuality);
        Assert.Equal(!enabled, ConfigModSystem.Config.WaterRefractionEnabled);
        Assert.Equal(3 - quality, ConfigModSystem.Config.WaterRefractionQuality);
        Assert.Equal(3 - scale, ConfigModSystem.Config.WaterRefractionBackgroundScale);
        api.Verify(x => x.LoadModConfig<JObject>(It.IsAny<string>()), Times.Exactly(2));
    }
    /// <summary>Startup writes completed nested defaults before ConfigLib starts reading the document.</summary>
    [Fact]
    public void SparseStartupStoresCompletedDocument()
    {
        // Arrange
        using var lifetime = new ConfigLifetime();
        lifetime.System.Dispose();
        var api = new Mock<ICoreAPI>();
        api.SetupGet(x => x.Logger).Returns(Mock.Of<ILogger>());
        api.Setup(x => x.LoadModConfig<JObject>(It.IsAny<string>())).Returns(JObject.Parse("{\"LumOn\":{\"Enabled\":false},\"WaterRefractionEnabled\":true}"));
        JObject? stored = null;
        api.Setup(x => x.StoreModConfig(It.IsAny<JObject>(), It.IsAny<string>()))
            .Callback<JObject, string>((document, _) => stored = document);
        // Act
        ConfigModSystem.EnsureConfigLoaded(api.Object);
        // Assert defaults were persisted before ConfigLib consumes the document.
        Assert.NotNull(stored);
        Assert.NotNull(stored["Atmosphere"]!["SkyLutQuality"]);
        Assert.NotNull(stored["MaterialAtlas"]!["TerrainSubdivision"]!["MaximumLevel"]);
        Assert.False(stored["LumOn"]!["Enabled"]!.Value<bool>());
        Assert.True(stored["WaterRefractionEnabled"]!.Value<bool>());
        Assert.Equal(2,stored["WaterRefractionQuality"]!.Value<int>());
        Assert.Equal(2,stored["WaterRefractionBackgroundScale"]!.Value<int>());
        Assert.True(ConfigModSystem.Config.WaterRefractionEnabled);
        Assert.Equal(2,ConfigModSystem.Config.WaterRefractionQuality);
        Assert.Equal(2,ConfigModSystem.Config.WaterRefractionBackgroundScale);
        api.Verify(x => x.StoreModConfig(It.IsAny<JObject>(), It.IsAny<string>()), Times.Once);
    }
    #endregion
    #endregion

    #region Private helpers
    /// <summary>Restores the shared configuration and startup latch after exercising production lifecycle methods.</summary>
    private sealed class ConfigLifetime : IDisposable
    {
        #region Public API
        /// <summary>Restores the configuration owner and startup latch after each test.</summary>
        public void Dispose()
        {
            System.Dispose();
            typeof(ConfigModSystem).GetProperty(nameof(ConfigModSystem.Config))!.SetValue(null, previous);
            loadedField.SetValue(null, loaded);
        }
        #endregion

        #region Internal
        /// <summary>Provides the owner instance whose lifecycle the test exercises.</summary>
        internal ConfigModSystem System { get; } = new();

        /// <summary>Snapshots the process-wide startup latch before tests reset it.</summary>
        internal ConfigLifetime() => loaded = (bool)loadedField.GetValue(null)!;
        #endregion

        #region Private
        private readonly VgeConfig previous = ConfigModSystem.Config;
        private readonly FieldInfo loadedField = typeof(ConfigModSystem).GetField("configLoaded", BindingFlags.Static | BindingFlags.NonPublic)!;
        private readonly bool loaded;
        #endregion
    }
    #endregion
}


