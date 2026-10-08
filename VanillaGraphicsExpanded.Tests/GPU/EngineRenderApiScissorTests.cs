using System.Diagnostics;
using System.Runtime.CompilerServices;
using HarmonyLib;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.HarmonyPatches;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using Vintagestory.API.Client;
using Vintagestory.Client;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Exercises real engine GUI callers compiled before native state routing is installed.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class EngineRenderApiScissorTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>Warm GUI calls must update both enablement and rectangle before later state boundaries restore them.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WarmedApiScissorCallsRemainTracked(bool pushPop)
    {
        EnsureContextValid();
        using var dependencies = new EngineDependencyResolution();
        var platform = (ClientPlatformWindows)RuntimeHelpers.GetUninitializedObject(typeof(ClientPlatformWindows));
        var render = (RenderAPIGame)RuntimeHelpers.GetUninitializedObject(typeof(RenderAPIGame));
        AccessTools.Field(typeof(RenderAPIBase), "plat").SetValue(render, platform);
        AccessTools.Field(typeof(RenderAPIBase), "scissorBoundsStacks").SetValue(render, new Stack<ElementBounds>());
        AccessTools.Field(typeof(ClientPlatformWindows), "curFb").SetValue(platform, new FrameBufferRef { Height = 1377 });
        var bounds = new ElementBounds { ParentBounds = ElementBounds.Empty, absFixedX = 1939, absFixedY = 1289, absInnerWidth = 66, absInnerHeight = 66 };
        // These installed release methods become eligible for tiered inlining before either patch pass.
        // Run each theory case in a fresh test host when qualifying the precompiled-caller regression.
        var warmup = Stopwatch.StartNew();
        while (warmup.Elapsed < TimeSpan.FromSeconds(2))
        {
            for (int i = 0; i < 1000; i++)
                if (pushPop) { render.PushScissor(bounds); render.PopScissor(); }
                else { render.GlScissorFlag(false); render.GlScissor(1939, 22, 66, 66); }
            Thread.Sleep(1);
        }
        var harmony = new Harmony("VGE.Tests.RenderApiStatePatches.Warmed");
        try
        {
            // Match startup ordering: route native callers first, then regenerate the entire render API.
            harmony.CreateClassProcessor(typeof(EngineStateSwitchingHook)).Patch();
            EngineRenderApiStatePatches.Apply(harmony);
            var cache = StateCache.Current;
            cache.SetCapability(EnableCap.ScissorTest, true);
            cache.SetScissor(15, 501, 1083, 34);
            if (pushPop) { render.PushScissor(bounds); render.PopScissor(); }
            else { render.GlScissorFlag(false); render.GlScissor(1939, 22, 66, 66); }
            Assert.False(GL.IsEnabled(EnableCap.ScissorTest));
            int[] rectangle = new int[4];
            GL.GetInteger(GetPName.ScissorBox, rectangle);
            Assert.Equal(new[] { 1939, 22, 66, 66 }, rectangle);
            using (cache.PreserveScissorState()) cache.SetCapability(EnableCap.ScissorTest, false);
            Assert.False(GL.IsEnabled(EnableCap.ScissorTest));
            // A stale cached rectangle would suppress this command and retain the hotbar rectangle.
            cache.SetScissor(15, 501, 1083, 34);
            GL.GetInteger(GetPName.ScissorBox, rectangle);
            Assert.Equal(new[] { 15, 501, 1083, 34 }, rectangle);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            harmony.UnpatchAll(harmony.Id);
            GL.Disable(EnableCap.ScissorTest);
            StateCache.Current.InvalidateAll();
        }
    }
    #endregion
}
