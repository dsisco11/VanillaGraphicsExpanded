using VanillaGraphicsExpanded.PBR.CameraExposure;

namespace VanillaGraphicsExpanded.Tests;

/// <summary>Checks temporal exposure resets only across discontinuous camera and world inputs.</summary>
public sealed class CameraExposureHistoryTests
{
    #region Public API
    /// <summary>Normal movement and wrapped yaw retain history; camera cuts, teleports and lifecycle retirement reset it.</summary>
    [Fact]
    public void CameraContinuityAndRetirementControlHistory()
    {
        var history = new CameraExposureHistory();
        var settings = new CameraExposureSettings().Snapshot();
        Assert.True(history.Capture(settings, .016f, 0, 0, 0, MathF.PI - .01f, 0, 0, 0));
        Assert.False(history.Capture(settings, .016f, 1, 1, 1, -MathF.PI + .01f, .01f, 0, 0));
        Assert.True(history.Capture(settings, .016f, 100, 1, 1, -MathF.PI + .01f, .01f, 0, 0));
        Assert.False(history.Capture(settings, .016f, 100, 1, 1, -MathF.PI + .01f, .01f, 0, 0));
        Assert.True(history.Capture(settings, .016f, 100, 1, 1, 0, .01f, 0, 0));
        Assert.True(history.Capture(settings, .016f, 100, 1, 1, 0, 2, 0, 0));
        Assert.True(history.Capture(settings, .016f, 100, 1, 1, 0, 2, 1, 0));
        Assert.True(history.Capture(settings, .016f, 100, 1, 1, 0, 2, 1, 1));
        Assert.False(history.Capture(settings, .016f, 100, 1, 1, 0, 2, 1, 1));
        history.Reset();
        Assert.True(history.Capture(settings, .016f, 100, 1, 1, 0, 2, 1, 1));
    }

    /// <summary>Settings transitions and invalid or long frame gaps reject stale temporal values.</summary>
    [Fact]
    public void SettingsAndTimingChangesRequireFreshMetering()
    {
        var history = new CameraExposureHistory();
        var settings = new CameraExposureSettings().Snapshot();
        Assert.True(history.Capture(settings, .016f, 0, 0, 0, 0, 0, 0, 0));
        Assert.False(history.Capture(settings, .016f, 0, 0, 0, 0, 0, 0, 0));
        settings = settings with { Enabled = false };
        Assert.True(history.Capture(settings, .016f, 0, 0, 0, 0, 0, 0, 0));
        Assert.False(history.Capture(settings, .016f, 0, 0, 0, 0, 0, 0, 0));
        settings = settings with { Enabled = true, Compensation = 1 };
        Assert.True(history.Capture(settings, .016f, 0, 0, 0, 0, 0, 0, 0));
        foreach (float dt in new[] { float.NaN, float.PositiveInfinity, -1, 2 })
            Assert.True(history.Capture(settings, dt, 0, 0, 0, 0, 0, 0, 0));
        Assert.False(history.Capture(settings, .016f, 0, 0, 0, 0, 0, 0, 0));
    }
    #endregion
}
