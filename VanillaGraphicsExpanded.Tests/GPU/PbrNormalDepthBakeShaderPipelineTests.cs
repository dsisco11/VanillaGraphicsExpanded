using System;
using System.IO;

using OpenTK.Graphics.OpenGL;

using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;

using Xunit;

namespace VanillaGraphicsExpanded.Tests.GPU;

[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class PbrNormalDepthBakeShaderPipelineTests : RenderTestBase
{
    private readonly HeadlessGLFixture fixture;

    public PbrNormalDepthBakeShaderPipelineTests(HeadlessGLFixture fixture) : base(fixture)
    {
        this.fixture = fixture;
    }

    [Fact]
    public void PackToAtlas_EncodesHeightIntoAlpha01_NotAllWhite()
    {
        EnsureContextValid();

        fixture.MakeCurrent();

        string shaderPath = Path.Combine(AppContext.BaseDirectory, "assets", "shaders");
        string includePath = Path.Combine(AppContext.BaseDirectory, "assets", "shaders", "includes");

        if (!Directory.Exists(shaderPath) || !Directory.Exists(includePath))
        {
            Assert.SkipWhen(true, "Test shader assets not found in output directory.");
        }

        using var helper = new ShaderTestHelper(shaderPath, includePath);
        var programResult = helper.CompileAndLink(
            vertexFilename: "pbr_heightbake_fullscreen.vsh",
            fragmentFilename: "pbr_heightbake_pack_to_atlas.fsh");

        Assert.True(programResult.IsSuccess, programResult.ErrorMessage);
        int programId = programResult.ProgramId;
        Assert.True(programId > 0);

        const int w = 16;
        const int h = 8;

        // Signed height ramp across X in [-2..2]. The packing shader clamps to [-1..1] and encodes to [0..1].
        float[] heightData = new float[w * h];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                float t = w == 1 ? 0f : (float)x / (w - 1);
                heightData[y * w + x] = t * 4f - 2f;
            }
        }

        using var heightTex = DynamicTexture.CreateWithData(w, h, PixelInternalFormat.R32f, heightData, TextureFilterMode.Nearest);
        using var target = CreateRenderTarget(w, h, PixelInternalFormat.Rgba16f);

        target.Bind();
        GL.Viewport(0, 0, w, h);
        GL.ClearColor(0f, 0f, 0f, 0f);
        GL.Clear(ClearBufferMask.ColorBufferBit);

        GL.UseProgram(programId);

        heightTex.Bind(0);

        int locHeight = GL.GetUniformLocation(programId, "u_height");
        int locSolverSize = GL.GetUniformLocation(programId, "u_solverSize");
        int locTileSize = GL.GetUniformLocation(programId, "u_tileSize");
        int locViewportOrigin = GL.GetUniformLocation(programId, "u_viewportOrigin");
        int locNormalStrength = GL.GetUniformLocation(programId, "u_normalStrength");

        Assert.True(locHeight >= 0, "u_height uniform missing");
        Assert.True(locSolverSize >= 0, "u_solverSize uniform missing");
        Assert.True(locTileSize >= 0, "u_tileSize uniform missing");
        Assert.True(locViewportOrigin >= 0, "u_viewportOrigin uniform missing");
        Assert.True(locNormalStrength >= 0, "u_normalStrength uniform missing");

        GL.Uniform1(locHeight, 0);
        GL.Uniform2(locSolverSize, w, h);
        GL.Uniform2(locTileSize, w, h);
        GL.Uniform2(locViewportOrigin, 0, 0);
        GL.Uniform1(locNormalStrength, 0f);

        RenderFullscreenQuad();

        GL.UseProgram(0);
        GBuffer.Unbind();

        float[] pixels = target[0].ReadPixels();
        Assert.NotEmpty(pixels);

        float minA = float.PositiveInfinity;
        float maxA = float.NegativeInfinity;

        for (int i = 0; i < pixels.Length; i += 4)
        {
            float a = pixels[i + 3];
            if (a < minA) minA = a;
            if (a > maxA) maxA = a;
        }

        // Sanity: should vary and stay in a renderdoc-friendly range.
        Assert.True(maxA - minA > 0.25f, $"Alpha appears constant; min={minA}, max={maxA}");
        Assert.InRange(minA, -0.01f, 0.10f);
        Assert.InRange(maxA, 0.90f, 1.01f);

        // Spot-check RGB normal output when normalStrength=0: should be approximately (0.5, 0.5, 1.0).
        Assert.InRange(pixels[0], 0.49f, 0.51f);
        Assert.InRange(pixels[1], 0.49f, 0.51f);
        Assert.InRange(pixels[2], 0.99f, 1.01f);
    }

    [Fact]
    public void NormalizeThenPack_ProducesNonFlatAlpha_ForBipolarInput()
    {
        EnsureContextValid();

        fixture.MakeCurrent();

        string shaderPath = Path.Combine(AppContext.BaseDirectory, "assets", "shaders");
        string includePath = Path.Combine(AppContext.BaseDirectory, "assets", "shaders", "includes");

        if (!Directory.Exists(shaderPath) || !Directory.Exists(includePath))
        {
            Assert.SkipWhen(true, "Test shader assets not found in output directory.");
        }

        using var helper = new ShaderTestHelper(shaderPath, includePath);

        var normalizeProgram = helper.CompileAndLink(
            vertexFilename: "pbr_heightbake_fullscreen.vsh",
            fragmentFilename: "pbr_heightbake_normalize.fsh");
        Assert.True(normalizeProgram.IsSuccess, normalizeProgram.ErrorMessage);

        var packProgram = helper.CompileAndLink(
            vertexFilename: "pbr_heightbake_fullscreen.vsh",
            fragmentFilename: "pbr_heightbake_pack_to_atlas.fsh");
        Assert.True(packProgram.IsSuccess, packProgram.ErrorMessage);

        const int w = 32;
        const int h = 16;

        // Bipolar input around zero so a correct mean subtraction should produce both signs.
        float[] hData = new float[w * h];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                float fx = (float)x / (w - 1);
                float fy = (float)y / (h - 1);
                // Centered-ish pattern in [-1..1]
                hData[y * w + x] = (fx - 0.5f) * 2f + (fy - 0.5f) * 0.5f;
            }
        }

        using var hTex = DynamicTexture.CreateWithData(w, h, PixelInternalFormat.R32f, hData, TextureFilterMode.Nearest);
        using var hnTex = DynamicTexture.Create(w, h, PixelInternalFormat.R32f, TextureFilterMode.Nearest);
        using var hnFbo = GBuffer.CreateSingle(hnTex, ownsTextures: false) ?? throw new InvalidOperationException("Failed to create Hn FBO");

        // Pass 1: normalize into hnTex
        hnFbo.Bind();
        GL.Viewport(0, 0, w, h);
        GL.ClearColor(0f, 0f, 0f, 0f);
        GL.Clear(ClearBufferMask.ColorBufferBit);

        GL.UseProgram(normalizeProgram.ProgramId);
        hTex.Bind(0);

        int locH = GL.GetUniformLocation(normalizeProgram.ProgramId, "u_h");
        int locSize = GL.GetUniformLocation(normalizeProgram.ProgramId, "u_size");
        int locMean = GL.GetUniformLocation(normalizeProgram.ProgramId, "u_mean");
        int locStrength = GL.GetUniformLocation(normalizeProgram.ProgramId, "u_heightStrength");
        int locGamma = GL.GetUniformLocation(normalizeProgram.ProgramId, "u_gamma");

        Assert.True(locH >= 0);
        Assert.True(locSize >= 0);
        Assert.True(locMean >= 0);
        Assert.True(locStrength >= 0);
        Assert.True(locGamma >= 0);

        GL.Uniform1(locH, 0);
        GL.Uniform2(locSize, w, h);
        GL.Uniform1(locMean, 0f);
        GL.Uniform1(locStrength, 1.0f);
        GL.Uniform1(locGamma, 1.0f);

        RenderFullscreenQuad();
        GL.UseProgram(0);
        GBuffer.Unbind();

        // Pass 2: pack into RGBA16F output
        using var outFbo = CreateRenderTarget(w, h, PixelInternalFormat.Rgba16f);
        outFbo.Bind();
        GL.Viewport(0, 0, w, h);
        GL.ClearColor(0f, 0f, 0f, 0f);
        GL.Clear(ClearBufferMask.ColorBufferBit);

        GL.UseProgram(packProgram.ProgramId);

        hnTex.Bind(0);
        int locHeight = GL.GetUniformLocation(packProgram.ProgramId, "u_height");
        int locSolverSize = GL.GetUniformLocation(packProgram.ProgramId, "u_solverSize");
        int locTileSize = GL.GetUniformLocation(packProgram.ProgramId, "u_tileSize");
        int locViewportOrigin = GL.GetUniformLocation(packProgram.ProgramId, "u_viewportOrigin");
        int locNormalStrength2 = GL.GetUniformLocation(packProgram.ProgramId, "u_normalStrength");

        Assert.True(locHeight >= 0);
        Assert.True(locSolverSize >= 0);
        Assert.True(locTileSize >= 0);
        Assert.True(locViewportOrigin >= 0);
        Assert.True(locNormalStrength2 >= 0);

        GL.Uniform1(locHeight, 0);
        GL.Uniform2(locSolverSize, w, h);
        GL.Uniform2(locTileSize, w, h);
        GL.Uniform2(locViewportOrigin, 0, 0);
        GL.Uniform1(locNormalStrength2, 0f);

        RenderFullscreenQuad();

        GL.UseProgram(0);
        GBuffer.Unbind();

        float[] pixels = outFbo[0].ReadPixels();
        Assert.NotEmpty(pixels);

        float minA = float.PositiveInfinity;
        float maxA = float.NegativeInfinity;
        int ones = 0;
        int zeros = 0;

        for (int i = 0; i < pixels.Length; i += 4)
        {
            float a = pixels[i + 3];
            if (a < minA) minA = a;
            if (a > maxA) maxA = a;
            if (a > 0.999f) ones++;
            if (a < 0.001f) zeros++;
        }

        Assert.True(maxA - minA > 0.10f, $"Alpha too flat; min={minA}, max={maxA}");
        Assert.True(ones < (w * h * 0.95), $"Alpha appears saturated to 1.0; ones={ones}/{w*h}");
        Assert.True(zeros < (w * h * 0.95), $"Alpha appears saturated to 0.0; zeros={zeros}/{w*h}");
    }

    [Fact]
    public void PackToAtlas_WithBlendEnabled_DoesNotTurnAlphaSolidWhite()
    {
        EnsureContextValid();

        fixture.MakeCurrent();

        string shaderPath = Path.Combine(AppContext.BaseDirectory, "assets", "shaders");
        string includePath = Path.Combine(AppContext.BaseDirectory, "assets", "shaders", "includes");

        if (!Directory.Exists(shaderPath) || !Directory.Exists(includePath))
        {
            Assert.SkipWhen(true, "Test shader assets not found in output directory.");
        }

        using var helper = new ShaderTestHelper(shaderPath, includePath);
        var programResult = helper.CompileAndLink(
            vertexFilename: "pbr_heightbake_fullscreen.vsh",
            fragmentFilename: "pbr_heightbake_pack_to_atlas.fsh");

        Assert.True(programResult.IsSuccess, programResult.ErrorMessage);
        int programId = programResult.ProgramId;
        Assert.True(programId > 0);

        const int w = 32;
        const int h = 16;

        float[] heightData = new float[w * h];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                float t = (float)x / (w - 1);
                heightData[y * w + x] = t * 2f - 1f;
            }
        }

        using var heightTex = DynamicTexture.CreateWithData(w, h, PixelInternalFormat.R32f, heightData, TextureFilterMode.Nearest);
        using var target = CreateRenderTarget(w, h, PixelInternalFormat.Rgba16f);

        // Simulate engine state bleed: blending enabled with an additive-ish alpha path.
        GL.Enable(EnableCap.Blend);
        GL.BlendFunc(BlendingFactor.One, BlendingFactor.One);

        target.Bind();
        GL.Viewport(0, 0, w, h);
        GL.ClearColor(0f, 0f, 0f, 0f);
        GL.Clear(ClearBufferMask.ColorBufferBit);

        GL.UseProgram(programId);

        heightTex.Bind(0);

        int locHeight = GL.GetUniformLocation(programId, "u_height");
        int locSolverSize = GL.GetUniformLocation(programId, "u_solverSize");
        int locTileSize = GL.GetUniformLocation(programId, "u_tileSize");
        int locViewportOrigin = GL.GetUniformLocation(programId, "u_viewportOrigin");
        int locNormalStrength = GL.GetUniformLocation(programId, "u_normalStrength");

        Assert.True(locHeight >= 0);
        Assert.True(locSolverSize >= 0);
        Assert.True(locTileSize >= 0);
        Assert.True(locViewportOrigin >= 0);
        Assert.True(locNormalStrength >= 0);

        GL.Uniform1(locHeight, 0);
        GL.Uniform2(locSolverSize, w, h);
        GL.Uniform2(locTileSize, w, h);
        GL.Uniform2(locViewportOrigin, 0, 0);
        GL.Uniform1(locNormalStrength, 0f);

        RenderFullscreenQuad();

        GL.UseProgram(0);
        GBuffer.Unbind();
        GL.Disable(EnableCap.Blend);

        float[] pixels = target[0].ReadPixels();
        Assert.NotEmpty(pixels);

        int ones = 0;
        for (int i = 0; i < pixels.Length; i += 4)
        {
            if (pixels[i + 3] > 0.999f)
            {
                ones++;
            }
        }

        Assert.True(ones < (w * h * 0.98), $"Alpha appears solid white under blending; ones={ones}/{w*h}");
    }
}
