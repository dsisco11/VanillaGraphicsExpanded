using VanillaGraphicsExpanded.LumOn.Scene.Geometry;
using VanillaGraphicsExpanded.WorldPartition;
using VanillaGraphicsExpanded.Tests.Support.Lighting;

namespace VanillaGraphicsExpanded.Tests.Component.LumOn.Scene;

/// <summary>Checks publication progress and retained geometry under per-frame work limits.</summary>
public sealed class TraceGeometryWorkBudgetTests
{
    #region Public API
    #region Publication budgets
    /// <summary>Single-cell throughput gives both overlapping consumer classes progress before the broad window finishes.</summary>
    [Theory]
    [InlineData(1, 8388608)]
    [InlineData(16, 65536)]
    public void NarrowPublicationBudgetDoesNotStarveEitherConsumer(int cells, long bytes)
    {
        // Arrange
        var coverage = TraceGeometryCoverage.Plan(new(0, 128, 0), true, 128, 256);
        using var fixture = new TraceGeometryFixture(coverage.Resolution);

        // Act and Assert
        for (int frame = 0; frame < 100 && fixture.Backend.Publications.Count < 16; frame++)
        {
            fixture.Frame(coverage, new(2, cells, bytes));
            fixture.Complete();
        }
        Assert.True(fixture.Backend.Publications.Count >= 16);
        Assert.Contains(fixture.Backend.Publications.Take(16), key => coverage.IsNear(key.Coordinate));
        Assert.Contains(fixture.Backend.Publications.Take(16), key => !coverage.IsNear(key.Coordinate));
    }

    /// <summary>Shared byte ceilings bound uploads and pauses retain already published object identity.</summary>
    [Fact]
    public void SmallBudgetProgressesAndPausesWithoutDiscardingPublication()
    {
        // Arrange
        using var fixture = new TraceGeometryFixture();
        var coverage = TraceGeometryCoverage.Plan(new(1, 40, 1), true, null, 256);
        var budget = new TraceGeometryWorkBudget(1, 1, 1L << 16);

        // Act and assert: each frame respects request, publication, and upload ceilings while work progresses.
        for (int frame = 0; frame < 120; frame++)
        {
            int requests = fixture.Requests.Count;
            int publications = fixture.Backend.Publications.Count;
            long bytes = fixture.Backend.UploadedBytes;
            fixture.Frame(coverage, budget);
            fixture.Complete();
            Assert.InRange(fixture.Requests.Count - requests, 0, 1);
            Assert.InRange(fixture.Backend.Publications.Count - publications, 0, 1);
            Assert.InRange(fixture.Backend.UploadedBytes - bytes, 0, 1L << 16);
        }

        Assert.Equal(27, fixture.Backend.Ready.Count);
        var identities = fixture.Backend.Ready.ToArray();
        foreach (var pause in new[] { new TraceGeometryWorkBudget(8, 0, 8L << 20), new TraceGeometryWorkBudget(8, 32, 0) })
        {
            long bytes = fixture.Backend.UploadedBytes;
            for (int frame = 0; frame < 4; frame++)
            {
                fixture.Frame(coverage, pause);
            }
            Assert.Equal(bytes, fixture.Backend.UploadedBytes);
            foreach (var pair in identities)
            {
                Assert.Same(pair.Value, fixture.Backend.Ready[pair.Key]);
            }
        }

        fixture.Frame(coverage, TraceGeometryWorkBudget.Default);
        Assert.Equal(27, fixture.Backend.Publications.Count);
    }

    /// <summary>Disabling new source requests still permits publication from completed retained snapshots.</summary>
    [Fact]
    public void ZeroSourceBudgetPublishesCachedCells()
    {
        // Arrange
        using var fixture = new TraceGeometryFixture();
        var coverage = TraceGeometryCoverage.Plan(new(1, 40, 1), true, null, 256);

        fixture.Frame(coverage, new(1, 16, 8L << 20));
        // Act and Assert
        fixture.Complete();
        Assert.Single(fixture.Requests);
        for (int frame = 0; frame < 10; frame++)
        {
            fixture.Frame(coverage, new(0, 16, 8L << 20));
        }

        Assert.Single(fixture.Requests);
        Assert.NotEmpty(fixture.Backend.Ready);
        for (int frame = 0; frame < 20; frame++)
        {
            fixture.Frame(coverage);
            fixture.Complete();
        }
        Assert.Equal(27, fixture.Backend.Ready.Count);
    }

    /// <summary>Expanded logical windows converge while source requests and snapshot storage remain bounded.</summary>
    [Theory]
    [InlineData(192)]
    [InlineData(256)]
    public void ExpandedCoverageConvergesWithBoundedStorage(int resolution)
    {
        // Arrange
        var coverage = TraceGeometryCoverage.Plan(new(-1, 128, -1), true, resolution, 256);
        using var fixture = new TraceGeometryFixture(coverage.Resolution, observationMode: TraceGeometryObservationMode.MetadataOnly);
        int expected = new PartitionLayout(new(16, 16, 16)).Intersecting(coverage.Clip(coverage.Surface!.Value)).Count();

        // Act and Assert
        for (int frame = 0; frame < 700 && fixture.Backend.PublishedKeys.Count < expected; frame++)
        {
            int requests = fixture.RequestHistory.Count;
            int publications = fixture.Backend.Publications.Count;
            fixture.Frame(coverage);
            fixture.Complete();
            Assert.InRange(fixture.RequestHistory.Count - requests, 0, 2);
            Assert.InRange(fixture.Backend.Publications.Count - publications, 0, 16);
            Assert.InRange(fixture.Cache.InFlight, 0, 8);
            Assert.InRange(fixture.Cache.SnapshotBytes, 0, 8L * 393216);
        }

        Assert.Equal(expected, fixture.Backend.PublishedKeys.Count);
        Assert.Equal(expected, fixture.Backend.Publications.Count);
        Assert.Equal(fixture.RequestHistory.Count, fixture.RequestHistory.Distinct().Count());
    }
    #endregion
    #endregion
}







