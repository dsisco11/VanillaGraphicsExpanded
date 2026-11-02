using VanillaGraphicsExpanded.Tests.GPU.Assertions;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.LumOn;
using System.Collections.Generic;

using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.GPU;

[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
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

        using var primaryDepth = TestFramework.CreateTexture(w, h, TextureFormat.R32f, baseDepth);

        using var hierarchy=new DepthHierarchyPass();
        using var draw=new VanillaGraphicsExpanded.PBR.Postprocessing.PostprocessDraw();
        hierarchy.Prepare(w,h,Programs.CreateDepthHierarchy());
        hierarchy.Render(primaryDepth);
        var hzb=hierarchy.Texture!;
        using var mip0=new GpuImageSnapshot(hzb,GpuImageSnapshot.Mip(hzb,0));
        using var mip1=new GpuImageSnapshot(hzb,GpuImageSnapshot.Mip(hzb,1));
        using var mip2=new GpuImageSnapshot(hzb,GpuImageSnapshot.Mip(hzb,2));

        ImageAssertions.Predicates.Predicate(mip1.Texture, SurfacePredicates.LumOnHzbFunctionalTests_HZB_BuildProducesMonotonicDepthMips,mip0.Texture,parameters:new(Epsilon,0,0,0),mappedSources:true).AssertEqual();
        ImageAssertions.Predicates.Predicate(mip2.Texture, SurfacePredicates.LumOnHzbFunctionalTests_HZB_BuildProducesMonotonicDepthMips,mip1.Texture,parameters:new(Epsilon,0,0,0),mappedSources:true).AssertEqual();
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

        using var primaryDepth = TestFramework.CreateTexture(screenW, screenH, TextureFormat.R32f, depthData);
        using var primaryColor = TestFramework.CreateTexture(screenW, screenH, TextureFormat.Rgba16f, colorData);
        using var material = TestFramework.CreateTexture(screenW, screenH, TextureFormat.Rgba16f, new float[screenW * screenH * 4]);

        // Match existing test conventions: 2x2 probes, 16x16 atlas.
        var anchorPosData = CreateValidProbeAnchors();
        var anchorNormalData = CreateProbeNormalsUpward();
        var historyData = new float[AtlasWidth * AtlasHeight * 4];

        using var probePos = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, TextureFormat.Rgba16f, anchorPosData);
        using var probeNorm = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, TextureFormat.Rgba16f, anchorNormalData);
        using var history = TestFramework.CreateTexture(AtlasWidth, AtlasHeight, TextureFormat.Rgba16f, historyData);
        using var metaHistory = TestFramework.CreateTexture(AtlasWidth, AtlasHeight, TextureFormat.Rg32f, new float[AtlasWidth * AtlasHeight * 2]);

        using var outputAtlas = TestFramework.CreateTestGBuffer(AtlasWidth, AtlasHeight, TextureFormat.Rgba16f, TextureFormat.Rg32f);

        var progMip0 = Programs.Create<LumOnScreenProbeAtlasTraceShaderProgram>(settings: new Dictionary<string, string?>
            {
                ["VGE_LUMON_ATLAS_TEXELS_PER_FRAME"] = "64",
                ["VGE_LUMON_RAY_STEPS"] = "8",
                ["VGE_LUMON_RAY_MAX_DISTANCE"] = "2.0",
                ["VGE_LUMON_RAY_THICKNESS"] = "0.5",
                ["VGE_LUMON_SKY_MISS_WEIGHT"] = "0.0",
                ["VGE_LUMON_HZB_COARSE_MIP"] = "0"
            });
        var progMip1 = Programs.Create<LumOnScreenProbeAtlasTraceShaderProgram>(settings: new Dictionary<string, string?>
            {
                ["VGE_LUMON_ATLAS_TEXELS_PER_FRAME"] = "64",
                ["VGE_LUMON_RAY_STEPS"] = "8",
                ["VGE_LUMON_RAY_MAX_DISTANCE"] = "2.0",
                ["VGE_LUMON_RAY_THICKNESS"] = "0.5",
                ["VGE_LUMON_SKY_MISS_WEIGHT"] = "0.0",
                ["VGE_LUMON_HZB_COARSE_MIP"] = "1"
            });

        using var hierarchy=new DepthHierarchyPass();
        using var draw=new VanillaGraphicsExpanded.PBR.Postprocessing.PostprocessDraw();
        hierarchy.Prepare(screenW,screenH,Programs.CreateDepthHierarchy());
        hierarchy.Render(primaryDepth);
        var hzb=hierarchy.Texture!;
        float[] invProj = LumOnTestInputFactory.CreateRealisticInverseProjection();
        float[] proj = LumOnTestInputFactory.CreateRealisticProjection();
        float[] view = LumOnTestInputFactory.CreateIdentityView();
        float[] invView = LumOnTestInputFactory.CreateIdentityView();

        // Render with coarse mip 0
            using var anchorInputs1 = LayeredTestTexture.Create(ImageAssertions.Programs, probePos, probeNorm);
            progMip0.ProbeAnchors = anchorInputs1;
            progMip0.PrimaryDepth = primaryDepth;
            progMip0.SurfaceAlbedo = primaryColor;
            using var surfaceInput1 = LayeredTestTexture.Create(ImageAssertions.Programs, null, material, null);
            progMip0.GBufferSurface = surfaceInput1;
            progMip0.ScreenProbeAtlasMetaHistory = metaHistory;
            progMip0.ScreenProbeAtlasHistory = history;
            progMip0.HzbDepth = hzb;
            progMip0.IndirectTint = new(1,1,1);
            UpdateAndBindLumOnFrameUbo(progMip0, invProjectionMatrix: invProj, projectionMatrix: proj,
                viewMatrix: view, invViewMatrix: invView, sunPosition: new(0,1,0), sunColor: new(1,1,1));

        TestFramework.RenderQuadTo(progMip0, outputAtlas);
        var mip0 = outputAtlas[0].ReadPixelsRegion(8,8,1,1);

        TestFramework.RenderQuadTo(progMip0, outputAtlas);
        var outMip0 = outputAtlas[0].ReadPixelsRegion(8,8,1,1);

        // Render with a coarser mip (still uniform depth, should match)
        TestFramework.RenderQuadTo(progMip0, outputAtlas);
        var outMip0Second = outputAtlas[0].ReadPixelsRegion(8,8,1,1);

        // If the environment supports only 1 mip, this is equivalent; otherwise compare with mip 0 again.
        // In a uniform depth scene, using a coarser mip should not change results.
        float[] outCoarse;
        if (hzb.MipLevels > 1)
        {
            using var anchorInputs2 = LayeredTestTexture.Create(ImageAssertions.Programs, probePos, probeNorm);
            progMip1.ProbeAnchors = anchorInputs2;
            progMip1.PrimaryDepth = primaryDepth;
            progMip1.SurfaceAlbedo = primaryColor;
            using var surfaceInput2 = LayeredTestTexture.Create(ImageAssertions.Programs, null, material, null);
            progMip1.GBufferSurface = surfaceInput2;
            progMip1.ScreenProbeAtlasMetaHistory = metaHistory;
            progMip1.ScreenProbeAtlasHistory = history;
            progMip1.HzbDepth = hzb;
            progMip1.IndirectTint = new(1,1,1);
            UpdateAndBindLumOnFrameUbo(progMip1, invProjectionMatrix: invProj, projectionMatrix: proj,
                viewMatrix: view, invViewMatrix: invView, sunPosition: new(0,1,0), sunColor: new(1,1,1));

            TestFramework.RenderQuadTo(progMip1, outputAtlas);
            outCoarse = outputAtlas[0].ReadPixelsRegion(8,8,1,1);
        }
        else
        {
            outCoarse = outMip0;
        }

        int coarseMip = hzb.MipLevels > 1 ? 1 : 0;

        // Compare a single texel (center-ish of atlas)
        int idx=0; // Each observation retains only the original nominated centre tuple.
        for (int c = 0; c < 4; c++)
        {
            Assert.True(MathF.Abs(mip0[idx + c] - outMip0[idx + c]) < 1e-3f,
                $"Mismatch channel {c} (mip0): {mip0[idx + c]} vs {outMip0[idx + c]}");
            Assert.True(MathF.Abs(outMip0[idx + c] - outMip0Second[idx + c]) < 1e-3f,
                $"Non-determinism channel {c} (mip0 rerun): {outMip0[idx + c]} vs {outMip0Second[idx + c]}");
            Assert.True(MathF.Abs(outMip0[idx + c] - outCoarse[idx + c]) < 1e-3f,
                $"Mismatch channel {c} (coarseMip={coarseMip}): {outMip0[idx + c]} vs {outCoarse[idx + c]}");
        }

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
    #region Private
    /// <summary>Prepares this suite's declared observation programs before subject execution.</summary>
    private protected override void PrepareObservations(VanillaGraphicsExpanded.Tests.GPU.Assertions.GpuObservationPrograms programs)
    {
        base.PrepareObservations(programs);
        programs.Prepare("layer", 0, 0);
        programs.Prepare("predicate-surface", 0, 0);
    }
    #endregion
}
