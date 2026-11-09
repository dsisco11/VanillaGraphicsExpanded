using VanillaGraphicsExpanded.WorldPartition;
using VanillaGraphicsExpanded.Numerics;

using Xunit;

namespace VanillaGraphicsExpanded.Tests.Unit.LumOn.WorldCells;

/// <summary>Checks world cell contract behavior and contract rules.</summary>
public sealed class WorldCellContractTests
{
    #region Public API
    /// <summary>Verifies the kind matches key kind behavior.</summary>
    [Fact]
    public void Kind_MatchesKeyKind()
    {
        // Arrange
        var cell = new DummyCell(new WorldCellKey(WorldCellKind.LumonSceneFar, 42));
        // Act and Assert
        Assert.Equal(WorldCellKind.LumonSceneFar, cell.Kind);
        Assert.Equal(cell.Kind, cell.Key.Kind);
    }

    /// <summary>Verifies the calculate priority does not mutate stored priority behavior.</summary>
    [Fact]
    public void CalculatePriority_DoesNotMutateStoredPriority()
    {
        // Arrange
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
        // Act
        float calculated = cell.CalculatePriority(in ctx);
        // Assert
        Assert.Equal(10f, calculated);
        Assert.Equal(123f, cell.Priority);
    }

    #endregion
    #region Private
    /// <summary>Provides the dummy cell test helper.</summary>
    private sealed class DummyCell : WorldCell
    {
        /// <summary>Creates a cell with the supplied stable key.</summary>
        public DummyCell(WorldCellKey key)
            : base(key)
        {
        }

        /// <summary>Provides the calculate priority behavior used by this test helper.</summary>
        public override float CalculatePriority(in WorldCellPriorityContext context)
            => context.NowTick;
    }
    #endregion
}
