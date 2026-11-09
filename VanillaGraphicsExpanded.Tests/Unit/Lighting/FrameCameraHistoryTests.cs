using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.Tests.Unit.Lighting;

/// <summary>Verifies temporal matrices retain the render origin belonging to their committed frame.</summary>
public sealed class FrameCameraHistoryTests
{
    #region Public API
    /// <summary>Position projection includes rebasing and eye translation while cloud displacement excludes both.</summary>
    [Fact]
    public void SharedMatrixSeparatesPositionAndDisplacementTranslation()
    {
        // Arrange a perspective matrix and a prior view matrix whose translated values expose rebasing behavior.
        float[] perspective = [2, 0, 0, 0, 0, 3, 0, 0, 0, 0, -1.02f, -1, 0, 0, -.202f, 0];
        float[] previous = (float[])perspective.Clone();
        previous[12] = 8;
        previous[13] = -6;
        previous[14] = -.202f + 1.02f;
        previous[15] = 1;
        var history = new FrameCameraHistory();

        // Act by committing the translated frame and capturing its rebased successor.
        history.Capture(previous, 16777216.25, 32, -16777216.25);
        history.Commit();
        history.Capture(perspective, 16777216.5, 32.5, -16777216.125);
        float[] matrix = history.PreviousViewProjection;
        float positionW = matrix[11] * -10 + matrix[15];
        float displacementW = matrix[11] * -10 + perspective[15];

        // Assert the shared matrix handles position and displacement homogeneous coordinates differently.
        Assert.Equal(8.5f, matrix[12]);
        Assert.Equal(-4.5f, matrix[13]);
        Assert.Equal(10.875f, positionW);
        Assert.Equal(10, displacementW);
        Assert.Equal(2, perspective[0]);
        Assert.Equal(previous[..12], matrix[..12]);
    }

    /// <summary>Rebasing composes through every perspective row, including homogeneous clip W.</summary>
    [Fact]
    public void CaptureComposesOriginDeltaThroughPerspectiveW()
    {
        // Arrange a perspective matrix and capture its prior-origin frame.
        float[] perspective = [2, 0, 0, 0, 0, 3, 0, 0, 0, 0, -1.02f, -1, 0, 0, -.202f, 0];
        var history = new FrameCameraHistory();
        history.Capture(perspective, 16777216.25, 32, -16777216.25);
        history.Commit();

        // Act by capturing the same projection from a slightly shifted origin.
        history.Capture(perspective, 16777216.5, 32.5, -16777216.125);

        // Assert the origin delta is composed into all four perspective rows.
        Assert.Equal(.5f, history.PreviousViewProjection[12]);
        Assert.Equal(1.5f, history.PreviousViewProjection[13]);
        Assert.Equal(-.202f - 1.02f * .125f, history.PreviousViewProjection[14]);
        Assert.Equal(-.125f, history.PreviousViewProjection[15]);
    }

    /// <summary>Initial frames and repeated captures cannot accumulate shifts or overwrite committed history.</summary>
    [Theory]
    [InlineData(16777216.25)]
    [InlineData(-16777216.25)]
    public void CaptureRebasesCommittedFrameWithoutAccumulation(double origin)
    {
        // Arrange two immutable source matrices and capture an initial frame at either large origin sign.
        float[] first = [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, .125f, -.25f, 0, 1];
        float[] second = [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, -.5f, .25f, 0, 1];
        var history = new FrameCameraHistory();

        // Act by capturing the initial frame.
        history.Capture(first, origin, 32, -origin);

        // Assert that the first frame initializes history without a shift.
        Assert.Equal(first, history.PreviousViewProjection);

        // Act through repeated origin changes, committing the latest captured matrix before the next transition.
        history.Commit();
        foreach (double movement in new[] { .125, 32.25, -.5, .125 })
        {
            history.Capture(second, origin + movement, 32.5, -origin - movement);

            // Assert each relative translation stays tied to the same committed frame.
            Assert.Equal(.125f + (float)movement, history.PreviousViewProjection[12]);
            Assert.Equal(.25f, history.PreviousViewProjection[13]);
            Assert.Equal(-(float)movement, history.PreviousViewProjection[14]);
        }
        history.Commit();
        history.Capture(first, origin + .25, 32.75, -origin - .25);

        // Assert the latest result uses the committed second matrix and leaves both source arrays unchanged.
        Assert.Equal(-.375f, history.PreviousViewProjection[12]);
        Assert.Equal(.5f, history.PreviousViewProjection[13]);
        Assert.Equal(-.125f, history.PreviousViewProjection[14]);
        Assert.Equal(.125f, first[12]);
        Assert.Equal(-.5f, second[12]);
    }
    #endregion
}
