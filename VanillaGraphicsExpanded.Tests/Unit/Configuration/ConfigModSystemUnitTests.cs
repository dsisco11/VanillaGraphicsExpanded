using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Reflection;

using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.ModSystems;

using Vintagestory.API.Datastructures;

namespace VanillaGraphicsExpanded.Tests.Unit.Configuration;

/// <summary>Checks how ConfigModSystem translates ConfigLib inputs into VgeConfig values.</summary>
[Trait("Category", "Unit")]
public sealed class ConfigModSystemUnitTests
{
    #region Public API
    #region ConfigLib setting application
    /// <summary>A mapped ConfigLib event updates a nested boolean property.</summary>
    [Fact]
    public void Apply_SingleMappingKey_UpdatesNestedBooleanProperty()
    {
        // Arrange
        var cfg = new VgeConfig();
        Assert.False(cfg.Debug.LumOnRuntimeSelfCheckEnabled);

        var data = new TreeAttribute();
        data.SetString("MappingKey", "SELF_CHECK");
        data.SetString("Value", "true");

        var mapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["SELF_CHECK"] = "Debug.LumOnRuntimeSelfCheckEnabled"
        };

        // Act
        (int applied, string? summary) = Apply(cfg, data, mapping);

        // Assert
        Assert.Equal(1, applied);
        Assert.NotNull(summary);
        Assert.True(cfg.Debug.LumOnRuntimeSelfCheckEnabled);
    }

    /// <summary>A settings batch applies each mapped property in one ConfigLib event.</summary>
    [Fact]
    public void Apply_BatchSettings_UpdatesMultipleProperties()
    {
        // Arrange
        var cfg = new VgeConfig();
        Assert.Equal(3, cfg.LumOn.HzbCoarseMip);
        Assert.True(cfg.LumOn.HalfResolution);

        var entry1 = new TreeAttribute();
        entry1.SetString("MappingKey", "HZB_MIP");
        entry1.SetString("Value", "4");

        var entry2 = new TreeAttribute();
        entry2.SetString("MappingKey", "HALF_RES");
        entry2.SetString("Value", "false");

        var settings = new TreeAttribute();
        settings.SetAttribute("a", entry1);
        settings.SetAttribute("b", entry2);

        var data = new TreeAttribute();
        data.SetAttribute("Settings", settings);

        var mapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["HZB_MIP"] = "LumOn.HzbCoarseMip",
            ["HALF_RES"] = "LumOn.HalfResolution"
        };

        // Act
        (int applied, _) = Apply(cfg, data, mapping);

        // Assert
        Assert.Equal(2, applied);
        Assert.Equal(4, cfg.LumOn.HzbCoarseMip);
        Assert.False(cfg.LumOn.HalfResolution);
    }

    /// <summary>A JSON-encoded enum value is converted to the configured enum member.</summary>
    [Fact]
    public void Apply_EnumValue_AsJsonString_UpdatesEnumProperty()
    {
        // Arrange
        var cfg = new VgeConfig();
        Assert.Equal(VgeConfig.ProbeAtlasGatherMode.EvaluateProjectedSH, cfg.LumOn.ProbeAtlasGather);

        var data = new TreeAttribute();
        data.SetString("MappingKey", "GATHER_MODE");
        data.SetString("Value", "\"IntegrateAtlas\"");

        var mapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["GATHER_MODE"] = "LumOn.ProbeAtlasGather"
        };

        // Act
        (int applied, _) = Apply(cfg, data, mapping);

        // Assert
        Assert.Equal(1, applied);
        Assert.Equal(VgeConfig.ProbeAtlasGatherMode.IntegrateAtlas, cfg.LumOn.ProbeAtlasGather);
    }

    /// <summary>A direct dotted property code is accepted without a separate key mapping.</summary>
    [Fact]
    public void Apply_WhenConfigLibSendsCodePathDirectly_DoesNotRequireMapping()
    {
        // Arrange
        var cfg = new VgeConfig();
        Assert.Equal(3, cfg.LumOn.HzbCoarseMip);

        var data = new TreeAttribute();
        data.SetString("Code", "LumOn.HzbCoarseMip");
        data.SetString("Value", "5");

        // Act
        (int applied, _) = Apply(cfg, data, new Dictionary<string, string>());

        // Assert
        Assert.Equal(1, applied);
        Assert.Equal(5, cfg.LumOn.HzbCoarseMip);
    }

    /// <summary>An unknown mapped path leaves configuration unchanged.</summary>
    [Fact]
    public void Apply_UnknownPath_DoesNotApplyAnyChanges()
    {
        // Arrange
        var cfg = new VgeConfig();
        Assert.False(cfg.Debug.LumOnRuntimeSelfCheckEnabled);

        var data = new TreeAttribute();
        data.SetString("MappingKey", "BAD_KEY");
        data.SetString("Value", "true");

        var mapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["BAD_KEY"] = "Nope.Wrong"
        };

        // Act
        (int applied, _) = Apply(cfg, data, mapping);

        // Assert
        Assert.Equal(0, applied);
        Assert.False(cfg.Debug.LumOnRuntimeSelfCheckEnabled);
    }
    #endregion

    #region Shipped definition paths
    /// <summary>Every shipped setting name resolves to a serialized configuration leaf.</summary>
    [Fact]
    public void DefinitionNamesResolveAllSerializedConfigProperties()
    {
        // Arrange
        var definition = Newtonsoft.Json.Linq.JObject.Parse(TestAssetSnapshot.ReadBuiltText(Path.Combine(AppContext.BaseDirectory, "assets/config/configlib-patches.json")));
        var config = Newtonsoft.Json.Linq.JObject.FromObject(new VgeConfig());
        // Act
        int validated = 0;
        foreach (var category in ((Newtonsoft.Json.Linq.JObject)definition["settings"]!).Properties())
        foreach (var setting in ((Newtonsoft.Json.Linq.JObject)category.Value).Properties())
        {
            string code = (string)setting.Value["code"]!;
            string path = (string)setting.Value["name"]!;
            Assert.Equal(code.Replace('.', '/'), path);
            Newtonsoft.Json.Linq.JToken? value = config;
            foreach (string segment in path.Split('/')) value = value?[segment];
            Assert.True(value is not null, $"Missing serialized setting path: {path}");
            validated++;
        }
        // Assert
        Assert.True(validated > 0);
    }

    /// <summary>An explicitly saved false value is applied instead of falling back to its true default.</summary>
    [Fact]
    public void EnabledFalseResolvesAndAppliesWithoutDefaultFallback()
    {
        // Arrange
        var definition = Newtonsoft.Json.Linq.JObject.Parse(TestAssetSnapshot.ReadBuiltText(Path.Combine(AppContext.BaseDirectory, "assets/config/configlib-patches.json")));
        var setting = definition["settings"]!["boolean"]!["ENABLED"]!;
        string path = (string)setting["name"]!;
        string code = (string)setting["code"]!;
        var stored = Newtonsoft.Json.Linq.JObject.FromObject(new VgeConfig());
        stored["LumOn"]!["Enabled"] = false;
        Newtonsoft.Json.Linq.JToken? value = stored;
        foreach (string segment in path.Split('/')) value = value?[segment];
        Assert.NotNull(value);
        Assert.False(value.Value<bool>());
        var eventData = new TreeAttribute();
        eventData.SetString("MappingKey", "ENABLED");
        eventData.SetString("Value", value.Value<bool>() ? "true" : "false");
        var target = new VgeConfig();
        target.LumOn.Enabled = true;
        // Act
        var applied = Apply(target, eventData, new Dictionary<string, string> { ["ENABLED"] = code });
        // Assert
        Assert.Equal(1, applied.applied);
        Assert.False(target.LumOn.Enabled);
    }
    #endregion
    #endregion

    #region Private helpers
    /// <summary>Invokes the setting-application boundary and returns its count and diagnostic summary.</summary>
    private static (int applied, string? summary) Apply(VgeConfig config, IAttribute data, IReadOnlyDictionary<string, string> mapping)
    {
        MethodInfo method = typeof(ConfigModSystem).GetMethod(
            "ApplyConfigLibSettingsToModConfig",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Could not locate ConfigModSystem.ApplyConfigLibSettingsToModConfig via reflection.");

        object?[] args = new object?[] { config, data, mapping, null };
        int applied = (int)(method.Invoke(null, args) ?? 0);
        string? summary = args[3] as string;
        return (applied, summary);
    }
    #endregion
}
