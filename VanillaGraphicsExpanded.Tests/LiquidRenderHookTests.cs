using System.Reflection.Emit;
using HarmonyLib;
using VanillaGraphicsExpanded.HarmonyPatches;
using VanillaGraphicsExpanded.PBR.Liquids;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.Tests;

/// <summary>Checks suppression boundaries against installed engine IL in both atlas-patch orders.</summary>
public sealed class LiquidRenderHookTests
{
    #region Installed engine contract
    /// <summary>The bypass encloses liquid work while retaining matrix cleanup and transparent submission.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SuppressionPreservesTransparentAndMatrixBoundaries(bool atlasFirst)
    {
        var method = AccessTools.Method(typeof(ChunkRenderer), "RenderOIT");
        var generator = new DynamicMethod("liquidBoundary", typeof(void), Type.EmptyTypes).GetILGenerator();
        var original = PatchProcessor.GetOriginalInstructions(method, generator).ToArray();
        var code = original.Select(i => new CodeInstruction(i)).ToArray();
        if (atlasFirst) code = TerrainAtlasDrawBindingHook.Transpiler(code, generator, method).ToArray();
        code = LiquidRenderHooks.Transpiler(code, generator).ToArray();
        if (!atlasFirst) code = TerrainAtlasDrawBindingHook.Transpiler(code, generator, method).ToArray();
        int gate = Array.FindIndex(code, i => i.Calls(AccessTools.Method(typeof(LiquidRenderer), nameof(LiquidRenderer.ConsumeEngineSuppression))));
        Assert.True(gate >= 0);
        Assert.Equal(OpCodes.Brtrue, code[gate + 1].opcode);
        var target = (Label)code[gate + 1].operand;
        int resume = Array.FindIndex(code, i => i.labels.Contains(target));
        int transparent = Array.FindIndex(code, i => i.LoadsField(AccessTools.Field(typeof(ShaderPrograms), nameof(ShaderPrograms.Chunktransparent))));
        Assert.InRange(resume, gate + 2, transparent - 1);
        Assert.True(code[gate + 2].LoadsField(AccessTools.Field(typeof(ShaderPrograms), nameof(ShaderPrograms.Chunkliquid))));
        Assert.True(code[resume - 1].Calls(AccessTools.Method(typeof(ShaderProgramBase), nameof(ShaderProgramBase.Stop))));
        // No matrix operation is skipped: each original push/pop remains on both execution paths.
        Assert.DoesNotContain(code.Skip(gate + 2).Take(resume - gate - 2), i =>
            i.operand is System.Reflection.MethodInfo m && m.Name is "GlPushMatrix" or "GlPopMatrix");
        Assert.Equal(2, code.Count(i => i.Calls(AccessTools.Method(typeof(TerrainAtlasDrawBindingHook), "BindAtlas"))));
    }

    /// <summary>Unrecognized engine IL must fail explicitly rather than suppress an arbitrary draw range.</summary>
    [Fact]
    public void MissingLiquidBoundaryIsRejected()
    {
        var generator = new DynamicMethod("missingLiquid", typeof(void), Type.EmptyTypes).GetILGenerator();
        Assert.Throws<InvalidOperationException>(() => LiquidRenderHooks.Transpiler([], generator).ToArray());
    }
    #endregion
}
