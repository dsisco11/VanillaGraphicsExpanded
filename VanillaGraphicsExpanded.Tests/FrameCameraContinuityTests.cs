using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.Tests;

/// <summary>Verifies camera history discontinuities without coupling them to any lighting effect.</summary>
public sealed class FrameCameraContinuityTests
{
    #region Public API
    /// <summary>Wrapped yaw and small displacement retain history while teleports, view changes and long gaps reject it.</summary>
    [Fact]
    public void ContinuityTracksViewAndWorldLifetime()
    {
        var continuity = new FrameCameraContinuity();
        Assert.True(continuity.Capture(.016f, 0, 0, 0, MathF.PI-.01f, 0, 0, 0));
        Assert.False(continuity.Capture(.016f, 1, 1, 1, -MathF.PI+.01f, .01f, 0, 0));
        Assert.True(continuity.Capture(.016f, 100, 1, 1, -MathF.PI+.01f, .01f, 0, 0));
        Assert.False(continuity.Capture(.016f, 100, 1, 1, -MathF.PI+.01f, .01f, 0, 0));
        Assert.True(continuity.Capture(.016f, 100, 1, 1, 0, .01f, 0, 0));
        Assert.True(continuity.Capture(.016f, 100, 1, 1, 0, 2, 0, 0));
        Assert.True(continuity.Capture(.016f, 100, 1, 1, 0, 2, 1, 0));
        Assert.True(continuity.Capture(.016f, 100, 1, 1, 0, 2, 1, 1));
        Assert.False(continuity.Capture(.016f, 100, 1, 1, 0, 2, 1, 1));
        Assert.True(continuity.Capture(2, 100, 1, 1, 0, 2, 1, 1));
        continuity.Reset();
        Assert.True(continuity.Capture(.016f, 100, 1, 1, 0, 2, 1, 1));
    }
    #endregion
}
