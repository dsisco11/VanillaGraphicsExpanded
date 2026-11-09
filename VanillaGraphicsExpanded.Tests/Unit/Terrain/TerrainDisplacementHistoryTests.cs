using VanillaGraphicsExpanded.PBR.Tessellation;

namespace VanillaGraphicsExpanded.Tests.Unit.Terrain;

/// <summary>Checks when terrain displacement history becomes invalid without render timing or GPU readback.</summary>
public sealed class TerrainDisplacementHistoryTests
{
    private static readonly (double X, double Y, double Z, float Focal, int Level, float Pixels, float Start, float End) View =
        (0, 0, 0, 256, 8, 16, 8, 24);

    #region Public API
    #region Atlas and view transitions
    /// <summary>A completed replacement invalidates history once, then permits steady reuse.</summary>
    [Fact]
    public void CompleteToCompleteReplacementRejectsHistory()
    {
        // Arrange a fresh history tracker with a stable view and a completed atlas.
        var history = new TerrainDisplacementHistory();

        // Act on the first observation and assert the new history requests work.
        Assert.True(history.Observe(View, 1, true));

        // Act on the same stable atlas and assert cached history can now be reused.
        Assert.False(history.Observe(View, 1, true));

        // Act on an entirely completed atlas replacement, then assert invalidation occurs only once.
        Assert.True(history.Observe(View, 3, true));
        Assert.False(history.Observe(View, 3, true));
    }

    /// <summary>Streaming rejects incomplete observations and the first completed observation.</summary>
    [Fact]
    public void IncompleteBuildRejectsUntilReadySettles()
    {
        // Arrange a fresh history tracker and admit one completed baseline observation.
        var history = new TerrainDisplacementHistory();
        Assert.True(history.Observe(View, 1, true));
        Assert.False(history.Observe(View, 1, true));

        // Act on an incomplete replacement and assert streaming keeps invalidating history.
        Assert.True(history.Observe(View, 2, false));
        Assert.True(history.Observe(View, 2, false));

        // Act once the replacement is complete; its first ready observation still invalidates the old history.
        Assert.True(history.Observe(View, 2, true));

        // Assert the next observation can reuse the now-stable history.
        Assert.False(history.Observe(View, 2, true));
    }

    /// <summary>Camera, subdivision, and explicit reset transitions each invalidate an otherwise stable atlas.</summary>
    [Fact]
    public void GeometryAndResetRejectHistory()
    {
        // Arrange a stable baseline before changing one view dimension at a time.
        var history = new TerrainDisplacementHistory();
        history.Observe(View, 1, true);
        var moved = View;
        moved.X = 1;

        // Act on camera movement and assert one invalidation before reuse resumes.
        Assert.True(history.Observe(moved, 1, true));
        Assert.False(history.Observe(moved, 1, true));

        // Act on subdivision change and assert history invalidates again.
        moved.Level = 4;
        Assert.True(history.Observe(moved, 1, true));
        Assert.False(history.Observe(moved, 1, true));

        // Act on explicit reset and assert the next observation starts a fresh history sequence.
        history.Reset();
        Assert.True(history.Observe(moved, 1, true));
        Assert.False(history.Observe(moved, 1, true));
    }
    #endregion
    #endregion
}
