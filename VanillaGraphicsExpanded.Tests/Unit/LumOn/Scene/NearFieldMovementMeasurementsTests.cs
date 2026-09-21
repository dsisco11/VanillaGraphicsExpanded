using System.Diagnostics;
using VanillaGraphicsExpanded.LumOn.Scene.NearField;
using VanillaGraphicsExpanded.Tests.Fixtures.NearField;
using VanillaGraphicsExpanded.WorldPartition;

namespace VanillaGraphicsExpanded.Tests.Unit.LumOn.Scene;

/// <summary>Fixed-window movement measurement using actual source coalescing and partition publication.</summary>
public sealed class NearFieldMovementMeasurementsTests
{
    private readonly ITestOutputHelper output;

    /// <summary>Emits measurements into test receipts without treating synthetic timing as live-game evidence.</summary>
    public NearFieldMovementMeasurementsTests(ITestOutputHelper output) => this.output = output;

    #region Fixed-window movement measurement
    /// <summary>Moving by one cell reuses overlap, coalesces source reads, and eventually acknowledges all demand.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FixedWindowReusesOverlap(bool runtimeBudgets)
    {
        using var fixture = new NearFieldGeometryFixture(limits: runtimeBudgets
            ? new(27, 64, 64, 32, 8L * 1024 * 1024) : null) { Deferred = true };
        var policy = new NearFieldCoveragePolicy();
        var layout = new PartitionLayout(new(16, 16, 16));
        long tick = 0, warmPublications = 0, warmReads = 0, peakSnapshots = 0;
        int maximumSettleFrames = 0, resolution = 0;
        double updateMilliseconds = 0;
        for (int step = 0; step <= 16; step++)
        {
            var position = new PartitionPoint(step, .5, .5);
            Assert.True(policy.TryPlan(position, 1, out var plan));
            resolution = plan.Resolution;
            var min = new PartitionCoordinate(plan.WindowOrigin.X / 16, plan.WindowOrigin.Y / 16, plan.WindowOrigin.Z / 16);
            int cells = plan.Resolution / 16;
            fixture.Window = new(min, new(min.X + cells, min.Y + cells, min.Z + cells));
            fixture.Coordinator.SetSource(new(1, fixture.Instance, "test", position, plan.Required));
            long start = Stopwatch.GetTimestamp();
            fixture.Frame(tick++);
            updateMilliseconds += Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            Assert.True(layout.Intersecting(plan.Required).ToHashSet().SetEquals(fixture.Coordinator.Cells(fixture.Instance)
                .Where(c => c.Desired == PartitionResidency.Active).Select(c => c.Key.Coordinate)));
            if (step == 0) Assert.All(fixture.Coordinator.Cells(fixture.Instance), c => Assert.False(c.Ready));
            int settle = 0;
            while (fixture.Coordinator.Cells(fixture.Instance).Any(c => !c.Ready) && settle < 200)
            {
                foreach (var work in fixture.Pending.Where(w => !w.Completion.Task.IsCompleted).ToArray())
                    work.Completion.SetResult(new(work.Key, work.Version, fixture.Source));
                start = Stopwatch.GetTimestamp();
                fixture.Frame(tick++);
                updateMilliseconds += Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                settle++;
            }
            Assert.True(settle < 200, "Supported finite demand failed to settle after delayed source completion.");
            Assert.All(fixture.Coordinator.Cells(fixture.Instance), c => Assert.True(c.Ready));
            maximumSettleFrames = Math.Max(maximumSettleFrames, settle);
            peakSnapshots = Math.Max(peakSnapshots, fixture.Cache.ResidentSnapshotBytes);
            if (step == 0) { warmPublications = fixture.PublicationCount; warmReads = fixture.Cache.SourceReads; }
        }
        // This monotonic path must not reload a source generation still inside the ring envelope.
        Assert.Equal(fixture.SourceRequests.Count, fixture.SourceRequests.Distinct().Count());
        Assert.True(fixture.Cache.CacheHits > 0);
        long movementCells = fixture.PublicationCount - warmPublications;
        Assert.Equal(27, warmPublications);
        Assert.Equal(9, movementCells);
        Assert.Equal(48, resolution);
        output.WriteLine($"SYNTHETIC runtimeBudgets={runtimeBudgets} prefetch=0 resolution={resolution} initialCells={warmPublications} movementCells={movementCells} " +
            $"initialReads={warmReads} movementReads={fixture.Cache.SourceReads-warmReads} duplicateReads=0 cacheHits={fixture.Cache.CacheHits} " +
            $"movementVoxelTransferBytes={movementCells*4096*20} peakSnapshotPayloadBytes={peakSnapshots} " +
            $"textureVoxelBytes={(long)resolution*resolution*resolution*8} maxSettleFrames={maximumSettleFrames} " +
            $"frameCalls={tick} totalFrameThreadMs={updateMilliseconds:F3}");
    }
    #endregion
}
