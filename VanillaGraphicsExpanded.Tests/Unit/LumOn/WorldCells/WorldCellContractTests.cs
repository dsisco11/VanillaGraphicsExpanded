using VanillaGraphicsExpanded.WorldPartition;
using VanillaGraphicsExpanded.Numerics;

using Xunit;

namespace VanillaGraphicsExpanded.Tests.Unit.LumOn.WorldCells;

public sealed class WorldCellContractTests
{
    [Fact]
    public void Kind_MatchesKeyKind()
    {
        var cell = new DummyCell(new WorldCellKey(WorldCellKind.LumonSceneFar, 42));

        Assert.Equal(WorldCellKind.LumonSceneFar, cell.Kind);
        Assert.Equal(cell.Kind, cell.Key.Kind);
    }

    [Fact]
    public void CalculatePriority_DoesNotMutateStoredPriority()
    {
        var cell = new DummyCell(new WorldCellKey(WorldCellKind.LumonSceneNear, 1));
        cell.Priority = 123f;

        var ctx = new WorldCellPriorityContext(
            CameraBlockPos: new VectorInt3(0, 0, 0),
            AnchorBlockPos: new VectorInt3(0, 0, 0),
            HasAnchor: false,
            WindowMinRegion: default,
            WindowMaxRegion: default,
            HasWindow: false,
            NowTick: 10);

        float calculated = cell.CalculatePriority(in ctx);

        Assert.Equal(10f, calculated);
        Assert.Equal(123f, cell.Priority);
    }

    private sealed class DummyCell : WorldCell
    {
        public DummyCell(WorldCellKey key)
            : base(key)
        {
        }

        public override float CalculatePriority(in WorldCellPriorityContext context)
            => context.NowTick;
    }
}
