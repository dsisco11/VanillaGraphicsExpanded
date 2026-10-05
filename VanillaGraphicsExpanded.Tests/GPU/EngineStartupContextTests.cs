using System.Reflection;
using System.Runtime.CompilerServices;
using OpenTK.Graphics.OpenGL;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Integration;
using VanillaGraphicsExpanded.Rendering.Pipeline.State;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using Vintagestory.Client;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Exercises engine state routing before renderer initialization registers its window.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class EngineStartupContextTests(HeadlessGLFixture fixture)
{
    #region Public API
    /// <summary>Menu viewport routing registers the actual engine owner and reuses its capability lifetime.</summary>
    [Fact]
    public unsafe void EarlyViewportRegistersEngineWindowAndRetirementCreatesNewGeneration()
    {
        fixture.MakeCurrent();
        var previousPlatform = ScreenManager.Platform;
        int[] previousViewport = new int[4];
        GL.GetInteger(GetPName.Viewport, previousViewport);
        var owner = (GameWindowNative)RuntimeHelpers.GetUninitializedObject(typeof(GameWindowNative));
        // This adapter borrows the fixture's native context and must never dispose its window.
        GC.SuppressFinalize(owner);
        var platform = (ClientPlatformWindows)RuntimeHelpers.GetUninitializedObject(typeof(ClientPlatformWindows));
        GC.SuppressFinalize(platform);
        SetWindowField(owner, "<WindowPtr>k__BackingField", Pointer.Box(GLFW.GetCurrentContext(), typeof(Window*)));
        SetWindowField(owner, "<Exists>k__BackingField", true);
        platform.window = owner;
        try
        {
            RenderContextRegistry.Retire(fixture);
            ScreenManager.Platform = platform;
            Assert.Equal(0, RenderContextRegistry.Current().Generation);
            // The former adapter path reproduces the reported failure without changing production code.
            Assert.Throws<InvalidOperationException>(() => StateCache.Current.ApplyDynamic(new DynamicDrawState { Width = 1, Height = 1 }));

            EngineStateCalls.Viewport(2, 3, 37, 41);
            long generation = RenderContextRegistry.Current().Generation;
            Assert.True(generation > 0);
            int[] actualViewport = new int[4];
            GL.GetInteger(GetPName.Viewport, actualViewport);
            Assert.Equal(new[] { 2, 3, 37, 41 }, actualViewport);
            long captures = GpuSupport.CaptureCount;
            EngineStateCalls.Viewport(2, 3, 37, 41);
            EngineRenderContext.RegisterCurrent();
            Assert.Equal(generation, RenderContextRegistry.Current().Generation);
            Assert.Equal(captures, GpuSupport.CaptureCount);

            RenderContextRegistry.Retire(owner);
            EngineStateCalls.Viewport(4, 5, 43, 47);
            Assert.True(RenderContextRegistry.Current().Generation > generation);
            Assert.Equal(captures + 1, GpuSupport.CaptureCount);
            GL.GetInteger(GetPName.Viewport, actualViewport);
            Assert.Equal(new[] { 4, 5, 43, 47 }, actualViewport);

            RenderContextRegistry.Retire(owner);
            SetWindowField(owner, "<WindowPtr>k__BackingField", Pointer.Box(null, typeof(Window*)));
            Assert.Throws<InvalidOperationException>(() => EngineStateCalls.Viewport(0, 0, 1, 1));
            Assert.Equal(0, RenderContextRegistry.Current().Generation);
        }
        finally
        {
            ScreenManager.Platform = previousPlatform;
            RenderContextRegistry.Retire(owner);
            SetWindowField(owner, "<Exists>k__BackingField", false);
            RenderContextRegistry.RegisterCurrent(fixture, static target => ((HeadlessGLFixture)target).IsContextValid);
            GL.Viewport(previousViewport[0], previousViewport[1], previousViewport[2], previousViewport[3]);
            StateCache.Current.InvalidateAll();
        }
        Assert.Equal(OpenTK.Graphics.OpenGL.ErrorCode.NoError, GL.GetError());
    }

    /// <summary>A registered external fixture keeps its generation without requiring an engine platform.</summary>
    [Fact]
    public void RegisteredContextDoesNotRequireEngineOwnerOrRecaptureCapabilities()
    {
        fixture.MakeCurrent();
        var previousPlatform = ScreenManager.Platform;
        int[] previousViewport = new int[4];
        GL.GetInteger(GetPName.Viewport, previousViewport);
        try
        {
            ScreenManager.Platform = null;
            GpuSupport.EnsureCurrentContext();
            long generation = RenderContextRegistry.Current().Generation;
            long captures = GpuSupport.CaptureCount;
            EngineStateCalls.Viewport(0, 0, 1, 1);
            long calls = StateCache.Current.FixedFunctionCalls;
            EngineStateCalls.Viewport(0, 0, 1, 1);
            Assert.Equal(calls, StateCache.Current.FixedFunctionCalls);
            Assert.Equal(generation, RenderContextRegistry.Current().Generation);
            Assert.Equal(captures, GpuSupport.CaptureCount);
            Assert.Equal(OpenTK.Graphics.OpenGL.ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            ScreenManager.Platform = previousPlatform;
            GL.Viewport(previousViewport[0], previousViewport[1], previousViewport[2], previousViewport[3]);
            StateCache.Current.InvalidateAll();
        }
    }
    #endregion

    #region Private
    /// <summary>Installs only the native identity and liveness fields read by the engine registration adapter.</summary>
    private static void SetWindowField(GameWindowNative owner, string name, object value)
    {
        var field = typeof(NativeWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        field.SetValue(owner, value);
    }
    #endregion
}


