using VanillaGraphicsExpanded.LumOn.Scene;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.Unit.LumOn.LumonScene;

public sealed class LumonScenePoolSizingUtilTests
{
    [Fact]
    public void ComputeBoxFieldBudget_MatchesCoveredPlusMaxEdgeTimesTwo()
    {
        LumonSceneFieldChunkBudget b = LumonScenePoolSizingUtil.ComputeBoxFieldBudget(radiusXZChunks: 8, radiusYChunks: 0);
        Assert.Equal(8, b.RadiusXZChunks);
        Assert.Equal(0, b.RadiusYChunks);
        Assert.Equal(new(17, 1, 17), b.DimsChunks);
        Assert.Equal(289, b.CoveredChunks);
        Assert.Equal(34, b.ExtraChunks);
        Assert.Equal(323, b.TotalChunks);
        Assert.Equal(323, LumonScenePoolSizingUtil.ComputeGuaranteedResidentPages(b));
        Assert.Equal(323, LumonScenePoolSizingUtil.ComputeGuaranteedResidentPagesBoxField(radiusXZChunks: 8, radiusYChunks: 0));
    }

    [Fact]
    public void ComputeBoxFieldBudget_ZeroRadiusHasThreeTotalChunks()
    {
        LumonSceneFieldChunkBudget b = LumonScenePoolSizingUtil.ComputeBoxFieldBudget(radiusXZChunks: 0, radiusYChunks: 0);
        Assert.Equal(new(1, 1, 1), b.DimsChunks);
        Assert.Equal(1, b.CoveredChunks);
        Assert.Equal(2, b.ExtraChunks);
        Assert.Equal(3, b.TotalChunks);
    }

    [Fact]
    public void ComputeFarAnnulusBudget_SubtractsNearCoveredAndKeepsFarExtra()
    {
        LumonSceneFieldChunkBudget b = LumonScenePoolSizingUtil.ComputeFarAnnulusBudget(nearRadiusXZChunks: 8, nearRadiusYChunks: 0, farRadiusXZChunks: 32, farRadiusYChunks: 0);
        Assert.Equal(32, b.RadiusXZChunks);
        Assert.Equal(0, b.RadiusYChunks);
        Assert.Equal(new(65, 1, 65), b.DimsChunks);
        Assert.Equal(4225 - 289, b.CoveredChunks);
        Assert.Equal(130, b.ExtraChunks);
        Assert.Equal((4225 - 289) + 130, b.TotalChunks);
        Assert.Equal((4225 - 289) + 130, LumonScenePoolSizingUtil.ComputeGuaranteedResidentPagesFarAnnulus(nearRadiusXZChunks: 8, nearRadiusYChunks: 0, farRadiusXZChunks: 32, farRadiusYChunks: 0));
    }
}
