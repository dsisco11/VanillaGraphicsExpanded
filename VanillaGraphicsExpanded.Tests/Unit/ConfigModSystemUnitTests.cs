using System;
using System.Collections.Generic;
using System.Reflection;

using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.ModSystems;

using Vintagestory.API.Datastructures;

namespace VanillaGraphicsExpanded.Tests.Unit;

[Trait("Category", "Unit")]
public sealed class ConfigModSystemUnitTests
{
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

    [Fact]
    public void Apply_SingleMappingKey_UpdatesNestedBooleanProperty()
    {
        var cfg = new VgeConfig();
        Assert.False(cfg.Debug.LumOnRuntimeSelfCheckEnabled);

        var data = new TreeAttribute();
        data.SetString("MappingKey", "SELF_CHECK");
        data.SetString("Value", "true");

        var mapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["SELF_CHECK"] = "Debug.LumOnRuntimeSelfCheckEnabled"
        };

        (int applied, string? summary) = Apply(cfg, data, mapping);

        Assert.Equal(1, applied);
        Assert.NotNull(summary);
        Assert.True(cfg.Debug.LumOnRuntimeSelfCheckEnabled);
    }

    [Fact]
    public void Apply_BatchSettings_UpdatesMultipleProperties()
    {
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

        (int applied, _) = Apply(cfg, data, mapping);

        Assert.Equal(2, applied);
        Assert.Equal(4, cfg.LumOn.HzbCoarseMip);
        Assert.False(cfg.LumOn.HalfResolution);
    }

    [Fact]
    public void Apply_EnumValue_AsJsonString_UpdatesEnumProperty()
    {
        var cfg = new VgeConfig();
        Assert.Equal(VgeConfig.ProbeAtlasGatherMode.EvaluateProjectedSH, cfg.LumOn.ProbeAtlasGather);

        var data = new TreeAttribute();
        data.SetString("MappingKey", "GATHER_MODE");
        data.SetString("Value", "\"IntegrateAtlas\"");

        var mapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["GATHER_MODE"] = "LumOn.ProbeAtlasGather"
        };

        (int applied, _) = Apply(cfg, data, mapping);

        Assert.Equal(1, applied);
        Assert.Equal(VgeConfig.ProbeAtlasGatherMode.IntegrateAtlas, cfg.LumOn.ProbeAtlasGather);
    }

    [Fact]
    public void Apply_WhenConfigLibSendsCodePathDirectly_DoesNotRequireMapping()
    {
        var cfg = new VgeConfig();
        Assert.Equal(3, cfg.LumOn.HzbCoarseMip);

        var data = new TreeAttribute();
        data.SetString("Code", "LumOn.HzbCoarseMip");
        data.SetString("Value", "5");

        (int applied, _) = Apply(cfg, data, new Dictionary<string, string>());

        Assert.Equal(1, applied);
        Assert.Equal(5, cfg.LumOn.HzbCoarseMip);
    }

    [Fact]
    public void Apply_UnknownPath_DoesNotApplyAnyChanges()
    {
        var cfg = new VgeConfig();
        Assert.False(cfg.Debug.LumOnRuntimeSelfCheckEnabled);

        var data = new TreeAttribute();
        data.SetString("MappingKey", "BAD_KEY");
        data.SetString("Value", "true");

        var mapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["BAD_KEY"] = "Nope.Wrong"
        };

        (int applied, _) = Apply(cfg, data, mapping);

        Assert.Equal(0, applied);
        Assert.False(cfg.Debug.LumOnRuntimeSelfCheckEnabled);
    }
}

