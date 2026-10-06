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
    #region Public API
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

        AssertStencilReadbackPreservesPackLayout(texture.ReadStencilPixels, 15, 0x5a);
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
        AssertStencilReadbackPreservesPackLayout(texture.ReadStencilPixels, 16, 0x36);
    }
    #endregion

    #region Private
    /// <summary>Verifies tightly packed stencil pixels and restoration of all cached and native pack fields.</summary>
    private static void AssertStencilReadbackPreservesPackLayout(Func<byte[]> readback, int count, byte expected)
    {
        var cache = StateCache.Current;
        var hostile = new StateCache.PixelPackState(8, 17, 2, 3, true, true);
        // The outer scope preserves the fixture's layout; readback must restore this hostile inner owner.
        using var restore = cache.SetPixelPackScope(hostile);
        byte[] pixels = readback();
        Assert.Equal(count, pixels.Length);
        Assert.All(pixels, value => Assert.Equal(expected, value));
        Assert.Equal(hostile, cache.GetPixelPackState());
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
