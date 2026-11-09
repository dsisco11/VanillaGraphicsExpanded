using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.LumOn.Scene;

namespace VanillaGraphicsExpanded.Tests.Unit.LumOn.Scene;

/// <summary>Verifies rejected GPU atlas allocations return their reserved capacity.</summary>
public sealed class SurfaceLightingBudgetTests
{
    #region Public API
    /// <summary>Over-budget allocations fail before invoking GL and return their reserved credit.</summary>
    [Fact]
    public void RejectedAllocationDoesNotConsumeCredit()
    {
        // Arrange: record available capacity before the rejected request.
        bool before = LumonScenePhysicalAtlasGpuResources.CanAllocate(1);

        // Act: request more physical atlases than the configured capacity permits.
        Assert.Throws<InvalidOperationException>(() => new LumonScenePhysicalAtlasGpuResources(LumonSceneField.Near, 7, 4));

        // Assert: the failed request did not retain allocation credit.
        Assert.Equal(before, LumonScenePhysicalAtlasGpuResources.CanAllocate(1));
    }
    #endregion
}










