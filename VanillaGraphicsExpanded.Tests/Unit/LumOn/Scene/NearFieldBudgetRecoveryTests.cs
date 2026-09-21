using VanillaGraphicsExpanded.LumOn.Scene.NearField;
using VanillaGraphicsExpanded.Tests.Fixtures.NearField;
using VanillaGraphicsExpanded.WorldPartition;

namespace VanillaGraphicsExpanded.Tests.Unit.LumOn.Scene;

/// <summary>Production-sized resource limits must converge after movement, edits and delayed teleport completions.</summary>
public sealed class NearFieldBudgetRecoveryTests
{
    private readonly ITestOutputHelper output;
    private static readonly PartitionLimits Limits = new(27, 64, 64, 32, 8L * 1024 * 1024);

    /// <summary>Records deterministic recovery counts independently from machine-dependent timing.</summary>
    public NearFieldBudgetRecoveryTests(ITestOutputHelper output) => this.output = output;

    #region Resource-bounded recovery
    /// <summary>The full pipeline honors runtime budgets, preserves overlap and never exposes obsolete terrain.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-16777216)]
    [InlineData(16777216)]
    public void MovementEditAndTeleportConvergeWithinRuntimeLimits(int anchor)
    {
        using var fixture = new NearFieldGeometryFixture(limits: Limits) { Deferred = true };
        long tick = 0;
        SetWindow(fixture, new(anchor + .5, 64.5, anchor + .5));
        Pump(fixture, tick++);
        Assert.Equal(27, fixture.Coordinator.Statistics(fixture.Instance).Required);
        Assert.Empty(fixture.Published);
        int initial = Settle(fixture, ref tick);
        Assert.Equal(27, fixture.PublicationCount);

        SetWindow(fixture, new(anchor + 16.5, 64.5, anchor + .5));
        Pump(fixture, tick++);
        Assert.Equal(18, fixture.Published.Count);
        int movement = Settle(fixture, ref tick);
        Assert.Equal(36, fixture.PublicationCount);

        // Invalidate ready data and delay its replacement before teleporting away.
        fixture.Version++;
        Array.Fill(fixture.Source, new NearFieldSourceCell(2, default));
        Pump(fixture, tick++);
        Assert.Empty(fixture.Published);
        var abandoned = fixture.Pending.Where(p => !p.Completion.Task.IsCompleted).ToArray();
        Assert.NotEmpty(abandoned);
        SetWindow(fixture, new(anchor + 1024.5, 64.5, anchor - 1024.5));
        Pump(fixture, tick++);
        Assert.All(abandoned, p => Assert.True(p.Cancellation.IsCancellationRequested));
        Assert.Empty(fixture.Published);
        // Complete cancelled snapshots with stale geometry; no destination can publish them.
        foreach (var pending in abandoned)
            pending.Completion.SetResult(new(pending.Key, pending.Version, new NearFieldSourceCell[32768]));
        int teleport = Settle(fixture, ref tick);
        Assert.All(fixture.Published.Values, cells => Assert.All(cells, cell => Assert.Equal(2u, cell.Geometry)));

        // A subsequent terrain edit must also invalidate and replace the current window.
        fixture.Version++;
        Array.Fill(fixture.Source, new NearFieldSourceCell(1, default));
        Pump(fixture, tick++);
        Assert.Empty(fixture.Published);
        int edit = Settle(fixture, ref tick);
        Assert.All(fixture.Published.Values, cells => Assert.All(cells, cell => Assert.Equal(1u, cell.Geometry)));
        Assert.Equal(fixture.SourceRequests.Count, fixture.SourceRequests.Distinct().Count());
        output.WriteLine($"RECOVERY anchor={anchor} initialTicks={initial} movementTicks={movement} " +
            $"teleportTicks={teleport} editTicks={edit} reads={fixture.Cache.SourceReads} publications={fixture.PublicationCount}");
    }
    #endregion

    #region Scenario execution
    /// <summary>Applies the actual fixed-window planner without conflating demand with publication.</summary>
    private static void SetWindow(NearFieldGeometryFixture fixture, in PartitionPoint position)
    {
        Assert.True(new NearFieldCoveragePolicy().TryPlan(position, 20, out var plan, 256));
        var min = new PartitionCoordinate(plan.WindowOrigin.X / 16, plan.WindowOrigin.Y / 16, plan.WindowOrigin.Z / 16);
        fixture.Window = new(min, new(min.X + 3, min.Y + 3, min.Z + 3));
        fixture.Coordinator.SetSource(new(1, fixture.Instance, "test", position, plan.Required));
    }

    /// <summary>Advances one update and asserts actual source/publication service and resident ceilings.</summary>
    private static void Pump(NearFieldGeometryFixture fixture, long tick)
    {
        long reads = fixture.Cache.SourceReads, publications = fixture.PublicationCount;
        fixture.Frame(tick);
        Assert.InRange(fixture.Cache.SourceReads - reads, 0, 2);
        Assert.InRange(fixture.Cache.InFlight, 0, 8);
        // The provider reserves a worst-case palette per publication even when unchanged.
        long reservation = 4096L * 20 + 1 + NearFieldMaterialRegistry.MaximumUploadBytes;
        Assert.True((fixture.PublicationCount - publications) * reservation <= Limits.UploadBytes);
        Assert.InRange(fixture.Coordinator.Statistics(fixture.Instance).Resident, 0, 27);
        Assert.InRange(fixture.Cache.ResidentSnapshotBytes, 0, 8L * 32768 * 20);
        Assert.All(fixture.Published.Keys, key => Assert.True(fixture.ContainsCell(key.Coordinate)));
    }

    /// <summary>Completes each bounded batch one update later and requires finite ready convergence.</summary>
    private static int Settle(NearFieldGeometryFixture fixture, ref long tick)
    {
        for (int elapsed = 1; elapsed <= 64; elapsed++)
        {
            foreach (var pending in fixture.Pending.Where(p => !p.Completion.Task.IsCompleted).ToArray())
                pending.Completion.SetResult(new(pending.Key, pending.Version, fixture.Source));
            Pump(fixture, tick++);
            var diagnostics = Assert.Single(fixture.Coordinator.Diagnostics());
            if (diagnostics.RequiredReady != 27) continue;
            Assert.Equal(0, diagnostics.Statistics.CaptureBacklog);
            Assert.Equal(0, diagnostics.Statistics.UploadBacklog);
            Assert.Equal(0, diagnostics.Statistics.CapacityShortfall);
            Assert.Equal(0, diagnostics.OldestRequiredWaitTicks);
            Assert.Equal(27, fixture.Published.Count);
            return elapsed;
        }
        throw new Xunit.Sdk.XunitException("Ready coverage failed to converge within 64 updates under runtime limits.");
    }
    #endregion
}
