using VanillaGraphicsExpanded.LumOn.Scene;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.Unit.LumOn.LumonScene;

public sealed class LumonScenePatchRegistryTests
{
    [Fact]
    public void GetOrCreate_ReturnsStablePatchIdsAcrossRemeshes()
    {
        var reg = new LumonScenePatchRegistry();

        var aKey = LumonScenePatchKey.CreateVoxelFacePatch(LumonSceneFace.North, planeIndex: 7, patchUIndex: 3, patchVIndex: 1);
        var bKey = LumonScenePatchKey.CreateVoxelFacePatch(LumonSceneFace.Up, planeIndex: 0, patchUIndex: 7, patchVIndex: 7);

        reg.BeginRemesh(1);
        LumonScenePatchId a1 = reg.GetOrCreate(aKey);
        LumonScenePatchId b1 = reg.GetOrCreate(bKey);
        Assert.Equal(2, reg.ActivePatchIds.Count);

        reg.BeginRemesh(2);
        LumonScenePatchId b2 = reg.GetOrCreate(bKey);
        LumonScenePatchId a2 = reg.GetOrCreate(aKey);

        Assert.Equal(a1, a2);
        Assert.Equal(b1, b2);
        Assert.Equal(2, reg.ActivePatchIds.Count);
    }

    [Fact]
    public void PatchId_IsStableWhenPatchDisappearsAndReappears()
    {
        var reg = new LumonScenePatchRegistry();

        var aKey = LumonScenePatchKey.CreateVoxelFacePatch(LumonSceneFace.East, planeIndex: 31, patchUIndex: 0, patchVIndex: 0);
        var bKey = LumonScenePatchKey.CreateMeshCard(instanceStableId: 0x1234_5678_9abc_def0ul, cardIndex: 9);

        reg.BeginRemesh(1);
        LumonScenePatchId a1 = reg.GetOrCreate(aKey);
        LumonScenePatchId b1 = reg.GetOrCreate(bKey);

        reg.BeginRemesh(2);
        LumonScenePatchId a2 = reg.GetOrCreate(aKey);
        Assert.Equal(a1, a2);
        Assert.Single(reg.ActivePatchIds);

        reg.BeginRemesh(3);
        LumonScenePatchId b2 = reg.GetOrCreate(bKey);
        Assert.Equal(b1, b2);
        Assert.Single(reg.ActivePatchIds);
    }
}

