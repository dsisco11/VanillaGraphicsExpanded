using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Verifies depth and stencil readback from their matching framebuffer attachments.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class DepthStencilTextureTests : RenderTestBase
{
    /// <summary>Initializes the headless OpenGL fixture.</summary>
    public DepthStencilTextureTests(HeadlessGLFixture fixture) : base(fixture) { }

    /// <summary>Depth-only storage reads cleared depth values.</summary>
    [Fact]
    public void DepthTexture_ReadPixels_UsesDepthAttachment()
    {
        EnsureContextValid();
        using var texture = new DepthTexture(4, 4, PixelInternalFormat.DepthComponent24);
        using var framebuffer = GpuFramebuffer.CreateDepthOnly(texture)
            ?? throw new InvalidOperationException("Depth framebuffer creation failed.");

        framebuffer.Bind();
        GL.DepthMask(true);
        GL.ClearDepth(0.375);
        GL.Clear(ClearBufferMask.DepthBufferBit);
        GpuFramebuffer.Unbind();

        float[] pixels = texture.ReadPixels();
        Assert.Equal(16, pixels.Length);
        Assert.All(pixels, value => Assert.InRange(value, 0.374f, 0.376f));
    }

    /// <summary>Stencil-only storage reads integer stencil indices.</summary>
    [Fact]
    public void StencilTexture_ReadStencilPixels_UsesStencilAttachment()
    {
        EnsureContextValid();
        using var texture = new StencilTexture(3, 5);
        using var framebuffer = GpuFramebuffer.CreateEmpty("StencilReadbackTest");
        framebuffer.Attach(texture);
        GL.DrawBuffer(DrawBufferMode.None);
        GL.ReadBuffer(ReadBufferMode.None);
        Assert.True(framebuffer.CheckStatus(out string? error), error);

        framebuffer.Bind();
        GL.StencilMask(0xff);
        GL.ClearStencil(0x5a);
        GL.Clear(ClearBufferMask.StencilBufferBit);
        GpuFramebuffer.Unbind();

        Assert.All(texture.ReadStencilPixels(), value => Assert.Equal((byte)0x5a, value));
    }

    /// <summary>Packed storage exposes both aspects without treating either as color.</summary>
    [Fact]
    public void DepthStencilTexture_ReadsBothAspects()
    {
        EnsureContextValid();
        using var texture = new DepthStencilTexture(4, 4);
        using var framebuffer = GpuFramebuffer.CreateDepthOnly(texture)
            ?? throw new InvalidOperationException("Depth-stencil framebuffer creation failed.");

        framebuffer.Bind();
        GL.DepthMask(true);
        GL.StencilMask(0xff);
        GL.ClearDepth(0.625);
        GL.ClearStencil(0x36);
        GL.Clear(ClearBufferMask.DepthBufferBit | ClearBufferMask.StencilBufferBit);
        GpuFramebuffer.Unbind();

        Assert.All(texture.ReadPixels(), value => Assert.InRange(value, 0.624f, 0.626f));
        Assert.All(texture.ReadStencilPixels(), value => Assert.Equal((byte)0x36, value));
    }
}
