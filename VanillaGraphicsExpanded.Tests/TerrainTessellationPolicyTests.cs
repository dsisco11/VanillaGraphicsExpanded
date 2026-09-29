using VanillaGraphicsExpanded.Rendering;
using System.Reflection.Emit;
using HarmonyLib;
using VanillaGraphicsExpanded.HarmonyPatches;
using VanillaGraphicsExpanded.PBR.Tessellation;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.Tests;

/// <summary>Checks terrain-only selection and guarded engine draw rewriting.</summary>
[Collection("GPU")]
public sealed class TerrainTessellationPolicyTests
{
    #region Displacement boundaries
    /// <summary>Admission expands a copied engine volume by the maximum displacement on every axis.</summary>
    [Fact]
    public void AdmissionBoundsReserveMaximumDisplacement()
    {
        var original=new Vintagestory.API.MathTools.Sphere { radius=1,radiusY=2,radiusZ=3 };
        var expanded=original;
        TerrainDisplacementBoundsHook.Prefix(ref expanded);
        Assert.Equal(1,original.radius);Assert.Equal(2,original.radiusY);Assert.Equal(3,original.radiusZ);
        Assert.InRange(expanded.radius,1.04999,1.05001);Assert.InRange(expanded.radiusY,2.04999,2.05001);Assert.InRange(expanded.radiusZ,3.04999,3.05001);
    }

    /// <summary>The shared shadow shader accepts only opaque and topsoil pool identities.</summary>
    [Fact]
    public void ShadowPoolProvenanceExcludesOtherTerrainPasses()
    {
        var previous=MeshPoolClassifier.Current;
        var opaque=(Vintagestory.API.Client.MeshDataPoolManager)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Vintagestory.API.Client.MeshDataPoolManager));
        var soil=(Vintagestory.API.Client.MeshDataPoolManager)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Vintagestory.API.Client.MeshDataPoolManager));
        var liquid=(Vintagestory.API.Client.MeshDataPoolManager)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Vintagestory.API.Client.MeshDataPoolManager));
        GC.SuppressFinalize(opaque);GC.SuppressFinalize(soil);GC.SuppressFinalize(liquid);
        try
        {
            MeshPoolClassifier.Current=null;Assert.False(TerrainDisplacementRuntime.IsEligiblePool(opaque));
            MeshPoolClassifier.Current=MeshPoolClassifier.For([[opaque],[liquid],[],[],[],[soil]]);
            Assert.True(TerrainDisplacementRuntime.IsEligiblePool(opaque));Assert.True(TerrainDisplacementRuntime.IsEligiblePool(soil));
            Assert.False(TerrainDisplacementRuntime.IsEligiblePool(liquid));
        }
        finally { MeshPoolClassifier.Current=previous; }
    }
    #endregion

    #region Installed engine hook contract
    /// <summary>Harmony field injection matches the installed pool ownership and dimension fields.</summary>
    [Fact]
    public void PoolHookFieldsMatchInstalledEngine()
    {
        Assert.Equal(typeof(int),AccessTools.Field(typeof(Vintagestory.API.Client.MeshDataPool),"dimensionId").FieldType);
        var targets=TerrainDisplacementRenderScope.TargetMethods().ToArray();
        Assert.Equal(new[]{AccessTools.Method(typeof(ChunkRenderer),nameof(ChunkRenderer.RenderOpaque)),AccessTools.Method(typeof(ChunkRenderer),nameof(ChunkRenderer.RenderShadow))},targets.OrderBy(method=>method.Name).ToArray());
        Assert.Equal(typeof(Vintagestory.API.Client.MeshDataPoolManager[][]),AccessTools.Field(typeof(ChunkRenderer),"poolsByRenderPass").FieldType);
    }
    #endregion

    #region Selection
    /// <summary>Only the intended opaque and topsoil terrain families are eligible.</summary>
    [Theory]
    [InlineData("chunkopaque", true)]
    [InlineData("chunktopsoil", true)]
    [InlineData("chunktransparent", false)]
    [InlineData("chunkliquid", false)]
    [InlineData("standard", false)]
    [InlineData(null, false)]
    public void EligibilityMatchesTerrainScope(string? name, bool expected) => Assert.Equal(expected, TerrainTessellationPrograms.Eligible(name));
    #endregion

    #region Engine draw layout
    /// <summary>Both engine draw branches receive topology selection without replacing their original operands.</summary>
    [Fact]
    public void GroupedDrawRewritesBothTopologyLoads()
    {
        var field = AccessTools.Field(typeof(VAO), nameof(VAO.drawMode));
        var first = new CodeInstruction(OpCodes.Ldfld, field);
        var second = new CodeInstruction(OpCodes.Ldfld, field);
        var actual = TerrainTessellationDrawHook.Transpiler(new[] { first, new CodeInstruction(OpCodes.Nop), second }).ToArray();
        Assert.Equal(5, actual.Length);
        Assert.Same(first, actual[0]); Assert.Same(second, actual[3]);
        Assert.Equal(OpCodes.Call, actual[1].opcode); Assert.Equal(OpCodes.Call, actual[4].opcode);
        Assert.Equal(AccessTools.Method(typeof(TerrainTessellationPrograms), nameof(TerrainTessellationPrograms.Topology)), actual[1].operand);
        Assert.Equal(actual[1].operand, actual[4].operand);
    }

    /// <summary>Unexpected engine layouts retain ordinary drawing instead of installing a partial rewrite.</summary>
    [Fact]
    public void ChangedGroupedDrawLayoutIsRejected()
    {
        var original = new[] { new CodeInstruction(OpCodes.Nop) };
        Assert.Equal(original, TerrainTessellationDrawHook.Transpiler(original).ToArray());
        Assert.False(TerrainTessellationPrograms.DrawHookAvailable);
    }
    #endregion
}
