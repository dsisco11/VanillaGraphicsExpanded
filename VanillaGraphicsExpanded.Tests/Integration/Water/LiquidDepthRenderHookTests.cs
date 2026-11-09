using System.Reflection.Emit;
using HarmonyLib;
using VanillaGraphicsExpanded.HarmonyPatches;
using VanillaGraphicsExpanded.PBR.Liquids;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.Tests.Integration.Water;

/// <summary>Verifies liquid depth suppression against installed engine rendering code.</summary>
[Collection("CPU state")]
public sealed class LiquidDepthRenderHookTests
{
    #region Public API
    #region Installed renderer boundaries
    /// <summary>The bypass preserves framebuffer and matrix cleanup around the owned depth draw.</summary>
    [Fact]
    public void SuppressionRetainsFramebufferAndMatrixCleanup()
    {
        // Arrange Harmony's installed engine method and materialize its original IL for the production transpiler.
        var method = AccessTools.Method(typeof(ChunkRenderer), nameof(ChunkRenderer.OnRenderBefore));
        var generator = new DynamicMethod("liquidDepthBoundary", typeof(void), Type.EmptyTypes).GetILGenerator();
        var original = PatchProcessor.GetOriginalInstructions(method, generator).ToArray();

        // Act by applying the production rewrite to the copied engine instruction stream.
        var code = LiquidDepthRenderHook.Transpiler(original.Select(i => new CodeInstruction(i)), generator).ToArray();

        // Assert the injected gate follows engine camera setup and skips only the owned shader/mesh work.
        int gate = Array.FindIndex(code, i => i.Calls(AccessTools.Method(typeof(LiquidDepthRenderer), nameof(LiquidDepthRenderer.TryRender))));
        Assert.True(gate >= 0);
        int loadView = Array.FindIndex(code, i => i.operand is System.Reflection.MethodInfo m && m.Name == "GlLoadMatrix");
        Assert.InRange(loadView, 0, gate - 1);
        Assert.Contains(code.Take(loadView), i => i.operand is System.Reflection.FieldInfo f && f.Name == "CameraMatrixOrigin");
        Assert.Equal(OpCodes.Brtrue, code[gate + 1].opcode);
        var target = (Label)code[gate + 1].operand;
        int resume = Array.FindIndex(code, i => i.labels.Contains(target));
        Assert.Contains(code.Skip(gate).Take(resume - gate), i => i.operand is System.Reflection.MethodInfo m && m.Name == "get_CurrentProjectionMatrix");
        Assert.Contains(code.Skip(gate).Take(resume - gate), i => i.operand is System.Reflection.MethodInfo m && m.Name == "get_CurrentModelViewMatrix");
        Assert.True(code[gate + 2].LoadsField(AccessTools.Field(typeof(ShaderPrograms), nameof(ShaderPrograms.Chunkliquiddepth))));
        Assert.True(code[resume - 1].Calls(AccessTools.Method(typeof(ShaderProgramBase), nameof(ShaderProgramBase.Stop))));
        Assert.DoesNotContain(code.Skip(gate + 2).Take(resume - gate - 2), i =>
            i.operand is System.Reflection.MethodInfo m && m.Name is "GlPushMatrix" or "GlPopMatrix" or "LoadFrameBuffer" or "UnloadFrameBuffer");
        Assert.Contains(code.Skip(resume), i => i.operand is System.Reflection.MethodInfo m && m.Name == "UnloadFrameBuffer");
    }

    /// <summary>Harmony accepts the production depth hook on the installed engine method.</summary>
    [Fact]
    public void HookInstallsOnInstalledEngineMethod()
    {
        // Arrange the exact engine target and an owner id so cleanup removes only this test's patch.
        var method = AccessTools.Method(typeof(ChunkRenderer), nameof(ChunkRenderer.OnRenderBefore));
        var harmony = new Harmony("VGE.Tests.LiquidDepthRenderHook");
        try
        {
            // Act by installing the real production patch against the loaded engine method.
            harmony.CreateClassProcessor(typeof(LiquidDepthRenderHook)).Patch();

            // Assert Harmony records the owned transpiler on that exact target.
            Assert.Contains(Harmony.GetPatchInfo(method).Transpilers,
                patch => patch.owner == harmony.Id && patch.PatchMethod.DeclaringType == typeof(LiquidDepthRenderHook));
        }
        finally { harmony.UnpatchAll(harmony.Id); }
    }
    #endregion
    #endregion
}
