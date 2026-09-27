using VanillaGraphicsExpanded.PBR.Tessellation;

namespace VanillaGraphicsExpanded.Tests;

/// <summary>Checks history invalidation independently of render timing and GPU readback.</summary>
public sealed class TerrainDisplacementHistoryTests
{
    private static readonly (double X, double Y, double Z, float Focal, int Level, float Pixels, float Start, float End) View =
        (0, 0, 0, 256, 8, 16, 8, 24);

    #region Atlas and view transitions
    /// <summary>A completed replacement between observations must invalidate once, then permit steady reuse.</summary>
    [Fact]
    public void CompleteToCompleteReplacementRejectsHistory()
    {
        var history = new TerrainDisplacementHistory();
        Assert.True(history.Observe(View, 1, true));
        Assert.False(history.Observe(View, 1, true));
        Assert.True(history.Observe(View, 3, true));
        Assert.False(history.Observe(View, 3, true));
    }

    /// <summary>Streaming rejects every incomplete observation and the first completed observation.</summary>
    [Fact]
    public void IncompleteBuildRejectsUntilReadySettles()
    {
        var history = new TerrainDisplacementHistory();
        Assert.True(history.Observe(View, 1, true));
        Assert.False(history.Observe(View, 1, true));
        Assert.True(history.Observe(View, 2, false));
        Assert.True(history.Observe(View, 2, false));
        Assert.True(history.Observe(View, 2, true));
        Assert.False(history.Observe(View, 2, true));
    }

    /// <summary>Camera or subdivision changes and explicit resets invalidate an otherwise stable atlas.</summary>
    [Fact]
    public void GeometryAndResetRejectHistory()
    {
        var history = new TerrainDisplacementHistory();
        history.Observe(View, 1, true);
        var moved = View;
        moved.X = 1;
        Assert.True(history.Observe(moved, 1, true));
        Assert.False(history.Observe(moved, 1, true));
        moved.Level = 4;
        Assert.True(history.Observe(moved, 1, true));
        Assert.False(history.Observe(moved, 1, true));
        history.Reset();
        Assert.True(history.Observe(moved, 1, true));
        Assert.False(history.Observe(moved, 1, true));
    }
    #endregion
}
