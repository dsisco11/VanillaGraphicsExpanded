using System.Runtime.CompilerServices;
using HarmonyLib;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.HarmonyPatches;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Executes both installed engine routing signatures through the production call-site adapter.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class EngineFramebufferRoutingTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>Engine-style native writes update shared route ownership and cannot suppress a later owned restoration.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PatchedNativeRoutingSharesTheOwnedMapping(bool array)
    {
        EnsureContextValid();
        using var target = CreateMRTRenderTarget(2, 2, PixelInternalFormat.Rgba32f, PixelInternalFormat.Rgba32f);
        target.Bind();
        var cache = StateCache.Current;
        cache.SetDrawBuffer(DrawBufferMode.ColorAttachment0);
        var harmony = new Harmony("Tests.EngineFramebufferRouting");
        try
        {
            var method = AccessTools.Method(typeof(EngineFramebufferRoutingTests), nameof(ChangeRouting));
            harmony.Patch(method, transpiler: new HarmonyMethod(typeof(EngineStateSwitchingHook), nameof(EngineStateSwitchingHook.Transpiler)));
            using var calls = new GraphicsTransitionCapture();
            ChangeRouting(array);
            ChangeRouting(array);
            // Exact intercepted calls remain observable even when their routing arguments repeat.
            Assert.Equal(2, calls.Calls.Count(call => call.StartsWith("Routing")));
            Assert.Equal(DrawBuffersEnum.ColorAttachment1, cache.CaptureFramebufferDrawRouting(target.FboId)[0]);
            cache.SetDrawBuffer(DrawBufferMode.ColorAttachment0);
            Assert.Equal(3, calls.Calls.Count(call => call.StartsWith("Routing")));
            Assert.Equal((int)DrawBufferMode.ColorAttachment0, GL.GetInteger(GetPName.DrawBuffer0));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { harmony.UnpatchAll(harmony.Id); }
    }
    #endregion

    #region Private
    /// <summary>Retains both native signatures as uninlined call sites for the actual runtime transpiler.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ChangeRouting(bool array)
    {
        if (array) GL.DrawBuffers(1, new[] { DrawBuffersEnum.ColorAttachment1 });
        else GL.DrawBuffer(DrawBufferMode.ColorAttachment1);
    }
    #endregion
}
