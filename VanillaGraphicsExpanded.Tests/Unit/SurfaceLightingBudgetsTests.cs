using VanillaGraphicsExpanded.LumOn.Scene;

namespace VanillaGraphicsExpanded.Tests.Unit;

/// <summary>Checks independent requested budgets against the shared publication bound.</summary>
public sealed class SurfaceLightingBudgetsTests
{
    #region Allocation
    /// <summary>Unconstrained stages preserve individual limits including explicit disablement.</summary>
    [Theory]
    [InlineData(8, 8, 4)] [InlineData(8, 8, 0)] [InlineData(0, 0, 4)] [InlineData(0, 0, 0)]
    public void IndependentBudgetsSurviveWhenCapacityAllows(int seed, int direct, int indirect)
        => Assert.Equal(new SurfaceLightingBudgets(seed, direct, indirect), SurfaceLightingBudgets.Resolve(seed, direct, indirect, 16, 0));

    /// <summary>Every tile size and starting stage respects both requested limits and total texel ceiling.</summary>
    [Theory]
    [InlineData(4)] [InlineData(16)] [InlineData(32)] [InlineData(64)] [InlineData(128)] [InlineData(256)]
    public void SharedCeilingBoundsAggregateWork(int tile)
    {
        for (int frame = 0; frame < 6; frame++)
        {
            var budget = SurfaceLightingBudgets.Resolve(400, -1, 256, tile, frame);
            Assert.InRange(budget.Seed, 0, 256); Assert.Equal(0, budget.Direct);
            Assert.InRange(budget.Indirect, 0, 256);
            Assert.Equal(Math.Min(512, 65536 / (tile * tile)), budget.Seed + budget.Indirect);
        }
    }

    /// <summary>A single available slot rotates among all requesting stages instead of starving any stage.</summary>
    [Fact]
    public void ScarceCapacityRotatesAcrossStages()
    {
        var total = new int[3];
        for (int frame = 0; frame < 6; frame++)
        {
            var budget = SurfaceLightingBudgets.Resolve(8, 8, 4, 256, frame);
            total[0] += budget.Seed; total[1] += budget.Direct; total[2] += budget.Indirect;
            Assert.Equal(1, budget.Seed + budget.Direct + budget.Indirect);
        }
        Assert.Equal(new[] { 2, 2, 2 }, total);
    }
    #endregion
}
