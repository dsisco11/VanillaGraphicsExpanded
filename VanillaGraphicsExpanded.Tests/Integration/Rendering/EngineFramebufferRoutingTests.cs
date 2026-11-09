using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System.Runtime.CompilerServices;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.Support.GPU.Native;

namespace VanillaGraphicsExpanded.Tests.Integration.Rendering;

/// <summary>Checks both native draw-routing signatures against shared route ownership.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class EngineFramebufferRoutingTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>Engine-style native writes update shared route ownership and cannot suppress a later owned restoration.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeRoutingCallsUpdateTheOwnedMapping(bool array)
    {
        // Arrange: Seed the owned route and capture native routing calls on a real MRT framebuffer.
        EnsureContextValid();
        using var target = CreateMRTRenderTarget(2, 2, TextureFormat.Rgba32f, TextureFormat.Rgba32f);
        target.Bind();
        var cache = OpenGlStateCache.Current;
        OpenGLFramebufferRouting.SetDrawBuffer(cache, DrawBufferMode.ColorAttachment0);
        using var calls = new GraphicsTransitionCapture();
        // Act: Repeat the external route mutation, then restore through the manager-owned API.
        ChangeRouting(array);
        ChangeRouting(array);
        // Assert: Both repeated native calls are observed and the owned mapping reflects the external route.
        Assert.Equal(2, calls.Calls.Count(call => call.StartsWith("Routing")));
        Assert.Equal(DrawBuffersEnum.ColorAttachment1, OpenGLFramebufferRouting.Capture(cache, target.FboId)[0]);
        // Act: Restore the original route through the owner after the external call changed it.
        OpenGLFramebufferRouting.SetDrawBuffer(cache, DrawBufferMode.ColorAttachment0);
        // Assert: The restoration is submitted and the driver again exposes the original mapping.
        Assert.Equal(3, calls.Calls.Count(call => call.StartsWith("Routing")));
        Assert.Equal((int)DrawBufferMode.ColorAttachment0, GL.GetInteger(GetPName.DrawBuffer0));
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }
    #endregion

    #region Private
    /// <summary>Keeps both native signatures at distinct OpenTK binding call sites.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ChangeRouting(bool array)
    {
        if (array) GL.DrawBuffers(1, new[] { DrawBuffersEnum.ColorAttachment1 });
        else GL.DrawBuffer(DrawBufferMode.ColorAttachment1);
    }
    #endregion
}
