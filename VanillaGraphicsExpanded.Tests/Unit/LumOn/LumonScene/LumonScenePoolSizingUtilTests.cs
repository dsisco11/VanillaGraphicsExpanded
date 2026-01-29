using VanillaGraphicsExpanded.LumOn.Scene;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.Unit.LumOn.LumonScene;

public sealed class LumonScenePoolSizingUtilTests
{
    [Fact]
    public void ComputeSquareFieldBudget_MatchesCoveredPlusEdgeTimesTwo()
    {
        LumonSceneFieldChunkBudget b = LumonScenePoolSizingUtil.ComputeSquareFieldBudget(radiusChunks: 8);
        Assert.Equal(8, b.RadiusChunks);
        Assert.Equal(17, b.SideChunks);
        Assert.Equal(289, b.CoveredChunks);
        Assert.Equal(34, b.ExtraChunks);
        Assert.Equal(323, b.TotalChunks);
        Assert.Equal(323, LumonScenePoolSizingUtil.ComputeGuaranteedResidentPages(b));
        Assert.Equal(323, LumonScenePoolSizingUtil.ComputeGuaranteedResidentPagesSquareField(radiusChunks: 8));
    }

    [Fact]
    public void ComputeSquareFieldBudget_ZeroRadiusHasThreeTotalChunks()
    {
        LumonSceneFieldChunkBudget b = LumonScenePoolSizingUtil.ComputeSquareFieldBudget(radiusChunks: 0);
        Assert.Equal(1, b.SideChunks);
        Assert.Equal(1, b.CoveredChunks);
        Assert.Equal(2, b.ExtraChunks);
        Assert.Equal(3, b.TotalChunks);
    }

    [Fact]
    public void ComputeFarAnnulusBudget_SubtractsNearCoveredAndKeepsFarExtra()
    {
        LumonSceneFieldChunkBudget b = LumonScenePoolSizingUtil.ComputeFarAnnulusBudget(nearRadiusChunks: 8, farRadiusChunks: 32);
        Assert.Equal(32, b.RadiusChunks);
        Assert.Equal(65, b.SideChunks);
        Assert.Equal(4225 - 289, b.CoveredChunks);
        Assert.Equal(130, b.ExtraChunks);
        Assert.Equal((4225 - 289) + 130, b.TotalChunks);
        Assert.Equal((4225 - 289) + 130, LumonScenePoolSizingUtil.ComputeGuaranteedResidentPagesFarAnnulus(nearRadiusChunks: 8, farRadiusChunks: 32));
    }
}
