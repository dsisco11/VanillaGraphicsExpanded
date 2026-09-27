using Newtonsoft.Json;
using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.PBR.Materials;

namespace VanillaGraphicsExpanded.Tests.Unit.PBR.Materials;

/// <summary>Checks explicit surface-detail selection without legacy migration without rendering.</summary>
public sealed class TerrainSurfaceDetailModeTests
{
    #region Reload contract
    /// <summary>Compile-time relief settings change the reload snapshot.</summary>
    [Fact]
    public void SnapshotTracksOnlyActiveSettings()
    {
        var config=new VgeConfig.MaterialAtlasConfig();config.Sanitize();
        var initial=TerrainReliefConfiguration.Capture(config);

        config.ParallaxMinSteps++;Assert.NotEqual(initial,TerrainReliefConfiguration.Capture(config));
        initial=TerrainReliefConfiguration.Capture(config);config.ParallaxFadeEnd++;Assert.NotEqual(initial,TerrainReliefConfiguration.Capture(config));
        initial=TerrainReliefConfiguration.Capture(config);config.ParallaxDebugMode++;Assert.NotEqual(initial,TerrainReliefConfiguration.Capture(config));
        initial=TerrainReliefConfiguration.Capture(config);config.TerrainSurfaceDetailMode=TerrainSurfaceDetailMode.Disabled;Assert.NotEqual(initial,TerrainReliefConfiguration.Capture(config));
        config.ParallaxFadeStart=float.NaN;config.ParallaxFadeEnd=float.NaN;config.Sanitize();
        Assert.True(float.IsFinite(config.ParallaxFadeStart)&&float.IsFinite(config.ParallaxFadeEnd));
        config.ParallaxFadeEnd=config.ParallaxFadeStart;config.Sanitize();
        Assert.True(config.ParallaxFadeEnd>config.ParallaxFadeStart);
    }
    #endregion

    #region Configuration selection
    /// <summary>Missing mode uses relief; explicit selections survive and legacy flags are ignored.</summary>
    [Theory]
    [InlineData("{}", TerrainSurfaceDetailMode.Relief)]
    [InlineData("{\"EnableParallaxOcclusionMapping\":true}", TerrainSurfaceDetailMode.Relief)]
    [InlineData("{\"EnableParallaxOcclusionMapping\":true,\"TerrainSurfaceDetailMode\":0}", TerrainSurfaceDetailMode.Disabled)]
    [InlineData("{\"EnableParallaxOcclusionMapping\":false,\"TerrainSurfaceDetailMode\":1}", TerrainSurfaceDetailMode.Relief)]
    [InlineData("{\"TerrainSurfaceDetailMode\":2}", TerrainSurfaceDetailMode.Tessellation)]
    [InlineData("{\"TerrainSurfaceDetailMode\":99}", TerrainSurfaceDetailMode.Disabled)]
    public void SelectionSurvivesSanitizeAndRoundTrip(string json, TerrainSurfaceDetailMode expected)
    {
        var config = JsonConvert.DeserializeObject<VgeConfig.MaterialAtlasConfig>(json)!;
        config.Sanitize();
        Assert.Equal(expected, config.TerrainSurfaceDetailMode);

        config.Sanitize();
        Assert.Equal(expected, config.TerrainSurfaceDetailMode);
        var restored = JsonConvert.DeserializeObject<VgeConfig.MaterialAtlasConfig>(JsonConvert.SerializeObject(config))!;
        restored.Sanitize();
        Assert.Equal(expected, restored.TerrainSurfaceDetailMode);
        config.EnableNormalMaps = false;
        Assert.Equal(expected != TerrainSurfaceDetailMode.Disabled, config.RequiresNormalDepthAtlas);
    }
    #endregion
}

