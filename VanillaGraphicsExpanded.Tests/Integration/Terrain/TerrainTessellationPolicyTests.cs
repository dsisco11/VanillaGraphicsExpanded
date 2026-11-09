using HarmonyLib;
using VanillaGraphicsExpanded.HarmonyPatches;
using VanillaGraphicsExpanded.PBR.Tessellation;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.Tests.Integration.Terrain;

/// <summary>Checks tessellation hook field binding against the installed engine types.</summary>
[Collection("CPU state")]
public sealed class TerrainTessellationPolicyTests
{
    #region Public API
    #region Installed engine bindings
    /// <summary>Injected fields match the installed engine pool ownership and dimension declarations.</summary>
    [Fact]
    public void PoolHookFieldsMatchInstalledEngine()
    {
        // Arrange engine-owned pool and renderer types from the loaded client assembly.

        // Act by resolving the production hook targets and field metadata.
        var dimension = AccessTools.Field(typeof(Vintagestory.API.Client.MeshDataPool), "dimensionId");
        var targets = TerrainPassDrawHook.TargetMethods().ToArray();
        var pools = AccessTools.Field(typeof(ChunkRenderer), "poolsByRenderPass");

        // Assert injection metadata matches the installed renderer's actual declarations and methods.
        Assert.Equal(typeof(int), dimension.FieldType);
        Assert.Equal(new[] { AccessTools.Method(typeof(ChunkRenderer), nameof(ChunkRenderer.RenderOpaque)), AccessTools.Method(typeof(ChunkRenderer), nameof(ChunkRenderer.RenderShadow)) },
            targets.OrderBy(method => method.Name).ToArray());
        Assert.Equal(typeof(Vintagestory.API.Client.MeshDataPoolManager[][]), pools.FieldType);
    }
    #endregion
    #endregion
}
