using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.Rendering.Shaders;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Numerics;

using OpenTK.Graphics.OpenGL;


using VanillaGraphicsExpanded.LumOn.Shaders;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;

using Xunit;

namespace VanillaGraphicsExpanded.Tests.GPU;

[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class PbrLumOnFullPipelineIntegrationTests : LumOnShaderFunctionalTestBase
{
    public PbrLumOnFullPipelineIntegrationTests(HeadlessGLFixture fixture) : base(fixture) { }

    [Fact]
    public void Integration_OneFrame_FullPipeline_PropagatesIndirectIntoComposite()
    {
        EnsureShaderTestAvailable();

        // Binding audit: these tests are intended to verify correct program sampler wiring.
        // If a uniform gets optimized out, that's usually a sign the shader changed and the
        // test should be revisited rather than silently skipping.

        // Fixed matrices / params.
        float[] invProj = LumOnTestInputFactory.CreateRealisticInverseProjection();
        float[] proj = LumOnTestInputFactory.CreateRealisticProjection();
        float[] identity = LumOnTestInputFactory.CreateIdentityMatrix();

        // Inputs (deterministic).
        // Sentinel-ish: make primary scene color asymmetric so accidental swaps are easier to spot.
        var primarySceneData = PbrLumOnPipelineInputFactory.CreatePrimarySceneColorUniform(0.11f, 0.23f, 0.37f);
        var primaryDepthData = PbrLumOnPipelineInputFactory.CreatePrimaryDepthUniform(depthRaw: 0.5f);
        var gBufferAlbedoData = PbrLumOnPipelineInputFactory.CreateGBufferAlbedoUniform(0.61f, 0.42f, 0.19f);
        var gBufferNormalData = PbrLumOnPipelineInputFactory.CreateGBufferNormalEncodedUniform(0f, 0f, 1f);
        var gBufferMaterialData = PbrLumOnPipelineInputFactory.CreateGBufferMaterialUniform(
            roughness: 0.63f,
            metallic: 0.0f,
            emissiveScalar: 1.25f,
            reflectivity: 0.7f);

        var shadowNearData = PbrLumOnPipelineInputFactory.CreateShadowMapUniform(depthRaw: 1.0f);
        var shadowFarData = PbrLumOnPipelineInputFactory.CreateShadowMapUniform(depthRaw: 1.0f);

        // History inputs (fixed). We trace all texels so history should not materially matter,
        // but these must still be bound for shader completeness.
        var historyRadianceData = PbrLumOnPipelineInputFactory.CreateProbeAtlasHistoryRadianceUniform(
            r: 0.0f,
            g: 0.0f,
            b: 0.0f,
            encodedHitDistance: 0.0f);
        var historyMetaData = PbrLumOnPipelineInputFactory.CreateProbeAtlasHistoryMetaUniform(confidence: 0.0f, flags: 0);

        var zeroIndirectFullData = CreateUniformColorData(ScreenWidth, ScreenHeight, 0f, 0f, 0f, 1f);
        var injectedIndirectFullData = CreateUniformColorData(ScreenWidth, ScreenHeight, 0.2f, 0.2f, 0.2f, 1f);

        using var primaryScene = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f, primarySceneData);
        using var primaryDepth = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.R32f, primaryDepthData);
        using var gBufferAlbedo = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f, gBufferAlbedoData);
        using var gBufferNormal = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f, gBufferNormalData);
        using var gBufferMaterial = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f, gBufferMaterialData);

        using var shadowNear = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.R32f, shadowNearData);
        using var shadowFar = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.R32f, shadowFarData);

        using var historyRadiance = TestFramework.CreateTexture(AtlasWidth, AtlasHeight, PixelInternalFormat.Rgba16f, historyRadianceData);
        using var historyMeta = TestFramework.CreateTexture(AtlasWidth, AtlasHeight, PixelInternalFormat.Rg32f, historyMetaData);

        using var zeroIndirectFull = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f, zeroIndirectFullData);
        using var injectedIndirectFull = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f, injectedIndirectFullData);

        using var targets = new PbrLumOnPipelineTargets();
        using var baselineComposite = TestFramework.CreateTestGBuffer(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f);
        using var injectedComposite = TestFramework.CreateTestGBuffer(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f);

        {
            var pbrDirectProg = Programs.Create<PBRDirectLightingShaderProgram>();
            var velocityProg = Programs.Create<LumOnVelocityShaderProgram>();
            var hzbCopyProg = Programs.Create<LumOnHzbCopyShaderProgram>();
            var hzbDownProg = Programs.Create<LumOnHzbDownsampleShaderProgram>();
            var anchorProg = Programs.Create<LumOnProbeAnchorShaderProgram>();
                var traceProg = Programs.Create<LumOnScreenProbeAtlasTraceShaderProgram>(settings: new Dictionary<string, string?>
                    {
                        ["VGE_LUMON_ATLAS_TEXELS_PER_FRAME"] = "64",
                        ["VGE_LUMON_RAY_STEPS"] = "8",
                        ["VGE_LUMON_RAY_MAX_DISTANCE"] = "2.0",
                        ["VGE_LUMON_RAY_THICKNESS"] = "0.5",
                        ["VGE_LUMON_HZB_COARSE_MIP"] = "0",
                        ["VGE_LUMON_SKY_MISS_WEIGHT"] = "1.0"
                    });
                var temporalProg = Programs.Create<LumOnScreenProbeAtlasTemporalShaderProgram>(settings: new Dictionary<string, string?>
                    {
                        ["VGE_LUMON_ATLAS_TEXELS_PER_FRAME"] = "64"
                    });
            var filterProg = Programs.Create<LumOnScreenProbeAtlasFilterShaderProgram>();
            var gatherProg = Programs.Create<LumOnScreenProbeAtlasGatherShaderProgram>();
            var upsampleProg = Programs.Create<LumOnUpsampleShaderProgram>();
            var pbrCompositeProg = Programs.Create<PBRCompositeShaderProgram>();

            // -----------------------------------------------------------------
            // Stage: PBR Direct Lighting (MRT)
            // -----------------------------------------------------------------
            using var pbrDirectProgUse = pbrDirectProg.UseScope();

            // Directional light aligned with the test normal.

            // Shadow uniforms are present but currently not used by the fragment shader.
            // These calls safely no-op if uniforms are optimized out.

            // Phase 23: UBO-backed direct lighting params (VgePbrDirectLightingParamsUBO @ object binding).
            // The shader now aliases former uniform names to this UBO; without binding it, outputs will be black.

            {
                pbrDirectProg.InvProjectionMatrix = invProj;
                pbrDirectProg.InvModelViewMatrix = identity;

                // Shadows are wired but effectively disabled for this test.
                pbrDirectProg.ToShadowMapSpaceMatrixNear = identity;
                pbrDirectProg.ToShadowMapSpaceMatrixFar = identity;
                pbrDirectProg.ZPlanesAndShadowRanges = (zNear: ZNear, zFar: ZFar, shadowRangeNear: 0f, shadowRangeFar: 0f);
                pbrDirectProg.ShadowZExtendNear = 0; pbrDirectProg.ShadowZExtendFar = 0; pbrDirectProg.DropShadowIntensity = 0;


                pbrDirectProg.LightDirection = new(0f, 0f, 1f);
                pbrDirectProg.RgbaLightIn = new(0.35f, 0.55f, 0.75f);
                pbrDirectProg.RgbaAmbientIn = new(0,0,0);

                pbrDirectProg.SetPointLights(0, null, null);
            }

            pbrDirectProg.PrimaryScene = primaryScene.TextureId;
            pbrDirectProg.PrimaryDepth = primaryDepth.TextureId;
            pbrDirectProg.GBufferNormal = gBufferNormal.TextureId;
            pbrDirectProg.GBufferMaterial = gBufferMaterial.TextureId;
            pbrDirectProg.ShadowMapNear = shadowNear.TextureId;
            pbrDirectProg.ShadowMapFar = shadowFar.TextureId;

            // Binding audit (used samplers only)
            AssertSampler2DBinding("Stage: PBR Direct", pbrDirectProg, "primaryScene", primaryScene);
            AssertSampler2DBinding("Stage: PBR Direct", pbrDirectProg, "primaryDepth", primaryDepth);
            AssertSampler2DBinding("Stage: PBR Direct", pbrDirectProg, "gBufferNormal", gBufferNormal);
            AssertSampler2DBinding("Stage: PBR Direct", pbrDirectProg, "gBufferMaterial", gBufferMaterial);

            AssertGBufferFboAttachments("Stage: PBR Direct", targets.DirectLightingMrt);
            TestFramework.RenderQuadTo(pbrDirectProg, targets.DirectLightingMrt);
            AssertNoGLError("Stage: PBR Direct");

            var directDiffusePixels = targets.DirectLightingMrt[0].ReadPixels();
            var directSpecularPixels = targets.DirectLightingMrt[1].ReadPixels();
            var emissivePixels = targets.DirectLightingMrt[2].ReadPixels();

            AssertStageHasRgbEnergy(directDiffusePixels, 1e-4f, "Stage: PBR Direct → directDiffuse");
            AssertAllFinite(directSpecularPixels, "Stage: PBR Direct (directSpecular)");
            AssertStageHasRgbEnergy(emissivePixels, 1e-4f, "Stage: PBR Direct → emissive");
            AssertAllFinite(directDiffusePixels, "Stage: PBR Direct (directDiffuse)");
            AssertAllFinite(emissivePixels, "Stage: PBR Direct (emissive)");

            // -----------------------------------------------------------------
            // Stage: LumOn Velocity
            // -----------------------------------------------------------------
            using var velocityProgUse = velocityProg.UseScope();

            // Phase 23: UBO-backed frame state.
            UpdateAndBindLumOnFrameUbo(
                velocityProg,
                invCurrViewProjMatrix: invProj,
                prevViewProjMatrix: proj,
                historyValid: 1);

            velocityProg.PrimaryDepth = primaryDepth.TextureId;

            AssertSampler2DBinding("Stage: Velocity", velocityProg, "primaryDepth", primaryDepth);
            AssertGBufferFboAttachments("Stage: Velocity", targets.Velocity);
            TestFramework.RenderQuadTo(velocityProg, targets.Velocity);
            AssertNoGLError("Stage: Velocity");

            var velocityPixels = targets.Velocity[0].ReadPixels();
            AssertAllFinite(velocityPixels, "Stage: Velocity");

            // Expect near-zero velocity (RG) when prev == curr.
            for (int i = 0; i < velocityPixels.Length; i += 4)
            {
                Assert.InRange(velocityPixels[i + 0], -TestEpsilon, TestEpsilon);
                Assert.InRange(velocityPixels[i + 1], -TestEpsilon, TestEpsilon);
            }

            // -----------------------------------------------------------------
            // Stage: LumOn HZB build
            // -----------------------------------------------------------------
            // Copy mip0
            targets.Hzb.BindMipForWrite(0);
            using var hzbCopyProgUse = hzbCopyProg.UseScope();
            hzbCopyProg.PrimaryDepth = primaryDepth.TextureId;

            AssertSampler2DBinding("Stage: HZB Copy", hzbCopyProg, "primaryDepth", primaryDepth);
            AssertFboColorAttachment0("Stage: HZB Copy", expectedTextureId: targets.Hzb.Texture.TextureId, expectedMipLevel: 0);
            AssertDrawBuffersForSingleColorTarget("Stage: HZB Copy");
            AssertTexture2DLevelFormatAndSize(
                stage: "Stage: HZB Copy",
                textureId: targets.Hzb.Texture.TextureId,
                mipLevel: 0,
                expectedInternalFormat: PixelInternalFormat.R32f,
                expectedWidth: ScreenWidth,
                expectedHeight: ScreenHeight);
            TestFramework.RenderQuad(hzbCopyProg);
            targets.Hzb.Unbind();
            AssertNoGLError("Stage: HZB Copy");

            // Downsample mip0->mip1 and mip1->mip2
            for (int dstMip = 1; dstMip <= 2; dstMip++)
            {
                int srcMip = dstMip - 1;

                targets.Hzb.BindMipForWrite(dstMip);
                using var hzbDownProgUse = hzbDownProg.UseScope();
                hzbDownProg.HzbDepth = targets.Hzb.Texture;
                // The source mip belongs to the HZB parameter block, not a standalone uniform.
                hzbDownProg.SrcMip = srcMip;

                AssertSampler2DBinding($"Stage: HZB Downsample mip{dstMip}", hzbDownProg, "hzbDepth", targets.Hzb.Texture);
                AssertFboColorAttachment0($"Stage: HZB Downsample mip{dstMip}", expectedTextureId: targets.Hzb.Texture.TextureId, expectedMipLevel: dstMip);
                AssertDrawBuffersForSingleColorTarget($"Stage: HZB Downsample mip{dstMip}");
                AssertTexture2DLevelFormatAndSize(
                    stage: $"Stage: HZB Downsample mip{dstMip}",
                    textureId: targets.Hzb.Texture.TextureId,
                    mipLevel: dstMip,
                    expectedInternalFormat: PixelInternalFormat.R32f,
                    expectedWidth: Math.Max(1, ScreenWidth >> dstMip),
                    expectedHeight: Math.Max(1, ScreenHeight >> dstMip));
                TestFramework.RenderQuad(hzbDownProg);
                targets.Hzb.Unbind();
                AssertNoGLError($"Stage: HZB Downsample mip{dstMip}");
            }

            var hzbMip0 = targets.Hzb.Texture.ReadPixels(mipLevel: 0);
            var hzbMip2 = targets.Hzb.Texture.ReadPixels(mipLevel: 2);
            AssertAllFinite(hzbMip0, "Stage: HZB mip0");
            AssertAllFinite(hzbMip2, "Stage: HZB mip2");

            // Uniform depth scene => mip0 and mip2 should match 0.5.
            Assert.All(hzbMip0, v => Assert.InRange(v, 0.5f - TestEpsilon, 0.5f + TestEpsilon));
            Assert.InRange(hzbMip2[0], 0.5f - TestEpsilon, 0.5f + TestEpsilon);

            // -----------------------------------------------------------------
            // Stage: LumOn Probe Anchor
            // -----------------------------------------------------------------
            using var anchorProgUse = anchorProg.UseScope();

            // Phase 23: UBO-backed frame state.
            UpdateAndBindLumOnFrameUbo(
                anchorProg,
                invProjectionMatrix: invProj,
                invViewMatrix: identity,
                probeSpacing: ProbeSpacing,
                frameIndex: 0,
                anchorJitterEnabled: 0,
                anchorJitterScale: 0f,
                pmjCycleLength: 1);

            // Phase 23: UBO-backed probe params.
            anchorProg.DepthDiscontinuityThreshold = .1f;

            anchorProg.PrimaryDepth = primaryDepth.TextureId;
            anchorProg.GBufferNormal = gBufferNormal.TextureId;

            AssertSampler2DBinding("Stage: Probe Anchor", anchorProg, "primaryDepth", primaryDepth);
            AssertSampler2DBinding("Stage: Probe Anchor", anchorProg, "gBufferNormal", gBufferNormal);

            AssertGBufferFboAttachments("Stage: Probe Anchor", targets.ProbeAnchor);
            TestFramework.RenderQuadTo(anchorProg, targets.ProbeAnchor);
            AssertNoGLError("Stage: Probe Anchor");

            var anchorPosPixels = targets.ProbeAnchor[0].ReadPixels();
            var anchorNormalPixels = targets.ProbeAnchor[1].ReadPixels();
            AssertAllFinite(anchorPosPixels, "Stage: Probe Anchor (pos)");
            AssertAllFinite(anchorNormalPixels, "Stage: Probe Anchor (normal)");

            bool anyValid = false;
            for (int i = 0; i < anchorPosPixels.Length; i += 4)
            {
                if (anchorPosPixels[i + 3] > 0.5f)
                {
                    anyValid = true;
                    break;
                }
            }
            Assert.True(anyValid, "Stage: Probe Anchor → no valid probes");

            // -----------------------------------------------------------------
            // Stage: LumOn Atlas Trace
            // -----------------------------------------------------------------

            using var traceProgUse = traceProg.UseScope();

            // Deterministic non-zero indirect: allow sky miss fallback to contribute.
            // This makes the one-frame integration test robust even if ray hits are rare.

            // Phase 23: UBO-backed frame state.
            UpdateAndBindLumOnFrameUbo(
                traceProg,
                invProjectionMatrix: invProj,
                projectionMatrix: proj,
                viewMatrix: identity,
                invViewMatrix: identity,
                frameIndex: 0,
                sunPosition: new Vintagestory.API.MathTools.Vec3f(0f, 1f, 0f),
                sunColor: new Vintagestory.API.MathTools.Vec3f(0.2f, 0.2f, 0.2f),
                ambientColor: new Vintagestory.API.MathTools.Vec3f(0.1f, 0.1f, 0.1f));

            // Phase 23: UBO-backed probe params (indirectTint/intensity etc).
            traceProg.IndirectTint = new(1,1,1);

            traceProg.ProbeAnchorPosition = targets.ProbeAnchor[0];
            traceProg.ProbeAnchorNormal = targets.ProbeAnchor[1];
            traceProg.PrimaryDepth = primaryDepth.TextureId;
            traceProg.SurfaceAlbedo = gBufferAlbedo;
            traceProg.GBufferMaterial = gBufferMaterial.TextureId;
            traceProg.ScreenProbeAtlasHistory = historyRadiance;
            traceProg.HzbDepth = targets.Hzb.Texture;
            traceProg.ScreenProbeAtlasMetaHistory = historyMeta;

            AssertSampler2DBinding("Stage: Atlas Trace", traceProg, "probeAnchorPosition", targets.ProbeAnchor[0]);
            AssertSampler2DBinding("Stage: Atlas Trace", traceProg, "probeAnchorNormal", targets.ProbeAnchor[1]);
            AssertSampler2DBinding("Stage: Atlas Trace", traceProg, "primaryDepth", primaryDepth);
            AssertSampler2DBinding("Stage: Atlas Trace", traceProg, "surfaceAlbedo", gBufferAlbedo);
            AssertSampler2DBinding("Stage: Atlas Trace", traceProg, "gBufferMaterial", gBufferMaterial);
            AssertSampler2DBinding("Stage: Atlas Trace", traceProg, "octahedralHistory", historyRadiance);
            AssertSampler2DBinding("Stage: Atlas Trace", traceProg, "hzbDepth", targets.Hzb.Texture);
            AssertSampler2DBinding("Stage: Atlas Trace", traceProg, "probeAtlasMetaHistory", historyMeta);

            AssertGBufferFboAttachments("Stage: Atlas Trace", targets.AtlasTrace);
            TestFramework.RenderQuadTo(traceProg, targets.AtlasTrace);
            AssertNoGLError("Stage: Atlas Trace");

            var traceRadiance = targets.AtlasTrace[0].ReadPixels();
            var traceMeta = targets.AtlasTrace[1].ReadPixels();
            AssertAllFinite(traceRadiance, "Stage: Atlas Trace (radiance)");
            AssertAllFinite(traceMeta, "Stage: Atlas Trace (meta)");

            // Confidence is stored in meta.r.
            for (int i = 0; i < traceMeta.Length; i += 2)
            {
                Assert.InRange(traceMeta[i + 0], 0.0f, 1.0f);
            }

            AssertStageHasRgbEnergy(traceRadiance, 1e-6f, "Stage: Atlas Trace → radiance");

            // -----------------------------------------------------------------
            // Stage: LumOn Atlas Temporal
            // -----------------------------------------------------------------
            using var temporalProgUse = temporalProg.UseScope();

            // Keep jitter off in this test (matches Probe Anchor stage).

            // Phase 14: enable velocity reprojection (velocity is near-zero in this test)

            // Phase 23: UBO-backed frame state.
            UpdateAndBindLumOnFrameUbo(
                temporalProg,
                probeSpacing: ProbeSpacing,
                frameIndex: 0,
                anchorJitterEnabled: 0,
                anchorJitterScale: 0f,
                pmjCycleLength: 1,
                enableVelocityReprojection: 1,
                velocityRejectThreshold: 0.01f);

            // Phase 23: UBO-backed probe params (temporalAlpha/hitDistanceRejectThreshold).
            temporalProg.TemporalAlpha = .9f; temporalProg.HitDistanceRejectThreshold = .3f;

            temporalProg.ScreenProbeAtlasCurrent = targets.AtlasTrace[0];
            temporalProg.ScreenProbeAtlasHistory = historyRadiance;
            temporalProg.ProbeAnchorPosition = targets.ProbeAnchor[0];
            temporalProg.ScreenProbeAtlasMetaCurrent = targets.AtlasTrace[1];
            temporalProg.ScreenProbeAtlasMetaHistory = historyMeta;
            temporalProg.VelocityTex = targets.Velocity[0];

            AssertSampler2DBinding("Stage: Atlas Temporal", temporalProg, "octahedralCurrent", targets.AtlasTrace[0]);
            AssertSampler2DBinding("Stage: Atlas Temporal", temporalProg, "octahedralHistory", historyRadiance);
            AssertSampler2DBinding("Stage: Atlas Temporal", temporalProg, "probeAnchorPosition", targets.ProbeAnchor[0]);
            AssertSampler2DBinding("Stage: Atlas Temporal", temporalProg, "probeAtlasMetaCurrent", targets.AtlasTrace[1]);
            AssertSampler2DBinding("Stage: Atlas Temporal", temporalProg, "probeAtlasMetaHistory", historyMeta);
            AssertSampler2DBinding("Stage: Atlas Temporal", temporalProg, "velocityTex", targets.Velocity[0]);

            AssertGBufferFboAttachments("Stage: Atlas Temporal", targets.AtlasTemporal);
            TestFramework.RenderQuadTo(temporalProg, targets.AtlasTemporal);
            AssertNoGLError("Stage: Atlas Temporal");

            var temporalRadiance = targets.AtlasTemporal[0].ReadPixels();
            AssertAllFinite(temporalRadiance, "Stage: Atlas Temporal (radiance)");
            AssertStageHasRgbEnergy(temporalRadiance, 1e-6f, "Stage: Atlas Temporal → radiance");

            // -----------------------------------------------------------------
            // Stage: LumOn Atlas Filter
            // -----------------------------------------------------------------
            using var filterProgUse = filterProg.UseScope();

            // Phase 23: UBO-backed frame state (probeGridSize).
            UpdateAndBindLumOnFrameUbo(filterProg);

            // Phase 23: UBO-backed probe params (filterRadius/hitDistanceSigma).
            filterProg.FilterRadius = 1; filterProg.HitDistanceSigma = 1;

            filterProg.ScreenProbeAtlas = targets.AtlasTemporal[0];
            filterProg.ScreenProbeAtlasMeta = targets.AtlasTemporal[1];
            filterProg.ProbeAnchorPosition = targets.ProbeAnchor[0];

            AssertSampler2DBinding("Stage: Atlas Filter", filterProg, "octahedralAtlas", targets.AtlasTemporal[0]);
            AssertSampler2DBinding("Stage: Atlas Filter", filterProg, "probeAtlasMeta", targets.AtlasTemporal[1]);
            AssertSampler2DBinding("Stage: Atlas Filter", filterProg, "probeAnchorPosition", targets.ProbeAnchor[0]);

            AssertGBufferFboAttachments("Stage: Atlas Filter", targets.AtlasFiltered);
            TestFramework.RenderQuadTo(filterProg, targets.AtlasFiltered);
            AssertNoGLError("Stage: Atlas Filter");

            var filteredRadiance = targets.AtlasFiltered[0].ReadPixels();
            AssertAllFinite(filteredRadiance, "Stage: Atlas Filter (radiance)");

            // -----------------------------------------------------------------
            // Stage: LumOn Atlas Gather (half-res)
            // -----------------------------------------------------------------
            using var gatherProgUse = gatherProg.UseScope();

            // Phase 23: UBO-backed frame state.
            UpdateAndBindLumOnFrameUbo(
                gatherProg,
                invProjectionMatrix: invProj,
                viewMatrix: identity,
                probeSpacing: ProbeSpacing);

            // Phase 23: UBO-backed probe params (intensity/indirectTint/leakThreshold/sampleStride).
            gatherProg.Intensity = 1; gatherProg.IndirectTint = [1,1,1]; gatherProg.LeakThreshold = .5f; gatherProg.SampleStride = 1;

            gatherProg.ScreenProbeAtlas = targets.AtlasFiltered[0];
            gatherProg.ProbeAnchorPosition = targets.ProbeAnchor[0];
            gatherProg.ProbeAnchorNormal = targets.ProbeAnchor[1];
            gatherProg.PrimaryDepth = primaryDepth.TextureId;
            gatherProg.GBufferNormal = gBufferNormal.TextureId;

            AssertSampler2DBinding("Stage: Gather", gatherProg, "octahedralAtlas", targets.AtlasFiltered[0]);
            AssertSampler2DBinding("Stage: Gather", gatherProg, "probeAnchorPosition", targets.ProbeAnchor[0]);
            AssertSampler2DBinding("Stage: Gather", gatherProg, "probeAnchorNormal", targets.ProbeAnchor[1]);
            AssertSampler2DBinding("Stage: Gather", gatherProg, "primaryDepth", primaryDepth);
            AssertSampler2DBinding("Stage: Gather", gatherProg, "gBufferNormal", gBufferNormal);

            AssertGBufferFboAttachments("Stage: Gather", targets.IndirectHalf);
            TestFramework.RenderQuadTo(gatherProg, targets.IndirectHalf);
            AssertNoGLError("Stage: Gather");

            var indirectHalf = targets.IndirectHalf[0].ReadPixels();
            AssertAllFinite(indirectHalf, "Stage: Gather (indirectHalf)");
            AssertStageHasRgbEnergy(indirectHalf, 1e-6f, "Stage: Gather → indirectHalf");

            // -----------------------------------------------------------------
            // Stage: LumOn Upsample (full-res)
            // -----------------------------------------------------------------
            using var upsampleProgUse = upsampleProg.UseScope();

            // Phase 23: UBO-backed frame state.
            UpdateAndBindLumOnFrameUbo(upsampleProg);

            // Phase 23: UBO-backed upsample params (depth/normal/spatial sigmas, hole-fill thresholds).
            upsampleProg.UpsampleDepthSigma = .1f; upsampleProg.UpsampleNormalSigma = 16; upsampleProg.UpsampleSpatialSigma = 1; upsampleProg.HoleFillMinConfidence = .05f; upsampleProg.HoleFillRadius = 2;

            upsampleProg.IndirectHalf = targets.IndirectHalf[0];
            upsampleProg.PrimaryDepth = primaryDepth.TextureId;
            upsampleProg.GBufferNormal = gBufferNormal.TextureId;

            AssertSampler2DBinding("Stage: Upsample", upsampleProg, "indirectHalf", targets.IndirectHalf[0]);
            AssertSampler2DBinding("Stage: Upsample", upsampleProg, "primaryDepth", primaryDepth);
            AssertSampler2DBinding("Stage: Upsample", upsampleProg, "gBufferNormal", gBufferNormal);

            AssertGBufferFboAttachments("Stage: Upsample", targets.IndirectFull);
            TestFramework.RenderQuadTo(upsampleProg, targets.IndirectFull);
            AssertNoGLError("Stage: Upsample");

            var indirectFull = targets.IndirectFull[0].ReadPixels();
            AssertAllFinite(indirectFull, "Stage: Upsample (indirectFull)");
            AssertStageHasRgbEnergy(indirectFull, 1e-6f, "Stage: Upsample → indirectFull");

            // -----------------------------------------------------------------
            // Stage: PBR Composite
            // -----------------------------------------------------------------
            using var compositeUse = pbrCompositeProg.UseScope();
            // Full composite (indirect from pipeline)
            SetupPbrCompositeUniforms(pbrCompositeProg, invProj, identity, lumOnEnabled: 1);

            pbrCompositeProg.DirectDiffuse = targets.DirectLightingMrt[0];
            pbrCompositeProg.DirectSpecular = targets.DirectLightingMrt[1];
            pbrCompositeProg.Emissive = targets.DirectLightingMrt[2];
            pbrCompositeProg.IndirectDiffuse = targets.IndirectFull[0];
            pbrCompositeProg.GBufferAlbedo = gBufferAlbedo.TextureId;
            pbrCompositeProg.GBufferMaterial = gBufferMaterial.TextureId;
            pbrCompositeProg.GBufferNormal = gBufferNormal.TextureId;
            pbrCompositeProg.PrimaryDepth = primaryDepth.TextureId;

            AssertSampler2DBinding("Stage: Composite (full)", pbrCompositeProg, "directDiffuse", targets.DirectLightingMrt[0]);
            AssertSampler2DBinding("Stage: Composite (full)", pbrCompositeProg, "directSpecular", targets.DirectLightingMrt[1]);
            AssertSampler2DBinding("Stage: Composite (full)", pbrCompositeProg, "emissive", targets.DirectLightingMrt[2]);
            AssertSampler2DBinding("Stage: Composite (full)", pbrCompositeProg, "indirectDiffuse", targets.IndirectFull[0]);
            AssertSampler2DBinding("Stage: Composite (full)", pbrCompositeProg, "gBufferAlbedo", gBufferAlbedo);
            AssertSampler2DBinding("Stage: Composite (full)", pbrCompositeProg, "gBufferMaterial", gBufferMaterial);
            AssertSampler2DBinding("Stage: Composite (full)", pbrCompositeProg, "gBufferNormal", gBufferNormal);
            AssertSampler2DBinding("Stage: Composite (full)", pbrCompositeProg, "primaryDepth", primaryDepth);

            AssertGBufferFboAttachments("Stage: Composite (full)", targets.Composite);
            TestFramework.RenderQuadTo(pbrCompositeProg, targets.Composite);
            AssertNoGLError("Stage: Composite (full)");

            var compositeFull = targets.Composite[0].ReadPixels();
            AssertAllFinite(compositeFull, "Stage: Composite (full)");

            // Baseline (same wiring, but indirectDiffuse is forced to 0)
            SetupPbrCompositeUniforms(pbrCompositeProg, invProj, identity, lumOnEnabled: 1);

            pbrCompositeProg.DirectDiffuse = targets.DirectLightingMrt[0];
            pbrCompositeProg.DirectSpecular = targets.DirectLightingMrt[1];
            pbrCompositeProg.Emissive = targets.DirectLightingMrt[2];
            pbrCompositeProg.IndirectDiffuse = zeroIndirectFull;
            pbrCompositeProg.GBufferAlbedo = gBufferAlbedo.TextureId;
            pbrCompositeProg.GBufferMaterial = gBufferMaterial.TextureId;
            pbrCompositeProg.GBufferNormal = gBufferNormal.TextureId;
            pbrCompositeProg.PrimaryDepth = primaryDepth.TextureId;

            AssertSampler2DBinding("Stage: Composite (baseline)", pbrCompositeProg, "directDiffuse", targets.DirectLightingMrt[0]);
            AssertSampler2DBinding("Stage: Composite (baseline)", pbrCompositeProg, "directSpecular", targets.DirectLightingMrt[1]);
            AssertSampler2DBinding("Stage: Composite (baseline)", pbrCompositeProg, "emissive", targets.DirectLightingMrt[2]);
            AssertSampler2DBinding("Stage: Composite (baseline)", pbrCompositeProg, "indirectDiffuse", zeroIndirectFull);
            AssertSampler2DBinding("Stage: Composite (baseline)", pbrCompositeProg, "gBufferAlbedo", gBufferAlbedo);
            AssertSampler2DBinding("Stage: Composite (baseline)", pbrCompositeProg, "gBufferMaterial", gBufferMaterial);
            AssertSampler2DBinding("Stage: Composite (baseline)", pbrCompositeProg, "gBufferNormal", gBufferNormal);
            AssertSampler2DBinding("Stage: Composite (baseline)", pbrCompositeProg, "primaryDepth", primaryDepth);

            AssertGBufferFboAttachments("Stage: Composite (baseline)", baselineComposite);
            TestFramework.RenderQuadTo(pbrCompositeProg, baselineComposite);
            AssertNoGLError("Stage: Composite (baseline)");

            var compositeBaselinePixels = baselineComposite[0].ReadPixels();
            AssertAllFinite(compositeBaselinePixels, "Stage: Composite (baseline)");

            // Baseline must match "direct-only" expectation (fog disabled, indirect=0):
            // composite.rgb == directDiffuse + directSpecular + emissive.
            AssertCompositeMatchesDirectOnly(
                compositeBaselinePixels,
                directDiffusePixels,
                directSpecularPixels,
                emissivePixels,
                epsilon: 2e-3f);

            // Indirect-injected sanity: bypass LumOn, bind a known constant indirect and prove
            // composite brightens vs baseline. This isolates composite binding/uniform logic.
            SetupPbrCompositeUniforms(pbrCompositeProg, invProj, identity, lumOnEnabled: 1);

            pbrCompositeProg.DirectDiffuse = targets.DirectLightingMrt[0];
            pbrCompositeProg.DirectSpecular = targets.DirectLightingMrt[1];
            pbrCompositeProg.Emissive = targets.DirectLightingMrt[2];
            pbrCompositeProg.IndirectDiffuse = injectedIndirectFull;
            pbrCompositeProg.GBufferAlbedo = gBufferAlbedo.TextureId;
            pbrCompositeProg.GBufferMaterial = gBufferMaterial.TextureId;
            pbrCompositeProg.GBufferNormal = gBufferNormal.TextureId;
            pbrCompositeProg.PrimaryDepth = primaryDepth.TextureId;

            AssertSampler2DBinding("Stage: Composite (injected)", pbrCompositeProg, "directDiffuse", targets.DirectLightingMrt[0]);
            AssertSampler2DBinding("Stage: Composite (injected)", pbrCompositeProg, "directSpecular", targets.DirectLightingMrt[1]);
            AssertSampler2DBinding("Stage: Composite (injected)", pbrCompositeProg, "emissive", targets.DirectLightingMrt[2]);
            AssertSampler2DBinding("Stage: Composite (injected)", pbrCompositeProg, "indirectDiffuse", injectedIndirectFull);
            AssertSampler2DBinding("Stage: Composite (injected)", pbrCompositeProg, "gBufferAlbedo", gBufferAlbedo);
            AssertSampler2DBinding("Stage: Composite (injected)", pbrCompositeProg, "gBufferMaterial", gBufferMaterial);
            AssertSampler2DBinding("Stage: Composite (injected)", pbrCompositeProg, "gBufferNormal", gBufferNormal);
            AssertSampler2DBinding("Stage: Composite (injected)", pbrCompositeProg, "primaryDepth", primaryDepth);

            AssertGBufferFboAttachments("Stage: Composite (injected)", injectedComposite);
            TestFramework.RenderQuadTo(pbrCompositeProg, injectedComposite);
            AssertNoGLError("Stage: Composite (injected)");

            var injectedCompositePixels = injectedComposite[0].ReadPixels();
            AssertAllFinite(injectedCompositePixels, "Stage: Composite (injected-indirect)");

            float baselineLuma = ComputeAverageLuminance(compositeBaselinePixels);
            float injectedLuma = ComputeAverageLuminance(injectedCompositePixels);
            Assert.True(injectedLuma > baselineLuma + 1e-4f,
                $"Composite did not brighten with injected indirect (baselineLuma={baselineLuma}, injectedLuma={injectedLuma})");

            // Require that the pipeline's composite is measurably different from direct-only baseline.
            float maxDelta = 0f;
            for (int i = 0; i < compositeFull.Length; i++)
            {
                maxDelta = MathF.Max(maxDelta, MathF.Abs(compositeFull[i] - compositeBaselinePixels[i]));
            }

            Assert.True(maxDelta > 1e-4f, $"Composite did not change with indirect enabled (maxDelta={maxDelta})");

            // Final hygiene checkpoint: no GL errors should remain queued for subsequent tests.
            AssertNoGLError("PbrLumOnFullPipelineIntegrationTests end");
        }

    }

    private static void AssertAllFinite(float[] values, string stage)
    {
        for (int i = 0; i < values.Length; i++)
        {
            float v = values[i];
            Assert.False(float.IsNaN(v) || float.IsInfinity(v), $"{stage}: non-finite value at idx {i}: {v}");
        }
    }

    private static bool AnyRgbAbove(float[] rgba, float threshold)
    {
        for (int i = 0; i + 2 < rgba.Length; i += 4)
        {
            if (rgba[i + 0] > threshold || rgba[i + 1] > threshold || rgba[i + 2] > threshold)
            {
                return true;
            }
        }

        return false;
    }

    private static void AssertStageHasRgbEnergy(float[] rgba, float threshold, string stage)
    {
        if (AnyRgbAbove(rgba, threshold))
        {
            return;
        }

        (float min, float max) = GetMinMax(rgba);
        Assert.Fail($"{stage} unexpectedly has no RGB energy (threshold={threshold}, min={min}, max={max})");
    }

    private static (float min, float max) GetMinMax(float[] values)
    {
        if (values.Length == 0)
        {
            return (0f, 0f);
        }

        float min = float.PositiveInfinity;
        float max = float.NegativeInfinity;
        for (int i = 0; i < values.Length; i++)
        {
            float v = values[i];
            if (v < min) min = v;
            if (v > max) max = v;
        }

        return (min, max);
    }

    private static void AssertCompositeMatchesDirectOnly(
        float[] compositeRgba,
        float[] directDiffuseRgba,
        float[] directSpecularRgba,
        float[] emissiveRgba,
        float epsilon)
    {
        Assert.Equal(directDiffuseRgba.Length, compositeRgba.Length);
        Assert.Equal(directSpecularRgba.Length, compositeRgba.Length);
        Assert.Equal(emissiveRgba.Length, compositeRgba.Length);

        for (int i = 0; i + 3 < compositeRgba.Length; i += 4)
        {
            for (int c = 0; c < 3; c++)
            {
                float expected = directDiffuseRgba[i + c] + directSpecularRgba[i + c] + emissiveRgba[i + c];
                float actual = compositeRgba[i + c];
                float delta = MathF.Abs(actual - expected);
                Assert.True(delta <= epsilon,
                    $"Stage: Composite baseline mismatch at idx {i / 4} channel {c} (expected={expected}, actual={actual}, delta={delta}, eps={epsilon})");
            }
        }
    }

    private static float ComputeAverageLuminance(float[] rgba)
    {
        // Rec.709 luma for quick scalar compare.
        const float wr = 0.2126f;
        const float wg = 0.7152f;
        const float wb = 0.0722f;

        float sum = 0f;
        int count = 0;
        for (int i = 0; i + 2 < rgba.Length; i += 4)
        {
            sum += rgba[i + 0] * wr + rgba[i + 1] * wg + rgba[i + 2] * wb;
            count++;
        }

        return count == 0 ? 0f : sum / count;
    }

    private static void AssertSampler2DBinding(string stage, GpuProgram program, string samplerUniform, DynamicTexture2D expectedTexture)
    {
        ArgumentNullException.ThrowIfNull(expectedTexture);
        Assert.True(expectedTexture.IsValid, $"{stage}: expected texture for '{samplerUniform}' is invalid/disposed");
        Assert.NotEqual(0, expectedTexture.TextureId);

        int loc = program.ProgramLayout.BinaryInterface!.GetUniformLocation(samplerUniform);
        Assert.True(loc >= 0, $"{stage}: sampler uniform '{samplerUniform}' not found");

        Assert.True(program.ProgramLayout.TryGetContractSamplerUnit(samplerUniform, out int expectedUnit));
        GL.GetUniform(program.ProgramId, loc, out int actualUnit);
        Assert.Equal(expectedUnit, actualUnit);

        // Preserve active texture unit.
        GL.GetInteger(GetPName.ActiveTexture, out int prevActiveTex);

        GL.ActiveTexture(TextureUnit.Texture0 + expectedUnit);
        GL.GetInteger(GetPName.TextureBinding2D, out int boundTexId);

        // Restore.
        GL.ActiveTexture((TextureUnit)prevActiveTex);

        Assert.Equal(expectedTexture.TextureId, boundTexId);
    }

    private static void AssertGBufferFboAttachments(string stage, GpuFramebuffer target)
    {
        ArgumentNullException.ThrowIfNull(target);
        Assert.True(target.IsValid, $"{stage}: target GBuffer is invalid/disposed");

        // Bind (so we can query attachment state) and restore.
        target.BindWithViewport();

        // Ensure we are querying the correct FBO.
        GL.GetInteger(GetPName.FramebufferBinding, out int boundFbo);
        Assert.Equal(target.FboId, boundFbo);

        AssertDrawBuffersForMrtTarget(stage, target.ColorAttachmentCount);

        for (int i = 0; i < target.ColorAttachmentCount; i++)
        {
            var expected = target[i];
            Assert.True(expected.IsValid, $"{stage}: attachment {i} texture invalid");
            AssertFboColorAttachment(
                stage,
                attachmentIndex: i,
                expectedTextureId: expected.TextureId,
                expectedMipLevel: 0);

            AssertTexture2DLevelFormatAndSize(
                stage: stage,
                textureId: expected.TextureId,
                mipLevel: 0,
                expectedInternalFormat: expected.InternalFormat,
                expectedWidth: expected.Width,
                expectedHeight: expected.Height);
        }

        GpuFramebuffer.Unbind();
    }

    private static void AssertFboColorAttachment0(string stage, int expectedTextureId, int expectedMipLevel)
    {
        AssertFboColorAttachment(stage, attachmentIndex: 0, expectedTextureId, expectedMipLevel);
    }

    private static void AssertFboColorAttachment(string stage, int attachmentIndex, int expectedTextureId, int expectedMipLevel)
    {
        var attachment = FramebufferAttachment.ColorAttachment0 + attachmentIndex;

        int type = GetFramebufferAttachmentInt(FramebufferTarget.Framebuffer, attachment, FramebufferParameterName.FramebufferAttachmentObjectType);
        Assert.Equal(FramebufferAttachmentObjectType.Texture, (FramebufferAttachmentObjectType)type);

        int objectName = GetFramebufferAttachmentInt(FramebufferTarget.Framebuffer, attachment, FramebufferParameterName.FramebufferAttachmentObjectName);
        Assert.Equal(expectedTextureId, objectName);

        int level = GetFramebufferAttachmentInt(FramebufferTarget.Framebuffer, attachment, FramebufferParameterName.FramebufferAttachmentTextureLevel);
        Assert.Equal(expectedMipLevel, level);
    }

    private static void AssertDrawBuffersForMrtTarget(string stage, int colorAttachmentCount)
    {
        Assert.True(colorAttachmentCount > 0, $"{stage}: expected at least one color attachment");

        for (int i = 0; i < colorAttachmentCount; i++)
        {
            var pname = (GetPName)((int)GetPName.DrawBuffer0 + i);
            GL.GetInteger(pname, out int drawBufferEnum);

            var expected = (DrawBufferMode)((int)DrawBufferMode.ColorAttachment0 + i);
            Assert.Equal(expected, (DrawBufferMode)drawBufferEnum);
        }
    }

    private static void AssertDrawBuffersForSingleColorTarget(string stage)
    {
        GL.GetInteger(GetPName.DrawBuffer0, out int drawBufferEnum);
        Assert.Equal(DrawBufferMode.ColorAttachment0, (DrawBufferMode)drawBufferEnum);
    }

    private static void AssertTexture2DLevelFormatAndSize(
        string stage,
        int textureId,
        int mipLevel,
        PixelInternalFormat expectedInternalFormat,
        int expectedWidth,
        int expectedHeight)
    {
        Assert.True(textureId != 0, $"{stage}: textureId is 0");

        // Preserve binding.
        GL.GetInteger(GetPName.TextureBinding2D, out int prevBinding);
        GL.BindTexture(TextureTarget.Texture2D, textureId);

        GL.GetTexLevelParameter(TextureTarget.Texture2D, mipLevel, GetTextureParameter.TextureInternalFormat, out int internalFormat);
        GL.GetTexLevelParameter(TextureTarget.Texture2D, mipLevel, GetTextureParameter.TextureWidth, out int width);
        GL.GetTexLevelParameter(TextureTarget.Texture2D, mipLevel, GetTextureParameter.TextureHeight, out int height);

        GL.BindTexture(TextureTarget.Texture2D, prevBinding);

        Assert.Equal((int)expectedInternalFormat, internalFormat);
        Assert.Equal(expectedWidth, width);
        Assert.Equal(expectedHeight, height);
    }

    private static int GetFramebufferAttachmentInt(FramebufferTarget target, FramebufferAttachment attachment, FramebufferParameterName pname)
    {
        int[] values = new int[1];
        GL.GetFramebufferAttachmentParameter(target, attachment, pname, values);
        return values[0];
    }

    /// <summary>Uses production setters for composition parameters, preserving the controlled lighting comparison.</summary>
    private static void SetupPbrCompositeUniforms(PBRCompositeShaderProgram program, float[] invProjection, float[] viewMatrix, int lumOnEnabled)
    {
        using var use = program.UseScope();
        program.InvProjectionMatrix = invProjection;
        program.ViewMatrix = viewMatrix;
        program.RgbaFogIn = new(0,0,0,0);
        program.FogDensityIn = 0; program.FogMinIn = 0;
        program.IndirectIntensity = 1; program.IndirectTint = new(1,1,1);
        program.DiffuseAOStrength = 1; program.SpecularAOStrength = 1;
    }
}
