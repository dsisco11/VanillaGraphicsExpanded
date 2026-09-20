using System;
using System.Collections.Generic;
using System.Globalization;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.LumOn.Shaders;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.GPU;

[Collection("GPU")]
[Trait("Category", "GPU")]
public partial class LumOnProbeAtlasTraceWorldProbeFallbackFunctionalTests : LumOnShaderFunctionalTestBase
{
    private const uint LUMON_META_WORLDPROBE_FALLBACK = 1u << 5;

    private const int WorldProbeTileSize = 16;

    private const int RaySteps = 16;
    private const float RayMaxDistance = 50f;
    private const float RayThickness = 0.5f;
    private const float SkyMissWeight = 1.0f;

    public LumOnProbeAtlasTraceWorldProbeFallbackFunctionalTests(HeadlessGLFixture fixture) : base(fixture) { }

    private int CompileProbeAtlasTraceShaderWithWorldProbeFallback(int wpLevels, int wpResolution, float wpBaseSpacing)
        => CompileShaderWithDefines(
            "lumon_probe_atlas_trace.vsh",
            "lumon_probe_atlas_trace.fsh",
            new Dictionary<string, string?>
            {
                ["VGE_LUMON_RAY_STEPS"] = RaySteps.ToString(CultureInfo.InvariantCulture),
                ["VGE_LUMON_ATLAS_TEXELS_PER_FRAME"] = "64",
                ["VGE_LUMON_RAY_MAX_DISTANCE"] = RayMaxDistance.ToString(CultureInfo.InvariantCulture),
                ["VGE_LUMON_RAY_THICKNESS"] = RayThickness.ToString(CultureInfo.InvariantCulture),
                ["VGE_LUMON_SKY_MISS_WEIGHT"] = SkyMissWeight.ToString(CultureInfo.InvariantCulture),
                ["VGE_LUMON_HZB_COARSE_MIP"] = "0",

                ["VGE_LUMON_WORLDPROBE_ENABLED"] = "1",
                ["VGE_LUMON_WORLDPROBE_LEVELS"] = wpLevels.ToString(CultureInfo.InvariantCulture),
                ["VGE_LUMON_WORLDPROBE_RESOLUTION"] = wpResolution.ToString(CultureInfo.InvariantCulture),
                ["VGE_LUMON_WORLDPROBE_BASE_SPACING"] = wpBaseSpacing.ToString("0.0####", CultureInfo.InvariantCulture),
                ["VGE_LUMON_WORLDPROBE_OCTAHEDRAL_SIZE"] = WorldProbeTileSize.ToString(CultureInfo.InvariantCulture),
                ["VGE_LUMON_BIND_WORLDPROBE_RADIANCE_ATLAS"] = "1",
            });

    private static float[] CreateUniformData(int width, int height, int channels, params float[] value)
    {
        if (value.Length != channels) throw new ArgumentOutOfRangeException(nameof(value));

        var data = new float[checked(width * height * channels)];
        for (int i = 0; i < width * height; i++)
        {
            int idx = i * channels;
            for (int c = 0; c < channels; c++)
            {
                data[idx + c] = value[c];
            }
        }

        return data;
    }

    private static float[] CreateRadianceAtlasWithRedFirstProbeTile(int width, int height)
    {
        float alphaHitEncoded = (float)Math.Log(1.0 + 1.0);
        var data = CreateUniformData(width, height, 4, 0f, 0f, 1f, alphaHitEncoded);

        for (int y = 0; y < WorldProbeTileSize; y++)
        {
            for (int x = 0; x < WorldProbeTileSize; x++)
            {
                int index = (y * width + x) * 4;
                data[index + 0] = 1f;
                data[index + 1] = 0f;
                data[index + 2] = 0f;
            }
        }

        return data;
    }

    /// <summary>Retains the coordinate-space and paired-history regression with synthetic colored tiles.</summary>
    [Fact]
    public void TraceMiss_UsesPlayerRelativeCoordinates_ForWorldProbeFallback()
        => RunWorldProbeTraceScenario();

