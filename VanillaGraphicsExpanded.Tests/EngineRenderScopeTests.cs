using System.Reflection.Emit;
using HarmonyLib;
using VanillaGraphicsExpanded.HarmonyPatches;
using VanillaGraphicsExpanded.Rendering.Profiling;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.Tests;

/// <summary>Checks engine scope coverage against the installed dispatcher and registration metadata.</summary>
public sealed class EngineRenderScopeTests
{
    #region Dispatcher contract
    /// <summary>Both profiling modes retain their handler metadata and dispatch through the scoped callback.</summary>
    [Fact]
    public void InstalledDispatcherScopesBothCallbackSites()
    {
        var method = AccessTools.Method(typeof(ClientEventManager), nameof(ClientEventManager.TriggerRenderStage));
        var original = PatchProcessor.GetOriginalInstructions(method).ToArray();
        var rewritten = EngineRenderStageScopeHook.Transpiler(original.Select(instruction => new CodeInstruction(instruction))).ToArray();
        var callback = AccessTools.Method(typeof(IRenderer), nameof(IRenderer.OnRenderFrame));
        var wrapper = AccessTools.Method(typeof(EngineRenderScopes), nameof(EngineRenderScopes.Render));
        Assert.Equal(2, original.Count(instruction => instruction.Calls(callback)));
        Assert.Equal(2, rewritten.Count(instruction => instruction.Calls(wrapper)));
        Assert.DoesNotContain(rewritten, instruction => instruction.Calls(callback));
        Assert.Equal(original.Length, rewritten.Length);
        Assert.Equal(original.Where(instruction => instruction.opcode == OpCodes.Callvirt && !instruction.Calls(callback)).Select(instruction => instruction.operand),
            rewritten.Where(instruction => instruction.opcode == OpCodes.Callvirt).Select(instruction => instruction.operand));
    }

    /// <summary>A changed engine dispatcher cannot silently leave renderer work unscoped.</summary>
    [Fact]
    public void MissingCallbacksAreRejected() => Assert.Throws<InvalidOperationException>(() => EngineRenderStageScopeHook.Transpiler([]));

    /// <summary>Every installed stage has a stable name, including new stages omitted by the old allowlist.</summary>
    [Fact]
    public void AllStagesAreNamed()
    {
        foreach (var stage in Enum.GetValues<EnumRenderStage>())
            Assert.Equal($"VS.{stage}", EngineRenderScopes.StageName(stage));
    }

    /// <summary>Action renderers retain distinct engine registration names instead of collapsing to DummyRenderer.</summary>
    [Fact]
    public void RegistrationNamesDistinguishActionRenderers()
    {
        var first = new RenderHandler { Renderer = new DummyRenderer { action = IgnoreFrame }, ProfilingName = "terrain" };
        var second = new RenderHandler { Renderer = new DummyRenderer { action = IgnoreFrame }, ProfilingName = "first-person" };
        Assert.Equal("terrain", EngineRenderScopes.HandlerName(first));
        Assert.Equal("first-person", EngineRenderScopes.HandlerName(second));
        Assert.Same(EngineRenderScopes.HandlerName(first), EngineRenderScopes.HandlerName(first));
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
        var target = Assert.Single(hook.GetCustomAttributes(typeof(HarmonyPatch), false).Cast<HarmonyPatch>()).info;
        Assert.Equal(typeof(ClientPlatformWindows), target.declaringType);
        Assert.Equal(name, target.methodName);
        Assert.NotNull(AccessTools.Method(target.declaringType, target.methodName, target.argumentTypes));
    }
    /// <summary>The audited postprocessor reaches the fullscreen scope for each of its eleven internal submissions.</summary>
    [Fact]
    public void AllPostprocessingSubpassesReachFullscreenScope()
    {
        var method = AccessTools.Method(typeof(ClientPlatformWindows), nameof(ClientPlatformWindows.RenderPostprocessingEffects));
        var fullscreen = AccessTools.Method(typeof(ClientPlatformWindows), nameof(ClientPlatformWindows.RenderFullscreenTriangle));
        Assert.Equal(11, PatchProcessor.GetOriginalInstructions(method).Count(instruction => instruction.Calls(fullscreen)));
    }

    /// <summary>Concrete menu and world screen overrides, including non-stage work, are covered.</summary>
    [Fact]
    public void InstalledScreensAreCovered()
    {
        using var dependencies = new EngineDependencyResolution();
        var methods = EngineScreenScopeHook.TargetMethods().ToArray();
        Assert.Contains(methods, method => method.DeclaringType?.Name == "GuiScreenRunningGame" && method.Name == "RenderToPrimary");
        var frame = Assert.Single(typeof(EngineFrameScopeHook).GetCustomAttributes(typeof(HarmonyPatch), false).Cast<HarmonyPatch>()).info;
        Assert.NotNull(AccessTools.Method(frame.declaringType, frame.methodName, frame.argumentTypes));
        var menu = Assert.Single(typeof(EngineMenuBackgroundScopeHook).GetCustomAttributes(typeof(HarmonyPatch), false).Cast<HarmonyPatch>()).info;
        Assert.NotNull(AccessTools.Method(menu.declaringType, menu.methodName, menu.argumentTypes));
        Assert.Equal(methods.Length, methods.Distinct().Count());
    }

    /// <summary>Supplies a named engine action without issuing rendering work.</summary>
    private static void IgnoreFrame(float dt) { }
    #endregion
}


