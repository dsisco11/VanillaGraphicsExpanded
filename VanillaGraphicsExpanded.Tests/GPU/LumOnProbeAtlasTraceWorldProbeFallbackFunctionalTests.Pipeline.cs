using System.Numerics;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.LumOn.Shaders;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Follows accepted world samples through independent temporal histories and both gather modes.</summary>
public partial class LumOnProbeAtlasTraceWorldProbeFallbackFunctionalTests
{
    #region Paired Pipeline
    /// <summary>Processes actual trace outputs for two frames with separate history textures.</summary>
    private void AssertPairedHistoryReachesGather(float[] normalTrace, float[] suppressedTrace, float[] metadata, bool sh9, bool expectLighting = true)
    {
        var programs = new List<int>();
        try
        {
            int temporal = CompileShaderWithDefines("lumon_probe_atlas_temporal.vsh", "lumon_probe_atlas_temporal.fsh",
                new Dictionary<string, string?> { ["VGE_LUMON_ATLAS_TEXELS_PER_FRAME"] = "64" });
            programs.Add(temporal);
            int filter = CompileShader("lumon_probe_atlas_filter.vsh", "lumon_probe_atlas_filter.fsh");
            programs.Add(filter);
            int project = CompileShader("lumon_probe_atlas_project_sh9.vsh", "lumon_probe_atlas_project_sh9.fsh");
            programs.Add(project);
            int gather = CompileShader(sh9 ? "lumon_probe_sh9_gather.vsh" : "lumon_probe_atlas_gather.vsh",
                sh9 ? "lumon_probe_sh9_gather.fsh" : "lumon_probe_atlas_gather.fsh");
            programs.Add(gather);

            using var anchors = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f,
                CreateUniformData(ProbeGridWidth, ProbeGridHeight, 4, 0, 0, -5, 1));
            using var normals = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f,
                CreateUniformData(ScreenWidth, ScreenHeight, 4, 0.5f, 0.5f, 1, 0));
            // Standard perspective projection maps the accepted anchor's z=-5 to this hardware depth.
            float depthValue = ZFar / (ZFar - ZNear) - ZFar * ZNear / ((ZFar - ZNear) * 5f);
            using var depth = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.R32f,
                CreateUniformData(ScreenWidth, ScreenHeight, 1, depthValue));
            using var meta = TestFramework.CreateTexture(AtlasWidth, AtlasHeight, PixelInternalFormat.Rg32f, metadata);
            using var paramsBuffer = new ObjectParamsUbo("Tests.WorldProbePairedPipeline");
            var parameters = new LumOnProbeParamsUbo
            {
                TemporalAlpha = 0.9f, HitDistanceRejectThreshold = 0.3f,
                FilterRadius = 1, HitDistanceSigma = 1f, Intensity = 1f,
                IndirectTint = Vector3.One, LeakThreshold = 0.5f, SampleStride = 1
            };
            paramsBuffer.UploadAndBind(parameters.Bytes);
            foreach (int program in programs)
                UniformBlockBindingUtil.EnsureBlockBound(program, LumOnProbeParamsUbo.BlockName, GpuBindingRegistry.Ubo.Object);

            // Each branch owns its own temporal outputs. Frame two reads only its own first frame.
            float[][] gathered = new float[2][];
            for (int branch = 0; branch < 2; branch++)
            {
                using var trace = TestFramework.CreateTexture(AtlasWidth, AtlasHeight, PixelInternalFormat.Rgba16f,
                    branch == 0 ? normalTrace : suppressedTrace);
                using var first = TestFramework.CreateTestGBuffer(AtlasWidth, AtlasHeight, PixelInternalFormat.Rgba16f, PixelInternalFormat.Rg32f);
                using var second = TestFramework.CreateTestGBuffer(AtlasWidth, AtlasHeight, PixelInternalFormat.Rgba16f, PixelInternalFormat.Rg32f);
                using var filtered = TestFramework.CreateTestGBuffer(AtlasWidth, AtlasHeight, PixelInternalFormat.Rgba16f);
                using var projected = TestFramework.CreateTestGBuffer(ProbeGridWidth, ProbeGridHeight,
                    Enumerable.Repeat(PixelInternalFormat.Rgba16f, 7).ToArray());
                using var output = TestFramework.CreateTestGBuffer(HalfResWidth, HalfResHeight, PixelInternalFormat.Rgba16f);

                for (int frame = 0; frame < 2; frame++)
                {
                    UpdateAndBindLumOnFrameUbo(temporal, frameIndex: frame, historyValid: frame, enableVelocityReprojection: 0);
                    if (frame == 0) { trace.Bind(0); trace.Bind(1); meta.Bind(3); meta.Bind(4); }
                    else { first[0].Bind(0); first[0].Bind(1); first[1].Bind(3); first[1].Bind(4); }
                    anchors.Bind(2);
                    BindPipelineSamplers(temporal, ("octahedralCurrent", 0), ("octahedralHistory", 1),
                        ("probeAnchorPosition", 2), ("probeAtlasMetaCurrent", 3), ("probeAtlasMetaHistory", 4));
                    TestFramework.RenderQuadTo(temporal, frame == 0 ? first : second);
                }
                UpdateAndBindLumOnFrameUbo(filter);
                second[0].Bind(0); second[1].Bind(1); anchors.Bind(2);
                BindPipelineSamplers(filter, ("octahedralAtlas", 0), ("probeAtlasMeta", 1), ("probeAnchorPosition", 2));
                TestFramework.RenderQuadTo(filter, filtered);
                if (sh9)
                {
                    UpdateAndBindLumOnFrameUbo(project);
                    filtered[0].Bind(0); second[1].Bind(1); anchors.Bind(2);
                    BindPipelineSamplers(project, ("octahedralAtlas", 0), ("probeAtlasMeta", 1), ("probeAnchorPosition", 2));
                    TestFramework.RenderQuadTo(project, projected);
                }
                UpdateAndBindLumOnFrameUbo(gather, invProjectionMatrix: LumOnTestInputFactory.CreateRealisticInverseProjection());
                anchors.Bind(7); normals.Bind(8); depth.Bind(9); normals.Bind(10);
                BindPipelineSamplers(gather, ("probeAnchorPosition", 7), ("probeAnchorNormal", 8), ("primaryDepth", 9), ("gBufferNormal", 10));
                if (sh9)
                {
                    for (int i = 0; i < 7; i++) { projected[i].Bind(i); BindPipelineSamplers(gather, ($"probeSh{i}", i)); }
                }
                else { filtered[0].Bind(0); BindPipelineSamplers(gather, ("octahedralAtlas", 0)); }
                TestFramework.RenderQuadTo(gather, output);
                gathered[branch] = output[0].ReadPixels();
            }
            for (int i = 0; i < gathered[0].Length; i += 4)
            {
                if (expectLighting)
                    Assert.True(gathered[0][i] + gathered[0][i + 1] + gathered[0][i + 2] > 0.1f,
                        $"World trace lighting was lost before {(sh9 ? "SH9" : "atlas")} gather");
                else
                    for (int channel = 0; channel < 3; channel++)
                        Assert.InRange(gathered[0][i + channel], -1e-6f, 1e-6f);
                Assert.True(gathered[0][i + 3] > 0.1f, "Screen gather must be well above the world-fallback threshold");
                Assert.Equal(gathered[0][i + 3], gathered[1][i + 3]);
                for (int channel = 0; channel < 3; channel++) Assert.Equal(0f, gathered[1][i + channel]);
            }
            AssertGatherLightingEffect(gathered[0], gathered[1], expectLighting);
        }
        finally { foreach (int program in programs) global::VanillaGraphicsExpanded.Tests.GPU.Helpers.TestShaderInterfaces.DeleteProgram(program); }
    }

    /// <summary>Binds named samplers for a production shader stage.</summary>
    private static void BindPipelineSamplers(int program, params (string name, int unit)[] samplers)
    {
        GL.UseProgram(program);
        foreach (var (name, unit) in samplers) GL.Uniform1(global::VanillaGraphicsExpanded.Tests.GPU.Helpers.TestShaderInterfaces.GetUniformLocation(program, name), unit);
        GL.UseProgram(0);
    }
    #endregion
}
