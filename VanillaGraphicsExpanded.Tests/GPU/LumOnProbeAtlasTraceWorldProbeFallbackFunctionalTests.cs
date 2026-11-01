using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.LumOn;
using System;
using System.Collections.Generic;
using System.Globalization;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.LumOn.Shaders;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;
using VanillaGraphicsExpanded.Tests.GPU.Assertions;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.GPU;

[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public partial class LumOnProbeAtlasTraceWorldProbeFallbackFunctionalTests : LumOnShaderFunctionalTestBase
{
    private const uint LUMON_META_WORLDPROBE_FALLBACK = 1u << 5;

    private const int WorldProbeTileSize = 16;

    private const int RaySteps = 16;
    private const float RayMaxDistance = 50f;
    private const float RayThickness = 0.5f;
    private const float SkyMissWeight = 1.0f;

    public LumOnProbeAtlasTraceWorldProbeFallbackFunctionalTests(HeadlessGLFixture fixture) : base(fixture) { }

    private LumOnScreenProbeAtlasTraceShaderProgram CompileProbeAtlasTraceShaderWithWorldProbeFallback(int wpLevels, int wpResolution, float wpBaseSpacing)
        => Programs.Create<LumOnScreenProbeAtlasTraceShaderProgram>(settings: new Dictionary<string, string?>
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
        float spacing = 1f, float anchorOffsetX = 0f, float expectedRadiance = -1f,
        float minimumConfidence = 0.20f, bool runPipeline = true, bool ringShift = false, bool emptyCoarseLevel = false)
    {
        EnsureShaderTestAvailable();

        // 1 level, 2x2x2 clipmap => per-probe scalar atlas width=4, height=2
        int wpLevels = emptyCoarseLevel ? 2 : 1;
        const int wpResolution = 2;
        float wpBaseSpacing = spacing;

        const int wpScalarAtlasWidth = wpResolution * wpResolution;
        int wpScalarAtlasHeight = wpResolution * wpLevels;

        // Radiance atlas is tile-packed: W = (N*N)*S, H = (N*levels)*S.
        const int wpRadianceAtlasWidth = (wpResolution * wpResolution) * WorldProbeTileSize;
        int wpRadianceAtlasHeight = (wpResolution * wpLevels) * WorldProbeTileSize;

        // The first physical probe tile is red; every other tile is blue. The anchor below maps exactly
        // to this first tile only when the shader uses player-relative surface coordinates directly.
        var wpRadianceAtlas = tracedAtlas?.Radiance ?? CreateRadianceAtlasWithRedFirstProbeTile(wpRadianceAtlasWidth, wpRadianceAtlasHeight);

        // vis0.xy = any octUV, vis0.z = skyIntensity, vis0.w = aoConf (not used for radiance fallback).
        var wpVis0 = tracedAtlas?.Visibility ?? CreateUniformData(wpScalarAtlasWidth, wpScalarAtlasHeight, 4, 0.5f, 1.0f, 0f, 0f);

        // meta0.r = confidence, meta0.g = flags-as-float (ignored here)
        var wpMeta0 = tracedAtlas?.Metadata ?? CreateUniformData(wpScalarAtlasWidth, wpScalarAtlasHeight, 2, 1.0f, 0f);

        if (emptyCoarseLevel && tracedAtlas is not null)
        {
            // Append a wholly unavailable coarser level without altering the fine data.
            Array.Resize(ref wpRadianceAtlas, wpRadianceAtlasWidth * wpRadianceAtlasHeight * 4);
            Array.Resize(ref wpVis0, wpScalarAtlasWidth * wpScalarAtlasHeight * 4);
            Array.Resize(ref wpMeta0, wpScalarAtlasWidth * wpScalarAtlasHeight * 2);
        }

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

        using var anchorPosTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, TextureFormat.Rgba16f, anchorPos);
        using var anchorNormalTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, TextureFormat.Rgba16f, anchorNormal);
        using var depthTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, TextureFormat.R32f, depth);
        using var directTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, TextureFormat.Rgba16f, direct);
        using var emissiveTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, TextureFormat.Rgba16f, emissive);
        using var historyAtlasTex = TestFramework.CreateTexture(AtlasWidth, AtlasHeight, TextureFormat.Rgba16f, historyAtlas);
        using var historyMetaTex = TestFramework.CreateTexture(AtlasWidth, AtlasHeight, TextureFormat.Rg32f, historyMeta);
        using var hzbTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, TextureFormat.R32f, hzb);

        using var wpRadianceAtlasTex = TestFramework.CreateTexture(wpRadianceAtlasWidth, wpRadianceAtlasHeight, TextureFormat.Rgba16f, wpRadianceAtlas);
        using var wpVis0Tex = TestFramework.CreateTexture(wpScalarAtlasWidth, wpScalarAtlasHeight, TextureFormat.Rgba16f, wpVis0);
        using var wpMeta0Tex = TestFramework.CreateTexture(wpScalarAtlasWidth, wpScalarAtlasHeight, TextureFormat.Rg32f, wpMeta0);

        using var output = TestFramework.CreateTestGBuffer(AtlasWidth, AtlasHeight, TextureFormat.Rgba16f, TextureFormat.Rg32f);

        var programId = CompileProbeAtlasTraceShaderWithWorldProbeFallback(wpLevels, wpResolution, wpBaseSpacing);
        {

            // Bind textures to fixed units
            using var anchorInputs1 = LayeredTestTexture.Create(anchorPosTex, anchorNormalTex);
            programId.ProbeAnchors = anchorInputs1;
            programId.PrimaryDepth = depthTex;
            programId.SurfaceAlbedo = directTex;
            using var surfaceInput1 = LayeredTestTexture.Create(null, emissiveTex, null);
            programId.GBufferSurface = surfaceInput1;
            programId.ScreenProbeAtlasHistory = historyAtlasTex;
            programId.HzbDepth = hzbTex;
            programId.ScreenProbeAtlasMetaHistory = historyMetaTex;

            programId.WorldProbeRadianceAtlas = wpRadianceAtlasTex;
            programId.WorldProbeVis0 = wpVis0Tex;
            programId.WorldProbeMeta0 = wpMeta0Tex;

            var invProjection = LumOnTestInputFactory.CreateRealisticInverseProjection();
            var projection = LumOnTestInputFactory.CreateRealisticProjection();
            var view = LumOnTestInputFactory.CreateIdentityView();
            var invView = LumOnTestInputFactory.CreateIdentityMatrix();

            // Sampler units (must match binds above)

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
                originMinCorner: [new System.Numerics.Vector3(-spacing * 0.5f, -spacing * 0.5f, -5f - spacing * 0.5f), new System.Numerics.Vector3(-spacing, -spacing, -5f - spacing)],
                ringOffset: [new System.Numerics.Vector3(ringShift ? 1f : 0f, 0f, 0f), System.Numerics.Vector3.Zero]);

            programId.IndirectTint = new(1,1,1);
            programId.SuppressWorldProbeRadiance = false;
            TestFramework.RenderQuadTo(programId, output);

            using var radianceOut = new GpuImageSnapshot(output[0]);
            using var metaOut = new GpuImageSnapshot(output[1]);

            // Validate a single texel (0,0): should be world-probe fallback, not sky.
            // radianceOut is RGBA, metaOut is RG (confidence, flagsBitsAsFloat).
            float[] selected=output[0].ReadPixelsRegion(0,0,1,1);
            float[] selectedMeta=output[1].ReadPixelsRegion(0,0,1,1);
            float r=selected[0],g=selected[1],b=selected[2],conf=selectedMeta[0],flagsF=selectedMeta[1];
            uint flags = unchecked((uint)BitConverter.SingleToInt32Bits(flagsF));

            Assert.True((flags & LUMON_META_WORLDPROBE_FALLBACK) != 0u, "Expected WORLDPROBE_FALLBACK flag on a miss texel");
            Assert.True(conf >= (tracedAtlas is null ? 0.9f : minimumConfidence), $"Expected high confidence from world-probe fallback, got {conf:F3}");

            // The selected first probe tile is red; the other tiles are blue and sky fallback is green.
            if (tracedAtlas is null)
                Assert.True(r > 0.8f && g < 0.2f && b < 0.2f, $"Expected red first-probe radiance, got ({r:F3}, {g:F3}, {b:F3})");
            else
            {
                var accepted=ImageAssertions.Predicate(radianceOut.Texture,"""
                    bool color=all(greaterThanEqual(a.rgb,vec3(multipliers.x))) && all(lessThanEqual(a.rgb,vec3(multipliers.y)));
                    bool confidence=b.r>=multipliers.z && (multipliers.z!=0 || b.r==0);
                    return bvec2(color && confidence && (floatBitsToUint(b.g)&32u)!=0u,false);
                    """,metaOut.Texture,parameters:new(expectedRadiance==-2?0:expectedRadiance-.002f,
                        expectedRadiance==-2?1.002f:expectedRadiance+.002f,minimumConfidence,0));
                accepted.AssertEqual();
            }
            if(expectedRadiance==-2)
            {
                var lit=ImageAssertions.Predicate(radianceOut.Texture,"return bvec2(true,a.r>.001);");
                lit.AssertEqual();Assert.True(lit.MatchingTexels>0);
            }
            // Suppressing accepted world radiance must preserve visibility and validity, rather
            // than taking the bright green sky fallback or changing the ray's hit distance.
            programId.SuppressWorldProbeRadiance = true;
            TestFramework.RenderQuadTo(programId, output);
            using var suppressedRadiance=new GpuImageSnapshot(output[0]);
            using var suppressedMeta=new GpuImageSnapshot(output[1]);
            ImageAssertions.CompareHistory(suppressedMeta.Texture,metaOut).AssertEqual();
            ImageAssertions.CompareConstant(suppressedRadiance.Texture,0,channelMask:7).AssertEqual();
            ImageAssertions.CompareHistory(suppressedRadiance.Texture,radianceOut,channelMask:8).AssertEqual();
            using var normalBranch = new ShaderLightingResources(Programs.Api);
            using var suppressedBranch = new ShaderLightingResources(Programs.Api);
            foreach (bool sh9 in runPipeline ? new[] { false, true } : Array.Empty<bool>())
            {
                AssertPairedHistoryReachesGather(normalBranch, suppressedBranch, radianceOut.Texture, suppressedRadiance.Texture, metaOut.Texture, sh9, expectedRadiance != 0);
            }
        }
    }
}
