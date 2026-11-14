using Newtonsoft.Json;
using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.Rendering.Shadows.Configuration;
using VanillaGraphicsExpanded.Rendering.Shadows.Pages;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.Unit.Rendering.Shadows;

/// <summary>Protects persisted age and source-motion limits for retained virtual shadow depths.</summary>
public sealed class ShadowTemporalSettingsTests
{
    #region Public API
    /// <summary>Configured temporal limits survive the existing user configuration JSON round trip.</summary>
    [Fact]
    public void TemporalLimitsPersistThroughOwningConfiguration()
    {
        // Arrange: author non-default limits, including a strict zero stale-reuse allowance.
        var config = new VgeConfig();
        config.VirtualShadows.Temporal = new ShadowTemporalSettings
        {
            MaxRenderedAgeSeconds = 0.8,
            MaxStaleAgeSeconds = 0,
            MaxPointTranslationMetres = 0.4,
            MaxDirectionalAngleDegrees = 0.6
        };

        // Act: restore and sanitize through the normal configuration owner.
        var restored = JsonConvert.DeserializeObject<VgeConfig>(JsonConvert.SerializeObject(config))!;
        restored.Sanitize();
        var effective = restored.VirtualShadows.Temporal.Freeze();

        // Assert: all independently configured values survive and zero continues to mean strict current-only sampling.
        Assert.Equal(0.8, effective.MaxRenderedAgeSeconds);
        Assert.Equal(0, effective.MaxStaleAgeSeconds);
        Assert.Equal(0.4, effective.MaxPointTranslationMetres);
        Assert.Equal(0.6, effective.MaxDirectionalAngleDegrees);
        restored.VirtualShadows.Temporal.MaxRenderedAgeSeconds = 0.1;
        Assert.Equal(0.8, effective.MaxRenderedAgeSeconds);
    }

    /// <summary>Defaults and sanitizer bounds recover malformed values while preserving supported strict-zero settings.</summary>
    [Fact]
    public void TemporalLimitsHaveStrictDefaultsAndBoundInvalidConfiguration()
    {
        // Arrange: verify defaults separately, then provide nonfinite, excessive, negative, and inconsistent values.
        var defaults = new ShadowTemporalSettings().Freeze();
        var invalid = new ShadowTemporalSettings
        {
            MaxRenderedAgeSeconds = double.NaN,
            MaxStaleAgeSeconds = 9,
            MaxPointTranslationMetres = double.PositiveInfinity,
            MaxDirectionalAngleDegrees = -1
        };

        // Act: use the same settings sanitizer as loading configuration and bound stale age to rendered age.
        invalid.Sanitize();
        var strict = new ShadowTemporalSettings { MaxRenderedAgeSeconds = 0, MaxStaleAgeSeconds = 0 }.Freeze();

        // Assert: defaults are finite, invalid values recover independently, and zero remains a valid strict policy.
        Assert.Equal(0.25, defaults.MaxRenderedAgeSeconds);
        Assert.Equal(0.1, defaults.MaxStaleAgeSeconds);
        Assert.Equal(0.15, defaults.MaxPointTranslationMetres);
        Assert.Equal(0.25, defaults.MaxDirectionalAngleDegrees);
        Assert.Equal(0.25, invalid.MaxRenderedAgeSeconds);
        Assert.Equal(0.25, invalid.MaxStaleAgeSeconds);
        Assert.Equal(0.15, invalid.MaxPointTranslationMetres);
        Assert.Equal(0.25, invalid.MaxDirectionalAngleDegrees);
        Assert.Equal(0, strict.MaxRenderedAgeSeconds);
        Assert.Equal(0, strict.MaxStaleAgeSeconds);
    }

    /// <summary>Integer clock transport keeps subsecond deadlines after a long-running session.</summary>
    [Fact]
    public void ShadowClockPreservesFractionalSecondsAtLongUptime()
    {
        // Arrange: use an uptime whose FP32 seconds representation would lose the quarter-second deadline.
        const double uptime = 1_000_000_000.25;
        var words = new uint[2];

        // Act: encode the sample as integer nanoseconds and reconstruct the exact unsigned tick count.
        ShadowClock.Write(words, 0, uptime);
        ulong ticks = ((ulong)words[1] << 32) | words[0];

        // Assert: the epoch remains exact at this scale and the fractional quarter second remains observable.
        Assert.Equal(1_000_000_000_250_000_000UL, ticks);
        Assert.Throws<ArgumentOutOfRangeException>(() => ShadowClock.Write(words, 0, double.PositiveInfinity));
    }
    #endregion
}
