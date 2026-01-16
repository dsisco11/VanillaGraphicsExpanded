using System;

using OpenTK.Graphics.OpenGL;

using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

using Xunit;

namespace VanillaGraphicsExpanded.Tests.GPU;

[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class DynamicTextureReadPixelsTests : RenderTestBase
{
    public DynamicTextureReadPixelsTests(HeadlessGLFixture fixture) : base(fixture) { }

    [Fact]
    public void ReadPixels_R32f_ReadsClearedValue_FromColorAttachment0()
    {
        EnsureContextValid();

        using var tex = DynamicTexture.Create(8, 8, PixelInternalFormat.R32f, TextureFilterMode.Nearest);
        using var fbo = GBuffer.CreateSingle(tex, ownsTextures: false) ?? throw new InvalidOperationException("Failed to create GBuffer");

        fbo.Bind();
        GL.Viewport(0, 0, tex.Width, tex.Height);

        // Clear to a non-trivial value so we catch broken read buffers.
        GL.ClearColor(0.25f, 0f, 0f, 0f);
        GL.Clear(ClearBufferMask.ColorBufferBit);

        GBuffer.Unbind();

        float[] pixels = tex.ReadPixels();
        Assert.Equal(tex.Width * tex.Height * 1, pixels.Length);

        // All pixels should be ~0.25
        float min = float.PositiveInfinity;
        float max = float.NegativeInfinity;
        for (int i = 0; i < pixels.Length; i++)
        {
            float v = pixels[i];
            if (v < min) min = v;
            if (v > max) max = v;
        }

        Assert.InRange(min, 0.24f, 0.26f);
        Assert.InRange(max, 0.24f, 0.26f);
    }

    [Fact]
    public void ReadPixels_Rgba16f_ReadsClearedRgba_FromColorAttachment0()
    {
        EnsureContextValid();

        using var tex = DynamicTexture.Create(4, 4, PixelInternalFormat.Rgba16f, TextureFilterMode.Nearest);
        using var fbo = GBuffer.CreateSingle(tex, ownsTextures: false) ?? throw new InvalidOperationException("Failed to create GBuffer");

        fbo.Bind();
        GL.Viewport(0, 0, tex.Width, tex.Height);

        GL.ClearColor(0.1f, 0.2f, 0.3f, 0.4f);
        GL.Clear(ClearBufferMask.ColorBufferBit);

        GBuffer.Unbind();

        float[] pixels = tex.ReadPixels();
        Assert.Equal(tex.Width * tex.Height * 4, pixels.Length);

        // Spot check first pixel.
        Assert.InRange(pixels[0], 0.09f, 0.11f);
        Assert.InRange(pixels[1], 0.19f, 0.21f);
        Assert.InRange(pixels[2], 0.29f, 0.31f);
        Assert.InRange(pixels[3], 0.39f, 0.41f);
    }
}
