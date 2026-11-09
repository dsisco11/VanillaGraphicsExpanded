using OpenTK.Graphics.OpenGL;
using HarmonyLib;
using VanillaGraphicsExpanded.HarmonyPatches;
using VanillaGraphicsExpanded.Rendering.Profiling;
using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;
using VanillaGraphicsExpanded.Tests.Support.GPU.Shaders;

namespace VanillaGraphicsExpanded.Tests.Integration.Rendering;

/// <summary>Verifies actual driver group nesting and exception retirement without compiling any shaders.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class EngineRenderScopeGpuTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    #region Installed hook ownership
    /// <summary>Harmony can install every diagnostic hook on this engine build and retire them without a game instance.</summary>
    [Fact]
    public void HooksInstallOnInstalledEngine()
    {
        // Arrange: Resolve optional engine dependencies before temporarily installing the diagnostic Harmony hooks.
        EnsureContextValid();
        // Resolve optional engine hook types before installing their patches, then inspect the
        // installed engine methods while the temporary owner still controls cleanup.
        using var dependencies = new EngineDependencyResolution();
        var harmony = new Harmony("vge.tests.engine-render-scopes");
        try
        {
            // Act: Install stage, composition, and screen hooks against this environment's actual engine methods.
            harmony.CreateClassProcessor(typeof(EngineRenderStageScopeHook)).Patch();
            Type[] compositionHooks = [typeof(EngineFullscreenScopeHook), typeof(EngineTransparentCompositionScopeHook),
                typeof(EnginePostprocessingScopeHook), typeof(EngineFinalCompositionScopeHook), typeof(EnginePrimaryBlitScopeHook),
                typeof(EngineMenuBackgroundScopeHook)];
            foreach (var hook in compositionHooks) harmony.CreateClassProcessor(hook).Patch();
            harmony.CreateClassProcessor(typeof(EngineScreenScopeHook)).Patch();
#if DEBUG
            // Assert: The expected installed engine targets carry this temporary patch owner's registration.
            var dispatcher = AccessTools.Method(typeof(ClientEventManager), nameof(ClientEventManager.TriggerRenderStage));
            Assert.Contains(harmony.Id, Harmony.GetPatchInfo(dispatcher).Owners);
            foreach (var method in compositionHooks.Select(hook => ((HarmonyPatch)Attribute.GetCustomAttribute(hook, typeof(HarmonyPatch))!).info).Select(target => AccessTools.Method(target.declaringType, target.methodName, target.argumentTypes)).Concat(EngineScreenScopeHook.TargetMethods()))
                Assert.Contains(harmony.Id, Harmony.GetPatchInfo(method).Owners);
#endif
        }
        finally { harmony.UnpatchAll(harmony.Id); }
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }
    #endregion

    #region Callback scope lifetime
    /// <summary>Callbacks execute inside the stage group and exceptions leave neither group on the driver stack.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CallbackGroupsRetireOnSuccessAndFailure(bool fail)
    {
        // Arrange: Record the driver's starting stack depth and configure a callback that may throw after entry.
        EnsureContextValid();
        int initial = GL.GetInteger(GetPName.DebugGroupStackDepth);
        var expectedError = new InvalidOperationException("renderer failure");
        int calls = 0;
        var renderer = new DummyRenderer { action = dt =>
        {
            calls++;
            Assert.Equal(0.125f, dt);
#if DEBUG
            Assert.Equal(initial + 2, GL.GetInteger(GetPName.DebugGroupStackDepth));
#endif
            if (fail) throw expectedError;
        } };
        var handler = new RenderHandler { Renderer = renderer, ProfilingName = "scope-test" };
        // The stage hook owns the outer group; the handler should add one nested group during callback execution.
        EngineRenderStageScopeHook.Prefix(EnumRenderStage.Opaque, out var scope);
        try
        {
            // Act: Render once through the installed scope owner, optionally throwing from inside the callback.
            if (fail) Assert.Same(expectedError, Assert.Throws<InvalidOperationException>(() => EngineRenderScopes.Render(handler, 0.125f, EnumRenderStage.Opaque)));
            else EngineRenderScopes.Render(handler, 0.125f, EnumRenderStage.Opaque);
        }
        // Assert: Both nested and outer groups retire after success or failure.
        finally { EngineRenderStageScopeHook.Finalizer(scope); }
        Assert.Equal(1, calls);
        Assert.Equal(initial, GL.GetInteger(GetPName.DebugGroupStackDepth));
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }
    #endregion
    #endregion
}


