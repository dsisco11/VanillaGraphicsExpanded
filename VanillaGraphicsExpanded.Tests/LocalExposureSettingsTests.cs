using Newtonsoft.Json;
using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.PBR.Postprocessing.LocalExposure;

namespace VanillaGraphicsExpanded.Tests;

/// <summary>Preserves enabled defaults, saved choices and finite local-exposure configuration.</summary>
public sealed class LocalExposureSettingsTests
{
    #region Public API
    /// <summary>Missing settings enable local exposure while preserving existing camera limits.</summary>
    [Fact]
    public void MissingSettingsEnableLocalExposure()
    {
        var config = JsonConvert.DeserializeObject<VgeConfig>("{}")!;
        Assert.True(config.Postprocessing.LocalExposure.Enabled);
        Assert.Equal(-2, config.CameraExposure.MinEV100);
        config.Postprocessing.LocalExposure = null!;
        config.Sanitize();
        Assert.NotNull(config.Postprocessing.LocalExposure);
        Assert.True(config.Postprocessing.LocalExposure.Enabled);
    }

    /// <summary>An explicit saved opt-out remains authoritative during loading and sanitization.</summary>
    [Fact]
    public void ExplicitSavedOptOutIsPreserved()
    {
        var config = JsonConvert.DeserializeObject<VgeConfig>("{\"Postprocessing\":{\"LocalExposure\":{\"Enabled\":false}}}")!;
        config.Sanitize();
        Assert.False(config.Postprocessing.LocalExposure.Enabled);
    }

    /// <summary>Malformed persisted values become bounded finite snapshot fields without mutating saved artistic controls.</summary>
    [Fact]
    public void InvalidCandidateValuesHaveFiniteBoundedSnapshot()
    {
        var settings = new LocalExposureSettings
        {
            Enabled = true, Width = -1, Height = int.MaxValue, RangeWidth = float.NaN,
            ShadowStrength = -1, HighlightStrength = float.PositiveInfinity, ShadowLimit = 100, HighlightLimit = -1
        };
        var result = settings.Snapshot();
        Assert.True(result.Enabled); Assert.Equal(1, result.Width); Assert.Equal(512, result.Height);
        Assert.Equal(1, result.RangeWidth); Assert.Equal(0, result.ShadowStrength); Assert.Equal(.5f, result.HighlightStrength);
        Assert.Equal(8, result.ShadowLimit); Assert.Equal(0, result.HighlightLimit);
        Assert.Equal(-1, settings.Width); Assert.True(float.IsNaN(settings.RangeWidth));
    }

    /// <summary>Explicit candidate settings round trip independently of manual camera controls.</summary>
    [Fact]
    public void CandidateRoundtripPreservesSavedCameraPolicy()
    {
        var config = new VgeConfig
        {
            Postprocessing = new() { LocalExposure = new() { Enabled = true, Width = 32, Height = 16, RangeWidth = 2, ShadowStrength = .25f } },
            CameraExposure = new() { Enabled = false, ManualEV100 = 2, MinEV100 = -3 }
        };
        var restored = JsonConvert.DeserializeObject<VgeConfig>(JsonConvert.SerializeObject(config))!;
        Assert.True(restored.Postprocessing.LocalExposure.Enabled); Assert.Equal(32, restored.Postprocessing.LocalExposure.Width);
        Assert.Equal(2, restored.Postprocessing.LocalExposure.RangeWidth); Assert.Equal(.25f, restored.Postprocessing.LocalExposure.ShadowStrength);
        Assert.False(restored.CameraExposure.Enabled); Assert.Equal(2, restored.CameraExposure.ManualEV100);
        Assert.Equal(-3, restored.CameraExposure.MinEV100);
    }
    #endregion
}

