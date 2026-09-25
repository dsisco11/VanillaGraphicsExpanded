using VanillaGraphicsExpanded.LumOn;

namespace VanillaGraphicsExpanded.Tests.Unit.LumOn;

/// <summary>Verifies temporal matrices retain the render origin belonging to their committed frame.</summary>
public sealed class LumOnTemporalReprojectionTests
{
    #region Frame lifecycle
    /// <summary>Rebasing composes through every perspective row, including homogeneous clip W.</summary>
    [Fact]
    public void CaptureComposesOriginDeltaThroughPerspectiveW()
    {
        float[] perspective = [2, 0, 0, 0, 0, 3, 0, 0, 0, 0, -1.02f, -1, 0, 0, -.202f, 0];
        var history = new LumOnTemporalReprojection();
        history.Capture(perspective, 16777216.25, 32, -16777216.25);
        history.Commit();
        history.Capture(perspective, 16777216.5, 32.5, -16777216.125);
        Assert.Equal(.5f, history.PreviousViewProjection[12]);
        Assert.Equal(1.5f, history.PreviousViewProjection[13]);
        Assert.Equal(-.202f - 1.02f * .125f, history.PreviousViewProjection[14]);
        Assert.Equal(-.125f, history.PreviousViewProjection[15]);
    }

    /// <summary>Initial frames and repeated captures cannot accumulate origin shifts or overwrite committed history.</summary>
    [Theory]
    [InlineData(16777216.25)]
    [InlineData(-16777216.25)]
    public void CaptureRebasesCommittedFrameWithoutAccumulation(double origin)
    {
        float[] first = [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, .125f, -.25f, 0, 1];
        float[] second = [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, -.5f, .25f, 0, 1];
        var history = new LumOnTemporalReprojection();
        history.Capture(first, origin, 32, -origin);
        Assert.Equal(first, history.PreviousViewProjection);
        history.Commit();
        foreach (double movement in new[] { .125, 32.25, -.5, .125 })
        {
            history.Capture(second, origin + movement, 32.5, -origin - movement);
            Assert.Equal(.125f + (float)movement, history.PreviousViewProjection[12]);
            Assert.Equal(.25f, history.PreviousViewProjection[13]);
            Assert.Equal(-(float)movement, history.PreviousViewProjection[14]);
        }
        history.Commit();
        history.Capture(first, origin + .25, 32.75, -origin - .25);
        Assert.Equal(-.375f, history.PreviousViewProjection[12]);
        Assert.Equal(.5f, history.PreviousViewProjection[13]);
        Assert.Equal(-.125f, history.PreviousViewProjection[14]);
        Assert.Equal(.125f, first[12]);
        Assert.Equal(-.5f, second[12]);
    }
    #endregion
}
