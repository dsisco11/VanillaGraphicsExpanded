using VanillaGraphicsExpanded.LumOn.Scene;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.Unit.LumOn.Scene;

/// <summary>Checks LumOn scene pool sizing util behavior and contract rules.</summary>
public sealed class LumonScenePoolSizingUtilTests
{
    #region Public API
    /// <summary>Verifies the compute box field budget matches covered plus max edge times two behavior.</summary>
    [Fact]
    public void ComputeBoxFieldBudget_MatchesCoveredPlusMaxEdgeTimesTwo()
    {
        // Arrange a radius-eight horizontal box with a single vertical layer.
        // Act by computing its budget and assert covered, edge-extra and total chunks.
        LumonSceneFieldChunkBudget b = LumonScenePoolSizingUtil.ComputeBoxFieldBudget(radiusXZChunks: 8, radiusYChunks: 0);
        // Assert
        Assert.Equal(8, b.RadiusXZChunks);
        Assert.Equal(0, b.RadiusYChunks);
        Assert.Equal(new(17, 1, 17), b.DimsChunks);
        Assert.Equal(289, b.CoveredChunks);
        Assert.Equal(34, b.ExtraChunks);
        Assert.Equal(323, b.TotalChunks);
        Assert.Equal(323, LumonScenePoolSizingUtil.ComputeGuaranteedResidentPages(b));
        Assert.Equal(323, LumonScenePoolSizingUtil.ComputeGuaranteedResidentPagesBoxField(radiusXZChunks: 8, radiusYChunks: 0));
    }

    /// <summary>Verifies the compute box field budget zero radius has three total chunks behavior.</summary>
    [Fact]
    public void ComputeBoxFieldBudget_ZeroRadiusHasThreeTotalChunks()
    {
        // Arrange zero radii on all axes to exercise the smallest box budget.
        // Act by computing that budget and assert the center plus two edge chunks.
        LumonSceneFieldChunkBudget b = LumonScenePoolSizingUtil.ComputeBoxFieldBudget(radiusXZChunks: 0, radiusYChunks: 0);
        // Assert
        Assert.Equal(new(1, 1, 1), b.DimsChunks);
        Assert.Equal(1, b.CoveredChunks);
        Assert.Equal(2, b.ExtraChunks);
        Assert.Equal(3, b.TotalChunks);
    }

    /// <summary>Verifies the compute far annulus budget subtracts near covered and keeps far extra behavior.</summary>
    [Fact]
    public void ComputeFarAnnulusBudget_SubtractsNearCoveredAndKeepsFarExtra()
    {
        // Arrange nested near and far radii so the far budget excludes near coverage.
        // Act by computing the annulus budget and assert far extras remain included.
        LumonSceneFieldChunkBudget b = LumonScenePoolSizingUtil.ComputeFarAnnulusBudget(nearRadiusXZChunks: 8, nearRadiusYChunks: 0, farRadiusXZChunks: 32, farRadiusYChunks: 0);
        // Assert
        Assert.Equal(32, b.RadiusXZChunks);
        Assert.Equal(0, b.RadiusYChunks);
        Assert.Equal(new(65, 1, 65), b.DimsChunks);
        Assert.Equal(4225 - 289, b.CoveredChunks);
        Assert.Equal(130, b.ExtraChunks);
        Assert.Equal((4225 - 289) + 130, b.TotalChunks);
        Assert.Equal((4225 - 289) + 130, LumonScenePoolSizingUtil.ComputeGuaranteedResidentPagesFarAnnulus(nearRadiusXZChunks: 8, nearRadiusYChunks: 0, farRadiusXZChunks: 32, farRadiusYChunks: 0));
    }
    #endregion
}











