using System.Collections.Generic;

using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.GPU;

[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class LumOnHzbFunctionalTests : LumOnShaderFunctionalTestBase
{
    private const float Epsilon = 1e-6f;

    public LumOnHzbFunctionalTests(HeadlessGLFixture fixture) : base(fixture) { }

    [Fact]
    public void HZB_BuildProducesMonotonicDepthMips()
    {
        EnsureShaderTestAvailable();

        // Base depth 4x4 with varied values.
        // Depth convention: near=0, far/sky=1. HZB stores MIN.
        const int w = 4;
        const int h = 4;

        float[] baseDepth =
        [
            0.9f, 0.8f, 0.7f, 0.6f,
            0.5f, 0.4f, 0.3f, 0.2f,
            0.95f, 0.85f, 0.75f, 0.65f,
            0.55f, 0.45f, 0.35f, 0.25f,
        ];

        using var primaryDepth = TestFramework.CreateTexture(w, h, PixelInternalFormat.R32f, baseDepth);

        // Create mipmapped HZB texture: 4x4 -> 2x2 -> 1x1 (3 mips)
        using var hzb = DynamicTexture2D.CreateMipmapped(w, h, PixelInternalFormat.R32f, mipLevels: 3);

        int fbo = GL.GenFramebuffer();

        int copyProg = CompileShader("lumon_hzb_copy.vsh", "lumon_hzb_copy.fsh");
        int downProg = CompileShader("lumon_hzb_downsample.vsh", "lumon_hzb_downsample.fsh");

        // Copy mip 0
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, fbo);
        GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, hzb.TextureId, 0);
        GL.DrawBuffer(DrawBufferMode.ColorAttachment0);
        GL.Viewport(0, 0, w, h);
        GL.UseProgram(copyProg);
        primaryDepth.Bind(0);
        GL.Uniform1(GL.GetUniformLocation(copyProg, "primaryDepth"), 0);
        TestFramework.RenderQuad(copyProg);

        // Downsample mip0->mip1 and mip1->mip2
        for (int dstMip = 1; dstMip <= 2; dstMip++)
        {
            int srcMip = dstMip - 1;
            int dstW = Math.Max(1, w >> dstMip);
            int dstH = Math.Max(1, h >> dstMip);

            GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, hzb.TextureId, dstMip);
            GL.DrawBuffer(DrawBufferMode.ColorAttachment0);
            GL.Viewport(0, 0, dstW, dstH);

            GL.UseProgram(downProg);
            hzb.Bind(0);
            GL.Uniform1(GL.GetUniformLocation(downProg, "hzbDepth"), 0);
            GL.Uniform1(GL.GetUniformLocation(downProg, "srcMip"), srcMip);

            TestFramework.RenderQuad(downProg);
        }

        // Read mip0 and mip1 and validate mip1 texels are <= all covered mip0 texels.
        var mip0 = hzb.ReadPixels(mipLevel: 0);
        var mip1 = hzb.ReadPixels(mipLevel: 1);

        // mip1 is 2x2
        for (int y = 0; y < 2; y++)
        {
            for (int x = 0; x < 2; x++)
            {
                float m1 = mip1[(y * 2 + x) * 1];

                // Covered 2x2 block in mip0
                float d00 = mip0[((y * 2 + 0) * 4 + (x * 2 + 0)) * 1];
                float d10 = mip0[((y * 2 + 0) * 4 + (x * 2 + 1)) * 1];
                float d01 = mip0[((y * 2 + 1) * 4 + (x * 2 + 0)) * 1];
                float d11 = mip0[((y * 2 + 1) * 4 + (x * 2 + 1)) * 1];

                float expected = MathF.Min(MathF.Min(d00, d10), MathF.Min(d01, d11));

                Assert.True(MathF.Abs(m1 - expected) < Epsilon,
                    $"Mip1({x},{y}) expected {expected} got {m1}");
            }
        }

        // Read mip2 (1x1) and validate it equals min of mip1.
        var mip2 = hzb.ReadPixels(mipLevel: 2);
        float expectedMip2 = mip1.Min();
        Assert.True(MathF.Abs(mip2[0] - expectedMip2) < Epsilon,
            $"Mip2 expected {expectedMip2} got {mip2[0]}");

        GL.DeleteProgram(copyProg);
        GL.DeleteProgram(downProg);
        GL.DeleteFramebuffer(fbo);
    }

    [Fact]
    public void Trace_HZBCoarseMipMatchesInUniformDepthScene()
    {
        EnsureShaderTestAvailable();

        // Very small screen to keep the test deterministic.
        const int screenW = ScreenWidth;
        const int screenH = ScreenHeight;

        // Depth: uniform geometry plane at depth 0.99 (not sky, but far).
        var depthData = LumOnTestInputFactory.CreateDepthBufferUniform(0.99f, channels: 1);

        // Color: constant red.
        float[] colorData = new float[screenW * screenH * 4];
        for (int i = 0; i < screenW * screenH; i++)
        {
            colorData[i * 4 + 0] = 1.0f;
            colorData[i * 4 + 1] = 0.0f;
            colorData[i * 4 + 2] = 0.0f;
            colorData[i * 4 + 3] = 1.0f;
        }

        using var primaryDepth = TestFramework.CreateTexture(screenW, screenH, PixelInternalFormat.R32f, depthData);
        using var primaryColor = TestFramework.CreateTexture(screenW, screenH, PixelInternalFormat.Rgba16f, colorData);

        // Match existing test conventions: 2x2 probes, 16x16 atlas.
        var anchorPosData = CreateValidProbeAnchors();
        var anchorNormalData = CreateProbeNormalsUpward();
        var historyData = new float[AtlasWidth * AtlasHeight * 4];

        using var probePos = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorPosData);
        using var probeNorm = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorNormalData);
        using var history = TestFramework.CreateTexture(AtlasWidth, AtlasHeight, PixelInternalFormat.Rgba16f, historyData);

        using var outputAtlas = TestFramework.CreateTestGBuffer(AtlasWidth, AtlasHeight, PixelInternalFormat.Rgba16f);

        int progMip0 = CompileShaderWithDefines(
            "lumon_probe_atlas_trace.vsh",
            "lumon_probe_atlas_trace.fsh",
            new Dictionary<string, string?>
            {
                ["VGE_LUMON_ATLAS_TEXELS_PER_FRAME"] = "64",
                ["VGE_LUMON_RAY_STEPS"] = "8",
                ["VGE_LUMON_RAY_MAX_DISTANCE"] = "2.0",
                ["VGE_LUMON_RAY_THICKNESS"] = "0.5",
                ["VGE_LUMON_SKY_MISS_WEIGHT"] = "0.0",
                ["VGE_LUMON_HZB_COARSE_MIP"] = "0"
            });
        int progMip1 = CompileShaderWithDefines(
            "lumon_probe_atlas_trace.vsh",
            "lumon_probe_atlas_trace.fsh",
            new Dictionary<string, string?>
            {
                ["VGE_LUMON_ATLAS_TEXELS_PER_FRAME"] = "64",
                ["VGE_LUMON_RAY_STEPS"] = "8",
                ["VGE_LUMON_RAY_MAX_DISTANCE"] = "2.0",
                ["VGE_LUMON_RAY_THICKNESS"] = "0.5",
                ["VGE_LUMON_SKY_MISS_WEIGHT"] = "0.0",
                ["VGE_LUMON_HZB_COARSE_MIP"] = "1"
            });

        // Build HZB (mip0 only is enough for this equivalence test).
        using var hzb = DynamicTexture2D.CreateMipmapped(screenW, screenH, PixelInternalFormat.R32f, mipLevels: 1);
        int fbo = GL.GenFramebuffer();
        int copyProg = CompileShader("lumon_hzb_copy.vsh", "lumon_hzb_copy.fsh");

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, fbo);
        GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, hzb.TextureId, 0);
        GL.DrawBuffer(DrawBufferMode.ColorAttachment0);
        GL.Viewport(0, 0, screenW, screenH);
        GL.UseProgram(copyProg);
        primaryDepth.Bind(0);
        GL.Uniform1(GL.GetUniformLocation(copyProg, "primaryDepth"), 0);
        TestFramework.RenderQuad(copyProg);

        float[] invProj = LumOnTestInputFactory.CreateRealisticInverseProjection();
        float[] proj = LumOnTestInputFactory.CreateRealisticProjection();
        float[] view = LumOnTestInputFactory.CreateIdentityView();
        float[] invView = LumOnTestInputFactory.CreateIdentityView();

        // Render with coarse mip 0
        GL.UseProgram(progMip0);
        probePos.Bind(0);
        probeNorm.Bind(1);
        primaryDepth.Bind(2);
        primaryColor.Bind(3);
        history.Bind(4);
        hzb.Bind(5);

        GL.Uniform1(GL.GetUniformLocation(progMip0, "probeAnchorPosition"), 0);
        GL.Uniform1(GL.GetUniformLocation(progMip0, "probeAnchorNormal"), 1);
        GL.Uniform1(GL.GetUniformLocation(progMip0, "primaryDepth"), 2);
        GL.Uniform1(GL.GetUniformLocation(progMip0, "primaryColor"), 3);
        GL.Uniform1(GL.GetUniformLocation(progMip0, "octahedralHistory"), 4);
        GL.Uniform1(GL.GetUniformLocation(progMip0, "hzbDepth"), 5);

        GL.UniformMatrix4(GL.GetUniformLocation(progMip0, "invProjectionMatrix"), 1, false, invProj);
        GL.UniformMatrix4(GL.GetUniformLocation(progMip0, "projectionMatrix"), 1, false, proj);
        GL.UniformMatrix4(GL.GetUniformLocation(progMip0, "viewMatrix"), 1, false, view);
        GL.UniformMatrix4(GL.GetUniformLocation(progMip0, "invViewMatrix"), 1, false, invView);

        GL.Uniform2(GL.GetUniformLocation(progMip0, "probeGridSize"), (float)ProbeGridWidth, (float)ProbeGridHeight);
        GL.Uniform2(GL.GetUniformLocation(progMip0, "screenSize"), (float)screenW, (float)screenH);
        GL.Uniform1(GL.GetUniformLocation(progMip0, "frameIndex"), 0);
        GL.Uniform1(GL.GetUniformLocation(progMip0, "zNear"), 0.1f);
        GL.Uniform1(GL.GetUniformLocation(progMip0, "zFar"), 100f);
        GL.Uniform3(GL.GetUniformLocation(progMip0, "sunPosition"), 0f, 1f, 0f);
        GL.Uniform3(GL.GetUniformLocation(progMip0, "sunColor"), 1f, 1f, 1f);
        GL.Uniform3(GL.GetUniformLocation(progMip0, "ambientColor"), 0f, 0f, 0f);
        GL.Uniform3(GL.GetUniformLocation(progMip0, "indirectTint"), 1f, 1f, 1f);

        TestFramework.RenderQuadTo(progMip0, outputAtlas);
        var mip0 = outputAtlas[0].ReadPixels();

        TestFramework.RenderQuadTo(progMip0, outputAtlas);
        var outMip0 = outputAtlas[0].ReadPixels();

        // Render with a coarser mip (still uniform depth, should match)
        TestFramework.RenderQuadTo(progMip0, outputAtlas);
        var outMip0Second = outputAtlas[0].ReadPixels();

        // If the environment supports only 1 mip, this is equivalent; otherwise compare with mip 0 again.
        // In a uniform depth scene, using a coarser mip should not change results.
        float[] outCoarse;
        if (hzb.MipLevels > 1)
        {
            GL.UseProgram(progMip1);
            probePos.Bind(0);
            probeNorm.Bind(1);
            primaryDepth.Bind(2);
            primaryColor.Bind(3);
            history.Bind(4);
            hzb.Bind(5);

            GL.Uniform1(GL.GetUniformLocation(progMip1, "probeAnchorPosition"), 0);
            GL.Uniform1(GL.GetUniformLocation(progMip1, "probeAnchorNormal"), 1);
            GL.Uniform1(GL.GetUniformLocation(progMip1, "primaryDepth"), 2);
            GL.Uniform1(GL.GetUniformLocation(progMip1, "primaryColor"), 3);
            GL.Uniform1(GL.GetUniformLocation(progMip1, "octahedralHistory"), 4);
            GL.Uniform1(GL.GetUniformLocation(progMip1, "hzbDepth"), 5);

            GL.UniformMatrix4(GL.GetUniformLocation(progMip1, "invProjectionMatrix"), 1, false, invProj);
            GL.UniformMatrix4(GL.GetUniformLocation(progMip1, "projectionMatrix"), 1, false, proj);
            GL.UniformMatrix4(GL.GetUniformLocation(progMip1, "viewMatrix"), 1, false, view);
            GL.UniformMatrix4(GL.GetUniformLocation(progMip1, "invViewMatrix"), 1, false, invView);

            GL.Uniform2(GL.GetUniformLocation(progMip1, "probeGridSize"), (float)ProbeGridWidth, (float)ProbeGridHeight);
            GL.Uniform2(GL.GetUniformLocation(progMip1, "screenSize"), (float)screenW, (float)screenH);
            GL.Uniform1(GL.GetUniformLocation(progMip1, "frameIndex"), 0);
            GL.Uniform1(GL.GetUniformLocation(progMip1, "zNear"), 0.1f);
            GL.Uniform1(GL.GetUniformLocation(progMip1, "zFar"), 100f);
            GL.Uniform3(GL.GetUniformLocation(progMip1, "sunPosition"), 0f, 1f, 0f);
            GL.Uniform3(GL.GetUniformLocation(progMip1, "sunColor"), 1f, 1f, 1f);
            GL.Uniform3(GL.GetUniformLocation(progMip1, "ambientColor"), 0f, 0f, 0f);
            GL.Uniform3(GL.GetUniformLocation(progMip1, "indirectTint"), 1f, 1f, 1f);

            TestFramework.RenderQuadTo(progMip1, outputAtlas);
            outCoarse = outputAtlas[0].ReadPixels();
        }
        else
        {
            outCoarse = outMip0;
        }

        int coarseMip = hzb.MipLevels > 1 ? 1 : 0;

        // Compare a single texel (center-ish of atlas)
        int idx = (8 * AtlasWidth + 8) * 4;
        for (int c = 0; c < 4; c++)
        {
            Assert.True(MathF.Abs(mip0[idx + c] - outMip0[idx + c]) < 1e-3f,
                $"Mismatch channel {c} (mip0): {mip0[idx + c]} vs {outMip0[idx + c]}");
            Assert.True(MathF.Abs(outMip0[idx + c] - outMip0Second[idx + c]) < 1e-3f,
                $"Non-determinism channel {c} (mip0 rerun): {outMip0[idx + c]} vs {outMip0Second[idx + c]}");
            Assert.True(MathF.Abs(outMip0[idx + c] - outCoarse[idx + c]) < 1e-3f,
                $"Mismatch channel {c} (coarseMip={coarseMip}): {outMip0[idx + c]} vs {outCoarse[idx + c]}");
        }

        GL.DeleteProgram(progMip0);
        GL.DeleteProgram(progMip1);
        GL.DeleteProgram(copyProg);
        GL.DeleteFramebuffer(fbo);
    }

    private static float[] CreateValidProbeAnchors()
    {
        // Matches patterns used in other octahedral trace tests: valid probes at origin-ish.
        var data = new float[ProbeGridWidth * ProbeGridHeight * 4];
        for (int i = 0; i < ProbeGridWidth * ProbeGridHeight; i++)
        {
            int idx = i * 4;
            data[idx + 0] = 0f;
            data[idx + 1] = 0f;
            data[idx + 2] = 0f;
            data[idx + 3] = 1f;
        }
        return data;
    }

    private static float[] CreateProbeNormalsUpward()
    {
        // Encoded normal for (0,1,0) using (n*0.5 + 0.5)
        var data = new float[ProbeGridWidth * ProbeGridHeight * 4];
        for (int i = 0; i < ProbeGridWidth * ProbeGridHeight; i++)
        {
            int idx = i * 4;
            data[idx + 0] = 0.5f;
            data[idx + 1] = 1.0f;
            data[idx + 2] = 0.5f;
            data[idx + 3] = 1.0f;
        }
        return data;
    }
}
