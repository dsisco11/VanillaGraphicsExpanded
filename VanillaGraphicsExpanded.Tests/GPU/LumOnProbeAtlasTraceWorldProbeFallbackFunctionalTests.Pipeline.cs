using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
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
        {
            var temporal = Programs.Create<LumOnScreenProbeAtlasTemporalShaderProgram>(shader => shader.TexelsPerFrame = 64);
            var filter = Programs.Create<LumOnScreenProbeAtlasFilterShaderProgram>();
            var project = Programs.Create<LumOnScreenProbeAtlasProjectSh9ShaderProgram>();
            var shGather = sh9 ? Programs.Create<LumOnProbeSh9GatherShaderProgram>() : null;
            var atlasGather = !sh9 ? Programs.Create<LumOnScreenProbeAtlasGatherShaderProgram>() : null;
            VanillaGraphicsExpanded.LumOn.Shaders.LumOnShaderProgram gather = shGather ?? (VanillaGraphicsExpanded.LumOn.Shaders.LumOnShaderProgram)atlasGather!;
            using var assets = new BinaryShaderApiFixture();
            var config = new VgeConfig(); config.LumOn.ProbeSpacingPx = ProbeSpacing;
            using var inputs = new LumOnBufferManager(assets.Api, config);
            inputs.EnsureBuffers(ScreenWidth, ScreenHeight);
            using var terrain = new EngineTerrainBuffers(ScreenWidth, ScreenHeight);
            using var guides = new GBufferTextures(ScreenWidth, ScreenHeight);
            var anchors = inputs.ProbeAnchorPositionTex!;
            anchors.UploadDataImmediate(CreateUniformData(ProbeGridWidth, ProbeGridHeight, 4, 0, 0, -5, 1));
            var normals = guides.Normal;
            normals.UploadDataImmediate(CreateUniformData(ScreenWidth, ScreenHeight, 4, 0.5f, 0.5f, 1, 0));
            // Project the accepted anchor at z=-5 into hardware depth.
            float depthValue = ZFar / (ZFar - ZNear) - ZFar * ZNear / ((ZFar - ZNear) * 5f);
            var depth = terrain.Depth;
            depth.UploadDataImmediate(CreateUniformData(ScreenWidth, ScreenHeight, 1, depthValue));
            var meta = inputs.ScreenProbeAtlasMetaHistoryTex!;
            meta.UploadDataImmediate(metadata);
            // Each branch owns its own temporal outputs. Frame two reads only its own first frame.
            float[][] gathered = new float[2][];
            for (int branch = 0; branch < 2; branch++)
            {
                using var buffers = new LumOnBufferManager(assets.Api, config);
                buffers.EnsureBuffers(ScreenWidth, ScreenHeight);
                var trace = buffers.ScreenProbeAtlasHistoryTex!;
                trace.UploadDataImmediate(branch == 0 ? normalTrace : suppressedTrace);
                var first = buffers.ScreenProbeAtlasCurrentFbo!;
                var second = buffers.ScreenProbeAtlasTraceFbo!;
                var filtered = buffers.ScreenProbeAtlasFilteredFbo!;
                var projected = buffers.ProbeSh9Fbo!;
                var output = buffers.IndirectHalfFbo!;
                for (int frame = 0; frame < 2; frame++)
                {
                    using var use = temporal.UseScope();
                    UpdateAndBindLumOnFrameUbo(temporal, frameIndex: frame, historyValid: frame, enableVelocityReprojection: 0);
                    temporal.ScreenProbeAtlasCurrent = frame == 0 ? trace : first[0];
                    temporal.ScreenProbeAtlasHistory = frame == 0 ? trace : first[0];
                    temporal.ScreenProbeAtlasMetaCurrent = frame == 0 ? meta : first[1];
                    temporal.ScreenProbeAtlasMetaHistory = frame == 0 ? meta : first[1];
                    temporal.ProbeAnchorPosition = anchors;
                    temporal.TemporalAlpha = .9f;
                    temporal.HitDistanceRejectThreshold = .3f;
                    TestFramework.RenderQuadTo(temporal, frame == 0 ? first : second);
                }
                using (filter.UseScope())
                {
                    UpdateAndBindLumOnFrameUbo(filter);
                    filter.ScreenProbeAtlas = second[0]; filter.ScreenProbeAtlasMeta = second[1]; filter.ProbeAnchorPosition = anchors;
                    filter.FilterRadius = 1; filter.HitDistanceSigma = 1;
                    TestFramework.RenderQuadTo(filter, filtered);
                }
                if (sh9)
                {
                    using var use = project.UseScope();
                    UpdateAndBindLumOnFrameUbo(project);
                    project.ScreenProbeAtlas = filtered[0]; project.ScreenProbeAtlasMeta = second[1]; project.ProbeAnchorPosition = anchors;
                    TestFramework.RenderQuadTo(project, projected);
                }
                using (gather.UseScope())
                {
                    UpdateAndBindLumOnFrameUbo(gather, invProjectionMatrix: LumOnTestInputFactory.CreateRealisticInverseProjection());
                    if (shGather != null)
                    {
                        shGather.ProbeAnchorPosition = anchors; shGather.ProbeAnchorNormal = normals;
                        shGather.PrimaryDepth = depth.TextureId; shGather.GBufferNormal = normals.TextureId;
                        shGather.ProbeSh0 = projected[0]; shGather.ProbeSh1 = projected[1]; shGather.ProbeSh2 = projected[2];
                        shGather.ProbeSh3 = projected[3]; shGather.ProbeSh4 = projected[4]; shGather.ProbeSh5 = projected[5]; shGather.ProbeSh6 = projected[6];
                        shGather.Intensity = 1; shGather.IndirectTint = [1,1,1];
                    }
                    else
                    {
                        atlasGather!.ScreenProbeAtlas = filtered[0];
                        atlasGather.ProbeAnchorPosition = anchors; atlasGather.ProbeAnchorNormal = normals;
                        atlasGather.PrimaryDepth = depth.TextureId; atlasGather.GBufferNormal = normals.TextureId;
                        atlasGather.Intensity = 1; atlasGather.IndirectTint = [1,1,1];
                        atlasGather.LeakThreshold = .5f; atlasGather.SampleStride = 1;
                    }
                    TestFramework.RenderQuadTo(gather, output);
                }
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
    }

    #endregion
}
