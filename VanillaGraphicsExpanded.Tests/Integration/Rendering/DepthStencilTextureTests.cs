using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.Integration.Rendering;

/// <summary>Verifies depth and stencil readback from their matching framebuffer attachments.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class DepthStencilTextureTests : RenderTestBase
{
    #region Public API
    /// <summary>Initializes the headless OpenGL fixture.</summary>
    public DepthStencilTextureTests(HeadlessGLFixture fixture) : base(fixture) { }

    /// <summary>Depth-only storage reads cleared depth values.</summary>
    [Fact]
    public void DepthTexture_ReadPixels_UsesDepthAttachment()
    {
        // Arrange: Attach depth-only storage and clear it to a nondefault value so readback source is unambiguous.
        EnsureContextValid();
        using var texture = new DepthTexture(1, 1, TextureFormat.DepthComponent24);
        using var framebuffer = DynamicGpuFramebuffer.CreateDepthOnly(texture)
            ?? throw new InvalidOperationException("Depth framebuffer creation failed.");

        framebuffer.Bind();
        GL.DepthMask(true);
        GL.ClearDepth(0.375);
        GL.Clear(ClearBufferMask.DepthBufferBit);
        GpuFramebuffer.Unbind();

        // Act: Read the depth aspect from the texture through its framebuffer attachment.
        float[] pixels = texture.ReadPixels();
        // Assert: Every returned sample reflects the clear rather than a color or stencil plane.
        Assert.Equal(1, pixels.Length);
        Assert.All(pixels, value => Assert.InRange(value, 0.374f, 0.376f));
    }

    /// <summary>Stencil-only storage reads integer stencil indices.</summary>
    [Fact]
    public void StencilTexture_ReadStencilPixels_UsesStencilAttachment()
    {
        // Arrange: Disable color routing before validating and clearing the stencil-only target.
        EnsureContextValid();
        using var texture = new StencilTexture(3, 2);
        using var framebuffer = DynamicGpuFramebuffer.CreateEmpty("StencilReadbackTest");
        framebuffer.Attach(texture);
        GL.DrawBuffer(DrawBufferMode.None); OpenGlStateCache.Current.Invalidate(EPipelineState.FramebufferRouting);
        GL.ReadBuffer(ReadBufferMode.None);
        Assert.True(framebuffer.CheckStatus(out string? error), error);

        framebuffer.Bind();
        GL.StencilMask(0xff);
        GL.ClearStencil(0x5a);
        GL.Clear(ClearBufferMask.StencilBufferBit);
        GpuFramebuffer.Unbind();

        // Act and assert: The readback helper checks packed bytes and restores the caller's hostile pack layout.
        AssertStencilReadbackPreservesPackLayout(texture.ReadStencilPixels, 6, 0x5a);
    }

    /// <summary>Packed storage exposes both aspects without treating either as color.</summary>
    [Fact]
    public void DepthStencilTexture_ReadsBothAspects()
    {
        // Arrange: Clear distinct depth and stencil values in packed depth-stencil storage.
        EnsureContextValid();
        using var texture = new DepthStencilTexture(3, 2);
        using var framebuffer = DynamicGpuFramebuffer.CreateDepthOnly(texture)
            ?? throw new InvalidOperationException("Depth-stencil framebuffer creation failed.");

        framebuffer.Bind();
        GL.DepthMask(true);
        GL.StencilMask(0xff);
        GL.ClearDepth(0.625);
        GL.ClearStencil(0x36);
        GL.Clear(ClearBufferMask.DepthBufferBit | ClearBufferMask.StencilBufferBit);
        GpuFramebuffer.Unbind();

        // Act: Read each aspect through its matching texture readback path; payload assertions stay independent of GPU checkers.
        float[] depth = texture.ReadPixels();
        // Assert: Both paths return the requested clear values with the expected element count.
        Assert.Equal(6, depth.Length);
        Assert.All(depth, value => Assert.InRange(value, 0.624f, 0.626f));
        AssertStencilReadbackPreservesPackLayout(texture.ReadStencilPixels, 6, 0x36);
    }
    #endregion

    #region Private
    /// <summary>Verifies tightly packed stencil pixels and restoration of all cached and native pack fields.</summary>
    private static void AssertStencilReadbackPreservesPackLayout(Func<byte[]> readback, int count, byte expected)
    {
        var cache = OpenGlStateCache.Current;
        var hostile = new OpenGlStateCache.PixelPackState(8, 17, 2, 3, true, true);
        // The outer scope preserves the fixture's layout; readback must restore this hostile inner owner.
        using var restore = OpenGLPixelLayouts.PackScope(cache, hostile);
        byte[] pixels = readback();
        Assert.Equal(count, pixels.Length);
        Assert.All(pixels, value => Assert.Equal(expected, value));
        Assert.Equal(hostile, OpenGLPixelLayouts.GetPack(cache));
        Assert.Equal(8, GL.GetInteger(GetPName.PackAlignment));
        Assert.Equal(17, GL.GetInteger(GetPName.PackRowLength));
        Assert.Equal(2, GL.GetInteger(GetPName.PackSkipRows));
        Assert.Equal(3, GL.GetInteger(GetPName.PackSkipPixels));
        Assert.Equal(1, GL.GetInteger(GetPName.PackSwapBytes));
        Assert.Equal(1, GL.GetInteger(GetPName.PackLsbFirst));
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }
    #endregion
}
