using System.Reflection;
using HarmonyLib;
using VanillaGraphicsExpanded.HarmonyPatches;
using VanillaGraphicsExpanded.PBR.SceneColor;
using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.Tests;

/// <summary>Checks target-aware scene selection and installed engine postprocess integration.</summary>
public sealed class SceneColorBindingTests(ITestOutputHelper output)
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

    /// <summary>The installed final retains every activation while the owned prefix replaces scene postprocessing.</summary>
    [Fact]
    public void InstalledPostprocessOwnersAcceptHarmonyBindingPatch()
    {
        var use = AccessTools.Method(typeof(ShaderProgramBase), nameof(ShaderProgramBase.Use));
        var wrapper = AccessTools.Method(typeof(SceneColorProgramBindings), nameof(SceneColorProgramBindings.UsePostprocess));
        var targets = SceneColorPostprocessBindingHook.TargetMethods().ToArray();
        Assert.Single(targets);
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
            harmony.CreateClassProcessor(typeof(OwnedPostprocessHook)).Patch();
            var ownedTarget = AccessTools.Method(typeof(ClientPlatformWindows), nameof(ClientPlatformWindows.RenderPostprocessingEffects));
            Assert.Contains(Harmony.GetPatchInfo(ownedTarget)!.Prefixes, patch => patch.owner == harmony.Id);
            foreach (var target in targets)
            {
                var patches = Harmony.GetPatchInfo(target)!;
                Assert.Contains(patches.Transpilers, patch => patch.owner == harmony.Id);
                Assert.Contains(patches.Finalizers, patch => patch.owner == harmony.Id);
            }
        }
        finally { harmony.UnpatchAll(harmony.Id); }
    }
    /// <summary>The installed engine completes AfterOIT before postprocessing and preserves the later UI/presentation boundaries.</summary>
    [Fact]
    public void InstalledScenePostprocessFollowsLateSceneRendering()
    {
        using var dependencies = new GPU.Fixtures.EngineDependencyResolution();
        var screen = PatchProcessor.GetOriginalInstructions(AccessTools.Method(typeof(Vintagestory.Client.ScreenManager), "Render")).ToArray();
        string[] ordered = ["RenderToPrimary", "RenderPostprocessingEffects", "RenderAfterPostProcessing", "RenderFinalComposition", "RenderAfterFinalComposition", "BlitPrimaryToDefault"];
        int previous = -1;
        foreach (string name in ordered)
        {
            int index = Array.FindIndex(screen, instruction => instruction.operand is MethodBase called && called.Name == name);
            Assert.True(index > previous, $"Installed {name} must follow the preceding scene/presentation boundary.");
            previous = index;
        }
        var running = AccessTools.TypeByName("Vintagestory.Client.GuiScreenRunningGame");
        var primary = PatchProcessor.GetOriginalInstructions(AccessTools.Method(running, "RenderToPrimary")).ToArray();
        Assert.Contains(primary, instruction => instruction.operand is MethodBase called && called.Name == "MainGameLoop");
        var game = PatchProcessor.GetOriginalInstructions(AccessTools.Method(typeof(ClientMain), "MainGameLoop")).ToArray();
        Assert.Contains(game, instruction => instruction.operand is MethodBase called && called.Name == "MainRenderLoop");
        var scene = PatchProcessor.GetOriginalInstructions(AccessTools.Method(typeof(ClientMain), "MainRenderLoop")).ToArray();
        int lastStage = Array.FindLastIndex(scene, instruction => instruction.operand is MethodBase called && called.Name == "TriggerRenderStage");
        Assert.True(lastStage >= 2);
        Assert.True(scene[lastStage - 2].LoadsConstant((long)EnumRenderStage.AfterOIT));
        int oit = Array.FindIndex(scene, instruction => instruction.operand is MethodBase called && called.Name == "MergeTransparentRenderPass");
        Assert.InRange(oit, 0, lastStage - 1);
        output.WriteLine("Installed order verified: OIT merge -> AfterOIT -> postprocessing -> postprocess overlays -> final -> post-final overlays -> presentation.");
    }
    #endregion
}
