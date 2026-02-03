using VanillaGraphicsExpanded.LumOn.WorldCells;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.Unit.LumOn.WorldCells;

public sealed class WorldCellStateMachineTests
{
    [Fact]
    public void DesiredActive_FromUnloaded_RequestsEnsureLoadedFirst()
    {
        Assert.True(WorldCellStateMachine.TryGetNextAction(WorldCellDesiredState.Active, WorldCellActualState.Unloaded, out var action));
        Assert.Equal(WorldCellTransitionAction.EnsureLoaded, action);
    }

    [Fact]
    public void DesiredLoaded_FromActive_RequestsDeactivateToLoaded()
    {
        Assert.True(WorldCellStateMachine.TryGetNextAction(WorldCellDesiredState.Loaded, WorldCellActualState.Active, out var action));
        Assert.Equal(WorldCellTransitionAction.DeactivateToLoaded, action);
    }

    [Fact]
    public void DesiredUnloaded_FromActive_RequestsEnsureUnloaded()
    {
        Assert.True(WorldCellStateMachine.TryGetNextAction(WorldCellDesiredState.Unloaded, WorldCellActualState.Active, out var action));
        Assert.Equal(WorldCellTransitionAction.EnsureUnloaded, action);
    }

    [Fact]
    public void CooldownBlocksTransitionSelection_WhenUsingContextOverload()
    {
        var cell = new TestCell(WorldCellKey.FromLumonSceneNear(123));
        cell.DesiredState = WorldCellDesiredState.Active;
        cell.ActualState = WorldCellActualState.Unloaded;
        cell.NextEligibleTick = 200;

        var ctx = new WorldCellStateTransitionContext(
            CameraBlockPos: default,
            AnchorBlockPos: default,
            HasAnchor: false,
            LoadedWindowMinRegion: default,
            LoadedWindowMaxRegion: default,
            HasLoadedWindow: false,
            ActiveWindowMinRegion: default,
            ActiveWindowMaxRegion: default,
            HasActiveWindow: false,
            NowTick: 100);

        Assert.False(WorldCellStateMachine.TryGetNextAction(cell, in ctx, out _));
    }

    [Fact]
    public void NotifyTransitionFailed_SetsIncreasingCooldown()
    {
        var cell = new TestCell(WorldCellKey.FromLumonSceneNear(456));
        long now = 100;

        WorldCellStateMachine.NotifyTransitionFailed(cell, nowTick: now, minCooldownTicks: 2, maxCooldownTicks: 120);
        long first = cell.NextEligibleTick;

        now++;
        WorldCellStateMachine.NotifyTransitionFailed(cell, nowTick: now, minCooldownTicks: 2, maxCooldownTicks: 120);
        long second = cell.NextEligibleTick;

        Assert.True(second > first);

        WorldCellStateMachine.NotifyTransitionSucceeded(cell);
        Assert.Equal(0, cell.CooldownStreak);
    }

    private sealed class TestCell : WorldCell
    {
        public TestCell(WorldCellKey key) : base(key)
        {
        }

        public override float CalculatePriority(in WorldCellPriorityContext context) => 0;

        // Expose internal fields for assertions.
        public new int CooldownStreak => base.CooldownStreak;
    }
}