    /// <summary>Runs production tracing and both gather paths over supplied world-probe atlas data.</summary>
    private void RunWorldProbeTraceScenario(
        VanillaGraphicsExpanded.Tests.Fixtures.WorldProbes.WorldProbeAtlasData? tracedAtlas = null,
        float spacing = 1f, float anchorOffsetX = 0f, float expectedRadiance = -1f)
    {
        EnsureShaderTestAvailable();

        // 1 level, 2x2x2 clipmap => per-probe scalar atlas width=4, height=2
        const int wpLevels = 1;
        const int wpResolution = 2;
        float wpBaseSpacing = spacing;

        const int wpScalarAtlasWidth = wpResolution * wpResolution;
        const int wpScalarAtlasHeight = wpResolution * wpLevels;

        // Radiance atlas is tile-packed: W = (N*N)*S, H = (N*levels)*S.
        const int wpRadianceAtlasWidth = (wpResolution * wpResolution) * WorldProbeTileSize;
        const int wpRadianceAtlasHeight = (wpResolution * wpLevels) * WorldProbeTileSize;

        // The first physical probe tile is red; every other tile is blue. The anchor below maps exactly
        // to this first tile only when the shader uses player-relative surface coordinates directly.
        var wpRadianceAtlas = tracedAtlas?.Radiance ?? CreateRadianceAtlasWithRedFirstProbeTile(wpRadianceAtlasWidth, wpRadianceAtlasHeight);

        // vis0.xy = any octUV, vis0.z = skyIntensity, vis0.w = aoConf (not used for radiance fallback).
        var wpVis0 = tracedAtlas?.Visibility ?? CreateUniformData(wpScalarAtlasWidth, wpScalarAtlasHeight, 4, 0.5f, 1.0f, 0f, 0f);

        // meta0.r = confidence, meta0.g = flags-as-float (ignored here)
        var wpMeta0 = tracedAtlas?.Metadata ?? CreateUniformData(wpScalarAtlasWidth, wpScalarAtlasHeight, 2, 1.0f, 0f);

        // Probe anchors: all valid, placed in front of camera.
        var anchorPos = CreateUniformData(ProbeGridWidth, ProbeGridHeight, 4, anchorOffsetX, 0f, -5f, 1.0f);
        var anchorNormal = CreateUniformData(ProbeGridWidth, ProbeGridHeight, 4, 0f, 0f, 1f, 0f);

        // Sky depth: all rays should miss screen geometry.
        var depth = LumOnTestInputFactory.CreateDepthBufferUniform(1.0f, channels: 1);

        // Direct/emissive are unused for misses; keep them black.
        var direct = CreateUniformData(ScreenWidth, ScreenHeight, 4, 0f, 0f, 0f, 0f);
        var emissive = CreateUniformData(ScreenWidth, ScreenHeight, 4, 0f, 0f, 0f, 0f);

        // History inputs: unused when we trace all texels (64 per frame), but must be bound.
        var historyAtlas = CreateUniformData(AtlasWidth, AtlasHeight, 4, 0f, 0f, 0f, 0f);
        var historyMeta = CreateUniformData(AtlasWidth, AtlasHeight, 2, 0f, 0f);

        // HZB mip0: same as depth in this test.
        var hzb = depth;

        using var anchorPosTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorPos);
        using var anchorNormalTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorNormal);
        using var depthTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.R32f, depth);
        using var directTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f, direct);
        using var emissiveTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f, emissive);
        using var historyAtlasTex = TestFramework.CreateTexture(AtlasWidth, AtlasHeight, PixelInternalFormat.Rgba16f, historyAtlas);
        using var historyMetaTex = TestFramework.CreateTexture(AtlasWidth, AtlasHeight, PixelInternalFormat.Rg32f, historyMeta);
        using var hzbTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.R32f, hzb);

        using var wpRadianceAtlasTex = TestFramework.CreateTexture(wpRadianceAtlasWidth, wpRadianceAtlasHeight, PixelInternalFormat.Rgba16f, wpRadianceAtlas);
        using var wpVis0Tex = TestFramework.CreateTexture(wpScalarAtlasWidth, wpScalarAtlasHeight, PixelInternalFormat.Rgba16f, wpVis0);
        using var wpMeta0Tex = TestFramework.CreateTexture(wpScalarAtlasWidth, wpScalarAtlasHeight, PixelInternalFormat.Rg32f, wpMeta0);

        using var output = TestFramework.CreateTestGBuffer(AtlasWidth, AtlasHeight, PixelInternalFormat.Rgba16f, PixelInternalFormat.Rg32f);

        int programId = 0;
        try
        {
            programId = CompileProbeAtlasTraceShaderWithWorldProbeFallback(wpLevels, wpResolution, wpBaseSpacing);

            // Bind textures to fixed units
            anchorPosTex.Bind(0);
            anchorNormalTex.Bind(1);
            depthTex.Bind(2);
            directTex.Bind(3);
            emissiveTex.Bind(4);
            historyAtlasTex.Bind(5);
            hzbTex.Bind(6);
            historyMetaTex.Bind(7);

            wpRadianceAtlasTex.Bind(8);
            wpVis0Tex.Bind(9);
            wpMeta0Tex.Bind(10);

            var invProjection = LumOnTestInputFactory.CreateRealisticInverseProjection();
            var projection = LumOnTestInputFactory.CreateRealisticProjection();
            var view = LumOnTestInputFactory.CreateIdentityView();
            var invView = LumOnTestInputFactory.CreateIdentityMatrix();

            GL.UseProgram(programId);

            GL.UniformMatrix4(GL.GetUniformLocation(programId, "invProjectionMatrix"), 1, false, invProjection);
            GL.UniformMatrix4(GL.GetUniformLocation(programId, "projectionMatrix"), 1, false, projection);
            GL.UniformMatrix4(GL.GetUniformLocation(programId, "viewMatrix"), 1, false, view);
            GL.UniformMatrix4(GL.GetUniformLocation(programId, "invViewMatrix"), 1, false, invView);

            GL.Uniform2(GL.GetUniformLocation(programId, "probeGridSize"), (float)ProbeGridWidth, (float)ProbeGridHeight);
            GL.Uniform2(GL.GetUniformLocation(programId, "screenSize"), (float)ScreenWidth, (float)ScreenHeight);
            GL.Uniform1(GL.GetUniformLocation(programId, "frameIndex"), 0);

            GL.Uniform1(GL.GetUniformLocation(programId, "zNear"), ZNear);
            GL.Uniform1(GL.GetUniformLocation(programId, "zFar"), ZFar);

            GL.Uniform3(GL.GetUniformLocation(programId, "sunPosition"), 0.0f, 1.0f, 0.0f);
            GL.Uniform3(GL.GetUniformLocation(programId, "sunColor"), 0.0f, 0.0f, 0.0f);
            GL.Uniform3(GL.GetUniformLocation(programId, "ambientColor"), 0.0f, 1.0f, 0.0f); // bright green sky fallback
            GL.Uniform3(GL.GetUniformLocation(programId, "indirectTint"), 1.0f, 1.0f, 1.0f);

            // Sampler units (must match binds above)
            GL.Uniform1(GL.GetUniformLocation(programId, "probeAnchorPosition"), 0);
            GL.Uniform1(GL.GetUniformLocation(programId, "probeAnchorNormal"), 1);
            GL.Uniform1(GL.GetUniformLocation(programId, "primaryDepth"), 2);
            GL.Uniform1(GL.GetUniformLocation(programId, "directDiffuse"), 3);
            GL.Uniform1(GL.GetUniformLocation(programId, "emissive"), 4);
            GL.Uniform1(GL.GetUniformLocation(programId, "octahedralHistory"), 5);
            GL.Uniform1(GL.GetUniformLocation(programId, "hzbDepth"), 6);
            GL.Uniform1(GL.GetUniformLocation(programId, "probeAtlasMetaHistory"), 7);

            GL.Uniform1(GL.GetUniformLocation(programId, "worldProbeRadianceAtlas"), 8);
            GL.Uniform1(GL.GetUniformLocation(programId, "worldProbeVis0"), 9);
            GL.Uniform1(GL.GetUniformLocation(programId, "worldProbeMeta0"), 10);

            // Phase 23: UBO-backed frame + world-probe state (GLSL 330 assigns block bindings in C#).
            UpdateAndBindLumOnFrameUbo(
                programId,
                invProjectionMatrix: invProjection,
                projectionMatrix: projection,
                viewMatrix: view,
                invViewMatrix: invView,
                frameIndex: 0,
                sunPosition: new Vintagestory.API.MathTools.Vec3f(0f, 1f, 0f),
                sunColor: new Vintagestory.API.MathTools.Vec3f(0f, 0f, 0f),
                ambientColor: new Vintagestory.API.MathTools.Vec3f(0f, 1f, 0f));

            UpdateAndBindLumOnWorldProbeUbo(
                programId,
                skyTint: new Vintagestory.API.MathTools.Vec3f(0f, 0f, 0f),
                // This is the stable absolute player origin, not a render-camera translation.
                // The old coordinate path subtracted it from the relative anchor and sampled out of bounds.
                cameraPosWS: new System.Numerics.Vector3(512000f, 3f, 512000f),
                originMinCorner: [new System.Numerics.Vector3(-spacing * 0.5f, -spacing * 0.5f, -5f - spacing * 0.5f)],
                ringOffset: [new System.Numerics.Vector3(0f, 0f, 0f)]);

            GL.UseProgram(0);

            using var probeParamsBuffer = new ObjectParamsUbo("Tests.WorldProbeComparison.Params");
            var probeParams = new LumOnProbeParamsUbo();
            UniformBlockBindingUtil.EnsureBlockBound(programId, LumOnProbeParamsUbo.BlockName, GpuBindingRegistry.Ubo.Object);
            probeParamsBuffer.UploadAndBind(probeParams.Bytes);
            TestFramework.RenderQuadTo(programId, output);

            var radianceOut = output[0].ReadPixels();
            var metaOut = output[1].ReadPixels();

            // Validate a single texel (0,0): should be world-probe fallback, not sky.
            // radianceOut is RGBA, metaOut is RG (confidence, flagsBitsAsFloat).
            int atlasX = 0;
            int atlasY = 0;
            int radIdx = (atlasY * AtlasWidth + atlasX) * 4;
            int metaIdx = (atlasY * AtlasWidth + atlasX) * 2;

            float r = radianceOut[radIdx + 0];
            float g = radianceOut[radIdx + 1];
            float b = radianceOut[radIdx + 2];
            float conf = metaOut[metaIdx + 0];
            float flagsF = metaOut[metaIdx + 1];

            uint flags = unchecked((uint)BitConverter.SingleToInt32Bits(flagsF));

            Assert.True((flags & LUMON_META_WORLDPROBE_FALLBACK) != 0u, "Expected WORLDPROBE_FALLBACK flag on a miss texel");
            Assert.True(conf >= (tracedAtlas is null ? 0.9f : 0.24f), $"Expected high confidence from world-probe fallback, got {conf:F3}");

            // The selected first probe tile is red; the other tiles are blue and sky fallback is green.
            if (tracedAtlas is null)
                Assert.True(r > 0.8f && g < 0.2f && b < 0.2f, $"Expected red first-probe radiance, got ({r:F3}, {g:F3}, {b:F3})");
            else
            {
                // All directions are actual CPU hit results. Equal corner confidence makes the
                // expected blend exactly the exterior corner weight, with no sky contribution.
                for (int i = 0; i < radianceOut.Length; i += 4)
                {
                    for (int channel = 0; channel < 3; channel++)
                        Assert.InRange(radianceOut[i + channel], expectedRadiance - 0.002f, expectedRadiance + 0.002f);
                    Assert.True(metaOut[i / 2] > 0.24f);
                    Assert.True((unchecked((uint)BitConverter.SingleToInt32Bits(metaOut[i / 2 + 1])) & LUMON_META_WORLDPROBE_FALLBACK) != 0);
                }
            }

            // Suppressing accepted world radiance must preserve visibility and validity, rather
            // than taking the bright green sky fallback or changing the ray's hit distance.
            probeParams.SuppressWorldProbeRadiance = true;
            probeParamsBuffer.UploadAndBind(probeParams.Bytes);
            TestFramework.RenderQuadTo(programId, output);
            var suppressedRadiance = output[0].ReadPixels();
            var suppressedMeta = output[1].ReadPixels();
            Assert.Equal(metaOut, suppressedMeta);
            for (int i = 0; i < radianceOut.Length; i += 4)
            {
                Assert.Equal(0f, suppressedRadiance[i]);
                Assert.Equal(0f, suppressedRadiance[i + 1]);
                Assert.Equal(0f, suppressedRadiance[i + 2]);
                Assert.Equal(radianceOut[i + 3], suppressedRadiance[i + 3]);
            }
            foreach (bool sh9 in new[] { false, true })
            {
                AssertPairedHistoryReachesGather(radianceOut, suppressedRadiance, metaOut, sh9, expectedRadiance != 0);
            }
        }
        finally
        {
            if (programId != 0) GL.DeleteProgram(programId);
        }
    }
}
