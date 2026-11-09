using VanillaGraphicsExpanded.LumOn.Scene.Geometry;

namespace VanillaGraphicsExpanded.Tests.Unit.LumOn.Scene;

/// <summary>Verifies exact cell-aligned windows produced by the coverage planner.</summary>
public sealed class TraceGeometryBackendTests
{
    #region Public API
    /// <summary>Every authored camera offset retains both logical windows within the allocation.</summary>
    [Theory]
    [InlineData(16)] [InlineData(32)] [InlineData(64)] [InlineData(128)] [InlineData(192)] [InlineData(256)]
    public void CoveragePreservesDomainsAtEveryOffset(int n)
    {
        // Arrange: examine every fractional cell offset at small and large world origins.
        // Act and Assert
        foreach (int anchor in new[] { -16777216, -32, 0, 16777216 })
        for (int x = 0; x < 16; x++)
        for (int y = 0; y < 16; y++)
        for (int z = 0; z < 16; z++)
        {
            var plan = TraceGeometryCoverage.Plan(new(anchor + x + .5, 40 + y, anchor + z), true, n, 256);
            foreach (var bounds in new[] { plan.NearField!.Value, plan.Surface!.Value })
            {
                Assert.True(bounds.Min.X >= plan.Window.Min.X && bounds.Max.X <= plan.Window.Max.X);
                Assert.True(bounds.Min.Y >= plan.Window.Min.Y && bounds.Max.Y <= plan.Window.Max.Y);
                Assert.True(bounds.Min.Z >= plan.Window.Min.Z && bounds.Max.Z <= plan.Window.Max.Z);
            }
            Assert.Equal(48, plan.NearField.Value.Max.X - plan.NearField.Value.Min.X);
        }
    }

    /// <summary>World-height clipping leaves horizontal coverage and consumer selection unchanged.</summary>
    [Theory]
    [InlineData(true, false, -5)] [InlineData(true, false, 255)]
    [InlineData(false, true, -5)] [InlineData(false, true, 255)]
    public void CoverageModesClipDemandAtWorldHeight(bool near, bool surface, int y)
    {
        // Arrange: make only the selected consumer's vertical range extend beyond world limits.
        var plan = TraceGeometryCoverage.Plan(new(-1, y, 16777216), near, surface ? 32 : null, 256);
        var bounds = (plan.NearField ?? plan.Surface)!.Value;

        // Act and Assert: clipping constrains Y and preserves the original X/Z extent and consumer selection.
        var clipped = plan.Clip(bounds);
        Assert.InRange(clipped.Min.Y, 0, 256);
        Assert.InRange(clipped.Max.Y, 0, 256);
        Assert.Equal(near, plan.NearField.HasValue);
        Assert.Equal(surface, plan.Surface.HasValue);
        Assert.Equal(bounds.Min.X, clipped.Min.X);
        Assert.Equal(bounds.Max.Z, clipped.Max.Z);
        Assert.True(bounds.Min.Y < 0 || bounds.Max.Y > 256);
    }
    #endregion
}
