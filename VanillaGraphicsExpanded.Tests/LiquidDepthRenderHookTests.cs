using System.Reflection.Emit;
using HarmonyLib;
using VanillaGraphicsExpanded.HarmonyPatches;
using VanillaGraphicsExpanded.PBR.Liquids;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.Tests;

/// <summary>Checks the owned depth-pass handoff against the installed engine method body.</summary>
[Collection("GPU")]
public sealed class LiquidDepthRenderHookTests
{
    #region Installed engine contract
    /// <summary>The bypass spans only the liquid-depth shader and mesh draw, retaining FBO cleanup.</summary>
    [Fact]
    public void SuppressionRetainsFramebufferAndMatrixCleanup()
    {
        var method = AccessTools.Method(typeof(ChunkRenderer), nameof(ChunkRenderer.OnRenderBefore));
        var generator = new DynamicMethod("liquidDepthBoundary", typeof(void), Type.EmptyTypes).GetILGenerator();
        var original = PatchProcessor.GetOriginalInstructions(method, generator).ToArray();
        var code = LiquidDepthRenderHook.Transpiler(original.Select(i => new CodeInstruction(i)), generator).ToArray();
        int gate = Array.FindIndex(code, i => i.Calls(AccessTools.Method(typeof(LiquidDepthRenderer), nameof(LiquidDepthRenderer.TryRender))));
        Assert.True(gate >= 0);
        Assert.Equal(OpCodes.Brtrue, code[gate + 1].opcode);
        var target = (Label)code[gate + 1].operand;
        int resume = Array.FindIndex(code, i => i.labels.Contains(target));
        Assert.True(code[gate + 2].LoadsField(AccessTools.Field(typeof(ShaderPrograms), nameof(ShaderPrograms.Chunkliquiddepth))));
        Assert.True(code[resume - 1].Calls(AccessTools.Method(typeof(ShaderProgramBase), nameof(ShaderProgramBase.Stop))));
        Assert.DoesNotContain(code.Skip(gate + 2).Take(resume - gate - 2), i =>
            i.operand is System.Reflection.MethodInfo m && m.Name is "GlPushMatrix" or "GlPopMatrix" or "LoadFrameBuffer" or "UnloadFrameBuffer");
        Assert.Contains(code.Skip(resume), i =>
            i.operand is System.Reflection.MethodInfo m && m.Name == "UnloadFrameBuffer");
    }

    /// <summary>Unknown installed engine bytecode fails rather than skipping unrelated work.</summary>
    [Fact]
    public void MissingDepthBoundaryIsRejected()
    {
        var generator = new DynamicMethod("missingDepth", typeof(void), Type.EmptyTypes).GetILGenerator();
        Assert.Throws<InvalidOperationException>(() => LiquidDepthRenderHook.Transpiler([], generator).ToArray());
    }

    /// <summary>Harmony accepts the actual installed engine method and the owned depth gate.</summary>
    [Fact]
    public void HookInstallsOnInstalledEngineMethod()
    {
        var method = AccessTools.Method(typeof(ChunkRenderer), nameof(ChunkRenderer.OnRenderBefore));
        var harmony = new Harmony("VGE.Tests.LiquidDepthRenderHook");
        try
        {
            harmony.CreateClassProcessor(typeof(LiquidDepthRenderHook)).Patch();
            Assert.Contains(Harmony.GetPatchInfo(method).Transpilers,
                patch => patch.owner == harmony.Id && patch.PatchMethod.DeclaringType == typeof(LiquidDepthRenderHook));
        }
        finally { harmony.UnpatchAll(harmony.Id); }
    }
    #endregion
}
