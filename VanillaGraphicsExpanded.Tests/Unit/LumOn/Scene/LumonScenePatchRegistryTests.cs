using VanillaGraphicsExpanded.LumOn.Scene;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.Unit.LumOn.Scene;

/// <summary>Checks LumOn scene patch registry behavior and contract rules.</summary>
public sealed class LumonScenePatchRegistryTests
{
    #region Public API
    /// <summary>Verifies the get or create returns stable patch ids across remeshes behavior.</summary>
    [Fact]
    public void GetOrCreate_ReturnsStablePatchIdsAcrossRemeshes()
    {
        // Arrange
        var reg = new LumonScenePatchRegistry();

        // Act
        var aKey = LumonScenePatchKey.CreateVoxelFacePatch(LumonSceneFace.North, planeIndex: 7, patchUIndex: 3, patchVIndex: 1);
        var bKey = LumonScenePatchKey.CreateVoxelFacePatch(LumonSceneFace.Up, planeIndex: 0, patchUIndex: 7, patchVIndex: 7);

        reg.BeginRemesh(1);
        LumonScenePatchId a1 = reg.GetOrCreate(aKey);
        LumonScenePatchId b1 = reg.GetOrCreate(bKey);
        // Assert
        Assert.Equal(2, reg.ActivePatchIds.Count);

        // Act and Assert
        reg.BeginRemesh(2);
        LumonScenePatchId b2 = reg.GetOrCreate(bKey);
        LumonScenePatchId a2 = reg.GetOrCreate(aKey);

        Assert.Equal(a1, a2);
        Assert.Equal(b1, b2);
        Assert.Equal(2, reg.ActivePatchIds.Count);
    }

    /// <summary>Verifies the patch id is stable when patch disappears and reappears behavior.</summary>
    [Fact]
    public void PatchId_IsStableWhenPatchDisappearsAndReappears()
    {
        // Arrange
        var reg = new LumonScenePatchRegistry();

        // Act
        var aKey = LumonScenePatchKey.CreateVoxelFacePatch(LumonSceneFace.East, planeIndex: 31, patchUIndex: 0, patchVIndex: 0);
        var bKey = LumonScenePatchKey.CreateMeshCard(instanceStableId: 0x1234_5678_9abc_def0ul, cardIndex: 9);

        reg.BeginRemesh(1);
        LumonScenePatchId a1 = reg.GetOrCreate(aKey);
        LumonScenePatchId b1 = reg.GetOrCreate(bKey);

        reg.BeginRemesh(2);
        LumonScenePatchId a2 = reg.GetOrCreate(aKey);
        // Assert
        Assert.Equal(a1, a2);
        Assert.Single(reg.ActivePatchIds);

        // Act and Assert
        reg.BeginRemesh(3);
        LumonScenePatchId b2 = reg.GetOrCreate(bKey);
        Assert.Equal(b1, b2);
        Assert.Single(reg.ActivePatchIds);
    }
    #endregion
}












