using System.Runtime.CompilerServices;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.HarmonyPatches;
using VanillaGraphicsExpanded.PBR;
using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.Tests;

/// <summary>Checks shared mesh-pool classification, cache reuse, and lifecycle refresh.</summary>
public sealed class MeshPoolClassifierTests
{
    #region Pool selection
    /// <summary>Only the no-cull manager is selected; unrelated and out-of-scope draws reset the policy.</summary>
    [Fact]
    public void SelectsOnlyOpaqueNoCullPool()
    {
        var opaque = (MeshDataPoolManager)RuntimeHelpers.GetUninitializedObject(typeof(MeshDataPoolManager));
        var foliage = (MeshDataPoolManager)RuntimeHelpers.GetUninitializedObject(typeof(MeshDataPoolManager));
        MeshDataPoolManager[][] pools = new MeshDataPoolManager[8][];
        pools[(int)EnumChunkRenderPass.Opaque] = [opaque];
        pools[(int)EnumChunkRenderPass.OpaqueNoCull] = [foliage];
        Assert.True(MeshPoolClassifier.For(pools).IsTwoSided(foliage));
        Assert.False(MeshPoolClassifier.For(pools).IsTwoSided(opaque));
        Assert.False(MeshPoolClassifier.For([]).IsTwoSided(foliage));
    }
    /// <summary>Runtime atlas additions and same-table replacements refresh both consumers at the lifecycle boundary.</summary>
    [Fact]
    public void LifecycleRefreshReplacesBothSetsWithoutDrawTimeRescans()
    {
        var old = (MeshDataPoolManager)RuntimeHelpers.GetUninitializedObject(typeof(MeshDataPoolManager));
        var next = (MeshDataPoolManager)RuntimeHelpers.GetUninitializedObject(typeof(MeshDataPoolManager));
        MeshDataPoolManager[][] pools = [[old], [old], [], [], [], []];
        MeshPoolLifecycleHooks.Postfix(pools);
        var before = MeshPoolClassifier.For(pools);
        Assert.True(before.IsTwoSided(old));
        Assert.True(before.IsDisplacementEligible(old));
        Assert.Same(before, MeshPoolClassifier.For(pools));
        pools[0] = [next];
        pools[1] = [next];
        // Draw lookup reads the snapshot, never scans mutable engine arrays.
        Assert.False(before.IsTwoSided(next));
        MeshPoolLifecycleHooks.Postfix(pools);
        var after = MeshPoolClassifier.For(pools);
        Assert.NotSame(before, after);
        Assert.False(after.IsTwoSided(old));
        Assert.False(after.IsDisplacementEligible(old));
        Assert.True(after.IsTwoSided(next));
        Assert.True(after.IsDisplacementEligible(next));
        Assert.False(MeshPoolClassifier.For([[], [], [], [], [], []]).IsTwoSided(next));
    }

    #endregion
}
