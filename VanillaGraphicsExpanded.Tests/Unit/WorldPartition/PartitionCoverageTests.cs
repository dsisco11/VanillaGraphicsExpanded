using VanillaGraphicsExpanded.WorldPartition;
using VanillaGraphicsExpanded.Tests.Fixtures.WorldPartition;

namespace VanillaGraphicsExpanded.Tests.Unit.WorldPartition;

/// <summary>Coverage contracts independent of rendering and game-world access.</summary>
public sealed class PartitionCoverageTests
{
    #region Coordinates and interval coverage
    /// <summary>Floor mapping preserves negative fractional coordinates and distinct axis extents.</summary>
    [Theory]
    [InlineData(-0.25, -1)]
    [InlineData(-16, -1)]
    [InlineData(-16.25, -2)]
    [InlineData(0, 0)]
    [InlineData(15.99, 0)]
    [InlineData(16, 1)]
    [InlineData(16777216.25, 1048576)]
    [InlineData(-16777216.25, -1048577)]
    public void FloorMapping(double x, long expected)
    {
        var layout = new PartitionLayout(new(16, 8, 0.25));
        Assert.Equal(new PartitionCoordinate(expected, -1, 1), layout.Coordinate(new(x, -0.125, 0.375)));
        Assert.Equal(new PartitionBounds(new(0, 0, 0), new(16, 8, 0.25)), layout.Bounds(new(0, 0, 0)));
    }

    /// <summary>Intersection includes slivers but excludes the upper boundary and empty intervals.</summary>
    [Fact]
    public void HalfOpenIntersection()
    {
        var layout = new PartitionLayout(new(16, 16, 16));
        Assert.Equal(new[] { new PartitionCoordinate(-1, 0, 0) }, layout.Intersecting(new(new(-16, 0, 0), new(0, 1, 1))));
        Assert.Equal(2, layout.Intersecting(new(new(15.99, 0, 0), new(16.01, 1, 1))).Count());
        Assert.Empty(layout.Intersecting(new(new(0, 0, 0), new(0, 1, 1))));
    }

    /// <summary>Every fractional offset preserves the complete requested domain before delayed publication.</summary>
    [Theory]
    [InlineData(16, 0)]
    [InlineData(32, -64)]
    [InlineData(16, 16777216)]
    public void FullIntervalSweep(int extent, double anchor)
    {
        var provider = new PartitionProviderFixture();
        var coordinator = new PartitionCoordinator(new(1000, 1000, 1000, 1000, 10000));
        long id = coordinator.Register("geometry", "world", new(new(extent, extent, extent)), new(0, 0, 0), new(1000, 1000, 1000, 1000, 10000), provider);
        for (int step = 0; step <= extent * 4; step++)
        {
            double x = anchor + step * 0.25;
            coordinator.SetSource(new(1, id, "world", new(x, 0, 0), new(new(x - 32, 0, 0), new(x + 32, 1, 1))));
            coordinator.Pump(step * 2);
            PartitionCellInfo[] cells = coordinator.Cells(id);
            long first = (long)Math.Floor((x - 32) / extent);
            long last = (long)Math.Ceiling((x + 32) / extent) - 1;
            Assert.Equal(Enumerable.Range(0, checked((int)(last - first + 1))).Select(i => first + i), cells.Select(c => c.Key.Coordinate.X).Order());
            Assert.All(cells, c => Assert.Equal(PartitionResidency.Active, c.Desired));
            if (step == 0) Assert.All(cells, c => Assert.False(c.Ready));
            provider.CompleteAll();
            coordinator.Pump(step * 2 + 1);
            Assert.All(coordinator.Cells(id), c => Assert.True(c.Ready));
        }
    }
    #endregion

    #region Source union and retention
    /// <summary>Same-category instances and source identities remain scoped by partition and world.</summary>
    [Fact]
    public void InstancesSourcesAndWorldsRemainIndependent()
    {
        var c = new PartitionCoordinator(new(100, 100, 100, 100, 10000));
        var p = new PartitionProviderFixture { AutoComplete = true };
        long a = c.Register("same", "a", new(new(16, 16, 16)), new(0, 0, 0), new(100, 100, 100, 100, 10000), p);
        long b = c.Register("same", "b", new(new(8, 8, 8)), new(0, 0, 0), new(100, 100, 100, 100, 10000), p);
        c.SetSource(new(1, a, "a", new(), new(new(), new(16, 1, 1))));
        c.SetSource(new(2, a, "a", new(), new(new(15, 0, 0), new(33, 1, 1))));
        c.SetSource(new(1, b, "b", new(), new(new(), new(16, 1, 1))));
        c.Pump(0);
        Assert.Equal(3, c.Cells(a).Length);
        Assert.Equal(2, c.Cells(b).Length);
        c.RemoveSource(a, 2);
        c.Pump(1);
        Assert.Single(c.Cells(a));
        Assert.Equal(2, c.Cells(b).Length);
        Assert.Throws<ArgumentException>(() => c.SetSource(new(3, a, "b", new(), new(new(), new(1, 1, 1)))));
        c.UnloadWorld("a");
        Assert.Equal(2, c.Cells(b).Length);
        Assert.All(p.Visible, key => Assert.Equal(b, key.Instance));
    }

    /// <summary>Boundary oscillation retains ready neighbors without delaying new required cells.</summary>
    [Fact]
    public void RetentionExpiresAndStableSourcesAvoidReevaluation()
    {
        var p = new PartitionProviderFixture { AutoComplete = true };
        var c = new PartitionCoordinator(new(10, 10, 10, 10, 100));
        long id = c.Register("p", "w", new(new(16, 16, 16)), new(0, 16, 2), new(10, 10, 10, 10, 100), p);
        PartitionSource source = new(1, id, "w", new(), new(new(15, 0, 0), new(16, 1, 1)));
        c.SetSource(source); c.Pump(0);
        long incarnation = c.Cells(id)[0].Incarnation;
        c.SetSource(source with { Required = new(new(16, 0, 0), new(17, 1, 1)) }); c.Pump(1);
        Assert.Equal(2, c.Cells(id).Length);
        Assert.Equal(PartitionResidency.Loaded, c.Cells(id).Single(x => x.Incarnation == incarnation).Desired);
        c.SetSource(source); c.Pump(2);
        Assert.Equal(incarnation, c.Cells(id).Single(x => x.Key.Coordinate.X == 0).Incarnation);
        long evaluations = c.Statistics(id).CoverageEvaluations;
        c.SetSource(source); c.Pump(5);
        Assert.Equal(evaluations, c.Statistics(id).CoverageEvaluations);
        Assert.Single(c.Cells(id));
    }
    #endregion
}
