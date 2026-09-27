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
