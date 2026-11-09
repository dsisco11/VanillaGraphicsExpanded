using System.Reflection.Emit;
using HarmonyLib;
using VanillaGraphicsExpanded.HarmonyPatches;
using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.PBR.Tessellation;
using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.Tests.Integration.Terrain;

/// <summary>Verifies terrain draw rewriting and nested scopes against installed engine methods.</summary>
[Collection("CPU state")]
public sealed class TerrainPassDrawTests
{
    private static int nesting;
    private static readonly List<bool> observed = [];

    #region Public API
    #region Installed engine selection
    /// <summary>Installed opaque and shadow draws retain original operands and unrelated calls.</summary>
    [Theory]
    [InlineData("RenderOpaque", 5)]
    [InlineData("RenderShadow", 4)]
    public void InstalledSelections(string name, int count)
    {
        // Arrange the selected installed method and collect its original engine instructions.
        var method = TerrainPassDrawHook.TargetMethods().Single(m => m.Name == name);
        var il = new DynamicMethod("passLayout", typeof(void), Type.EmptyTypes).GetILGenerator();
        var original = PatchProcessor.GetOriginalInstructions(method, il).ToArray();

        // Act by applying the production pass hook to the installed body.
        var rewritten = TerrainPassDrawHook.Transpiler(original, il).ToArray();

        // Assert all render calls receive dispatch and all unrelated instructions remain; a missing layout is rejected.
        var render = AccessTools.Method(typeof(MeshDataPoolManager), nameof(MeshDataPoolManager.Render));
        var dispatch = AccessTools.Method(typeof(TerrainPassDrawHook), nameof(TerrainPassDrawHook.Render));
        Assert.Equal(count, rewritten.Count(i => i.Calls(dispatch)));
        Assert.All(original.Where(i => !i.Calls(render)), instruction => Assert.Contains(instruction, rewritten));
        Assert.Throws<InvalidOperationException>(() => TerrainPassDrawHook.Transpiler(original.Where(i => !i.Calls(render)), il).ToArray());
    }
    #endregion

    #region Installed nested scopes
    /// <summary>Harmony installation works on engine IL and nested manager draws restore their enclosing policy.</summary>
    [Fact]
    public void InstalledHookAndNestedScopes()
    {
        // Arrange the original policy and install the production hooks plus one test recorder on the actual manager.
        var harmony = new Harmony("VGE.Tests.TerrainPassScopes");
        bool previous = TerrainDisplacementRuntime.EligiblePool;
        bool previousMovingPool = TerrainDisplacementRuntime.MovingPool;
        try
        {
            harmony.CreateClassProcessor(typeof(TerrainPassDrawHook)).Patch();
            harmony.CreateClassProcessor(typeof(TerrainDisplacementMovingPoolScope)).Patch();
            harmony.Patch(AccessTools.Method(typeof(MeshDataPoolManager), nameof(MeshDataPoolManager.Render)),
                prefix: new HarmonyMethod(typeof(TerrainPassDrawTests), nameof(NestedManager)));
            observed.Clear(); nesting = 0;
            TerrainDisplacementRuntime.EligiblePool = false;
            var manager = (MeshDataPoolManager)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(MeshDataPoolManager));

            // Act through the installed hook and assert nested eligibility transitions and outer-state restoration.
            TerrainPassDrawHook.Render(manager, new(), "origin", EnumFrustumCullMode.CullNormal, EnumChunkRenderPass.Opaque);
            Assert.Equal(new[] { true, false, true }, observed);
            Assert.False(TerrainDisplacementRuntime.EligiblePool);

            // Act through the moving-pool prefix/finalizer pair while retaining its enclosing state.
            TerrainDisplacementMovingPoolScope.Prefix(Vintagestory.API.Config.Dimensions.MiniDimensions, out var saved);
            Assert.True(TerrainDisplacementRuntime.MovingPool);
            TerrainDisplacementMovingPoolScope.Finalizer(saved);

            // Assert the moving-pool scope restores the prior value.
            Assert.Equal(previousMovingPool, TerrainDisplacementRuntime.MovingPool);
        }
        finally
        {
            harmony.UnpatchAll(harmony.Id);
            TerrainDisplacementRuntime.EligiblePool = previous;
            TerrainDisplacementRuntime.MovingPool = previousMovingPool;
        }
    }
    #endregion
    #endregion

    #region Private
    /// <summary>Records the engine draw category and suppresses its unrelated world-dependent body.</summary>
    private static bool NestedManager(MeshDataPoolManager __instance)
    {
        observed.Add(TerrainDisplacementRuntime.EligiblePool);
        if (nesting++ == 0)
        {
            TerrainPassDrawHook.Render(__instance, new(), "origin", EnumFrustumCullMode.CullNormal, EnumChunkRenderPass.BlendNoCull);
            observed.Add(TerrainDisplacementRuntime.EligiblePool);
        }
        return false;
    }
    #endregion
}
