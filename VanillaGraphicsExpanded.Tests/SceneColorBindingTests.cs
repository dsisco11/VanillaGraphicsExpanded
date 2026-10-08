using System.Reflection;
using HarmonyLib;
using VanillaGraphicsExpanded.HarmonyPatches;
using VanillaGraphicsExpanded.PBR.SceneColor;
using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.Tests;

/// <summary>Checks target-aware scene selection and installed engine postprocess integration.</summary>
public sealed class SceneColorBindingTests
{
    #region Public API
    /// <summary>Every stage and destination combination has an explicit scene or offscreen convention.</summary>
    [Fact]
    public void SceneConventionRequiresMatchingSceneTarget()
    {
        var primary = new FrameBufferRef { FboId = 11 };
        var oit = new FrameBufferRef { FboId = 12 };
        var other = new FrameBufferRef { FboId = 13 };
        foreach (EnumRenderStage stage in Enum.GetValues<EnumRenderStage>())
        {
            Assert.Equal(stage is EnumRenderStage.Opaque or EnumRenderStage.AfterOIT,
                SceneColorProgramBindings.SelectScene(stage, primary, primary, oit));
            Assert.Equal(stage == EnumRenderStage.OIT,
                SceneColorProgramBindings.SelectScene(stage, oit, primary, oit));
            Assert.False(SceneColorProgramBindings.SelectScene(stage, other, primary, oit));
            Assert.False(SceneColorProgramBindings.SelectScene(stage, null, primary, oit));
        }
    }

    /// <summary>Both installed owners retain every activation while assigning the input convention at those call sites.</summary>
    [Fact]
    public void InstalledPostprocessOwnersAcceptHarmonyBindingPatch()
    {
        var use = AccessTools.Method(typeof(ShaderProgramBase), nameof(ShaderProgramBase.Use));
        var wrapper = AccessTools.Method(typeof(SceneColorProgramBindings), nameof(SceneColorProgramBindings.UsePostprocess));
        var targets = SceneColorPostprocessBindingHook.TargetMethods().ToArray();
        Assert.Equal(2, targets.Length);
        foreach (var target in targets)
        {
            var original = PatchProcessor.GetOriginalInstructions(target).ToArray();
            int calls = original.Count(instruction => instruction.Calls(use));
            Assert.True(calls > 0);
            var patched = SceneColorPostprocessBindingHook.Transpiler(original.Select(instruction => new CodeInstruction(instruction))).ToArray();
            Assert.Equal(original.Length, patched.Length);
            Assert.Equal(calls, patched.Count(instruction => instruction.Calls(wrapper)));
            Assert.DoesNotContain(patched, instruction => instruction.Calls(use));
        }
        var harmony = new Harmony("VGE.Tests.SceneColorPostprocessBindings");
        try
        {
            harmony.CreateClassProcessor(typeof(SceneColorPostprocessBindingHook)).Patch();
            foreach (var target in targets)
            {
                var patches = Harmony.GetPatchInfo(target)!;
                Assert.Contains(patches.Transpilers, patch => patch.owner == harmony.Id);
                Assert.Contains(patches.Finalizers, patch => patch.owner == harmony.Id);
            }
        }
        finally { harmony.UnpatchAll(harmony.Id); }
    }
    #endregion
}
