using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.LumOn;

namespace VanillaGraphicsExpanded.Tests.Unit.Rendering;

/// <summary>Verifies temporal matrices retain the render origin belonging to their committed frame.</summary>
public sealed class FrameCameraHistoryTests
{
    #region Frame lifecycle
    /// <summary>Rebased position projection includes origin and eye translation, while cloud displacements exclude both.</summary>
    [Fact]
    public void SharedMatrixSeparatesPositionAndDisplacementTranslation()
    {
        float[] perspective=[2,0,0,0,0,3,0,0,0,0,-1.02f,-1,0,0,-.202f,0];
        float[] previous=(float[])perspective.Clone();
        // Native view translation is transformed by projection exactly once.
        previous[12]=8;previous[13]=-6;previous[14]=-.202f+1.02f;previous[15]=1;
        var history=new FrameCameraHistory();
        history.Capture(previous,16777216.25,32,-16777216.25);history.Commit();
        history.Capture(perspective,16777216.5,32.5,-16777216.125);
        var matrix=history.PreviousViewProjection;
        Assert.Equal(8.5f,matrix[12]);Assert.Equal(-4.5f,matrix[13]);
        // Position uses w=1, whereas displacement uses w=0 plus projection's perspective column.
        float positionW=matrix[11]*-10+matrix[15];
        float displacementW=matrix[11]*-10+perspective[15];
        Assert.Equal(10.875f,positionW);Assert.Equal(10,displacementW);
        Assert.Equal(2,perspective[0]);Assert.Equal(previous[..12],matrix[..12]);
    }
    /// <summary>Rebasing composes through every perspective row, including homogeneous clip W.</summary>
    [Fact]
    public void CaptureComposesOriginDeltaThroughPerspectiveW()
    {
        float[] perspective = [2, 0, 0, 0, 0, 3, 0, 0, 0, 0, -1.02f, -1, 0, 0, -.202f, 0];
        var history = new FrameCameraHistory();
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
        var history = new FrameCameraHistory();
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
