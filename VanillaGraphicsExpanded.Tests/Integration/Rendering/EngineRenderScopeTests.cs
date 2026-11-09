using System.Reflection.Emit;
using HarmonyLib;
using VanillaGraphicsExpanded.HarmonyPatches;
using VanillaGraphicsExpanded.Rendering.Profiling;
using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;
using VanillaGraphicsExpanded.Tests.Support.GPU.Shaders;

namespace VanillaGraphicsExpanded.Tests.Integration.Rendering;

/// <summary>Checks engine scope coverage against the installed dispatcher and registration metadata.</summary>
public sealed class EngineRenderScopeTests
{
    #region Public API
    /// <summary>Both profiling modes retain their handler metadata and dispatch through the scoped callback.</summary>
    [Fact]
    public void InstalledDispatcherScopesBothCallbackSites()
    {
        // Arrange: inspect the installed engine dispatcher and identify its callback and wrapper methods.
        var method = AccessTools.Method(typeof(ClientEventManager), nameof(ClientEventManager.TriggerRenderStage));
        var original = PatchProcessor.GetOriginalInstructions(method).ToArray();
        var callback = AccessTools.Method(typeof(IRenderer), nameof(IRenderer.OnRenderFrame));
        var wrapper = AccessTools.Method(typeof(EngineRenderScopes), nameof(EngineRenderScopes.Render));
        // Act: run the production transpiler on a copy of the installed instruction stream.
        var rewritten = EngineRenderStageScopeHook.Transpiler(original.Select(instruction => new CodeInstruction(instruction))).ToArray();
        // Assert: both callbacks are wrapped, stream length stays fixed, and unrelated calls remain ordered.
        Assert.Equal(2, original.Count(instruction => instruction.Calls(callback)));
        Assert.Equal(2, rewritten.Count(instruction => instruction.Calls(wrapper)));
        Assert.DoesNotContain(rewritten, instruction => instruction.Calls(callback));
        Assert.Equal(original.Length, rewritten.Length);
        Assert.Equal(original.Where(instruction => instruction.opcode == OpCodes.Callvirt && !instruction.Calls(callback)).Select(instruction => instruction.operand),
            rewritten.Where(instruction => instruction.opcode == OpCodes.Callvirt).Select(instruction => instruction.operand));
    }

    /// <summary>Unregistered platform composition and fullscreen submission are actual patch targets.</summary>
    [Theory]
    [InlineData(typeof(EngineTransparentCompositionScopeHook), nameof(ClientPlatformWindows.MergeTransparentRenderPass))]
    [InlineData(typeof(EnginePostprocessingScopeHook), nameof(ClientPlatformWindows.RenderPostprocessingEffects))]
    [InlineData(typeof(EngineFinalCompositionScopeHook), nameof(ClientPlatformWindows.RenderFinalComposition))]
    [InlineData(typeof(EnginePrimaryBlitScopeHook), nameof(ClientPlatformWindows.BlitPrimaryToDefault))]
    [InlineData(typeof(EngineFullscreenScopeHook), nameof(ClientPlatformWindows.RenderFullscreenTriangle))]
    public void InstalledCompositionBoundariesAreCovered(Type hook, string name)
    {
        // Arrange and act: inspect the installed patch metadata for the selected composition owner.
        var target = Assert.Single(hook.GetCustomAttributes(typeof(HarmonyPatch), false).Cast<HarmonyPatch>()).info;
        // Assert: the patch targets the expected platform method and that method is installed.
        Assert.Equal(typeof(ClientPlatformWindows), target.declaringType);
        Assert.Equal(name, target.methodName);
        Assert.NotNull(AccessTools.Method(target.declaringType, target.methodName, target.argumentTypes));
    }

    /// <summary>The audited postprocessor reaches the fullscreen scope for each of its eleven internal submissions.</summary>
    [Fact]
    public void AllPostprocessingSubpassesReachFullscreenScope()
    {
        // Arrange and act: inspect the installed postprocessing method's original calls.
        var method = AccessTools.Method(typeof(ClientPlatformWindows), nameof(ClientPlatformWindows.RenderPostprocessingEffects));
        var fullscreen = AccessTools.Method(typeof(ClientPlatformWindows), nameof(ClientPlatformWindows.RenderFullscreenTriangle));
        int submissions = PatchProcessor.GetOriginalInstructions(method).Count(instruction => instruction.Calls(fullscreen));
        // Assert: the eleven fullscreen submissions all cross the scoped engine boundary.
        Assert.Equal(11, submissions);
    }

    /// <summary>Concrete screen overrides retain their scopes except the running game's primary pass.</summary>
    [Fact]
    public void InstalledScreensAreCovered()
    {
        // Arrange: initialize engine dependencies and retrieve actual screen patch targets.
        using var dependencies = new EngineDependencyResolution();
        // Act: inspect the target set and menu background patch metadata.
        var methods = EngineScreenScopeHook.TargetMethods().ToArray();
        var menu = Assert.Single(typeof(EngineMenuBackgroundScopeHook).GetCustomAttributes(typeof(HarmonyPatch), false).Cast<HarmonyPatch>()).info;
        // Assert: the primary pass is excluded, later screen scopes remain, and target methods are unique.
        Assert.DoesNotContain(methods, method => method.DeclaringType?.Name == "GuiScreenRunningGame" && method.Name == "RenderToPrimary");
        Assert.Contains(methods, method => method.DeclaringType?.Name == "GuiScreenRunningGame" && method.Name == "RenderAfterPostProcessing");
        Assert.NotNull(AccessTools.Method(menu.declaringType, menu.methodName, menu.argumentTypes));
        Assert.Equal(methods.Length, methods.Distinct().Count());
    }
    #endregion
}
