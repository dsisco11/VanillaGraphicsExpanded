using System.Reflection;
using HarmonyLib;
using VanillaGraphicsExpanded.HarmonyPatches;
using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;
using VanillaGraphicsExpanded.Tests.Support.GPU.Shaders;

namespace VanillaGraphicsExpanded.Tests.Integration.Postprocessing;

/// <summary>Checks installed Harmony owners and engine execution order for scene postprocessing.</summary>
public sealed class SceneColorBindingTests(ITestOutputHelper output)
{
    #region Public API
    /// <summary>The installed final retains every activation while the owned prefix replaces scene postprocessing.</summary>
    [Fact]
    public void InstalledPostprocessOwnersAcceptHarmonyBindingPatch()
    {
        // Arrange: create an isolated patch owner for the installed postprocess and final-composition hooks.
        var harmony = new Harmony("VGE.Tests.SceneColorPostprocessBindings");
        try
        {
            // Act: install both production hook classes and retrieve their target methods.
            harmony.CreateClassProcessor(typeof(OwnedPostprocessHook)).Patch();
            harmony.CreateClassProcessor(typeof(OwnedFinalCompositionHook)).Patch();
            foreach (string name in new[] { nameof(ClientPlatformWindows.RenderPostprocessingEffects), nameof(ClientPlatformWindows.RenderFinalComposition) })
            {
                var target = AccessTools.Method(typeof(ClientPlatformWindows), name);
                // Assert: this owner installs a prefix on each real engine method.
                Assert.Contains(Harmony.GetPatchInfo(target)!.Prefixes, patch => patch.owner == harmony.Id);
            }
        }
        finally { harmony.UnpatchAll(harmony.Id); }
    }

    /// <summary>The installed engine completes AfterOIT before postprocessing and preserves the later UI/presentation boundaries.</summary>
    [Fact]
    public void InstalledScenePostprocessFollowsLateSceneRendering()
    {
        // Arrange: initialize the engine dependencies required by the installed method bodies.
        using var dependencies = new EngineDependencyResolution();
        // Act: inspect the actual screen, game, main-loop, and postprocessing instruction sequences.
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
        // Assert: report the integrated order only after each actual method-body boundary was verified.
        output.WriteLine("Installed order verified: OIT merge -> AfterOIT -> postprocessing -> postprocess overlays -> final -> post-final overlays -> presentation.");
    }
    #endregion
}
