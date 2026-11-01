using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using System.Numerics;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.LumOn.Shaders;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;
using VanillaGraphicsExpanded.Tests.GPU.Assertions;
using VanillaGraphicsExpanded.Rendering.RHI;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Follows accepted world samples through independent temporal histories and both gather modes.</summary>
public partial class LumOnProbeAtlasTraceWorldProbeFallbackFunctionalTests
{
    #region Paired Pipeline
    /// <summary>Processes actual trace outputs for two frames with separate history textures.</summary>
    private void AssertPairedHistoryReachesGather(ShaderLightingResources normalBranch, ShaderLightingResources suppressedBranch, GpuTexture normalTrace, GpuTexture suppressedTrace, GpuTexture metadata, bool sh9, bool expectLighting = true)
    {
        {
            var temporal = Programs.Create<LumOnScreenProbeAtlasTemporalShaderProgram>(shader => shader.TexelsPerFrame = 64);
            var filter = Programs.Create<LumOnScreenProbeAtlasFilterShaderProgram>();
            var project = Programs.Create<LumOnScreenProbeAtlasProjectSh9ShaderProgram>();
            var shGather = sh9 ? Programs.Create<LumOnProbeSh9GatherShaderProgram>() : null;
            var atlasGather = !sh9 ? Programs.Create<LumOnScreenProbeAtlasGatherShaderProgram>() : null;
            VanillaGraphicsExpanded.LumOn.Shaders.LumOnShaderProgram gather = shGather ?? (VanillaGraphicsExpanded.LumOn.Shaders.LumOnShaderProgram)atlasGather!;
            // Share authored guides, but retain separate screen/history owners for the two branches.
            var inputs = normalBranch.EnsureScreen(ScreenWidth, ScreenHeight, ProbeSpacing);
            normalBranch.Scene.EnsureSize(ScreenWidth, ScreenHeight);
            var terrain = normalBranch.Scene.Engine;
            var guides = normalBranch.Scene.Terrain;
            var anchors = inputs.ProbeAnchors!;
            anchors.UploadDataImmediate(CreateUniformData(ProbeGridWidth, ProbeGridHeight, 4, 0, 0, -5, 1), 0, 0, 0, ProbeGridWidth, ProbeGridHeight, 1);
            anchors.UploadDataImmediate(CreateUniformData(ProbeGridWidth, ProbeGridHeight, 4, .5f, .5f, 1, 0), 0, 0, 1, ProbeGridWidth, ProbeGridHeight, 1);
            var normals = guides.Surface;
            normals.UploadDataImmediate(CreateUniformData(ScreenWidth, ScreenHeight, 4, 0.5f, 0.5f, 1, 0), 0, 0, 0, ScreenWidth, ScreenHeight, 1);
            // Project the accepted anchor at z=-5 into hardware depth.
            float depthValue = ZFar / (ZFar - ZNear) - ZFar * ZNear / ((ZFar - ZNear) * 5f);
            var depth = terrain.Depth;
            depth.UploadDataImmediate(CreateUniformData(ScreenWidth, ScreenHeight, 1, depthValue));
            var meta = inputs.ScreenProbeAtlasMetaHistoryTex!;
            var region=new RHITextureRegion(new(0),0,0,0,AtlasWidth,AtlasHeight,1);
            RHICommandContext.Current.CopyTexture(metadata,region,meta,region);
            // Each branch owns its own temporal outputs. Frame two reads only its own first frame.
            GpuTexture[] gathered = new GpuTexture[2];
            for (int branch = 0; branch < 2; branch++)
            {
                var resources = branch == 0 ? normalBranch : suppressedBranch;
                var buffers = resources.EnsureScreen(ScreenWidth, ScreenHeight, ProbeSpacing);
                var trace = buffers.ScreenProbeAtlasHistoryTex!;
                RHICommandContext.Current.CopyTexture(branch==0?normalTrace:suppressedTrace,region,trace,region);
                var first = buffers.ScreenProbeAtlasCurrentPass!;
                var second = buffers.ScreenProbeAtlasTracePass!;
                var filtered = buffers.ScreenProbeAtlasFilteredPass!;
                var projected = buffers.ProbeSh9Pass!;
                var output = buffers.IndirectHalfPass!;
                for (int frame = 0; frame < 2; frame++)
                {
                    UpdateAndBindLumOnFrameUbo(temporal, frameIndex: frame, historyValid: frame, enableVelocityReprojection: 0);
                    temporal.ScreenProbeAtlasCurrent = frame == 0 ? trace : first.Colors[0].Image!.Value.Texture;
                    temporal.ScreenProbeAtlasHistory = frame == 0 ? trace : first.Colors[0].Image!.Value.Texture;
                    temporal.ScreenProbeAtlasMetaCurrent = frame == 0 ? meta : first.Colors[1].Image!.Value.Texture;
                    temporal.ScreenProbeAtlasMetaHistory = frame == 0 ? meta : first.Colors[1].Image!.Value.Texture;
                    temporal.ProbeAnchors = anchors;
                    temporal.PmjJitter = GetOrCreatePmjJitterTexture(1);
                    temporal.VelocityTex = buffers.VelocityTex;
                    temporal.TemporalAlpha = .9f;
                    temporal.HitDistanceRejectThreshold = .3f;
                    TestFramework.RenderQuadTo(temporal, frame == 0 ? first : second);
                }
                {
                    UpdateAndBindLumOnFrameUbo(filter);
                    filter.ScreenProbeAtlas = second.Colors[0].Image!.Value.Texture; filter.ScreenProbeAtlasMeta = second.Colors[1].Image!.Value.Texture; filter.ProbeAnchors = anchors;
                    filter.FilterRadius = 1; filter.HitDistanceSigma = 1;
                    TestFramework.RenderQuadTo(filter, filtered);
                }
                if (sh9)
                {
                    UpdateAndBindLumOnFrameUbo(project);
                    project.ScreenProbeAtlas = filtered.Colors[0].Image!.Value.Texture; project.ScreenProbeAtlasMeta = second.Colors[1].Image!.Value.Texture; project.ProbeAnchors = anchors;
                    TestFramework.RenderQuadTo(project, projected);
                }
                {
                    UpdateAndBindLumOnFrameUbo(gather, invProjectionMatrix: LumOnTestInputFactory.CreateRealisticInverseProjection());
                    if (shGather != null)
                    {
                        shGather.ProbeAnchors = anchors;
                        shGather.PrimaryDepth = depth; shGather.GBufferSurface = normals;
                        shGather.ProbeSh9 = buffers.ProbeSh9;
                        shGather.Intensity = 1; shGather.IndirectTint = [1,1,1];
                    }
                    else
                    {
                        atlasGather!.ScreenProbeAtlas = filtered.Colors[0].Image!.Value.Texture;
                        atlasGather.ProbeAnchors = anchors;
                        atlasGather.PrimaryDepth = depth; atlasGather.GBufferSurface = normals;
                        atlasGather.Intensity = 1; atlasGather.IndirectTint = [1,1,1];
                        atlasGather.LeakThreshold = .5f; atlasGather.SampleStride = 1;
                    }
                    TestFramework.RenderQuadTo(gather, output);
                }
                gathered[branch] = output.Colors[0].Image!.Value.Texture.Resource as GpuTexture ?? throw new InvalidOperationException("Expected fixture-owned gather storage.");
            }
            ImageAssertions.Predicate(gathered[0],"""
                precise float brightness=(a.r+a.g)+a.b;
                bool color=multipliers.x==1?brightness>.1:all(greaterThanEqual(a.rgb,vec3(-.000001))) && all(lessThanEqual(a.rgb,vec3(.000001)));
                return bvec2(color && a.a>.1 && same(a,b,8u) && all(equal(b.rgb,vec3(0))),false);
                """,gathered[1],parameters:new(expectLighting?1:0,0,0,0)).AssertEqual();
            var normalOutput = normalBranch.EnsureScreen(ScreenWidth, ScreenHeight, ProbeSpacing).IndirectHalfTex!;
            var suppressedOutput = suppressedBranch.EnsureScreen(ScreenWidth, ScreenHeight, ProbeSpacing).IndirectHalfTex!;
            suppressedBranch.Scene.EnsureSize(HalfResWidth, HalfResHeight);
            AssertGatherLightingEffect(normalOutput, suppressedOutput, suppressedBranch.Scene.Engine.Output,
                expectLighting);
        }
    }

    #endregion
}
